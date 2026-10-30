# CoreDevice serve-web: hợp đồng upstream cho bản thử Windows

- Ngày: 2026-10-29.
- Phạm vi: hợp đồng mà `tools/MacExtend.CoreDeviceProbe` dựa vào. Chỉ là bằng chứng từ mã nguồn và `--help`; chưa có thử nghiệm trên thiết bị thật. Kết quả phần cứng nằm ở [2026-10-29-coredevice-results.md](2026-10-29-coredevice-results.md).
- Không phải giao thức công khai của Apple: CoreDevice là dịch vụ lập trình viên được pymobiledevice3 dịch ngược. Mọi điểm dưới đây chỉ đúng cho phiên bản được khóa.

## 1. Phiên bản được khóa

| Mục | Giá trị | Cách xác minh |
|---|---|---|
| Gói | `pymobiledevice3==11.47.0` | `tools/coredevice/requirements.in`, lock có hash `requirements-win-x64-py313.lock.txt` |
| Tag | `v11.47.0` | `git ls-remote https://github.com/doronz88/pymobiledevice3 refs/tags/v11.47.0` |
| Commit của tag | `f7b034ec49696b1769c80257aadfa5387271dc6b` | cùng lệnh trên, ngày 2026-10-29 |
| Bản sửa cleanup Windows | `d700d8e22a55b27877d87c7d90acc6cf86bec31b` "cli: Let Ctrl-C cancel async commands instead of raising inside them" | GitHub compare `d700d8e...v11.47.0`: `status=ahead`, `ahead_by=157`, `behind_by=0`, merge base đúng bằng `d700d8e`, tức tag chứa bản sửa |
| Python | CPython 3.13 x64 trên Windows | `setup.ps1` từ chối interpreter khác; runtime của app dùng CPython 3.13 từ nuget.org (`tools/coredevice/runtime-python.txt`) |

Bản 11.17.0 không còn được hỗ trợ: nó chưa có `d700d8e`, nên Ctrl+C trên Windows có thể ném `KeyboardInterrupt` vào giữa tác vụ và bỏ qua bước trả phiên media. Không đổi phiên bản khi chưa lặp lại toàn bộ mục 2–7 với phiên bản mới.

Mã nguồn được đọc từ wheel 11.47.0 (`pymobiledevice3/remote/core_device/screen_stream.py`, `display_service.py`, `cli/developer/core_device.py`, `cli/cli_common.py`, `resources/serve_web/viewer.js`). Không có bản sửa đổi nào ở upstream.

## 2. Dòng lệnh

Lệnh probe chạy (tạo ở `CoreDeviceArguments.ServeWeb`):

```
<venv>\Scripts\python.exe -I -X utf8 -u -m pymobiledevice3 developer core-device display serve-web
    --bind 127.0.0.1 --http-port <port> --no-audio (--userspace | --tunnel <UDID>)
```

