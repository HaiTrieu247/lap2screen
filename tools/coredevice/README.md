# CoreDevice: runtime của app và probe cho người phát triển

Thư mục này có hai việc. Một là runtime Python (CPython 3.13 + `pymobiledevice3==11.47.0`) mà tính năng *Điều khiển trực tiếp* trong app MacExtend dùng, cùng các script build và test của nó trên CI. Hai là **probe**, một công cụ console riêng để người phát triển thử điều khiển iPhone/iPad bằng chạm thật qua dịch vụ CoreDevice (`pymobiledevice3 developer core-device display serve-web`), tách khỏi app để tách lỗi upstream. Probe không nằm trong app, installer hay release. Cách dùng tính năng trong app xem [README chính](../../README.md); phần còn lại của tài liệu này là về probe.

App và probe dùng chung cùng một runtime và cùng mã chạy tiến trình ở `src/MacExtend.CoreDeviceHost` (khởi động serve-web, một Ctrl+C có hạn 40 giây, dọn tiến trình).

- CoreDevice là dịch vụ lập trình viên của Apple được cộng đồng dịch ngược, không phải giao thức iPhone Mirroring công khai. Phiên bản được khóa ở `pymobiledevice3==11.47.0`; hợp đồng đã đọc từ mã nguồn ở [docs/research/2026-10-29-coredevice-probe.md](../../docs/research/2026-10-29-coredevice-probe.md).
- Trạng thái: mã và CI không cần thiết bị đã có; probe đã chạy trên thiết bị thật ở lượt 1, còn phần trong app chưa kiểm chứng (bảng D trong [docs/TESTING.md](../../docs/TESTING.md)). Kết quả ghi ở [docs/research/2026-10-29-coredevice-results.md](../../docs/research/2026-10-29-coredevice-results.md).
- Trong lúc phiên chạy, camera và micro trên thiết bị có thể không dùng được (hình đen, tiếng câm); phiên cũng không mở được khi ứng dụng phía trước đang giữ camera/micro.

## Thành phần

