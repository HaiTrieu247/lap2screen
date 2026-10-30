# CoreDevice probe: kết quả thử nghiệm

- Cập nhật: 2026-10-29.
- Phép thử định nghĩa ở [docs/TESTING.md](../TESTING.md), *Thử nghiệm C*. Cách chạy ở [tools/coredevice/README.md](../../tools/coredevice/README.md). Hợp đồng upstream ở [2026-10-29-coredevice-probe.md](2026-10-29-coredevice-probe.md).
- Lượt thiết bị đầu tiên (lượt 1, 2026-10-29) chỉ chạy cột *iPhone USB userspace*; các ô khác là **Chưa chạy**. HTTP 200, cổng đã nhả hoặc cây tiến trình đã hết không thay thế kiểm tra trên thiết bị.

## Kiểm tra không cần thiết bị

| Phép thử | Nơi chạy | Kết quả |
|---|---|---|
| Unit test Core (tham số, môi trường, client, log) | macOS, .NET 10 | Đạt, 2026-10-29 |
| Build probe net48 | macOS, .NET 10 SDK | Đạt (chỉ biên dịch) |
| `setup.ps1` hai lần, `pip check`, phiên bản, `serve-web --help` | job *CoreDevice probe*, windows-latest | Đạt, 2026-10-29: pymobiledevice3 11.47.0, CPython 3.12 x64 |
| Unit test Core, build solution, build probe net48 | job *CoreDevice probe*, windows-latest | Đạt, 2026-10-29 |
| Smoke probe net48 với serve-web giả, venv thật | job *CoreDevice probe*, windows-latest | Đạt, 2026-10-29: cả 19 kịch bản trong `SCENARIOS` |