- `serve-web` nhận `--display-id`, `--bind` (mặc định `127.0.0.1`), `--http-port` (mặc định 8080), `--password`, `--no-audio`, `--ltrp`, `--rtcp-fb`, `--https`, `--rctl/--no-rctl`, `--motion-idr/--no-motion-idr`, `--compensate/--no-compensate`, cùng nhóm Device Options chung. `setup.ps1` kiểm tra `--help` liệt kê `--bind`, `--http-port`, `--no-audio`, `--userspace`, `--tunnel`.
- `serve-web` không có `--udid`. Với `--userspace`, thiết bị lấy từ biến môi trường `PYMOBILEDEVICE3_UDID` (`cli_common.py`: `UDID_ENV_VAR`). Với `--tunnel`, UDID là đối số của `--tunnel`.
- Các biến `PYMOBILEDEVICE3_*` khác (ví dụ biến chọn tunnel, userspace, native, usbmux) cũng là `envvar` của Device Options, nên probe xóa toàn bộ biến có tiền tố này (không phân biệt hoa thường) trước khi đặt `PYMOBILEDEVICE3_UDID`. `FORCE_COLOR` cũng bị xóa và `NO_COLOR=1` để dòng log parse được.
- `--mobdev2` chỉ có ở các lệnh lockdown/usbmux (ví dụ `lockdown pair`, `lockdown wifi-connections`) và ở `remote tunneld`; không đưa vào `serve-web`.
- `--userspace`: tunnel iOS 17+ trong tiến trình, không cần quyền quản trị; DDI mount qua đường này chậm hơn. `--tunnel <UDID>`: dùng thiết bị do `remote tunneld` (chạy với quyền quản trị) phát hiện. Hình nhận khác nhau theo transport (`screen_stream.open_media_receiver`): với userspace, socket UDP nằm trên stack pytcp trong tiến trình; với tunnel, đó là socket UDP của hệ điều hành bind `::`, và địa chỉ báo cho thiết bị là địa chỉ local của kết nối RSD trên card tunnel. RTCP RR chỉ được gửi sau khi đã nhận gói RTP đầu tiên, nên với tunnel trên Windows, RTP từ thiết bị là gói vào không được yêu cầu, đi qua firewall trên card `pymobiledevice3-tunnel-<UDID>` (Public). Cách sửa ở README, *Firewall khi dùng tunnel*. Qua Wi-Fi: đường mobdev2 (lockdown, `CoreDeviceTunnelProxy`) bị thiết bị đóng ở bước bắt tay CDTunnel trên iOS 27.2; đường RemotePairing (`RemotePairingTunnelService`, `_remotepairing._tcp`, pair record tạo qua `lockdown remotepairing --pair`) chạy được với tunnel TCP. iOS 18.2+ bỏ QUIC; tunnel TCP cần TLS-PSK: Python 3.13+ có sẵn (`SSLContext.set_psk_client_callback`), Python 3.12 dùng `sslpsk-pmd3`, mà wheel `sslpsk_pmd3-1.0.3-cp312-cp312-win_amd64` có cả hai extension link tới `libssl-1_1-x64.dll` nên không import được trên Python 3.12 (OpenSSL 3).

Lệnh phụ cho runbook, xác nhận bằng `--help` hoặc mã nguồn 11.47.0:

| Lệnh | Nguồn | Ghi chú |
|---|---|---|
| `usbmux list` | mã nguồn `cli/usbmux.py` | lấy UDID |
| `lockdown pair` | `--help` | ghép nối tin cậy |
| `amfi reveal-developer-mode`, `amfi enable-developer-mode`, `amfi developer-mode-status` | mã nguồn `cli/amfi.py` (tên lệnh do typer sinh từ tên hàm) | chưa chạy `--help` riêng từng lệnh |
| `mounter auto-mount` | `--help` | thành công cả khi ảnh đã mount; chạy lại sau mỗi lần khởi động lại thiết bị |
| `mounter query-developer-mode-status` | `--help` | |
| `lockdown wifi-connections on` | `--help` | bật kết nối Wi-Fi, chạy qua USB lần đầu; có `--mobdev2` |
| `remote tunneld` | `--help` | `--host` mặc định 127.0.0.1, `--port` 49151, `--usb/--wifi/--usbmux/--mobdev2` |
| `remote start-tunnel` | `--help` | `-t usb|wifi`, `--udid` |
| `developer core-device display get-media-stream-server-status` | mã nguồn `cli/developer/core_device.py` | in JSON trạng thái media server; cùng Device Options |

## 3. HTTP

`serve-web` tự viết HTTP/1.1 trên `asyncio.start_server`. Các endpoint probe dùng:

| Endpoint | Body | Ghi chú |
|---|---|---|
| `POST /touch` | `{"type": "contact"|"release"|"tap", "x": int, "y": int}` | `tap` = contact, ngủ 50 ms, release tại cùng điểm |
| `POST /key` | `{"usages": [int, ...]}` | tập đầy đủ phím đang giữ; `[]` = nhả tất cả |
| `POST /button` | `{"name": "home"|"lock"|"volume-up"|"volume-down"|"mute"|"siri", "state": "press"|"down"|"up"}` | `press` = down, giữ (home 0,05 s; lock 0,5 s; siri khoảng 1 s), up |