| Đường dẫn | Vai trò |
|---|---|
| `requirements.in` | phiên bản duy nhất được hỗ trợ: `pymobiledevice3==11.47.0` |
| `requirements-win-x64-py313.lock.txt` | toàn bộ dependency cho CPython 3.13 x64 Windows, mỗi gói có hash |
| `compile-lock.ps1` | tạo lại lock bằng pip-tools 7.6.2 trên Windows Python 3.13 x64 |
| `setup.ps1` | tạo `tools\coredevice\.venv` từ lock và kiểm tra nó (chỉ cho probe) |
| `runtime-python.txt` | phiên bản và SHA-256 của gói CPython nuget.org dùng làm runtime của app |
| `build-runtime.ps1` | dựng runtime của app: CPython 3.13 từ nuget.org + lock, thư mục `python\python.exe` và `RUNTIME_ID` |
| `verify-runtime.ps1`, `check_runtime.py` | kiểm tra runtime sau khi chép sang thư mục có dấu cách; `check_runtime.py` chạy `pip check` tương đương ngay trong runtime |
| `test-setup.ps1` | CI (runner quản trị): các chế độ nâng quyền của `MacExtend.exe` với zip runtime thật, tác vụ, luật tường lửa, tunneld khởi động đúng dòng lệnh của tác vụ |
| `test-uninstall.ps1` | CI: bộ cài thật, thiết lập thật, rồi trình gỡ thật phải xóa tác vụ, luật và thư mục `coredevice` |
| `spike-task.ps1` | thử một lần xem tài khoản thường có chạy được tác vụ quyền cao đã đăng ký sẵn không (hàng D1 trong TESTING.md) |
| `test-probe.ps1`, `tests\` | smoke test probe net48 với serve-web giả, không cần thiết bị |
| `../MacExtend.CoreDeviceProbe/` | probe: chạy serve-web, quản lý tiến trình, gửi thao tác thử |

## Cài đặt trên Windows

1. Cài CPython 3.13 x64 từ python.org (không cần thêm vào PATH).
2. Tạo runtime. Chạy lại lệnh này dùng lại cùng venv và không cài gì ngoài lock; Python hệ thống không bị sửa.

   ```powershell
   .\tools\coredevice\setup.ps1 -Python "$env:LOCALAPPDATA\Programs\Python\Python313\python.exe"
   ```

   Script cài bằng `pip --require-hashes --no-deps --only-binary :all:`, chạy `pip check`, kiểm tra phiên bản 11.47.0 và `serve-web --help` có đủ tùy chọn probe dùng.
3. Lấy probe: tải artifact `coredevice-probe-<sha>` của job *CoreDevice probe* trong GitHub Actions (artifact này không nằm trong release), hoặc tự build `dotnet build tools/MacExtend.CoreDeviceProbe -c Release`.

### Tạo lại lock

```powershell
pwsh tools/coredevice/compile-lock.ps1 -Python "$env:LOCALAPPDATA\Programs\Python\Python313\python.exe"
```

Chỉ chạy trên Windows: lock tạo trên hệ khác sẽ theo dependency marker của hệ đó. Script cài pip-tools 7.6.2 vào venv tạm rồi chạy `python -m piptools compile --generate-hashes --resolver=backtracking --allow-unsafe --strip-extras --pip-args --only-binary=:all:`. Lock đang commit được tạo bằng `uv pip compile` nhắm Windows x64/Python 3.13 (dòng đầu file ghi uv); nó chỉ được coi là đã xác minh khi job *CoreDevice probe* trên `windows-latest` cài và kiểm tra đạt. Sau khi tạo lại lock, chạy `setup.ps1` và smoke test trước khi commit.

## Runtime của app

App không dùng venv (venv ghi đường dẫn tuyệt đối nên không chuyển được). `build-runtime.ps1` giải nén gói [`python`](https://www.nuget.org/packages/python) chính thức của nuget.org (CPython 3.13 đầy đủ, `tools\`) rồi cài thẳng lock vào `Lib\site-packages` bằng pip của Python build (`--require-hashes --no-deps --only-binary :all:`), biên dịch trước `.pyc` kiểu unchecked-hash để bản trong Program Files (chỉ đọc) không phải biên dịch lại mỗi lần chạy. Kết quả là `<Out>\python\python.exe` (cùng `pythonw.exe`) và `<Out>\RUNTIME_ID`. `verify-runtime.ps1` chép nó sang một thư mục có dấu cách rồi kiểm tra phiên bản, `check_runtime.py` và `serve-web --help`.

CI (`.github/workflows/build.yml`) chạy hai script này ở job `coredevice-runtime`, nén thành `MacExtend-<bản>-coredevice-runtime.zip` trong release, ghi `coredevice.id`, `coredevice.sha256` vào bản kê cập nhật, rồi chạy `test-setup.ps1` và `test-uninstall.ps1` ở job `app`. Runtime không nằm trong bộ cài: app tải gói này ở lần thiết lập đầu và kiểm tra SHA-256.

Chạy thử script build trên Windows có Python 3.13 x64:

```powershell
.\tools\coredevice\build-runtime.ps1 -Python "$env:LOCALAPPDATA\Programs\Python\Python313\python.exe" -Out build\coredevice-runtime
.\tools\coredevice\verify-runtime.ps1 -Runtime build\coredevice-runtime
```

Đổi phiên bản:
- CPython: sửa cả hai dòng `version` và `sha256` trong `runtime-python.txt` (script từ chối gói có SHA-256 khác), và dùng cùng minor 3.13 cho Python build.
- pymobiledevice3: sửa `requirements.in`, tạo lại lock (mục *Tạo lại lock*), sửa `$RequiredVersion` trong `setup.ps1`, `REQUIRED` trong `check_runtime.py` và hằng `RequiredVersion` trong `src/MacExtend.Core/CoreDevice/CoreDeviceOptions.cs`. Lặp lại toàn bộ bảng C và D của TESTING.md trước khi đổi.

## Chạy probe

```powershell
$py = (Resolve-Path .\tools\coredevice\.venv\Scripts\python.exe).Path
.\MacExtend.CoreDeviceProbe.exe --python $py --udid <UDID> [--transport userspace|tunnel] [--port 8080]
```

- Probe kiểm tra phiên bản pymobiledevice3 của chính interpreter đó, từ chối nếu khác 11.47.0, và từ chối nếu cổng đã có tiến trình khác dùng. Nó không bao giờ gắn vào một serve-web có sẵn.
- serve-web chỉ nghe `127.0.0.1`. UDID truyền qua `PYMOBILEDEVICE3_UDID`; các biến `PYMOBILEDEVICE3_*` khác bị xóa.
- *Sẵn sàng* nghĩa là: tiến trình còn sống, đã có dòng `Open http://127.0.0.1:<port>/`, `GET /` trả trang viewer và listener thuộc đúng cây tiến trình của probe. Tối đa 120 giây. Sẵn sàng **không** chứng minh hình HEVC giải mã được hay chạm đã tới thiết bị.
- Probe không tự mở viewer và không tự gửi thao tác.