Lượt CI: [run 40826987621](https://github.com/HaiTrieu247/lap2screen/actions/runs/40826987621), commit 6430ad9 trên `beta/bluetooth`, chạy tay (`workflow_dispatch`). Artifact `coredevice-probe-6430ad9`.

## Môi trường của mỗi lượt

Ghi cho mỗi lượt: Windows build, Python và mã commit của lock, model thiết bị và iOS/iPadOS, Apple service (iTunes cổ điển hay Apple Devices), trình duyệt và phiên bản, GPU và driver, transport, thời gian tới *sẵn sàng*. Không ghi UDID, tên thiết bị, địa chỉ mạng.

### Lượt 1, 2026-10-29

| Mục | Giá trị |
|---|---|
| Windows | Windows 11 (chưa ghi build) |
| Python, lock | CPython 3.12 x64 qua Python install manager (`pythoncore-3.12-64`), cài song song với 3.14 mặc định của máy; venv và probe từ artifact `coredevice-probe-6430ad9` |
| Thiết bị | iPhone 11, iOS 27.2 |
| Apple service | chưa ghi |
| Trình duyệt | Edge, Chrome (chưa ghi phiên bản) |
| GPU | NVIDIA RTX 3050 (chưa ghi driver) |
| Transport | USB userspace |
| Codec probe báo | `hev1.1.6.L123.B0` |
| Tới *sẵn sàng* | chưa ghi |
| Cột *iPhone USB tunneld* | `pmd3 remote tunneld --no-wifi --no-mobdev2` trong PowerShell quản trị; card tunnel `pymobiledevice3-tunnel-<UDID>` là Public. Cần sửa firewall theo README (*Firewall khi dùng tunnel*) mới có hình |
| Cột *iPhone Wi-Fi* | Laptop và iPhone cùng mạng (mạng chia sẻ từ Mac). Đường mobdev2: Bonjour thấy iPhone, Apple service thì không; tunneld ghép nối được nhưng iPhone đóng CoreDeviceProxy ở bước bắt tay (`ConnectionTerminatedError`), cả với `lockdown start-tunnel --mobdev2`. Đường chạy được: RemotePairing. `lockdown remotepairing --pair` qua USB, rồi tunneld `--no-usb --no-usbmux --no-mobdev2` chạy trên một venv Python 3.13 riêng (tunnel TCP); probe và serve-web vẫn trên venv 3.12 (chi tiết ở README, mục Wi-Fi) |
| Media server trong phiên | `get-media-stream-server-status` khi probe đang chạy: `running: true`, hai session. Video từ display 1, 832×1792, 60 fps, 333 kbit/s–6 Mbit/s; audio `audioSystemOutput`. Mỗi session có `RTCPTimeoutInterval` 20 s |

## Ma trận

Mỗi ô ghi Đạt / Một phần / Không đạt / Chưa chạy, kèm ghi chú ngắn. Cột Wi-Fi dùng tunneld.

| # | iPhone USB userspace | iPhone USB tunneld | iPhone Wi-Fi | iPad USB userspace | iPad USB tunneld | iPad Wi-Fi |
|---|---|---|---|---|---|---|
| C1 serve-web trực tiếp, hình HEVC (Edge, Chrome) | Đạt (lượt 1): có hình trên Edge và Chrome | Đạt (lượt 1) sau khi sửa firewall: lúc đầu viewer không có hình vì Windows Firewall chặn RTP/UDP trên card tunnel Public (`DROP UDP` trong pfirewall.log, luật Block Public cho Python 3.12); sửa xong lên hình ngay | Đạt (lượt 1, RemotePairing): sau khi đường mobdev2 thất bại | Chưa chạy | Chưa chạy | Chưa chạy |
| C2 chạm, giữ, kéo, Home qua viewer | Đạt (lượt 1) | Đạt (lượt 1) | Đạt (lượt 1) | Chưa chạy | Chưa chạy | Chưa chạy |
| C3 nhả khi rời hình, mất focus, đổi tab | Đạt (lượt 1): thả ngoài hình, Alt+Tab, Ctrl+Tab, Win+D khi đang giữ chuột; giữ phím rồi Alt+Tab | Đạt (lượt 1) | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |
| C4 bàn phím, phím tắt | Đạt (lượt 1); chưa ghi phím tắt nào bị Windows giữ lại | Đạt (lượt 1) | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |
| C5 tiếng Việt (UniKey/EVKey tắt), clipboard | Đạt (lượt 1): Telex trên iOS, *Send to device* và *Get from device*; chưa khảo sát khi bật UniKey | Đạt (lượt 1) | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |
| C6 xoay màn hình qua viewer | Đạt (lượt 1): xoay máy và nút xoay của viewer, chạm đúng ở dọc và ngang | Đạt (lượt 1) | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |
| C7 camera, ghi âm, cuộc gọi video (hai thứ tự) | Một phần (lượt 1, serve-web trực tiếp): Camera mở trước thì serve-web từ chối với `The device's camera or microphone is in use by another app`; serve-web chạy trước thì Camera hình đen, không báo lỗi. Sau khi dừng, Camera và micro trở lại bình thường. Chưa ghi Ghi âm và cuộc gọi video trong phiên | Một phần (lượt 1): Camera đang mở thì probe từ chối chạy; trong phiên Camera và Ghi âm không dùng được, dừng là có lại. Chưa ghi cuộc gọi video | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |
| C8 loa/tai nghe và âm thanh AirPlay trước/trong/sau | Đạt (lượt 1): trước, trong và sau phiên âm thanh vẫn ra iPhone; khi chạy cùng AirPlay, âm thanh ra laptop qua AirPlay. Chưa ghi *Enable Sound* của viewer | Đạt (lượt 1) | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |
| C9 lệnh API probe: tap, hold, drag, key, release, home | Đạt (lượt 1): cả sáu lệnh, hướng dọc | Đạt (lượt 1) | Đạt (lượt 1) | Chưa chạy | Chưa chạy | Chưa chạy |
| C10 media được trả sau `stop` | Đạt (lượt 1): sau `stop` của probe, Camera và micro hoạt động lại; chưa lưu output `get-media-stream-server-status` | Đạt (lượt 1): Camera và micro có lại ngay sau `stop` | Đạt (lượt 1): Camera và micro có lại ngay | Chưa chạy | Chưa chạy | Chưa chạy |
| C11 media sau Ctrl+C | Đạt (lượt 1): dừng rất nhanh, Camera và micro hoạt động lại. Probe dừng xong trước khi bấm kịp Ctrl+C thứ hai, nên không thấy `already stopping`; nhánh đó có trong smoke test `repeated_ctrl_c` và `ctrl_c_during_stop` | Đạt (lượt 1): Camera và micro có lại ngay sau Ctrl+C; tunneld vẫn chạy | Đạt (lượt 1): Camera và micro có lại ngay | Chưa chạy | Chưa chạy | Chưa chạy |
| C12 media sau kill cưỡng bức và probe bị đóng | Đạt (lượt 1): kill bằng Task Manager, đóng cửa sổ probe, và hết 40 giây (serve-web bị suspend bằng Resource Monitor). Windows kill cả cây tiến trình; Camera và micro có lại ngay lập tức | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |
| C13 mất kết nối, kết nối lại | Đạt (lượt 1): rút cáp thì chạy lại probe là dùng được; khóa rồi mở khóa iPhone thì hình tự quay lại, không cần chạy lại probe; không có thao tác bị gửi lại, K bình thường | Đạt (lượt 1): rút rồi cắm lại cáp, hình tự quay lại không cần chạy lại probe | Đạt (lượt 1): tắt rồi bật Wi-Fi trên iPhone, hình tự quay lại | Chưa chạy | Chưa chạy | Chưa chạy |
| C14 độ trễ (ít nhất 20 thao tác) | Một phần (lượt 1): cảm nhận độ trễ không đáng kể; chưa đo trung vị/lớn nhất | Một phần (lượt 1): cảm nhận như userspace; chưa đo | Một phần (lượt 1): cảm nhận đạt; chưa đo | Chưa chạy | Chưa chạy | Chưa chạy |
| C15 chạy cùng AirPlay của MacExtend | Đạt (lượt 1): AirPlay và CoreDevice chạy song song, chạm qua viewer được, âm thanh ra laptop qua AirPlay, AirPlay vẫn chạy sau khi dừng probe | Đạt (lượt 1) | Đạt (lượt 1) | Chưa chạy | Chưa chạy | Chưa chạy |

## Kết luận hiện tại

Chưa đủ bằng chứng để quyết định thay Bluetooth. Lượt 1 (iPhone 11, iOS 27.2) đã chạy hết cột *iPhone USB userspace*: mọi hàng đạt, trừ C7 và C14 là một phần. Hình HEVC trên Edge và Chrome, chạm qua viewer và qua probe, bàn phím, tiếng Việt, clipboard, xoay, kết nối lại và chạy cùng AirPlay đều ổn; độ trễ cảm nhận không đáng kể nhưng chưa đo. C7 xác nhận giới hạn đã biết: trong phiên, Camera trên thiết bị cho hình đen, và phiên không mở được khi Camera đang mở; sau khi dừng serve-web trực tiếp, `stop` (C10), Ctrl+C (C11) hoặc kill cưỡng bức (C12), Camera và micro đều trở lại ngay. Như vậy không dùng được camera của thiết bị trong lúc điều khiển bằng CoreDevice, điều cần cân nhắc khi so với Bluetooth.

Với userspace, tunnel nằm trong tiến trình serve-web nên bị kill cùng nó, và media có lại ngay lập tức. Giả thuyết, chưa kiểm chứng: thiết bị hủy stream ngay khi tunnel đóng. Với tunneld, tunnel còn sống sau khi serve-web chết, nên có thể thiết bị chỉ hủy stream khi mất RTCP quá `RTCPTimeoutInterval` 20 s; phải thử riêng C12 ở cột tunneld và ghi media có lại sau bao lâu. Lượt 1 bỏ qua C12 ở cột này, nên giả thuyết vẫn chưa kiểm chứng.

Với tunneld trên Windows, phải sửa firewall thì mới có hình (C1 cột tunneld).

Qua Wi-Fi, đường lockdown (mobdev2) không mở được tunnel: iPhone đóng dịch vụ CoreDeviceProxy ngay ở bước bắt tay. Đường RemotePairing (cách Xcode dùng) thì chạy được: các hàng đã thử ở cột Wi-Fi đều đạt, Camera và micro có lại ngay sau khi dừng. Cái giá là tunneld phải chạy bằng Python 3.13 trở lên với quyền quản trị, cần sửa firewall như cột tunneld, và phải tạo pair record RemotePairing qua USB một lần.

Màn hình khóa (quan sát trong lượt 1, chưa chạy thành bài thử riêng): viewer theo đúng màn hình thật. iPhone khóa thì viewer tối; mở khóa trên máy thì hình quay lại. Như vậy CoreDevice không điều khiển được khi màn hình thiết bị tắt, khác iPhone Mirroring của macOS. Theo mã nguồn, serve-web giữ một power assertion `PreventUserIdleSystemSleep` (gia hạn mỗi 2 phút) để thiết bị không tự khóa giữa phiên; điều này chưa kiểm chứng trên máy.

Hướng nghiên cứu sau (người dùng yêu cầu, 2026-10-30): bỏ phụ thuộc Apple Mobile Device Service của iTunes/Apple Devices, ví dụ bằng driver USB riêng và usbmuxd mã nguồn mở; và bỏ Developer Mode, việc này nhiều khả năng không làm được, vì đây là cổng của iOS cho mọi dịch vụ lập trình viên.

Còn thiếu: số đo C14; Ghi âm và cuộc gọi video ở C7; C12 ở cột tunneld; C3–C8 và C12 ở cột Wi-Fi; các cột iPad.