- HTTP 200 chỉ có nghĩa đã đưa vào hàng đợi: handler đọc body, gọi `_hid_queue.put_nowait`, trả `200 ok` ngay. Một worker duy nhất xử lý tuần tự; lỗi 400 từ handler (JSON sai, nút lạ) chỉ được ghi log `WARNING queued ... -> 400`, không trả cho client. Vì vậy client không thể biết thao tác đã tới thiết bị, và mọi request đã gửi có thể đã được xử lý dù client timeout.
- HID mở lười ở request đầu tiên (`_ensure_hid`); bàn phím được tạo ở `/key` đầu tiên và tạo lại sau khi stream khởi động lại.
- `GET /` trả trang viewer (đường dẫn có query vẫn ra trang viewer). Viewer còn dùng `/codec`, `/stream.bin`, `/audio.bin`, `/rotate`, `/clipboard`, `/clipboard/image`.
- Client probe tắt proxy, redirect và `Expect: 100-continue`, gửi tuần tự, không retry input (`CoreDeviceClient`).

## 4. Tọa độ và hướng xoay

- `viewer.js`: tọa độ HID là UInt16 `0..65535` chuẩn hóa trên buffer thiết bị chưa xoay; điểm chạm được kẹp vào `[0, 65535]`.
- Viewer xoay hình bên trong canvas theo `visualRotation`, rồi xoay ngược điểm bấm về hệ tọa độ buffer trước khi chuẩn hóa. Nút xoay gọi `POST /rotate {direction}`.
- Vì vậy lệnh API của probe (`tap`, `hold`, `drag`) dùng tọa độ HID gốc và chỉ được thử ở hướng dọc; xoay chỉ thử qua viewer.

## 5. Viewer: nhả, focus, bàn phím, clipboard, codec

- Con trỏ: `pointerdown/move` gửi `contact`, `pointerup` gửi `release`. `pointercancel`, `lostpointercapture`, `blur` của cửa sổ và `visibilitychange` đều gửi `release` tại tọa độ cuối. Kéo ra ngoài canvas vẫn giữ capture nên vẫn nhả đúng lúc thả.
- Bàn phím: `e.code` đổi sang HID usage trên trình duyệt; viewer gửi toàn bộ tập phím đang giữ qua `/key`. `blur` của canvas hoặc cửa sổ gửi tập rỗng. Văn bản đã ghép bởi bộ gõ Windows (UniKey, EVKey) không được gửi; iOS tự gõ theo bộ gõ trên thiết bị.
- Clipboard: "Send to device" (`POST /clipboard`), "Get from device" (`GET /clipboard`, `/clipboard/image`); phím dán với clipboard chia sẻ bật sẽ đẩy clipboard máy chủ lên trước.
- Chuột phải trên canvas gửi Home (`/button home press`).
- Codec: `/codec` trả `{codec, description}` cho WebCodecs; chuỗi codec `hev1.*`/`hvc1.*` từ SPS HEVC. Trình duyệt phải giải mã được HEVC qua WebCodecs; HTTP sẵn sàng không chứng minh điều này.
- Upstream chưa xử lý: không có xác nhận thao tác đã tới thiết bị, không có giới hạn thời gian giữ phía server, không có phát hiện nhiều viewer cùng gửi input, không có đồng bộ trạng thái giữa viewer và client khác.

## 6. Dòng log và vòng đời

Probe chỉ parse các dòng sau của logger `pymobiledevice3.remote.core_device.screen_stream` (`ServeWebLog`); output khác không được lưu.