Lệnh trong probe:

| Lệnh | Tác dụng |
|---|---|
| `status` | trạng thái, tiến trình trong job, phím/chạm probe cho là đang giữ |
| `open` | mở viewer bằng trình duyệt mặc định |
| `tap X Y` | một lần chạm |
| `hold X Y [MS]` | nhấn giữ, mặc định 1000 ms, tối đa 9000 |
| `drag X1 Y1 X2 Y2 [MS]` | nhấn, kéo thẳng, nhấc; mặc định 600 ms, tối đa 9000 |
| `key USAGE [USAGE ...]` | nhấn cùng lúc các HID usage trong 100 ms (thập phân hoặc 0x, 0x04–0xE7) |
| `release` | nhấc ngón và nhả mọi phím |
| `home` | nút Home |
| `stop` | dừng serve-web: một Ctrl+C, chờ tối đa 40 giây cho cleanup |
| `quit` | dừng nếu cần rồi thoát |
| Ctrl+C | như `quit`, kể cả khi `stop` đang chạy |

X, Y là tọa độ HID 0..65535 trên màn hình thiết bị ở hướng dọc (`32768 32768` là giữa màn hình). Lệnh API chỉ thử ở hướng dọc; xoay thì thử qua viewer.

`hold` và `drag` chạy theo đồng hồ: ngón tay nhấc sau đúng MS, và khi serve-web trả lời chậm, `drag` bỏ bớt bước di chuyển (tối đa một bước mỗi 16 ms) chứ không kéo dài thao tác. Cả thao tác có hạn MS + 1 giây, nên không quá 10 giây. Quá hạn thì probe cắt thao tác, nhấc ngón với hạn riêng 2 giây và báo `cut at its deadline`; nếu lúc đó còn request đang chờ trả lời, phiên không nhận thao tác mới nữa.

Viewer và lệnh API đều gửi thao tác, và probe không thấy thao tác của viewer. Đóng tab viewer trước khi dùng `tap`/`hold`/`drag`/`key`/`release`/`home`, và ngược lại.

HTTP 200 từ serve-web chỉ có nghĩa thao tác đã vào hàng đợi. Probe gửi tuần tự, không gửi lại. Khi một request lỗi hoặc quá hạn, probe coi chạm/phím có thể vẫn đang giữ, thử nhả một lần với hạn riêng rồi không nhận thao tác mới trong phiên đó; dừng và chạy lại probe. Probe không ghi phím, clipboard hay output thô của serve-web; chỉ một số dòng trạng thái đã lọc.

### Dừng

- `stop`, `quit`, Ctrl+C trong cửa sổ probe, đóng stdin hoặc khởi động thất bại đều đi qua cùng một trình tự: khóa thao tác mới, nhả (tối đa 2 giây), gửi **đúng một** Ctrl+C vào console riêng của serve-web, chờ tối đa 40 giây, báo tiến độ mỗi 5 giây. Ctrl+C lần nữa trong lúc đang dừng không gửi thêm tín hiệu. Ctrl+C luôn thoát probe khi việc dừng xong, kể cả khi việc dừng do lệnh `stop` bắt đầu; sau `stop`, menu vẫn dùng được (`status`, `quit`).
- Hết 40 giây hoặc gửi tín hiệu lỗi: probe kill cả cây tiến trình và báo **chưa xác nhận đã trả camera/micro**. Đóng cửa sổ probe hoặc probe bị kill thì Windows kill cả cây (Job Object), cũng không có cleanup.
- Probe chỉ báo *stopped* khi cây tiến trình đã hết và cổng đã nhả. Điều đó không chứng minh thiết bị đã trả camera/micro: luôn kiểm tra theo mục *Sau mỗi lần dừng*.

## Runbook trên thiết bị

Mọi lệnh dưới đây chạy bằng interpreter của venv:

```powershell
$py = (Resolve-Path .\tools\coredevice\.venv\Scripts\python.exe).Path
function pmd3 { & $py -I -X utf8 -m pymobiledevice3 @args }
```

### 1. Một lần cho mỗi thiết bị

1. Cắm USB, mở khóa thiết bị. Windows cần Apple Mobile Device Service: cài iTunes bản cổ điển (bản tải từ apple.com). Service của Apple Devices/iTunes trên Microsoft Store không hỗ trợ đường phát hiện qua Wi-Fi mà pymobiledevice3 dùng.
2. `pmd3 usbmux list` để lấy UDID. Ghép nối: `pmd3 lockdown pair --udid <UDID>`, bấm **Tin cậy** trên thiết bị.
3. Developer Mode: `pmd3 amfi reveal-developer-mode --udid <UDID>` để hiện tùy chọn, rồi bật ở **Cài đặt > Quyền riêng tư & Bảo mật > Chế độ nhà phát triển** (thiết bị khởi động lại), hoặc `pmd3 amfi enable-developer-mode --udid <UDID>`. Kiểm tra: `pmd3 mounter query-developer-mode-status --udid <UDID>`.

### 2. Sau mỗi lần thiết bị khởi động lại

`pmd3 mounter auto-mount --udid <UDID>`. Lệnh thành công cả khi ảnh đã mount, nên có thể chạy mỗi lần trước khi thử.

### 3. Chọn transport

- **userspace** (mặc định, iOS 17.4+ qua USB, không cần quyền quản trị): tunnel nằm trong tiến trình serve-web. Mất kết nối thì serve-web không tự phục hồi: dừng probe và chạy lại.
- **tunnel** (iOS 17.0–17.3.1 trên Windows, hoặc khi thử Wi-Fi): mở PowerShell **với quyền quản trị** và chạy `pmd3 remote tunneld` (nghe 127.0.0.1:49151). Mặc định nó dò cả USB lẫn Wi-Fi; chỉ USB thì thêm `--no-wifi --no-mobdev2`. Kiểm tra tunnel bằng `Invoke-RestMethod http://127.0.0.1:49151/`: qua USB thì `interface` là `usbmux-<UDID>-USB`. Giữ cửa sổ đó mở, sửa firewall như mục dưới, rồi chạy probe với `--transport tunnel`. Khi thiết bị rớt, serve-web log *disconnected; waiting for tunneld* rồi *back online via tunneld* và tự nối lại hình; chạm/phím đang dở không được gửi lại. Probe không tự cài, nâng quyền hay quản lý tunneld.

#### Firewall khi dùng tunnel

Với tunnel, thiết bị gửi hình (RTP qua UDP) tới card mạng ảo `pymobiledevice3-tunnel-<UDID>`, và Windows xếp card này là **Public**. serve-web không gửi gì cho thiết bị trước gói hình đầu tiên, nên firewall coi đó là gói vào không được yêu cầu. Nếu Python có luật Block cho Public (Windows tự tạo khi hộp thoại firewall chỉ tick Private), serve-web vẫn chạy và chạm vẫn tới thiết bị nhưng viewer không có hình. Userspace không bị, vì hình đi trong tiến trình. Đã gặp ở lượt 1. Trong app, bước thiết lập tự tạo các luật này (nhóm `MacExtend CoreDevice`, 4 luật cho `python.exe` và `pythonw.exe` của runtime); phần dưới là cho probe chạy bằng venv riêng.