| Dòng | Ý nghĩa |
|---|---|
| `INFO Open http://127.0.0.1:<port>/ in Safari/Chrome. Ctrl-C to stop.` | listener HTTP đã mở; là điều kiện cần của readiness, không đủ |
| `INFO hid worker started` | worker HID chạy (HID chưa mở với thiết bị) |
| `INFO WebCodecs codec string: hev1...` | stream video đã có tham số |
| `WARNING initial stream start timed out` | khởi động stream quá 25 s; vẫn phục vụ, thử lại khi có viewer |
| `WARNING device <udid> disconnected; waiting for tunneld ...` / `INFO device <udid> back online via tunneld ...` | chỉ với `--tunnel`; upstream tự nối lại stream, không phát lại input |
| `WARNING|ERROR shutdown: <bước> timed out after|raised` | một bước cleanup không xong |
| `INFO shutdown complete` | cleanup đã chạy hết |
| `ERROR ...` từ `pymobiledevice3.__main__` | lỗi gây thoát, ví dụ thông báo camera/micro đang dùng |

Khởi động: `serve()` gọi `_eager_stream_start` (audio trước, video sau, tối đa 25 s) trước khi mở listener. Nếu thiết bị trả lỗi camera/micro đang dùng (mã 9022), tiến trình thoát với `MEDIA_IN_USE_MESSAGE` và không mở listener.

Dừng: trên Windows `add_signal_handler` không có, nên Ctrl+C đi qua `KeyboardInterrupt`; với `d700d8e` nó hủy lệnh async thay vì ném vào giữa tác vụ. Nhánh `finally` của `serve()` lần lượt: đóng listener, hủy handler kết nối, dừng HID (tối đa 3 s), dừng video và audio (mỗi bước 3 s), `DisplayService.stop_all_streams` (6 s, bước duy nhất báo daemon thiết bị trả camera/micro và tắt chỉ báo chia sẻ màn hình), dừng clipboard monitor và accessibility (mỗi bước 3 s), hủy tác vụ còn lại (2 s), rồi log `shutdown complete`. Tổng các giới hạn khoảng 25 s cộng thời gian tunnel đóng, nên probe chờ 40 s sau Ctrl+C. Kill tiến trình bỏ qua toàn bộ chuỗi này.

## 7. Âm thanh

- `--no-audio` chỉ đổi mặc định của viewer (`audio_default_on=False`, người dùng vẫn bấm Enable Sound được). Nó không tắt phiên audio phía thiết bị.
- `_eager_stream_start` luôn mở audio trước video: docstring ghi phiên audio là tín hiệu còn sống của phiên, giống Xcode Mirror, và chạy dù không có ai nghe `/audio.bin`.
- serve-web mở phiên audio ngay khi khởi động, kể cả với `--no-audio` (đó là tín hiệu phiên còn sống với media manager của thiết bị). Nó tự giải mã AAC-ELD bằng AudioToolbox qua ctypes (`aac_eld.py`) rồi phát PCM s16le qua `/audio.bin`. Trên Windows bộ giải mã không mở được: `_audio_udp_recv` log `ERROR AudioToolbox AAC-ELD decoder failed to open` lúc khởi động và `/audio.bin` trả lỗi; hình không bị ảnh hưởng. Probe vẫn truyền `--no-audio` để viewer không tự yêu cầu `/audio.bin`.
- Hệ quả cần thử: khi phiên chạy, âm thanh thiết bị và âm thanh AirPlay của MacExtend có thể bị chuyển hướng hoặc tắt. Chưa sửa audio upstream trong đợt này.

## 8. Camera và micro

`MEDIA_IN_USE_MESSAGE`: phiên mirror không mở khi ứng dụng phía trước giữ camera hoặc micro. Ngược lại, khi phiên đang chạy, ứng dụng dùng camera/micro có thể nhận hình đen hoặc âm thanh câm. Giữ AirPlay để hiển thị và chỉ dùng CoreDevice để điều khiển không tránh được điều này, vì kênh chạm cần phiên media CoreDevice đang mở. Process đã dừng hoặc cổng đã nhả không chứng minh media đã được trả; phải kiểm tra trên thiết bị (runbook ở `tools/coredevice/README.md`).