- Kiểm tra (quản trị): `Set-NetFirewallProfile -All -LogBlocked True`, chạy serve-web và mở viewer, rồi `Select-String "$env:SystemRoot\System32\LogFiles\Firewall\pfirewall.log" -Pattern 'DROP UDP fd'`. Có dòng tức là firewall đang chặn. Xong thì `Set-NetFirewallProfile -All -LogBlocked False`.
- Sửa (quản trị). `$base` là đúng đường dẫn đã truyền cho `setup.ps1 -Python`: tiến trình nhận UDP là interpreter gốc, không phải `.venv\Scripts\python.exe`. Block luôn thắng Allow, nên tắt luật Block inbound của interpreter đó, rồi chỉ cho UDP từ dải địa chỉ tunnel `fc00::/7`:

  ```powershell
  $base = '<đường dẫn đã truyền cho setup.ps1 -Python>'
  Get-NetFirewallApplicationFilter | Where-Object Program -eq $base | Get-NetFirewallRule | Where-Object { $_.Direction -eq 'Inbound' -and $_.Action -eq 'Block' } | Disable-NetFirewallRule
  New-NetFirewallRule -DisplayName 'CoreDevice probe: Python UDP from tunnel' -Direction Inbound -Action Allow -Protocol UDP -RemoteAddress 'fc00::/7' -Program $base -Profile Public
  ```

  Mặc định Public đã chặn mọi gói vào không được yêu cầu, nên tắt luật Block không mở thêm gì; thứ được mở chỉ là UDP từ `fc00::/7` tới interpreter đó. Một số mạng công cộng cũng dùng địa chỉ `fd..`, nên gỡ luật khi thử xong: `Remove-NetFirewallRule -DisplayName 'CoreDevice probe: Python UDP from tunnel'`, rồi bật lại các luật Block bằng lệnh trên với `Enable-NetFirewallRule`.
- Đổi card tunnel sang Private (`Set-NetConnectionProfile -NetworkCategory Private`) chỉ có tác dụng tới khi tunnel được tạo lại: mỗi tunnel mới là một mạng Public mới.

### 4. Thứ tự thử

1. Chạy serve-web trực tiếp qua USB trước, không qua probe, để tách lỗi upstream:

   ```powershell
   $env:PYMOBILEDEVICE3_UDID = '<UDID>'
   pmd3 developer core-device display serve-web --bind 127.0.0.1 --http-port 8080 --no-audio --userspace
   ```

   Mở `http://127.0.0.1:8080/` bằng Edge rồi Chrome; ghi viewer có hình không (HEVC qua WebCodecs), chạm, giữ, kéo, phím. Dừng bằng **một** Ctrl+C và chờ đến dòng `shutdown complete`. Xóa biến sau đó: `Remove-Item Env:PYMOBILEDEVICE3_UDID`.
2. Thử camera/micro ngay (bảng C trong [docs/TESTING.md](../../docs/TESTING.md)) trước khi đo các thứ khác.
3. Lặp lại bằng probe, rồi tới tunneld, Wi-Fi, kết nối lại và chạy cùng AirPlay của MacExtend.

### 5. Wi-Fi

Đường chạy được là RemotePairing, cách Xcode dùng cho "Connect via network". Đường lockdown qua Wi-Fi (mobdev2) không dùng được: tunneld tìm và ghép nối được thiết bị, nhưng thiết bị đóng dịch vụ CoreDeviceProxy ngay ở bước bắt tay đầu tiên (`ConnectionTerminatedError`; lượt 1, iOS 27.2).

1. Qua USB, tạo pair record RemotePairing một lần (không hiện hộp thoại Tin cậy): `pmd3 lockdown remotepairing --pair --udid <UDID>`. File `remote_<...>.plist` nằm trong `%USERPROFILE%\.pymobiledevice3`.
2. Rút cáp, để thiết bị và laptop **cùng một mạng Wi-Fi** (kiểm tra tên mạng ở cả hai bên). Kiểm tra thiết bị có quảng bá không: `pmd3 bonjour remotepairing --timeout 15`. Trên Windows, Bonjour của pymobiledevice3 mặc định chỉ chờ 2 giây.
3. Tunneld phải chạy bằng **Python 3.13 trở lên**. Từ iOS 18.2, thiết bị bỏ tunnel QUIC (`QuicProtocolNotSupportedError`) và dùng tunnel TCP với TLS-PSK, có sẵn trong `ssl` của Python 3.13 (Python 3.12 không có). Runtime 3.13 dùng chung cho serve-web và tunneld; không cần venv riêng. Mở PowerShell **với quyền quản trị** rồi chạy tunneld bằng interpreter của venv do `setup.ps1` tạo:

   ```powershell
   & $py -I -X utf8 -m pymobiledevice3 -v remote tunneld --no-usb --no-usbmux --no-mobdev2
   ```

   Trong tunneld, `--wifi` là phần dò RemotePairing; `--no-usb --no-usbmux --no-mobdev2` chỉ để log gọn. Chờ `Created tunnel --rsd ...` của task `start-tunnel-task-wifi-...`. Khi Windows hỏi quyền mạng cho Python, cho phép Private.
4. Probe và serve-web chạy với `--transport tunnel`. Chúng chỉ hỏi tunneld qua `127.0.0.1:49151` rồi đi qua card tunnel của hệ điều hành, nên vẫn cần sửa firewall như mục *Firewall khi dùng tunnel*.

Trong app, việc này do app làm: bản cài vào Program Files có một tác vụ chạy `pythonw.exe` của runtime với quyền cao nhất (app chỉ bật nó, không nâng quyền), các bản khác hỏi UAC một lần mỗi phiên Wi-Fi. Tunneld của app tắt bằng `GET http://127.0.0.1:49151/shutdown`.

Đường lockdown cũ, để so sánh: `pmd3 lockdown wifi-connections on --udid <UDID>` qua USB, rồi `pmd3 bonjour mobdev2`. Apple service (`pmd3 usbmux list --network`) không thấy thiết bị qua Wi-Fi ở lượt 1.

### 6. Sau mỗi lần dừng

Làm sau **mỗi** kiểu dừng: `stop`, Ctrl+C, kill cưỡng bức (hết 40 giây hoặc kill bằng Task Manager), probe bị đóng.

1. Trạng thái media server, cùng transport và thiết bị vừa dùng:

   ```powershell
   $env:PYMOBILEDEVICE3_UDID = '<UDID>'; pmd3 developer core-device display get-media-stream-server-status --userspace
   # hoặc: pmd3 developer core-device display get-media-stream-server-status --tunnel <UDID>
   ```

   Lưu output vào `tools/coredevice/results/` (đã ignore) sau khi xóa UDID, tên thiết bị và địa chỉ mạng.
2. Trên thiết bị, mở Camera (có hình không) và Ghi âm (ghi 5 giây, phát lại có tiếng không). Kiểm tra chỉ báo chia sẻ màn hình đã tắt.
3. Chỉ status hoặc chỉ probe báo stopped không chứng minh đã phục hồi; cần cả bước 2.
4. Nếu camera/micro còn bị giữ: chạy lại serve-web với cùng thiết bị và transport, đợi sẵn sàng, rồi dừng đúng cách bằng một Ctrl+C và kiểm tra lại. Khởi động lại thiết bị là bước cuối cùng.

## Tiếng Việt và clipboard

Viewer gửi phím vật lý (`e.code`), không gửi văn bản Windows đã ghép. Khi thử: tắt UniKey/EVKey trên Windows và chọn bàn phím tiếng Việt trên iOS để chỉ một bộ gõ xử lý. Với văn bản hoàn chỉnh, dùng ô clipboard của viewer (*Send to device*). Bật UniKey chỉ để khảo sát, không phải cấu hình được hỗ trợ.

## Âm thanh

`--no-audio` chỉ làm viewer không tự bật tiếng; serve-web vẫn mở phiên audio cùng phiên hình ngay khi khởi động. serve-web tự giải mã âm thanh (AAC-ELD) bằng AudioToolbox của macOS, nên trên Windows mỗi lần chạy đều có dòng `ERROR AudioToolbox AAC-ELD decoder failed to open` kèm traceback `AAC-ELD decode requires macOS`. Dòng này vô hại: hình và chạm không bị ảnh hưởng, chỉ là viewer không có tiếng. Trong phiên, tiếng vẫn ra loa/tai nghe của thiết bị (lượt 1). Phải thử loa/tai nghe của thiết bị và âm thanh AirPlay của MacExtend trước, trong và sau phiên.
