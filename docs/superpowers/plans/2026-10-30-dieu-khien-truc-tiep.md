# Điều khiển trực tiếp (CoreDevice) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Người dùng tự commit.** Agent không chạy `git commit`, `git push`, tag hay merge. Mỗi task kết thúc bằng bước *Checkpoint*: danh sách file đã đổi và lệnh kiểm tra cùng kết quả mong đợi. Được dùng `git mv` để chuyển file (không phải commit).

**Goal:** Thêm vào MacExtend tính năng "Điều khiển trực tiếp": xem và điều khiển iPhone/iPad qua cáp USB hoặc Wi-Fi bằng dịch vụ CoreDevice (pymobiledevice3 serve-web), trong một cửa sổ riêng có WebView2, chạy độc lập với AirPlay và Bluetooth.

**Architecture:** MacExtend.exe tự chạy serve-web (Python 3.13 + pymobiledevice3 11.47.0, gói runtime riêng tải lần đầu và cài vào `{app}\coredevice`) trong Job Object, dùng lại phần quản lý tiến trình của probe (chuyển thành thư viện `MacExtend.CoreDeviceHost`). USB dùng tunnel userspace (không cần quyền quản trị). Wi-Fi dùng RemotePairing qua tunneld; tunneld chạy bằng một Scheduled Task "quyền cao nhất" do một lần UAC lúc thiết lập tạo ra, cùng các luật firewall. Mọi logic không phụ thuộc Windows nằm trong `MacExtend.Core` và được test trên Mac.

**Tech Stack:** C# netstandard2.0 (`MacExtend.Core`, xUnit trên net10.0), net48 WPF (`MacExtend.App`), net48 class library (`MacExtend.CoreDeviceHost`), Microsoft.Web.WebView2 (WPF), PowerShell (ScheduledTasks, NetSecurity), GitHub Actions windows-latest, Inno Setup 6, CPython 3.13 x64 (gói NuGet `python`), pymobiledevice3 11.47.0.

**Spec:** Không có file spec (người dùng nói "khỏi spec"). Thiết kế được duyệt trong chat ngày 2026-10-30, ghi lại ở mục *Thiết kế* dưới đây; đó là spec của plan này.

## Thiết kế (đã duyệt trong chat, 2026-10-30)

**Phạm vi.** USB và Wi-Fi ngay bản đầu. Bluetooth giữ nguyên. Hai tính năng tách rời: AirPlay(+Bluetooth) và Điều khiển trực tiếp (CoreDevice), CoreDevice không cần AirPlay.

**Phần 1, tổng thể.** Mục *Điều khiển* trong cửa sổ chính có thêm khối *Điều khiển trực tiếp* (trạng thái, nút *Thiết lập...*, nút *Mở điều khiển*); menu khay có *Điều khiển trực tiếp...*; cửa sổ điều khiển gồm hình thiết bị và thanh công cụ tiếng Việt. Khối mới: runtime `{app}\coredevice\` (Program Files), thư viện `src/MacExtend.CoreDeviceHost` (chuyển từ probe, probe dùng chung), logic trong `src/MacExtend.Core/CoreDevice`, `src/MacExtend.App/Services/CoreDevice/`, cửa sổ `Views/CoreDeviceWindow`, chế độ elevated `--coredevice-setup`, ngăn runtime thứ hai `coredevice` trong `update.ini`. Runtime phải ở Program Files vì Scheduled Task chạy python.exe với quyền cao; vì vậy người chỉ dùng USB cũng gặp đúng một lần UAC lúc thiết lập. Bản portable không tạo task, mỗi phiên Wi-Fi hỏi UAC.

**Phần 2, runtime.** Gói NuGet `python` chính thức (CPython 3.13 x64), không dùng embeddable, không dùng venv; gói cài thẳng vào `Lib\site-packages`. Lock mới `requirements-win-x64-py313.lock.txt` (hash, chỉ wheel, không tự giải phụ thuộc); probe và `setup.ps1` cũng sang 3.13; bỏ lock 3.12 và `sslpsk-pmd3`. CI job `coredevice-runtime`: ghim phiên bản và hash gói NuGet, cài lock, ghi `RUNTIME_ID`, chép runtime sang chỗ khác rồi kiểm tra (11.47.0, kiểm tra phụ thuộc, cờ `serve-web --help` và `remote tunneld --help`, smoke test probe), xuất `MacExtend-<phiên bản>-coredevice-runtime.zip`, thêm `coredevice.*` vào `update.ini`. Lần đầu: tải zip, kiểm tra `size`/`sha256`, một lần UAC chép vào `{app}\coredevice.new` rồi đổi tên. Cập nhật: tải thêm runtime khi máy đã cài và `coredevice.id` khác, chép trong cùng lần xin quyền với app. Gỡ cài đặt xóa runtime, task, luật firewall. Bản portable tải runtime vào thư mục của nó, không cần UAC. Giấy phép đi kèm runtime; README ghi công pymobiledevice3.

**Phần 3, thiết lập một lần.** Phía Windows (elevated): cài runtime; Scheduled Task "MacExtend CoreDevice tunnel" (tài khoản người dùng, quyền cao nhất, chỉ khi đăng nhập, không lịch) chạy `"{app}\MacExtend.exe" --coredevice-tunneld`, MacExtend.exe chạy python ẩn với `remote tunneld --no-usb --no-usbmux --no-mobdev2` trong Job Object và ghi `logs\tunneld.log`; app tắt bằng `GET /shutdown`, dự phòng `schtasks /End`. Luật firewall nhóm "MacExtend CoreDevice", chỉ cho `{app}\coredevice\python\python.exe`: UDP vào từ `fc00::/7` mọi loại mạng; UDP 5353 vào chỉ Private. Kiểm tra lại trước mỗi phiên Wi-Fi; thiếu thì đề nghị thiết lập lại. Bước đầu tiên của plan là kiểm chứng tài khoản thường bật được task. Phía thiết bị: Apple Mobile Device Service, cắm cáp và Tin cậy, Developer Mode (`amfi reveal-developer-mode`, người dùng bật, máy khởi động lại), ảnh DDI (`mounter auto-mount`, cũng chạy trước mỗi phiên), Wi-Fi tùy chọn (`lockdown remotepairing --pair`). Cửa sổ thiết lập nói rõ: camera/micro không dùng được trong phiên; màn hình thiết bị phải bật.

**Phần 4, một phiên.** Mở từ khay hoặc mục *Điều khiển*; chưa thiết lập thì mở cửa sổ thiết lập; nhiều thiết bị thì hỏi chọn; một phiên mỗi lúc. Tự chọn đường: cáp đang cắm thì USB userspace; không cắm cáp và thiết bị đã bật Wi-Fi thì bật task tunneld, hỏi `GET /` tới khi có tunnel của UDID (tối đa 30 giây), rồi serve-web `--tunnel UDID`; không có đường nào thì báo. Không tự đổi đường giữa phiên. Khởi động: `mounter auto-mount`, serve-web trong Job Object với cổng loopback trống, *sẵn sàng* như probe (tối đa 120 giây), cửa sổ hiện "Đang kết nối..." rồi WebView2 mở viewer. Trong phiên: nhãn *Qua cáp/Qua Wi-Fi*, nút Home, xoay trái/phải, *Gửi văn bản*/*Lấy văn bản*, toàn màn hình (F11). Dừng: đóng cửa sổ là cửa sổ biến mất; dừng ngầm theo trình tự probe (thả chạm/phím 2 giây, một Ctrl+C, chờ 40 giây), khay hiện "Đang dừng...", Wi-Fi thì `GET /shutdown`; kill cưỡng bức thì nhắc mở Camera để kiểm tra. Thoát app thì dừng phiên trước. USB bị rút: báo "Mất kết nối, cắm lại cáp", tự chạy lại khi thiết bị trở lại. Wi-Fi rớt: "Đang kết nối lại..." theo log, không gửi lại thao tác. Lỗi tiếng Việt theo loại lỗi. Nhật ký `logs\coredevice.log` và `logs\tunneld.log` chỉ chứa dòng trạng thái đã lọc. AirPlay và Bluetooth không bị ảnh hưởng.

**Phần 5, giao diện và kiểm thử.** Tên "Điều khiển trực tiếp" ("qua cáp hoặc Wi-Fi"); "CoreDevice" chỉ ở README và nhật ký. Cửa sổ theo theme sáng/tối, không emoji. CSS chèn trước khi trang tải ẩn khung viewer, chỉ giữ canvas và lớp offline; tắt menu chuột phải, DevTools, zoom, phím tắt trình duyệt. Nhớ kích thước và vị trí. WebView2 qua NuGet, dữ liệu ở `%LOCALAPPDATA%\MacExtend\WebView2`, thiếu runtime thì báo và đưa link. Cửa sổ thiết lập: các bước có nhãn trạng thái, hướng dẫn, nút; thanh tiến trình khi tải; ô *Cho phép điều khiển qua Wi-Fi*. Kiểm thử: unit test Core trên Mac, CI windows-latest (runtime, build, smoke probe trên runtime mới, smoke `--coredevice-tunneld`, chạy thử `--coredevice-setup` trên runner admin), bảng D trong `docs/TESTING.md` (D1 = task chạy được từ tài khoản thường), README.

### Chỗ plan điều chỉnh so với thiết kế (vì mã nguồn thực tế)

1. **Lần đầu lấy runtime:** thay vì đọc `update.ini` của đúng phiên bản, CI nhúng `id/file/sha256/size` của runtime vào chính MacExtend.exe (`AssemblyMetadata`). App tải từ release `v<InformationalVersion>`, hoặc người dùng chọn file zip từ artifact CI (bản beta trên nhánh không có release). `update.ini` vẫn có `coredevice.*` cho việc cập nhật.
2. **Nút xoay** bấm nút xoay ẩn của chính viewer (qua script WebView2), vì `viewer.js` tự giữ trạng thái xoay (`setVisualRotation` trước `POST /rotate`). Home và văn bản gọi HTTP trực tiếp (`ServeWebControl`), không qua `CoreDeviceClient` (lớp đó khóa phiên sau một lỗi).
3. **Không có lớp phủ đè lên hình:** WPF không vẽ được đè lên WebView2 (airspace). Trạng thái hiện ở một dải thông báo phía trên hình, hoặc ở khung thay thế khi chưa có hình. Lớp offline của viewer được đổi chữ sang tiếng Việt ("Nếu màn hình thiết bị đang tắt, hãy mở khóa"). Esc không tới được WPF khi WebView2 giữ phím nên chỉ dùng F11 và nút.
4. **tunneld chạy với `USERPROFILE` của người dùng** (đối số cố định trong task), để file RemotePairing trong hồ sơ người dùng vẫn đọc được khi nâng quyền bằng tài khoản admin khác.
5. **Sửa lỗi tiềm ẩn:** `ServeWebLog` chỉ nhận logger `pymobiledevice3.__main__`, nhưng chạy bằng `python -m pymobiledevice3` thì logger tên `__main__` (log thật lượt 1: `LEGION __main__[23084] ERROR The device's camera ...`). Vì vậy "camera đang bận" chưa bao giờ được nhận ra trên máy thật.
6. **Bước elevated tự kiểm tra hash:** `--coredevice-setup` nhận đường dẫn file zip, mở file một lần, tự tính SHA-256 theo dữ liệu nhúng trong exe rồi giải nén từ cùng handle, để chương trình quyền thường không tráo được nội dung giữa lúc kiểm tra và lúc cài.
7. **Thêm chế độ `--coredevice-remove`** (xóa task và luật) cho bộ gỡ cài đặt và cho CI.

## Global Constraints

- Agent không commit, push, tag, merge. Người dùng tự commit.
- App: WPF .NET Framework 4.8, x64, `TreatWarningsAsErrors`. Core: netstandard2.0, không thêm package. Test: xUnit trên net10.0 chạy trên Mac.
- dotnet trên Mac là `~/.dotnet/dotnet` (không có trong PATH).
- Giao diện tiếng Việt, không emoji, dùng resource theme (`DynamicResource`) để hợp cả sáng và tối.
- `pymobiledevice3==11.47.0` chính xác. CPython 3.13 x64. Không dùng Python 3.12 nữa.
- serve-web chỉ bind `127.0.0.1`. tunneld nghe `127.0.0.1:49151`.
- Không bao giờ ghi phím, clipboard, UDID đầy đủ hay output thô của Python vào log. Chỉ ghi dòng trạng thái đã phân loại.
- Mỗi lúc chỉ một phiên Điều khiển trực tiếp.
- Bluetooth, AirPlay và mọi hành vi hiện có giữ nguyên.
- Runtime nằm ở `{app}\coredevice\python\python.exe` và `{app}\coredevice\RUNTIME_ID`.
- Task: `\MacExtend\MacExtend CoreDevice tunnel`. Nhóm firewall: `MacExtend CoreDevice`.
- Chờ tối đa: tunnel Wi-Fi 30 giây sau khi tunneld chạy; serve-web sẵn sàng 120 giây; dọn dẹp 40 giây; thả chạm/phím 2 giây.

## Review Focus

1. **Camera hoặc Ghi âm đang mở khi bấm Mở điều khiển.** serve-web thoát ngay với "camera or microphone is in use". Người dùng phải thấy "Đóng Camera, Ghi âm hoặc cuộc gọi rồi thử lại", không phải lỗi chung. Test: `ServeWebLogTests.ReadsTheCliLoggerNamedMain` (Task C1), `SessionMachineTests.MediaInUseAtStartIsReported` (Task C6).
2. **Tên người dùng hoặc thư mục hồ sơ có dấu, khoảng trắng hay dấu nháy** (`C:\Users\Thịnh O'Hải`). Lệnh tạo task, đối số task và môi trường tunneld phải giữ nguyên đường dẫn. Test: `CoreDeviceSetupScriptsTests.KeepsUnicodeAndQuotesInTheTaskArgument` (Task C9), `CoreDeviceArgumentsTunneldTests.SetsTheUserProfile` (Task C10).
3. **tunneld đã chạy sẵn** (người dùng tự chạy, hoặc sót lại sau khi app crash). App phải dùng lại, không chạy thêm cái thứ hai, và không tắt cái không do mình bật. Test: `TunnelPlanTests` (Task C5).
4. **Rút cáp trong lúc phiên chạy, rồi đóng cửa sổ trước khi cắm lại.** Không được tự chạy lại phiên khi đã đóng; cũng không được lặp khởi động vô hạn khi serve-web cứ thoát. Test: `SessionMachineTests.StopWhileWaitingNeverRestarts`, `SessionMachineTests.RestartsAreBounded` (Task C6).
5. **File runtime tải dở, bị sửa, hoặc là zip của bản khác.** Không được cài; không được ghi ra ngoài thư mục đích. Test: `RuntimePackageTests` (Task C11), `UpdateStagerStreamTests.RejectsEntriesOutsideTarget` (Task C8).

---

## File Structure

**Tạo mới**

| File | Trách nhiệm |
|---|---|
| `tools/coredevice/spike-task.ps1` | Task 0: thử Scheduled Task quyền cao chạy từ tài khoản thường |
| `tools/coredevice/runtime-python.txt` | Ghim phiên bản và SHA-256 gói NuGet `python` |
| `tools/coredevice/build-runtime.ps1` | Dựng runtime chép đi đâu cũng chạy: `python\` và `RUNTIME_ID` |
| `tools/coredevice/check_runtime.py` | Kiểm tra phụ thuộc bên trong runtime (thay `pip check`) |
| `tools/coredevice/verify-runtime.ps1` | Chép runtime sang thư mục có dấu cách rồi kiểm tra; chạy smoke probe nếu có |
| `tools/coredevice/test-setup.ps1` | CI: chạy `--coredevice-setup` và `--coredevice-remove` trên runner admin |
| `tools/coredevice/requirements-win-x64-py313.lock.txt` | Lock cho Python 3.13 (sinh bằng `compile-lock.ps1`) |
| `src/MacExtend.CoreDeviceHost/MacExtend.CoreDeviceHost.csproj` | Thư viện net48 dùng chung cho app và probe |
| `src/MacExtend.CoreDeviceHost/CoreDeviceProcessHost.cs` | Chuyển từ probe: Job Object, một Ctrl+C, readiness, `RunCommand` |
| `src/MacExtend.CoreDeviceHost/NativeMethods.cs` | Chuyển từ probe |
| `src/MacExtend.CoreDeviceHost/CtrlCHelper.cs` | Chế độ `signal-ctrl-c` (trước là `Program.SignalCtrlC` của probe) |
| `src/MacExtend.Core/MiniJson.cs` | Đọc JSON của `print_json` và tunneld, không thêm thư viện |
| `src/MacExtend.Core/CoreDevice/DeviceQueries.cs` | Đọc stdout của `usbmux list`, `amfi developer-mode-status` |
| `src/MacExtend.Core/CoreDevice/TunneldListing.cs` | Đọc `GET /` của tunneld |
| `src/MacExtend.Core/CoreDevice/CliLog.cs` | Phân loại dòng ERROR của CLI pymobiledevice3 |
| `src/MacExtend.Core/CoreDevice/TunneldLog.cs` | Phân loại log của tunneld cho `tunneld.log` |
| `src/MacExtend.Core/CoreDevice/TransportChoice.cs` | Chọn USB/Wi-Fi, chọn thiết bị; `TunnelPlan` |
| `src/MacExtend.Core/CoreDevice/SessionMachine.cs` | Máy trạng thái của một phiên |
| `src/MacExtend.Core/CoreDevice/SetupSteps.cs` | Đánh giá các bước thiết lập |
| `src/MacExtend.Core/CoreDevice/CoreDeviceMessages.cs` | Chữ tiếng Việt cho trạng thái và lỗi |
| `src/MacExtend.Core/CoreDevice/CoreDeviceSetupScripts.cs` | PowerShell tạo/xóa task và luật firewall, truy vấn |
| `src/MacExtend.Core/CoreDevice/RuntimePackage.cs` | Thông tin runtime nhúng trong exe |
| `src/MacExtend.Core/CoreDevice/ServeWebControl.cs` | HTTP Home và clipboard cho thanh công cụ |
| `src/MacExtend.App/Native/Elevation.cs` | Đang elevated chưa, tài khoản có phải admin không |
| `src/MacExtend.App/Services/CoreDevice/CoreDeviceHelpers.cs` | Các chế độ helper của MacExtend.exe |
| `src/MacExtend.App/Services/CoreDevice/TunneldHelper.cs` | `--coredevice-tunneld` |
| `src/MacExtend.App/Services/CoreDevice/CoreDeviceRuntime.cs` | Trạng thái, tải, kiểm tra và cài runtime; gọi thiết lập elevated |
| `src/MacExtend.App/Services/CoreDevice/CoreDeviceCommands.cs` | Lệnh ngắn của pymobiledevice3 cho thiết lập và phiên |
| `src/MacExtend.App/Services/CoreDevice/WindowsSetupState.cs` | Task và luật firewall còn đủ không |
| `src/MacExtend.App/Services/CoreDevice/TunnelController.cs` | Bật, chờ, tắt tunneld |
| `src/MacExtend.App/Services/CoreDevice/CoreDeviceService.cs` | Điều phối phiên theo `SessionMachine` |
| `src/MacExtend.App/Services/CoreDevice/DirectControlUi.cs` | Chọn thiết bị, mở cửa sổ, mở thiết lập |
| `src/MacExtend.App/Views/ViewerBridge.cs` | Script chèn vào viewer |
| `src/MacExtend.App/Views/CoreDeviceWindow.xaml(.cs)` | Cửa sổ điều khiển |
| `src/MacExtend.App/Views/CoreDeviceSetupWindow.xaml(.cs)` | Cửa sổ thiết lập |
| `src/MacExtend.App/Views/DevicePickerWindow.xaml(.cs)` | Chọn thiết bị khi có nhiều |
| `src/MacExtend.App/ViewModels/CoreDeviceSetupViewModel.cs` | View model thiết lập |
| Test: `tests/MacExtend.Core.Tests/MiniJsonTests.cs`, `tests/MacExtend.Core.Tests/CoreDevice/{DeviceQueries,TunneldListing,CliLog,TunneldLog,TransportChoice,TunnelPlan,SessionMachine,SetupSteps,CoreDeviceMessages,CoreDeviceSetupScripts,CoreDeviceArgumentsTunneld,RuntimePackage,ServeWebControl}Tests.cs`, `tests/MacExtend.Core.Tests/Updates/UpdateStagerStreamTests.cs` | |

**Sửa**

`src/MacExtend.Core/CoreDevice/ServeWebLog.cs`, `CoreDeviceArguments.cs`; `src/MacExtend.Core/Updates/{UpdateManifest,UpdateDecision,UpdateApplier,UpdateStager,FileHash}.cs`; `src/MacExtend.Core/{AppSettings,SettingsStore}.cs`; `src/MacExtend.App/{MacExtend.App.csproj,App.xaml.cs,AppPaths.cs}`; `src/MacExtend.App/Services/{UpdateService,UpdateHelper}.cs`; `src/MacExtend.App/ViewModels/MainViewModel.cs`; `src/MacExtend.App/Views/MainWindow.xaml`; `src/MacExtend.App/Tray/TrayIcon.cs`; `tools/MacExtend.CoreDeviceProbe/{MacExtend.CoreDeviceProbe.csproj,Program.cs,ProbeCommands.cs}`; `tools/coredevice/{requirements.in,compile-lock.ps1,setup.ps1,README.md,tests/fake_serve_web.py}`; `installer/MacExtend.iss`; `.github/workflows/build.yml`; `MacExtend.sln`; `docs/TESTING.md`; `README.md`; tests hiện có trong `tests/MacExtend.Core.Tests/{SettingsStoreTests.cs,Updates/*,CoreDevice/ServeWebLogTests.cs}`.

**Xóa** `tools/coredevice/requirements-win-x64.lock.txt`.

---

## Task 0: Kiểm chứng Scheduled Task trên máy thật (người dùng chạy trên Windows)

Gate cho Phase D. Nếu FAIL, Phase D vẫn làm, nhưng `WindowsSetupState.UseTask` luôn là `false` (mỗi phiên Wi-Fi hỏi UAC), và ghi kết quả vào `docs/research/2026-10-29-coredevice-results.md`.

**Files:**
- Create: `tools/coredevice/spike-task.ps1`

- [ ] **Step 1: Viết script**

```powershell
<#
.SYNOPSIS
    Spike for "Điều khiển trực tiếp": can a normal (unelevated) PowerShell start a task registered elevated to run
    with highest privileges, and can it see the task and its firewall rules?

.DESCRIPTION
    1. Administrator PowerShell:  .\tools\coredevice\spike-task.ps1 -Create -User "$env:USERDOMAIN\$env:USERNAME"
    2. Normal PowerShell:         .\tools\coredevice\spike-task.ps1 -Run
    3. Administrator PowerShell:  .\tools\coredevice\spike-task.ps1 -Remove
#>
param([switch]$Create, [switch]$Run, [switch]$Remove, [string]$User)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$TaskPath = '\MacExtendSpike\'
$TaskName = 'Spike'
$Group = 'MacExtend Spike'
# C:\Users\Public: written by the elevated task, read by the normal shell
$Out = Join-Path $env:PUBLIC 'macextend-task-spike.txt'

function Test-Admin {
    ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if ($Create) {
    if (-not (Test-Admin)) { throw 'run -Create from an administrator PowerShell' }
    if (-not $User) { throw '-User DOMAIN\name is required (the account that will run -Run)' }
    # S-1-16-12288 is the High integrity label: present only when the task really runs elevated
    $cmd = "whoami /groups > `"$Out`""
    $action = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\cmd.exe" -Argument "/c $cmd"
    $principal = New-ScheduledTaskPrincipal -UserId $User -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -Action $action -Principal $principal -Settings $settings -Force | Out-Null
    New-NetFirewallRule -Group $Group -DisplayName 'MacExtend spike rule' -Direction Inbound -Action Allow -Protocol UDP -LocalPort 5353 -Program "$env:SystemRoot\System32\cmd.exe" -Profile Private | Out-Null
    Remove-Item -LiteralPath $Out -ErrorAction SilentlyContinue
    'created; now run .\tools\coredevice\spike-task.ps1 -Run from a normal PowerShell'
}
elseif ($Run) {
    if (Test-Admin) { throw 'run -Run from a normal PowerShell, not as administrator' }
    $seen = [bool](Get-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -ErrorAction SilentlyContinue)
    & schtasks.exe /Query /TN "$TaskPath$TaskName" | Out-Null
    $query = $LASTEXITCODE
    $rules = @(Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue).Count
    & schtasks.exe /Run /TN "$TaskPath$TaskName"
    $run = $LASTEXITCODE
    Start-Sleep -Seconds 3
    $text = if (Test-Path -LiteralPath $Out) { Get-Content -LiteralPath $Out -Raw } else { '' }
    $elevated = $text -match 'S-1-16-12288'
    "Get-ScheduledTask sees the task: $seen"
    "schtasks /Query exit code:      $query"
    "firewall rules visible:         $rules"
    "schtasks /Run exit code:        $run"
    "task ran elevated:              $elevated"
    if ($run -eq 0 -and $elevated) { 'PASS' } else { 'FAIL' }
}
elseif ($Remove) {
    if (-not (Test-Admin)) { throw 'run -Remove from an administrator PowerShell' }
    Get-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false
    Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    Remove-Item -LiteralPath $Out -ErrorAction SilentlyContinue
    'removed'
}
else {
    throw 'pass -Create, -Run or -Remove'
}
```

- [ ] **Step 2: Người dùng chạy ba bước trên laptop Windows** và gửi lại 6 dòng của `-Run`.

Kết quả mong đợi: `PASS`, `schtasks /Run exit code: 0`, `task ran elevated: True`, `firewall rules visible: 1`.

Cách đọc:
- `Get-ScheduledTask sees the task: False` nhưng `schtasks /Query exit code: 0`: Task C9 viết `Query()` dùng `schtasks /Query` thay cho `Get-ScheduledTask` (ghi chú trong Task C9).
- `FAIL`: Phase D vẫn làm; `WindowsSetupState.UseTask` trả `false`.

- [ ] **Step 3: Checkpoint**

File: `tools/coredevice/spike-task.ps1`. Kết quả spike ghi thêm một dòng vào mục "Kết luận hiện tại" của `docs/research/2026-10-29-coredevice-results.md`.

---

## Phase A: runtime Python 3.13

### Task A1: Lock cho Python 3.13

**Files:**
- Modify: `tools/coredevice/requirements.in`, `tools/coredevice/compile-lock.ps1`, `tools/coredevice/setup.ps1`, `.github/workflows/build.yml` (job `coredevice-probe`)
- Create: `tools/coredevice/requirements-win-x64-py313.lock.txt` (sinh ra)
- Delete: `tools/coredevice/requirements-win-x64.lock.txt`

**Interfaces:** Produces: `tools/coredevice/requirements-win-x64-py313.lock.txt` (Task A2 và `setup.ps1` dùng).

- [ ] **Step 1: Sửa `requirements.in`**

Thay dòng đầu:

```text
# CoreDevice runtime for MacExtend "Điều khiển trực tiếp" and the developer probe (Windows x64, Python 3.13).
```

và dòng `# Regenerate requirements-win-x64.lock.txt with compile-lock.ps1 on Windows.` thành:

```text
# Regenerate requirements-win-x64-py313.lock.txt with compile-lock.ps1 on Windows (CPython 3.13 x64).
# Python 3.13 because iOS 18.2+ refuses QUIC tunnels and the TCP tunnel needs TLS-PSK, which 3.13's ssl has;
# on 3.12 it needed sslpsk-pmd3, whose 1.0.3 Windows wheel links libssl-1_1-x64.dll and cannot import.
```

- [ ] **Step 2: Sửa `compile-lock.ps1`**

Trong `.SYNOPSIS`/`.DESCRIPTION`/`.PARAMETER`, đổi `requirements-win-x64.lock.txt` thành `requirements-win-x64-py313.lock.txt` và `3.12` thành `3.13`. Trong thân script:

```powershell
if ("$info".Trim() -ne '3.13 64') { throw "need CPython 3.13 x64; $Python reports '$("$info".Trim())'" }
```

```powershell
    $env:CUSTOM_COMPILE_COMMAND = 'pwsh tools/coredevice/compile-lock.ps1 -Python <python 3.13 x64>'
    Invoke-Native $toolsPython @('-I', '-m', 'piptools', 'compile', '--generate-hashes', '--resolver=backtracking',
        '--allow-unsafe', '--strip-extras', '--no-emit-index-url', '--pip-args', '--only-binary=:all:',
        '--output-file', 'requirements-win-x64-py313.lock.txt', 'requirements.in')
    Write-Host 'requirements-win-x64-py313.lock.txt regenerated; run setup.ps1 and the probe smoke tests before committing it'
```

- [ ] **Step 3: Sửa `setup.ps1`**

```powershell
$Lock = Join-Path $PSScriptRoot 'requirements-win-x64-py313.lock.txt'
```

Trong `Assert-Python312x64` đổi tên thành `Assert-Python313x64`, so sánh `'3.13'`, thông báo `"$What must be CPython 3.13 x64 on Windows; ..."`; sửa hai chỗ gọi hàm và `.PARAMETER Python` (`Absolute path to a Python 3.13 x64 interpreter`).

- [ ] **Step 4: Người dùng sinh lock trên Windows**

```powershell
$base313 = py -V:3.13 -c "import sys; print(sys.executable)"
pwsh tools/coredevice/compile-lock.ps1 -Python $base313
```

Rồi xóa lock cũ: `git rm tools/coredevice/requirements-win-x64.lock.txt`.

- [ ] **Step 5: Sửa job `coredevice-probe` trong `build.yml`**

`python-version: '3.12'` thành `'3.13'`. Trong bước `Package`, đổi `tools/coredevice/requirements-win-x64.lock.txt` thành `tools/coredevice/requirements-win-x64-py313.lock.txt`. (Task A3 sẽ đổi tiếp job này sang dùng runtime đóng gói.)

- [ ] **Step 6: Kiểm tra trên Mac**

Run: `grep -c '^sslpsk-pmd3' tools/coredevice/requirements-win-x64-py313.lock.txt; grep '^pymobiledevice3==' tools/coredevice/requirements-win-x64-py313.lock.txt; grep -rn 'requirements-win-x64.lock.txt' tools .github`

Expected: `0`, `pymobiledevice3==11.47.0 \`, và lệnh thứ ba không in gì.

- [ ] **Step 7: Checkpoint**

File: 3 script, `build.yml`, lock mới, lock cũ đã xóa. CI job *CoreDevice probe* trên windows-latest phải xanh (setup.ps1 hai lần và smoke test).

### Task A2: Script dựng và kiểm tra runtime

**Files:**
- Create: `tools/coredevice/runtime-python.txt`, `tools/coredevice/build-runtime.ps1`, `tools/coredevice/check_runtime.py`, `tools/coredevice/verify-runtime.ps1`

**Interfaces:**
- Consumes: lock của Task A1.
- Produces: thư mục runtime `<Out>\python\python.exe`, `<Out>\RUNTIME_ID` (một dòng). Task A3 và D dùng bố cục này.

- [ ] **Step 1: Ghim gói NuGet**

Lấy bản 3.13 mới nhất: mở `https://api.nuget.org/v3-flatcontainer/python/index.json`, chọn phiên bản `3.13.x` lớn nhất, tải `https://api.nuget.org/v3-flatcontainer/python/<ver>/python.<ver>.nupkg`, tính `Get-FileHash -Algorithm SHA256`. Ghi file:

```text
# Official CPython nuget.org package used as the CoreDevice runtime (tools/ folder, x64).
# Bump both lines together; build-runtime.ps1 refuses a package whose SHA-256 differs.
version=<3.13.x vừa chọn>
sha256=<sha256 chữ thường của python.<ver>.nupkg>
```

- [ ] **Step 2: Viết `check_runtime.py`**

```python
"""Checks a built CoreDevice runtime from the inside, the way `pip check` would, without pip in the runtime:
every installed distribution's requirements (with their markers) are installed at a matching version, and
pymobiledevice3 is exactly the version MacExtend was checked against."""
import importlib.metadata as md
import sys

from packaging.requirements import Requirement
from packaging.utils import canonicalize_name

REQUIRED = "11.47.0"


def main():
    installed = {canonicalize_name(d.metadata["Name"]): d.version for d in md.distributions()}
    problems = []
    for dist in md.distributions():
        for spec in dist.requires or []:
            req = Requirement(spec)
            if req.marker is not None and not req.marker.evaluate({"extra": ""}):
                continue
            have = installed.get(canonicalize_name(req.name))
            if have is None:
                problems.append(f"{dist.metadata['Name']} needs {req}, which is not installed")
            elif req.specifier and not req.specifier.contains(have, prereleases=True):
                problems.append(f"{dist.metadata['Name']} needs {req}, found {have}")
    if installed.get("pymobiledevice3") != REQUIRED:
        problems.append(f"pymobiledevice3 is {installed.get('pymobiledevice3')}, MacExtend needs {REQUIRED}")
    for problem in problems:
        print(problem)
    print("ok" if not problems else f"{len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 3: Viết `build-runtime.ps1`**

```powershell
<#
.SYNOPSIS
    Builds the relocatable CoreDevice runtime: CPython 3.13 x64 from the official nuget.org "python" package plus the
    locked pymobiledevice3, laid out as <Out>\python\python.exe and <Out>\RUNTIME_ID.

.DESCRIPTION
    No venv (a venv records absolute paths and cannot move) and no embeddable zip (no pip, its own sys.path rules):
    the nuget package is a full CPython meant to be unpacked anywhere. Packages go straight into its
    Lib\site-packages with the build interpreter's pip: hashes required, wheels only, nothing resolved beyond the
    lock. Everything is byte-compiled with unchecked-hash .pyc files, which stay valid wherever the folder moves and
    spare the read-only Program Files copy from compiling at every start.

.PARAMETER Python
    A CPython 3.13 x64 on Windows used only to run pip (for example actions/setup-python's).

.PARAMETER Out
    A missing or empty folder to build into.
#>
param(
    [Parameter(Mandatory = $true)] [string]$Python,
    [Parameter(Mandatory = $true)] [string]$Out
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Native {
    param([string]$Exe, [string[]]$Arguments)
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$([IO.Path]::GetFileName($Exe)) $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

function Get-Info([string]$Exe) {
    "$(& $Exe -I -c "import sys, struct; print('%d.%d %d %s' % (sys.version_info[0], sys.version_info[1], struct.calcsize('P') * 8, sys.platform))")".Trim()
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'build-runtime.ps1 builds the Windows runtime; run it on Windows' }
if ((Get-Info $Python) -ne '3.13 64 win32') { throw "-Python must be CPython 3.13 x64 on Windows; $Python reports '$(Get-Info $Python)'" }

$pin = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime-python.txt')) {
    if ($line -match '^\s*([a-z0-9]+)\s*=\s*(\S+)\s*$') { $pin[$Matches[1]] = $Matches[2] }
}
$version = $pin['version']
$sha = "$($pin['sha256'])".ToLowerInvariant()
if (-not $version -or $sha.Length -ne 64) { throw 'runtime-python.txt needs version=<3.13.x> and sha256=<64 hex digits>' }
$lock = Join-Path $PSScriptRoot 'requirements-win-x64-py313.lock.txt'

if (Test-Path -LiteralPath $Out) {
    if (@(Get-ChildItem -LiteralPath $Out -Force).Count -gt 0) { throw "$Out is not empty" }
} else {
    New-Item -ItemType Directory -Path $Out | Out-Null
}
$Out = (Resolve-Path -LiteralPath $Out).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ('coredevice-runtime-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $nupkg = Join-Path $work "python.$version.zip"
    Invoke-WebRequest -UseBasicParsing -Uri "https://api.nuget.org/v3-flatcontainer/python/$version/python.$version.nupkg" -OutFile $nupkg
    $actual = (Get-FileHash -LiteralPath $nupkg -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $sha) { throw "python.$version.nupkg has SHA-256 $actual; runtime-python.txt pins $sha" }
    Expand-Archive -LiteralPath $nupkg -DestinationPath (Join-Path $work 'pkg')
    Copy-Item -LiteralPath (Join-Path $work 'pkg\tools') -Destination (Join-Path $Out 'python') -Recurse

    $runtime = Join-Path $Out 'python\python.exe'
    if ((Get-Info $runtime) -ne '3.13 64 win32') { throw "the nuget package is not CPython 3.13 x64: '$(Get-Info $runtime)'" }

    $site = Join-Path $Out 'python\Lib\site-packages'
    Invoke-Native $Python @('-I', '-m', 'pip', 'install', '--disable-pip-version-check', '--no-input', '--no-compile',
        '--require-hashes', '--no-deps', '--only-binary', ':all:', '--target', $site, '-r', $lock)
    Invoke-Native $runtime @('-I', '-m', 'compileall', '-q', '-j', '0', '--invalidation-mode', 'unchecked-hash', (Join-Path $Out 'python'))

    $lockHash = (Get-FileHash -LiteralPath $lock -Algorithm SHA256).Hash.ToLowerInvariant()
    $scriptHash = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
    # changes whenever the interpreter, the lock or the way the folder is built changes
    $id = "py$version-$($sha.Substring(0, 12))-lock-$($lockHash.Substring(0, 12))-build-$($scriptHash.Substring(0, 8))"
    [IO.File]::WriteAllText((Join-Path $Out 'RUNTIME_ID'), "$id`n")
    Write-Host "CoreDevice runtime $id built in $Out"
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
```

- [ ] **Step 4: Viết `verify-runtime.ps1`**

```powershell
<#
.SYNOPSIS
    Checks a built CoreDevice runtime after copying it to a fresh folder whose name has a space, so nothing in it
    may depend on where it was built.

.PARAMETER Runtime
    The folder build-runtime.ps1 produced.

.PARAMETER Probe
    Optional MacExtend.CoreDeviceProbe.exe: when given, the probe smoke tests run on the copied runtime too.
#>
param(
    [Parameter(Mandatory = $true)] [string]$Runtime,
    [string]$Probe
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Native {
    param([string]$Exe, [string[]]$Arguments)
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$([IO.Path]::GetFileName($Exe)) $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

$copy = Join-Path ([IO.Path]::GetTempPath()) ('MacExtend runtime ' + [guid]::NewGuid().ToString('N').Substring(0, 8))
Copy-Item -LiteralPath (Resolve-Path -LiteralPath $Runtime).Path -Destination $copy -Recurse
try {
    $py = Join-Path $copy 'python\python.exe'
    if (-not (Test-Path -LiteralPath (Join-Path $copy 'RUNTIME_ID'))) { throw 'RUNTIME_ID is missing' }
    Invoke-Native $py @('-I', (Join-Path $PSScriptRoot 'check_runtime.py'))

    $env:NO_COLOR = '1'
    $serve = & $py -I -X utf8 -u -m pymobiledevice3 developer core-device display serve-web --help | Out-String
    foreach ($option in @('--bind', '--http-port', '--no-audio', '--userspace', '--tunnel')) {
        if ($serve -notmatch [regex]::Escape($option)) { throw "serve-web --help does not list $option" }
    }
    $tunneld = & $py -I -X utf8 -u -m pymobiledevice3 remote tunneld --help | Out-String
    foreach ($option in @('--no-usb', '--no-usbmux', '--no-mobdev2', '--wifi')) {
        if ($tunneld -notmatch [regex]::Escape($option)) { throw "remote tunneld --help does not list $option" }
    }
    # the RemotePairing TCP tunnel needs TLS-PSK from ssl itself (no sslpsk-pmd3 on 3.13)
    Invoke-Native $py @('-I', '-c', "import ssl; c = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT); c.set_ciphers('PSK'); c.set_psk_client_callback(lambda hint: (None, b'k')); print('psk ok')")

    if ($Probe) {
        & (Join-Path $PSScriptRoot 'test-probe.ps1') -Probe $Probe -Python $py
        if ($LASTEXITCODE -ne 0) { throw "probe smoke tests failed ($LASTEXITCODE)" }
    }
    Write-Host 'runtime ok'
}
finally {
    Remove-Item -LiteralPath $copy -Recurse -Force -ErrorAction SilentlyContinue
}
```

- [ ] **Step 5: Người dùng chạy thử trên Windows** (CI ở Task A3 cũng chạy)

```powershell
$base313 = py -V:3.13 -c "import sys; print(sys.executable)"
.\tools\coredevice\build-runtime.ps1 -Python $base313 -Out C:\cd-runtime
.\tools\coredevice\verify-runtime.ps1 -Runtime C:\cd-runtime
```

Expected: dòng `ok` của `check_runtime.py`, `psk ok`, `runtime ok`.

- [ ] **Step 6: Checkpoint**

File: 4 file mới. `verify-runtime.ps1` in `runtime ok`.

### Task A3: CI build runtime, zip và `update.ini`

**Files:**
- Modify: `.github/workflows/build.yml`

**Interfaces:**
- Produces: artifact `coredevice-runtime`; trong job `app`: output `steps.coredevice.outputs.{file,sha256,size,id}`; asset `MacExtend-<value>-coredevice-runtime.zip`; dòng `coredevice.id/file/sha256/size` trong `update.ini`; MSBuild property `CoreDeviceRuntimeId/File/Sha256/Size` truyền vào `Build app` (Task D3 đọc chúng).

- [ ] **Step 1: Thêm job `coredevice-runtime`** (đặt trước job `app`)

```yaml
  # Python 3.13 + the locked pymobiledevice3 for "Điều khiển trực tiếp", downloaded by the app on first use
  # (never in the installer or the portable zip). Built and checked on a relocated copy.
  coredevice-runtime:
    name: CoreDevice runtime (Python 3.13 + pymobiledevice3)
    runs-on: windows-latest
    timeout-minutes: 30
    steps:
      - uses: actions/checkout@v5

      - uses: actions/setup-python@v5
        id: python
        with:
          python-version: '3.13'
          architecture: x64

      - name: Build runtime
        shell: pwsh
        run: ./tools/coredevice/build-runtime.ps1 -Python '${{ steps.python.outputs.python-path }}' -Out build/coredevice-runtime

      - name: Verify runtime (relocated copy)
        shell: pwsh
        run: ./tools/coredevice/verify-runtime.ps1 -Runtime build/coredevice-runtime

      - uses: actions/upload-artifact@v4
        with:
          name: coredevice-runtime
          path: build/coredevice-runtime
          if-no-files-found: error
```

- [ ] **Step 2: Job `app`: phụ thuộc, zip runtime trước khi build app**

`needs: runtime` thành `needs: [runtime, coredevice-runtime]`. Ngay sau bước `Version`, thêm:

```yaml
      - uses: actions/download-artifact@v4
        with:
          name: coredevice-runtime
          path: build/coredevice-runtime

      - name: CoreDevice runtime zip
        id: coredevice
        shell: pwsh
        run: |
          New-Item -ItemType Directory -Force dist | Out-Null
          $zip = "dist/MacExtend-${{ steps.version.outputs.value }}-coredevice-runtime.zip"
          7z a -tzip $zip ./build/coredevice-runtime/* | Out-Null
          if ($LASTEXITCODE) { exit $LASTEXITCODE }
          $item = Get-Item $zip
          "file=$($item.Name)" >> $env:GITHUB_OUTPUT
          "sha256=$((Get-FileHash $item -Algorithm SHA256).Hash.ToLower())" >> $env:GITHUB_OUTPUT
          "size=$($item.Length)" >> $env:GITHUB_OUTPUT
          "id=$((Get-Content build/coredevice-runtime/RUNTIME_ID -Raw).Trim())" >> $env:GITHUB_OUTPUT
```

Bước `Build app` thêm bốn property vào cuối lệnh:

```yaml
        run: dotnet build src/MacExtend.App -c Release -o build/MacExtend -p:Version=${{ steps.version.outputs.number }} -p:InformationalVersion=${{ steps.version.outputs.value }} -p:UpdateRepository=${{ github.repository }} -p:DebugType=none -p:CoreDeviceRuntimeId=${{ steps.coredevice.outputs.id }} -p:CoreDeviceRuntimeFile=${{ steps.coredevice.outputs.file }} -p:CoreDeviceRuntimeSha256=${{ steps.coredevice.outputs.sha256 }} -p:CoreDeviceRuntimeSize=${{ steps.coredevice.outputs.size }}
```

- [ ] **Step 3: `update.ini` có `coredevice.*`**

Trong bước `Update packages`, thêm vào mảng `$lines` sau `runtime.size`:

```powershell
            "coredevice.id=${{ steps.coredevice.outputs.id }}"
            "coredevice.file=${{ steps.coredevice.outputs.file }}"
            "coredevice.sha256=${{ steps.coredevice.outputs.sha256 }}"
            "coredevice.size=${{ steps.coredevice.outputs.size }}"
```

- [ ] **Step 4: Job `coredevice-probe` dùng runtime đóng gói**

`needs: coredevice-runtime`. Bỏ `actions/setup-python` và bước `Python runtime from the lock`. Thêm sau `setup-dotnet`:

```yaml
      - uses: actions/download-artifact@v4
        with:
          name: coredevice-runtime
          path: build/coredevice-runtime
```

Thay bước smoke:

```yaml
      - name: Smoke tests (net48 probe, relocated runtime, fake serve-web)
        shell: pwsh
        run: ./tools/coredevice/verify-runtime.ps1 -Runtime build/coredevice-runtime -Probe build/coredevice-probe/bin/MacExtend.CoreDeviceProbe.exe
```

- [ ] **Step 5: Checkpoint**

File: `.github/workflows/build.yml`. Người dùng push nhánh rồi chạy workflow (workflow_dispatch). Mong đợi: job *CoreDevice runtime* và *CoreDevice probe* xanh; artifact `MacExtend-<value>` có `MacExtend-<value>-coredevice-runtime.zip` khoảng 90–120 MB; `update.ini` trong artifact có 4 dòng `coredevice.`.

---

## Phase B: thư viện dùng chung `MacExtend.CoreDeviceHost`

### Task B1: Chuyển host của probe sang thư viện

**Files:**
- Create: `src/MacExtend.CoreDeviceHost/MacExtend.CoreDeviceHost.csproj`, `src/MacExtend.CoreDeviceHost/CtrlCHelper.cs`
- Move: `tools/MacExtend.CoreDeviceProbe/CoreDeviceProcessHost.cs` và `NativeMethods.cs` sang `src/MacExtend.CoreDeviceHost/`
- Modify: `tools/MacExtend.CoreDeviceProbe/MacExtend.CoreDeviceProbe.csproj`, `Program.cs`, `ProbeCommands.cs`, `MacExtend.sln`

**Interfaces:**
- Produces (namespace `MacExtend.CoreDeviceHost`, tất cả `public`): `CoreDeviceProcessHost` (giữ nguyên các thành viên public hiện có), `CoreDeviceHostException` (tên mới của `ProbeException`), `ReadyFailure`, `StopExit`, `TcpListeners`, `PortListener`, `CtrlCHelper { const string Command = "signal-ctrl-c"; static bool TryRun(string[] args, out int exitCode); static void ClearInheritedCtrlCIgnore(); }`. `NativeMethods` vẫn `internal`.

- [ ] **Step 1: Tạo project**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- serve-web and tunneld process hosting shared by the app and the developer probe: kill-on-close job objects,
       exactly one Ctrl+C, readiness checks. See tools/coredevice/README.md. -->
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>latest</LangVersion>
    <PlatformTarget>x64</PlatformTarget>
    <RootNamespace>MacExtend.CoreDeviceHost</RootNamespace>
    <AssemblyName>MacExtend.CoreDeviceHost</AssemblyName>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Copyright>GPL-3.0-or-later</Copyright>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\MacExtend.Core\MacExtend.Core.csproj" />
    <Reference Include="System.Net.Http" />
  </ItemGroup>

</Project>
```

Run: `git mv tools/MacExtend.CoreDeviceProbe/CoreDeviceProcessHost.cs src/MacExtend.CoreDeviceHost/ && git mv tools/MacExtend.CoreDeviceProbe/NativeMethods.cs src/MacExtend.CoreDeviceHost/ && ~/.dotnet/dotnet sln MacExtend.sln add src/MacExtend.CoreDeviceHost/MacExtend.CoreDeviceHost.csproj --solution-folder src`

- [ ] **Step 2: Đổi namespace và độ truy cập**

Trong hai file đã chuyển: `namespace MacExtend.CoreDeviceProbe` thành `namespace MacExtend.CoreDeviceHost` (cả khối thứ hai chứa `PortListener`/`TcpListeners`). `internal sealed class ProbeException` thành `public sealed class CoreDeviceHostException`; thay mọi `ProbeException` bằng `CoreDeviceHostException`. `internal enum ReadyFailure`, `internal enum StopExit`, `internal sealed class CoreDeviceProcessHost`, `internal struct PortListener`, `internal static class TcpListeners` đổi `internal` thành `public`. Trong `RunHelper`, chuỗi `"signal-ctrl-c"` thay bằng `CtrlCHelper.Command`.

- [ ] **Step 3: `CtrlCHelper.cs`** (thân lấy nguyên từ `Program.SignalCtrlC` của probe)

```csharp
using System;
using System.Globalization;

namespace MacExtend.CoreDeviceHost
{
    /// <summary>
    /// The "signal-ctrl-c PID" helper mode of whatever exe hosts serve-web (the probe, MacExtend.exe): attaches to the
    /// console of PID and sends exactly one CTRL_C_EVENT to every process on it, ignoring it itself. A separate
    /// process, so the host never leaves its own console. The exit code says whether the event was certainly sent,
    /// certainly not sent, or unknown (CoreDeviceProcessHost.Helper*).
    /// </summary>
    public static class CtrlCHelper
    {
        public const string Command = "signal-ctrl-c";

        /// <returns>true when <paramref name="args"/> asked for the helper; the process should exit with <paramref name="exitCode"/>.</returns>
        public static bool TryRun(string[] args, out int exitCode)
        {
            exitCode = 0;
            if (args == null || args.Length == 0 || args[0] != Command) return false;
            if (args.Length != 2 || !uint.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid == 0)
            {
                exitCode = CoreDeviceProcessHost.HelperBadArguments;
                return true;
            }
            NativeMethods.FreeConsole();
            if (!NativeMethods.AttachConsole(pid))
            {
                exitCode = CoreDeviceProcessHost.HelperAttachFailed;
                return true;
            }
            NativeMethods.SetConsoleCtrlHandler(IntPtr.Zero, true);
            var sent = NativeMethods.GenerateConsoleCtrlEvent(NativeMethods.CTRL_C_EVENT, 0);
            exitCode = sent ? CoreDeviceProcessHost.HelperSent : CoreDeviceProcessHost.HelperGenerateFailed;
            return true;
        }

        /// <summary>
        /// "Ignore Ctrl+C" is inherited from whoever started this process (a CREATE_NEW_PROCESS_GROUP parent sets it)
        /// and would pass on to serve-web, which then never sees the one Ctrl+C that runs its cleanup.
        /// </summary>
        public static void ClearInheritedCtrlCIgnore() => NativeMethods.SetConsoleCtrlHandler(IntPtr.Zero, false);
    }
}
```

- [ ] **Step 4: Sửa probe**

`MacExtend.CoreDeviceProbe.csproj` thêm `<ProjectReference Include="..\..\src\MacExtend.CoreDeviceHost\MacExtend.CoreDeviceHost.csproj" />`. `Program.cs` và `ProbeCommands.cs` thêm `using MacExtend.CoreDeviceHost;`; thay `ProbeException` bằng `CoreDeviceHostException`. Trong `Program.Main`:

```csharp
            if (CtrlCHelper.TryRun(args, out var helperExit)) return helperExit;

            CtrlCHelper.ClearInheritedCtrlCIgnore();
```

thay cho dòng `if (args.Length > 0 && args[0] == HelperCommand) return SignalCtrlC(args);` và dòng `NativeMethods.SetConsoleCtrlHandler(IntPtr.Zero, false);` cùng chú thích của nó. Xóa hằng `HelperCommand` và hàm `SignalCtrlC`.

- [ ] **Step 5: Build**

Run: `~/.dotnet/dotnet build MacExtend.sln -c Release`
Expected: `Build succeeded.` `0 Warning(s)` `0 Error(s)`.

- [ ] **Step 6: Checkpoint**

File: project mới, 2 file chuyển, `CtrlCHelper.cs`, probe 3 file, `MacExtend.sln`. CI job *CoreDevice probe* (19 kịch bản smoke) phải xanh như trước.

### Task B2: `RunCommand` và nhận dòng thô (làm sau Task C3)

**Files:**
- Modify: `src/MacExtend.CoreDeviceHost/CoreDeviceProcessHost.cs`

**Interfaces:**
- Consumes: `CliLog.Parse(string) : CliError`, `CliLog.Pick(IEnumerable<CliError>) : CliError` (Task C3). Làm Task C3 trước task này.
- Produces:
  - `public sealed class CommandResult { public int ExitCode { get; } public string Stdout { get; } public CliError Error { get; } public bool TimedOut { get; } }`.
  - `public static CommandResult CoreDeviceProcessHost.RunCommand(string executable, IReadOnlyList<string> args, IDictionary<string, string> environment, TimeSpan timeout)`.
  - `public Action<string> RawLineHandler { get; set; }`: khi khác null, mỗi dòng stdout/stderr đi vào đây thay vì qua `ServeWebLog.Parse`. Người dùng handler tự phân loại và không được lưu dòng thô.

- [ ] **Step 1: Thêm `CommandResult` và `RunCommand`** (cạnh `RunToCompletion`)

```csharp
    /// <summary>A short pymobiledevice3 command's outcome. stderr is never kept: only the kind of error it reported.</summary>
    public sealed class CommandResult
    {
        public CommandResult(int exitCode, string stdout, CliError error, bool timedOut)
        {
            ExitCode = exitCode;
            Stdout = stdout ?? "";
            Error = error;
            TimedOut = timedOut;
        }

        /// <summary>-1 when the command was stopped at its timeout.</summary>
        public int ExitCode { get; }
        public string Stdout { get; }
        public CliError Error { get; }
        public bool TimedOut { get; }
        public bool Succeeded => ExitCode == 0 && !TimedOut;
    }
```

```csharp
        /// <summary>
        /// Runs a short command (usbmux list, amfi, mounter, lockdown remotepairing) in a job of its own, launched like
        /// serve-web, and waits up to <paramref name="timeout"/>; past that the job is terminated. stdout comes back
        /// whole (JSON from print_json); stderr lines are only classified with <see cref="CliLog"/>.
        /// </summary>
        public static CommandResult RunCommand(string executable, IReadOnlyList<string> args, IDictionary<string, string> environment, TimeSpan timeout)
        {
            using (var host = new CoreDeviceProcessHost())
            {
                var stdout = new StringBuilder();
                var errors = new List<CliError>();
                var child = host.LaunchInJob(executable, args, environment, false, _ => { });
                try
                {
                    var outReader = Task.Run(() => ReadAll(child.Stdout, stdout));
                    var errReader = Task.Run(() => ReadLines(child.Stderr, line =>
                    {
                        var error = CliLog.Parse(line);
                        if (error != CliError.None) lock (errors) errors.Add(error);
                    }));
                    var finished = NativeMethods.WaitForSingleObject(child.Process, (uint)timeout.TotalMilliseconds) == NativeMethods.WAIT_OBJECT_0;
                    uint exitCode = 1;
                    if (finished) NativeMethods.GetExitCodeProcess(child.Process, out exitCode);
                    // a descendant that outlived the command would hold the pipes open
                    NativeMethods.TerminateJobObject(host._job, 1);
                    Task.WaitAll(new Task[] { outReader, errReader }, 5000);
                    lock (errors)
                    lock (stdout)
                    {
                        return new CommandResult(finished ? (int)exitCode : -1, stdout.ToString(), CliLog.Pick(errors), !finished);
                    }
                }
                finally
                {
                    NativeMethods.TerminateJobObject(host._job, 1);
                    child.Dispose();
                }
            }
        }

        private static void ReadLines(SafeFileHandle pipe, Action<string> onLine)
        {
            using (var stream = new FileStream(pipe, FileAccess.Read, 4096, false))
            using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
            {
                string line;
                while ((line = reader.ReadLine()) != null) onLine(line);
            }
        }
```

Nếu `ReadAll` hiện có đã mở stream theo cách khác, giữ cùng cách mở pipe (UTF-8, không BOM) cho `ReadLines`.

- [ ] **Step 2: `RawLineHandler`**

Thêm thuộc tính:

```csharp
        /// <summary>
        /// When set, every stdout/stderr line goes here instead of <see cref="ServeWebLog"/> (tunneld uses it). The handler
        /// must classify the line and drop it: raw lines carry the UDID and addresses.
        /// </summary>
        public Action<string> RawLineHandler { get; set; }
```

Trong `StartReader`, vòng đọc đổi thành:

```csharp
                    while ((line = reader.ReadLine()) != null)
                    {
                        var raw = RawLineHandler;
                        if (raw != null) raw(line);
                        else OnLine(ServeWebLog.Parse(line));
                    }
```

- [ ] **Step 3: Build**

Run: `~/.dotnet/dotnet build MacExtend.sln -c Release`
Expected: `Build succeeded.` `0 Warning(s)` `0 Error(s)`.

- [ ] **Step 4: Checkpoint**

File: `CoreDeviceProcessHost.cs`. CI smoke probe vẫn xanh.

---

## Phase C: logic trong Core (viết test trước)

Mọi lệnh test trong phase này chạy trên Mac: `~/.dotnet/dotnet test tests/MacExtend.Core.Tests --filter "FullyQualifiedName~<Tên lớp test>"`. "PASS" nghĩa là dòng `Passed!  - Failed:     0`.

### Task C1: Sửa nhận dạng logger `__main__` (lỗi tiềm ẩn)

**Files:**
- Modify: `src/MacExtend.Core/CoreDevice/ServeWebLog.cs:52`, `tests/MacExtend.Core.Tests/CoreDevice/ServeWebLogTests.cs`, `tools/coredevice/tests/fake_serve_web.py:157`

- [ ] **Step 1: Test thất bại**

Thêm vào `ServeWebLogTests`:

```csharp
    // `python -m pymobiledevice3` names the CLI's logger "__main__" (seen on the device, run 1 of 2026-10-29)
    [Fact]
    public void ReadsTheCliLoggerNamedMain()
    {
        var line = ServeWebLog.Parse("2026-10-29 21:47:49 LEGION __main__[23084] ERROR The device's camera or microphone is in use by another app (e.g. Camera or Voice Memos). Quit that app on the device, then retry");

        Assert.Equal(ServeWebLogKind.MediaInUse, line.Kind);
    }

    [Fact]
    public void StillReadsTheImportedLoggerName()
    {
        Assert.Equal(ServeWebLogKind.ServiceUnavailable, ServeWebLog.Parse(Main + "ERROR Failed to start service. Possible reasons are:").Kind);
    }
```

- [ ] **Step 2: Chạy, mong đợi FAIL** ở `ReadsTheCliLoggerNamedMain` (`Expected: MediaInUse, Actual: Other`).

- [ ] **Step 3: Sửa**

```csharp
        // run as `python -m pymobiledevice3` the CLI logger is "__main__"; imported it is "pymobiledevice3.__main__"
        private const string Main = @"(?:pymobiledevice3\.)?__main__\[\d+\] ";
```

Trong `fake_serve_web.py` dòng 157: `log("pymobiledevice3.__main__", "ERROR",` thành `log("__main__", "ERROR",` để smoke test giống máy thật.

- [ ] **Step 4: Chạy `ServeWebLogTests`, mong đợi PASS.**

- [ ] **Step 5: Checkpoint**

File: 3 file trên. `~/.dotnet/dotnet test tests/MacExtend.Core.Tests` PASS toàn bộ.

### Task C2: `MiniJson`, `DeviceQueries`, `TunneldListing`

**Files:**
- Create: `src/MacExtend.Core/MiniJson.cs`, `src/MacExtend.Core/CoreDevice/DeviceQueries.cs`, `src/MacExtend.Core/CoreDevice/TunneldListing.cs`
- Test: `tests/MacExtend.Core.Tests/MiniJsonTests.cs`, `tests/MacExtend.Core.Tests/CoreDevice/DeviceQueriesTests.cs`, `tests/MacExtend.Core.Tests/CoreDevice/TunneldListingTests.cs`

**Interfaces:**
- Produces:
  - `MiniJson.Parse(string) : object` (null, bool, double, string, `List<object>`, `Dictionary<string, object>`; FormatException khi sai), `MiniJson.TryParse(string, out object) : bool`.
  - `DeviceQueries.ParseUdids(string stdout) : IReadOnlyList<string>`, `DeviceQueries.ParseBool(string stdout) : bool?`.
  - `TunnelEntry { string Address; int Port; string Interface; }`, `TunneldListing.Host = "127.0.0.1"`, `TunneldListing.Port = 49151`, `TunneldListing.Parse(string json) : IReadOnlyDictionary<string, IReadOnlyList<TunnelEntry>>`, `TunneldListing.HasTunnel(string json, string udid) : bool`.

Dữ kiện đã kiểm trong mã pymobiledevice3 11.47.0: `print_json` in `json.dumps(sort_keys=True, indent=4, ensure_ascii=False)`, có màu chỉ khi stdout là tty. `usbmux list --usb --simple` in mảng UDID. `amfi developer-mode-status` in `true`/`false`. tunneld `GET /` trả `{udid: [{"tunnel-address", "tunnel-port", "interface", "auxiliary-metadata", "origin"}]}`.

- [ ] **Step 1: Test thất bại**

`tests/MacExtend.Core.Tests/MiniJsonTests.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace MacExtend.Core.Tests;

public class MiniJsonTests
{
    [Fact]
    public void ReadsPrintJsonOfAStringList()
    {
        var value = MiniJson.Parse("[\n    \"00008030-001E185E343B802E\"\n]\n");

        Assert.Equal(new List<object> { "00008030-001E185E343B802E" }, value);
    }

    [Fact]
    public void ReadsObjectsNumbersBoolsAndNull()
    {
        var value = (Dictionary<string, object>)MiniJson.Parse("{\"a\": 1.5, \"b\": [true, false, null], \"c\": {\"d\": -2}}");

        Assert.Equal(1.5, value["a"]);
        Assert.Equal(new List<object> { true, false, null }, value["b"]);
        Assert.Equal(-2.0, ((Dictionary<string, object>)value["c"])["d"]);
    }

    [Fact]
    public void DecodesEscapesAndUnicode()
    {
        Assert.Equal("Th\u1ECBnh \"x\"\n\\", MiniJson.Parse("\"Th\\u1ecbnh \\\"x\\\"\\n\\\\\""));
        Assert.Equal("Thịnh", MiniJson.Parse("\"Thịnh\""));
    }

    [Theory]
    [InlineData("true false")]
    [InlineData("{\"a\":1,}")]
    [InlineData("[1 2]")]
    [InlineData("\"unterminated")]
    [InlineData("<html>")]
    [InlineData("")]
    public void RejectsWhatIsNotOneJsonValue(string text)
    {
        Assert.Throws<FormatException>(() => MiniJson.Parse(text));
        Assert.False(MiniJson.TryParse(text, out _));
    }

    [Fact]
    public void RejectsDeepNesting()
    {
        Assert.Throws<FormatException>(() => MiniJson.Parse(new string('[', 100) + new string(']', 100)));
    }
}
```

`tests/MacExtend.Core.Tests/CoreDevice/DeviceQueriesTests.cs`:

```csharp
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class DeviceQueriesTests
{
    private const string A = "00008030-001E185E343B802E";
    private const string B = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void ReadsUdidsAndDropsAnythingElse()
    {
        var udids = DeviceQueries.ParseUdids("[\n    \"" + A + "\",\n    \"" + B + "\",\n    \"not-a-udid\",\n    7,\n    \"" + A.ToLowerInvariant() + "\"\n]\n");

        Assert.Equal(new[] { A, B }, udids);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("Traceback (most recent call last):")]
    public void NoDevicesWhenTheOutputIsEmptyOrNotAList(string stdout)
    {
        Assert.Empty(DeviceQueries.ParseUdids(stdout));
    }

    [Theory]
    [InlineData("true\n", true)]
    [InlineData("false\n", false)]
    public void ReadsDeveloperModeStatus(string stdout, bool expected)
    {
        Assert.Equal(expected, DeviceQueries.ParseBool(stdout));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"true\"")]
    [InlineData("1")]
    public void UnknownWhenTheStatusIsNotABool(string stdout)
    {
        Assert.Null(DeviceQueries.ParseBool(stdout));
    }
}
```

`tests/MacExtend.Core.Tests/CoreDevice/TunneldListingTests.cs`:

```csharp
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class TunneldListingTests
{
    private const string Udid = "00008030-001E185E343B802E";

    private static string Listing(string udid, string address, string port, string iface) =>
        "{\"" + udid + "\": [{\"tunnel-address\": \"" + address + "\", \"tunnel-port\": " + port + ", \"interface\": \"" + iface + "\", \"auxiliary-metadata\": null, \"origin\": null}]}";

    [Fact]
    public void ReadsTunnelsByUdid()
    {
        var map = TunneldListing.Parse(Listing(Udid, "fd7b:167:4659::1", "60123", "usbmux-" + Udid + "-USB"));

        var entry = Assert.Single(map[Udid]);
        Assert.Equal("fd7b:167:4659::1", entry.Address);
        Assert.Equal(60123, entry.Port);
        Assert.Equal("usbmux-" + Udid + "-USB", entry.Interface);
        Assert.True(TunneldListing.HasTunnel(Listing(Udid, "fd7b::1", "1", "x"), Udid.ToLowerInvariant()));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("<!doctype html>")]
    [InlineData("{\"not-a-udid\": [{\"tunnel-address\": \"fd::1\", \"tunnel-port\": 1}]}")]
    [InlineData("{\"00008030-001E185E343B802E\": [{\"tunnel-address\": \"fd::1\", \"tunnel-port\": 0}]}")]
    [InlineData("{\"00008030-001E185E343B802E\": [{\"tunnel-port\": 5}]}")]
    public void NoTunnelForOtherAnswers(string json)
    {
        Assert.False(TunneldListing.HasTunnel(json, Udid));
    }
}
```

- [ ] **Step 2: Chạy `MiniJsonTests|DeviceQueriesTests|TunneldListingTests`, mong đợi FAIL** (lớp chưa có, lỗi biên dịch).

- [ ] **Step 3: `MiniJson.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MacExtend.Core
{
    /// <summary>
    /// Reads the JSON pymobiledevice3's print_json writes, and tunneld's GET /, without a JSON library: objects become
    /// Dictionary&lt;string, object&gt; (ordinal keys), arrays List&lt;object&gt;, numbers double, plus string, bool
    /// and null. Strict: one value, nothing after it, nesting at most 64 deep; anything else is a FormatException.
    /// </summary>
    public static class MiniJson
    {
        private const int MaxDepth = 64;

        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("no JSON text");
            var reader = new Reader(text);
            reader.SkipSpace();
            var value = reader.ReadValue(0);
            reader.SkipSpace();
            if (!reader.AtEnd) throw new FormatException("unexpected text after the JSON value at " + reader.Position);
            return value;
        }

        public static bool TryParse(string text, out object value)
        {
            try
            {
                value = Parse(text);
                return true;
            }
            catch (FormatException)
            {
                value = null;
                return false;
            }
        }

        private sealed class Reader
        {
            private readonly string _s;
            private int _i;

            public Reader(string s) => _s = s;

            public bool AtEnd => _i >= _s.Length;
            public int Position => _i;

            public void SkipSpace()
            {
                while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\r' || _s[_i] == '\n')) _i++;
            }

            public object ReadValue(int depth)
            {
                if (depth > MaxDepth) throw new FormatException("JSON nested too deeply");
                if (AtEnd) throw new FormatException("unexpected end of JSON");
                var c = _s[_i];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': ReadWord("true"); return true;
                    case 'f': ReadWord("false"); return false;
                    case 'n': ReadWord("null"); return null;
                }
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                throw new FormatException("unexpected '" + c + "' at " + _i);
            }

            private Dictionary<string, object> ReadObject(int depth)
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                _i++;
                SkipSpace();
                if (Peek('}'))
                {
                    _i++;
                    return result;
                }
                while (true)
                {
                    SkipSpace();
                    if (!Peek('"')) throw new FormatException("expected a property name at " + _i);
                    var key = ReadString();
                    SkipSpace();
                    Expect(':');
                    SkipSpace();
                    result[key] = ReadValue(depth + 1);
                    SkipSpace();
                    if (!Peek(',')) break;
                    _i++;
                }
                Expect('}');
                return result;
            }

            private List<object> ReadArray(int depth)
            {
                var result = new List<object>();
                _i++;
                SkipSpace();
                if (Peek(']'))
                {
                    _i++;
                    return result;
                }
                while (true)
                {
                    SkipSpace();
                    result.Add(ReadValue(depth + 1));
                    SkipSpace();
                    if (!Peek(',')) break;
                    _i++;
                }
                Expect(']');
                return result;
            }

            private string ReadString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw new FormatException("unterminated string");
                    var c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c < ' ') throw new FormatException("control character in a string at " + (_i - 1));
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (AtEnd) throw new FormatException("unterminated escape");
                    var e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length || !int.TryParse(_s.Substring(_i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                            {
                                throw new FormatException("bad \\u escape at " + _i);
                            }
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default:
                            throw new FormatException("bad escape \\" + e);
                    }
                }
            }

            private double ReadNumber()
            {
                var start = _i;
                _i++;
                while (_i < _s.Length && "0123456789.eE+-".IndexOf(_s[_i]) >= 0) _i++;
                var text = _s.Substring(start, _i - start);
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) throw new FormatException("bad number '" + text + "'");
                return value;
            }

            private void ReadWord(string word)
            {
                if (_i + word.Length > _s.Length || string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw new FormatException("expected " + word + " at " + _i);
                _i += word.Length;
            }

            private bool Peek(char c) => _i < _s.Length && _s[_i] == c;

            private void Expect(char c)
            {
                if (!Peek(c)) throw new FormatException("expected '" + c + "' at " + _i);
                _i++;
            }
        }
    }
}
```

- [ ] **Step 4: `DeviceQueries.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>Reads the stdout of the short pymobiledevice3 commands the app runs; all of them print with print_json.</summary>
    public static class DeviceQueries
    {
        /// <summary>`usbmux list --usb --simple`: a JSON array of UDIDs. Anything that is not a valid UDID is dropped.</summary>
        public static IReadOnlyList<string> ParseUdids(string stdout)
        {
            if (!MiniJson.TryParse((stdout ?? "").Trim(), out var value) || !(value is List<object> list)) return new string[0];
            return list.OfType<string>().Where(CoreDeviceOptions.IsValidUdid).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>`amfi developer-mode-status`: JSON true or false; null when the output is neither.</summary>
        public static bool? ParseBool(string stdout)
        {
            if (!MiniJson.TryParse((stdout ?? "").Trim(), out var value)) return null;
            return value is bool b ? b : (bool?)null;
        }
    }
}
```

- [ ] **Step 5: `TunneldListing.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace MacExtend.Core.CoreDevice
{
    public sealed class TunnelEntry
    {
        public TunnelEntry(string address, int port, string interfaceName)
        {
            Address = address;
            Port = port;
            Interface = interfaceName;
        }

        public string Address { get; }
        public int Port { get; }

        /// <summary>How tunneld reached the device: "usbmux-&lt;UDID&gt;-USB", "mobdev2-...", or the device's address for RemotePairing.</summary>
        public string Interface { get; }
    }

    /// <summary>tunneld's GET / on 127.0.0.1:49151: {"&lt;UDID&gt;": [{"tunnel-address", "tunnel-port", "interface", ...}]}.</summary>
    public static class TunneldListing
    {
        public const string Host = "127.0.0.1";
        public const int Port = 49151;

        public static IReadOnlyDictionary<string, IReadOnlyList<TunnelEntry>> Parse(string json)
        {
            var result = new Dictionary<string, IReadOnlyList<TunnelEntry>>(StringComparer.OrdinalIgnoreCase);
            if (!MiniJson.TryParse(json, out var value) || !(value is Dictionary<string, object> map)) return result;
            foreach (var pair in map)
            {
                if (!CoreDeviceOptions.IsValidUdid(pair.Key) || !(pair.Value is List<object> entries)) continue;
                var tunnels = new List<TunnelEntry>();
                foreach (var entry in entries.OfType<Dictionary<string, object>>())
                {
                    if (!(Get(entry, "tunnel-address") is string address) || address.Length == 0) continue;
                    if (!(Get(entry, "tunnel-port") is double port) || port < 1 || port > 65535 || port != Math.Floor(port)) continue;
                    tunnels.Add(new TunnelEntry(address, (int)port, Get(entry, "interface") as string ?? ""));
                }
                if (tunnels.Count > 0) result[pair.Key] = tunnels;
            }
            return result;
        }

        public static bool HasTunnel(string json, string udid) => udid != null && Parse(json).ContainsKey(udid);

        private static object Get(Dictionary<string, object> map, string key) => map.TryGetValue(key, out var v) ? v : null;
    }
}
```

- [ ] **Step 6: Chạy ba lớp test, mong đợi PASS.**

- [ ] **Step 7: Checkpoint**

File: 3 nguồn, 3 test. `~/.dotnet/dotnet test tests/MacExtend.Core.Tests` PASS.

### Task C3: `CliLog` và `TunneldLog`

**Files:**
- Create: `src/MacExtend.Core/CoreDevice/CliLog.cs`, `src/MacExtend.Core/CoreDevice/TunneldLog.cs`
- Test: `tests/MacExtend.Core.Tests/CoreDevice/CliLogTests.cs`, `tests/MacExtend.Core.Tests/CoreDevice/TunneldLogTests.cs`

**Interfaces:**
- Produces: `enum CliError { None, NotConnected, NeedsTrust, TrustDenied, DeviceLocked, DeveloperModeOff, NoUsbmux, NoInternet, ServiceUnavailable, Other }`, `CliLog.Parse(string line) : CliError`, `CliLog.Pick(IEnumerable<CliError>) : CliError`; `enum TunneldLogKind { Other, Listening, TunnelCreated, TunnelClosed, QuicRefused, Error }`, `TunneldLog.Parse(string) : TunneldLogKind`, `TunneldLog.Describe(TunneldLogKind) : string`.

Dữ kiện từ mã nguồn: `__main__._report` ghi `logger.error(str(error) or type(error).__name__)`; các exception không kèm message nên ra tên lớp (`PairingDialogResponsePendingError`, `UserDeniedPairingError`, `PasswordRequiredError`, `DeveloperModeIsNotEnabledError`, `ConnectionFailedToUsbmuxdError`); `mounter auto-mount` ghi `failed to query DeveloperDiskImage versions` khi không có mạng. tunneld ghi `[<task>] Created tunnel --rsd <addr> <port>` (INFO), `Disconnected from tunnel --rsd ...` (INFO), `[<task>] QuicProtocolNotSupportedError: ...` (WARNING), `Got exception from <task>` (ERROR); uvicorn in `INFO:     Uvicorn running on http://127.0.0.1:49151`.

- [ ] **Step 1: Test thất bại**

```csharp
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class CliLogTests
{
    private const string Main = "2026-10-30 12:36:22 LEGION __main__[19724] ERROR ";

    [Theory]
    [InlineData("PairingDialogResponsePendingError", CliError.NeedsTrust)]
    [InlineData("UserDeniedPairingError", CliError.TrustDenied)]
    [InlineData("PasswordRequiredError", CliError.DeviceLocked)]
    [InlineData("DeveloperModeIsNotEnabledError", CliError.DeveloperModeOff)]
    [InlineData("ConnectionFailedToUsbmuxdError", CliError.NoUsbmux)]
    [InlineData("failed to query DeveloperDiskImage versions", CliError.NoInternet)]
    [InlineData("Failed to start service. Possible reasons are:", CliError.ServiceUnavailable)]
    [InlineData("Device is not connected", CliError.NotConnected)]
    [InlineData("NoDeviceConnectedError", CliError.NotConnected)]
    [InlineData("Connection was terminated abruptly.", CliError.Other)]
    public void ClassifiesTheCliErrors(string message, CliError expected)
    {
        Assert.Equal(expected, CliLog.Parse(Main + message));
        Assert.Equal(expected, CliLog.Parse("2026-10-30 12:36:22 LEGION pymobiledevice3.__main__[1] ERROR " + message));
    }

    [Theory]
    [InlineData("2026-10-30 12:36:22 LEGION __main__[19724] WARNING Trying again over a no-root userspace tunnel")]
    [InlineData("2026-10-30 12:36:23 LEGION pymobiledevice3.cli.mounter[19724] INFO DeveloperDiskImage already mounted")]
    [InlineData("Traceback (most recent call last):")]
    [InlineData("")]
    [InlineData(null)]
    public void OtherLinesAreNotErrors(string line)
    {
        Assert.Equal(CliError.None, CliLog.Parse(line));
    }

    [Fact]
    public void ASpecificErrorWinsOverOther()
    {
        Assert.Equal(CliError.DeviceLocked, CliLog.Pick(new[] { CliError.Other, CliError.DeviceLocked, CliError.Other }));
        Assert.Equal(CliError.Other, CliLog.Pick(new[] { CliError.None, CliError.Other }));
        Assert.Equal(CliError.None, CliLog.Pick(new CliError[0]));
    }
}
```

```csharp
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class TunneldLogTests
{
    private const string Server = "2026-10-30 12:50:11 LEGION pymobiledevice3.tunneld.server[7560] ";

    [Theory]
    [InlineData("INFO:     Uvicorn running on http://127.0.0.1:49151 (Press CTRL+C to quit)", TunneldLogKind.Listening)]
    [InlineData(Server + "INFO [start-tunnel-task-wifi-192.168.2.4] Created tunnel --rsd fd7b::1 60123", TunneldLogKind.TunnelCreated)]
    [InlineData(Server + "INFO Disconnected from tunnel --rsd fd7b::1 60123", TunneldLogKind.TunnelClosed)]
    [InlineData(Server + "WARNING [start-tunnel-task-wifi-192.168.2.4] QuicProtocolNotSupportedError: iOS 18.2+ removed QUIC tunnel support.", TunneldLogKind.QuicRefused)]
    [InlineData(Server + "ERROR Got exception from start-tunnel-task-wifi-192.168.2.4", TunneldLogKind.Error)]
    [InlineData(Server + "DEBUG Skipping remote pairing service x@fe80::1%eth:49152: gaierror(11001)", TunneldLogKind.Other)]
    [InlineData("Traceback (most recent call last):", TunneldLogKind.Other)]
    public void ClassifiesTunneldLines(string line, TunneldLogKind expected)
    {
        Assert.Equal(expected, TunneldLog.Parse(line));
    }

    [Fact]
    public void DescriptionsNeverCarryTheLine()
    {
        foreach (TunneldLogKind kind in System.Enum.GetValues(typeof(TunneldLogKind)))
        {
            var text = TunneldLog.Describe(kind);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("fd7b", text);
        }
    }
}
```

- [ ] **Step 2: Chạy `CliLogTests|TunneldLogTests`, mong đợi FAIL** (lớp chưa có).

- [ ] **Step 3: `CliLog.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MacExtend.Core.CoreDevice
{
    public enum CliError
    {
        None,
        NotConnected,
        /// <summary>The Trust dialog is waiting on the device.</summary>
        NeedsTrust,
        TrustDenied,
        /// <summary>Locked with a passcode: unlock and retry.</summary>
        DeviceLocked,
        DeveloperModeOff,
        /// <summary>No Apple Mobile Device Service (usbmuxd) to talk to.</summary>
        NoUsbmux,
        /// <summary>auto-mount could not reach Apple to fetch the Developer Disk Image.</summary>
        NoInternet,
        ServiceUnavailable,
        Other,
    }

    /// <summary>
    /// Classifies the ERROR lines pymobiledevice3's CLI writes for a failed command,
    /// "&lt;date&gt; &lt;time&gt; &lt;host&gt; __main__[&lt;pid&gt;] ERROR &lt;message&gt;". The message is never kept.
    /// </summary>
    public static class CliLog
    {
        private static readonly Regex Ansi = new Regex(@"\x1b\[[0-9;]*m", RegexOptions.CultureInvariant);

        // run as `python -m pymobiledevice3` the logger is "__main__"; imported it is "pymobiledevice3.__main__"
        private static readonly Regex Error = new Regex(@"\A\S+ \S+ \S+ (?:pymobiledevice3\.)?__main__\[\d+\] ERROR (.*)\z", RegexOptions.CultureInvariant);

        public static CliError Parse(string line)
        {
            if (string.IsNullOrEmpty(line)) return CliError.None;
            var m = Error.Match(Ansi.Replace(line, ""));
            return m.Success ? Classify(m.Groups[1].Value) : CliError.None;
        }

        /// <summary>The error to tell the user about: the first specific one, else Other if any error was seen.</summary>
        public static CliError Pick(IEnumerable<CliError> seen)
        {
            var list = (seen ?? Enumerable.Empty<CliError>()).Where(e => e != CliError.None).ToList();
            var specific = list.FirstOrDefault(e => e != CliError.Other);
            if (specific != CliError.None) return specific;
            return list.Count > 0 ? CliError.Other : CliError.None;
        }

        private static CliError Classify(string message)
        {
            bool Has(string s) => message.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
            if (Has("PairingDialogResponsePending") || Has("answer the Trust dialog")) return CliError.NeedsTrust;
            if (Has("UserDeniedPairing") || Has("refused to trust")) return CliError.TrustDenied;
            if (Has("PasswordRequired") || Has("password protected")) return CliError.DeviceLocked;
            if (Has("DeveloperModeIsNotEnabled") || Has("Developer Mode is disabled")) return CliError.DeveloperModeOff;
            if (Has("ConnectionFailedToUsbmuxd") || Has("usbmuxd socket")) return CliError.NoUsbmux;
            if (Has("failed to query DeveloperDiskImage versions")) return CliError.NoInternet;
            if (Has("Failed to start service")) return CliError.ServiceUnavailable;
            if (Has("NoDeviceConnected") || Has("Device is not connected") || Has("DeviceNotFound") || Has("No device")) return CliError.NotConnected;
            return CliError.Other;
        }
    }
}
```

- [ ] **Step 4: `TunneldLog.cs`**

```csharp
using System.Text.RegularExpressions;

namespace MacExtend.Core.CoreDevice
{
    public enum TunneldLogKind
    {
        Other,
        Listening,
        TunnelCreated,
        TunnelClosed,
        /// <summary>The device refused a QUIC tunnel (iOS 18.2+): tunneld is on Python older than 3.13.</summary>
        QuicRefused,
        Error,
    }

    /// <summary>Classifies tunneld's output for logs\tunneld.log. Lines carry the UDID and addresses, so only the kind is kept.</summary>
    public static class TunneldLog
    {
        private const string Server = @"\A\S+ \S+ \S+ pymobiledevice3\.tunneld\.server\[\d+\] ";

        private static readonly Regex Ansi = new Regex(@"\x1b\[[0-9;]*m", RegexOptions.CultureInvariant);
        private static readonly Regex Listening = new Regex(@"Uvicorn running on http://127\.0\.0\.1:49151", RegexOptions.CultureInvariant);
        private static readonly Regex Created = new Regex(Server + @"INFO \[[^\]]*\] Created tunnel --rsd ", RegexOptions.CultureInvariant);
        private static readonly Regex Closed = new Regex(Server + @"INFO Disconnected from tunnel --rsd ", RegexOptions.CultureInvariant);
        private static readonly Regex Quic = new Regex(Server + @"WARNING .*QuicProtocolNotSupportedError", RegexOptions.CultureInvariant);
        private static readonly Regex Failure = new Regex(Server + @"ERROR ", RegexOptions.CultureInvariant);

        public static TunneldLogKind Parse(string line)
        {
            if (string.IsNullOrEmpty(line)) return TunneldLogKind.Other;
            line = Ansi.Replace(line, "");
            if (Listening.IsMatch(line)) return TunneldLogKind.Listening;
            if (Created.IsMatch(line)) return TunneldLogKind.TunnelCreated;
            if (Closed.IsMatch(line)) return TunneldLogKind.TunnelClosed;
            if (Quic.IsMatch(line)) return TunneldLogKind.QuicRefused;
            if (Failure.IsMatch(line)) return TunneldLogKind.Error;
            return TunneldLogKind.Other;
        }

        public static string Describe(TunneldLogKind kind)
        {
            switch (kind)
            {
                case TunneldLogKind.Listening: return "listening on 127.0.0.1:49151";
                case TunneldLogKind.TunnelCreated: return "tunnel created";
                case TunneldLogKind.TunnelClosed: return "tunnel closed";
                case TunneldLogKind.QuicRefused: return "device refused a QUIC tunnel (needs Python 3.13+)";
                case TunneldLogKind.Error: return "error while starting a tunnel";
                default: return "other";
            }
        }
    }
}
```

- [ ] **Step 5: Chạy hai lớp test, mong đợi PASS.**

- [ ] **Step 6: Checkpoint**

File: 2 nguồn, 2 test. Toàn bộ test PASS.

### Task C4: `TransportChoice`

**Files:**
- Create: `src/MacExtend.Core/CoreDevice/TransportChoice.cs`
- Test: `tests/MacExtend.Core.Tests/CoreDevice/TransportChoiceTests.cs`

**Interfaces:**
- Produces: `enum CoreDeviceRoute { None, Usb, Wifi }`, `TransportChoice.Choose(string udid, IEnumerable<string> usbUdids, IEnumerable<string> wifiUdids, bool wifiAvailable) : CoreDeviceRoute`, `TransportChoice.PickDevice(IEnumerable<string> usbUdids, IEnumerable<string> wifiUdids, bool wifiAvailable, string lastUsed) : string`, `TransportChoice.Candidates(...) : IReadOnlyList<string>`.

- [ ] **Step 1: Test thất bại**

```csharp
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class TransportChoiceTests
{
    private const string A = "00008030-001E185E343B802E";
    private const string B = "00008101-000A1B2C3D4E5F60";

    [Fact]
    public void TheCableWinsOverWifi()
    {
        Assert.Equal(CoreDeviceRoute.Usb, TransportChoice.Choose(A, new[] { A }, new[] { A }, wifiAvailable: true));
    }

    [Fact]
    public void WifiOnlyForADeviceSetUpForItAndOnlyWhenWindowsIsReady()
    {
        Assert.Equal(CoreDeviceRoute.Wifi, TransportChoice.Choose(A, new string[0], new[] { A.ToLowerInvariant() }, wifiAvailable: true));
        Assert.Equal(CoreDeviceRoute.None, TransportChoice.Choose(A, new string[0], new[] { A }, wifiAvailable: false));
        Assert.Equal(CoreDeviceRoute.None, TransportChoice.Choose(A, new string[0], new[] { B }, wifiAvailable: true));
        Assert.Equal(CoreDeviceRoute.None, TransportChoice.Choose(null, new[] { A }, null, wifiAvailable: true));
    }

    [Fact]
    public void PicksTheLastDeviceWhenItIsThere()
    {
        Assert.Equal(B, TransportChoice.PickDevice(new[] { A, B }, new string[0], true, B.ToLowerInvariant()));
    }

    [Fact]
    public void PicksTheOnlyDeviceOrAsks()
    {
        Assert.Equal(A, TransportChoice.PickDevice(new[] { A }, new[] { A }, true, null));
        Assert.Null(TransportChoice.PickDevice(new[] { A }, new[] { B }, true, null));
        Assert.Equal(A, TransportChoice.PickDevice(new[] { A }, new[] { B }, false, null));
        Assert.Null(TransportChoice.PickDevice(new string[0], new string[0], true, A));
    }
}
```

- [ ] **Step 2: Chạy `TransportChoiceTests`, mong đợi FAIL.**

- [ ] **Step 3: Viết**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace MacExtend.Core.CoreDevice
{
    public enum CoreDeviceRoute
    {
        None,
        /// <summary>The cable, through serve-web's in-process userspace tunnel: no administrator rights, no firewall.</summary>
        Usb,
        /// <summary>RemotePairing over Wi-Fi through tunneld, which runs elevated.</summary>
        Wifi,
    }

    public static class TransportChoice
    {
        /// <summary>The cable wins. Wi-Fi only for a device set up for it, and only when Windows' part is in place.</summary>
        public static CoreDeviceRoute Choose(string udid, IEnumerable<string> usbUdids, IEnumerable<string> wifiUdids, bool wifiAvailable)
        {
            if (udid == null) return CoreDeviceRoute.None;
            if (Contains(usbUdids, udid)) return CoreDeviceRoute.Usb;
            if (wifiAvailable && Contains(wifiUdids, udid)) return CoreDeviceRoute.Wifi;
            return CoreDeviceRoute.None;
        }

        /// <summary>Every device a session could use now, cable devices first.</summary>
        public static IReadOnlyList<string> Candidates(IEnumerable<string> usbUdids, IEnumerable<string> wifiUdids, bool wifiAvailable) =>
            (usbUdids ?? Enumerable.Empty<string>())
                .Concat(wifiAvailable ? wifiUdids ?? Enumerable.Empty<string>() : Enumerable.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// The device to open without asking: the last one used if it is a candidate, else the only candidate. null when
        /// the user has to choose (several) or there is none.
        /// </summary>
        public static string PickDevice(IEnumerable<string> usbUdids, IEnumerable<string> wifiUdids, bool wifiAvailable, string lastUsed)
        {
            var candidates = Candidates(usbUdids, wifiUdids, wifiAvailable);
            var last = candidates.FirstOrDefault(u => string.Equals(u, lastUsed, StringComparison.OrdinalIgnoreCase));
            if (last != null) return last;
            return candidates.Count == 1 ? candidates[0] : null;
        }

        private static bool Contains(IEnumerable<string> list, string udid) => list != null && list.Contains(udid, StringComparer.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Chạy, mong đợi PASS.**

- [ ] **Step 5: Checkpoint** — File: 1 nguồn, 1 test. Toàn bộ test PASS.

### Task C5: `TunnelPlan`

**Files:**
- Modify: `src/MacExtend.Core/CoreDevice/TransportChoice.cs` (thêm lớp cuối file)
- Test: `tests/MacExtend.Core.Tests/CoreDevice/TunnelPlanTests.cs`

**Interfaces:**
- Produces: `enum TunnelStep { UseExisting, WaitForExisting, StartTunneld }`, `TunnelPlan.First(bool tunneldAnswers, bool hasTunnelForDevice) : TunnelStep`, `TunnelPlan.ShutdownAfter(TunnelStep first) : bool`.

- [ ] **Step 1: Test thất bại**

```csharp
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class TunnelPlanTests
{
    [Fact]
    public void ATunnelThatIsAlreadyThereIsUsedAndNotShutDown()
    {
        var step = TunnelPlan.First(tunneldAnswers: true, hasTunnelForDevice: true);

        Assert.Equal(TunnelStep.UseExisting, step);
        Assert.False(TunnelPlan.ShutdownAfter(step));
    }

    [Fact]
    public void SomeoneElsesTunneldIsWaitedForNotStartedTwiceAndNotShutDown()
    {
        var step = TunnelPlan.First(tunneldAnswers: true, hasTunnelForDevice: false);

        Assert.Equal(TunnelStep.WaitForExisting, step);
        Assert.False(TunnelPlan.ShutdownAfter(step));
    }

    [Fact]
    public void OnlyATunneldThisSessionStartedIsShutDown()
    {
        var step = TunnelPlan.First(tunneldAnswers: false, hasTunnelForDevice: false);

        Assert.Equal(TunnelStep.StartTunneld, step);
        Assert.True(TunnelPlan.ShutdownAfter(step));
    }
}
```

- [ ] **Step 2: Chạy, mong đợi FAIL.**

- [ ] **Step 3: Thêm vào cuối `TransportChoice.cs`**

```csharp
    public enum TunnelStep
    {
        UseExisting,
        WaitForExisting,
        StartTunneld,
    }

    /// <summary>
    /// A tunneld that already answers on 127.0.0.1:49151 belongs to someone else (the user, or one left from an earlier
    /// run): it is used, never started a second time, and never shut down by this session.
    /// </summary>
    public static class TunnelPlan
    {
        public static TunnelStep First(bool tunneldAnswers, bool hasTunnelForDevice) =>
            hasTunnelForDevice ? TunnelStep.UseExisting : tunneldAnswers ? TunnelStep.WaitForExisting : TunnelStep.StartTunneld;

        public static bool ShutdownAfter(TunnelStep first) => first == TunnelStep.StartTunneld;
    }
```

(Đặt trong cùng `namespace MacExtend.Core.CoreDevice { ... }`.)

- [ ] **Step 4: Chạy, mong đợi PASS.**

- [ ] **Step 5: Checkpoint** — File: `TransportChoice.cs`, test mới. Toàn bộ test PASS.

### Task C6: `SessionMachine`

**Files:**
- Create: `src/MacExtend.Core/CoreDevice/SessionMachine.cs`
- Test: `tests/MacExtend.Core.Tests/CoreDevice/SessionMachineTests.cs`

**Interfaces:**
- Consumes: `CoreDeviceRoute`, `CliError`, `ServeWebLogKind`.
- Produces: `enum SessionPhase { Idle, StartingTunnel, Preparing, Starting, Ready, Reconnecting, WaitingForDevice, Stopping, Stopped, Failed }`; `enum SessionFailure { None, MediaInUse, DeviceNotFound, NeedsTrust, DeviceLocked, DeveloperSetup, NoInternet, NoAppleService, StartTimeout, TunnelTimeout, WindowsSetupMissing, AdminDeclined, KeepsExiting, Exited, Other }`; `enum SessionAction { None, StartTunnel, MountImage, StartServeWeb, StopServeWeb, WatchUsb }`; `enum TunnelOutcome { Ready, Timeout, SetupMissing, AdminDeclined }`; `sealed class SessionMachine` với `Route`, `Phase`, `Failure`, `StopRequested`, `IsActive`, `ServeWebMayRun`, và các hàm `Start()`, `TunnelReady(TunnelOutcome)`, `ImageMounted(bool ok, CliError error)`, `ServeWebReady()`, `ServeWebFailed(ServeWebLogKind lastFatal, bool timedOut)`, `Log(ServeWebLogKind)`, `UsbDeviceGone()`, `ServeWebExited(ServeWebLogKind lastFatal)`, `UsbDeviceBack()`, `Stop()` (mỗi hàm trả `SessionAction`), `StopFinished()`.

- [ ] **Step 1: Test thất bại**

```csharp
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class SessionMachineTests
{
    private static SessionMachine ReadyUsb()
    {
        var m = new SessionMachine(CoreDeviceRoute.Usb);
        m.Start();
        m.ImageMounted(true, CliError.None);
        m.ServeWebReady();
        return m;
    }

    [Fact]
    public void UsbMountsThenStartsServeWeb()
    {
        var m = new SessionMachine(CoreDeviceRoute.Usb);

        Assert.Equal(SessionAction.MountImage, m.Start());
        Assert.Equal(SessionPhase.Preparing, m.Phase);
        Assert.Equal(SessionAction.StartServeWeb, m.ImageMounted(true, CliError.None));
        Assert.Equal(SessionAction.None, m.ServeWebReady());
        Assert.Equal(SessionPhase.Ready, m.Phase);
        Assert.Equal(SessionAction.StopServeWeb, m.Stop());
        Assert.Equal(SessionPhase.Stopping, m.Phase);
        m.StopFinished();
        Assert.Equal(SessionPhase.Stopped, m.Phase);
        Assert.False(m.IsActive);
    }

    [Fact]
    public void WifiNeedsTheTunnelBeforeTheImage()
    {
        var m = new SessionMachine(CoreDeviceRoute.Wifi);

        Assert.Equal(SessionAction.StartTunnel, m.Start());
        Assert.Equal(SessionAction.MountImage, m.TunnelReady(TunnelOutcome.Ready));
        Assert.Equal(SessionAction.StartServeWeb, m.ImageMounted(true, CliError.None));
    }

    [Theory]
    [InlineData(TunnelOutcome.Timeout, SessionFailure.TunnelTimeout)]
    [InlineData(TunnelOutcome.SetupMissing, SessionFailure.WindowsSetupMissing)]
    [InlineData(TunnelOutcome.AdminDeclined, SessionFailure.AdminDeclined)]
    public void TunnelProblemsFailTheSession(TunnelOutcome outcome, SessionFailure expected)
    {
        var m = new SessionMachine(CoreDeviceRoute.Wifi);
        m.Start();

        Assert.Equal(SessionAction.None, m.TunnelReady(outcome));
        Assert.Equal(SessionPhase.Failed, m.Phase);
        Assert.Equal(expected, m.Failure);
    }

    [Theory]
    [InlineData(CliError.NotConnected, SessionFailure.DeviceNotFound)]
    [InlineData(CliError.NeedsTrust, SessionFailure.NeedsTrust)]
    [InlineData(CliError.TrustDenied, SessionFailure.NeedsTrust)]
    [InlineData(CliError.DeviceLocked, SessionFailure.DeviceLocked)]
    [InlineData(CliError.DeveloperModeOff, SessionFailure.DeveloperSetup)]
    [InlineData(CliError.ServiceUnavailable, SessionFailure.DeveloperSetup)]
    [InlineData(CliError.NoInternet, SessionFailure.NoInternet)]
    [InlineData(CliError.NoUsbmux, SessionFailure.NoAppleService)]
    [InlineData(CliError.Other, SessionFailure.Other)]
    public void MountFailuresAreExplained(CliError error, SessionFailure expected)
    {
        var m = new SessionMachine(CoreDeviceRoute.Usb);
        m.Start();

        m.ImageMounted(false, error);

        Assert.Equal(expected, m.Failure);
    }

    [Fact]
    public void MediaInUseAtStartIsReported()
    {
        var m = new SessionMachine(CoreDeviceRoute.Usb);
        m.Start();
        m.ImageMounted(true, CliError.None);

        Assert.Equal(SessionAction.StopServeWeb, m.ServeWebFailed(ServeWebLogKind.MediaInUse, timedOut: false));
        Assert.Equal(SessionPhase.Failed, m.Phase);
        Assert.Equal(SessionFailure.MediaInUse, m.Failure);
    }

    [Fact]
    public void AStartTimeoutIsNotTheLastLogLine()
    {
        var m = new SessionMachine(CoreDeviceRoute.Usb);
        m.Start();
        m.ImageMounted(true, CliError.None);

        m.ServeWebFailed(ServeWebLogKind.Error, timedOut: true);

        Assert.Equal(SessionFailure.StartTimeout, m.Failure);
    }

    [Fact]
    public void WifiDisconnectsAndReconnectsThroughTheLog()
    {
        var m = new SessionMachine(CoreDeviceRoute.Wifi);
        m.Start();
        m.TunnelReady(TunnelOutcome.Ready);
        m.ImageMounted(true, CliError.None);
        m.ServeWebReady();

        m.Log(ServeWebLogKind.DeviceDisconnected);
        Assert.Equal(SessionPhase.Reconnecting, m.Phase);
        m.Log(ServeWebLogKind.DeviceReconnected);
        Assert.Equal(SessionPhase.Ready, m.Phase);
    }

    [Fact]
    public void AnUnpluggedCableWaitsThenRestarts()
    {
        var m = ReadyUsb();

        Assert.Equal(SessionAction.StopServeWeb, m.UsbDeviceGone());
        Assert.Equal(SessionPhase.WaitingForDevice, m.Phase);
        Assert.Equal(SessionAction.WatchUsb, m.ServeWebExited(ServeWebLogKind.Other));
        Assert.Equal(SessionAction.MountImage, m.UsbDeviceBack());
        Assert.Equal(SessionPhase.Preparing, m.Phase);
    }

    [Fact]
    public void ServeWebExitingOnACableSessionWaitsForTheDevice()
    {
        var m = ReadyUsb();

        Assert.Equal(SessionAction.WatchUsb, m.ServeWebExited(ServeWebLogKind.Other));
        Assert.Equal(SessionPhase.WaitingForDevice, m.Phase);
    }

    [Fact]
    public void ServeWebExitingOverWifiFails()
    {
        var m = new SessionMachine(CoreDeviceRoute.Wifi);
        m.Start();
        m.TunnelReady(TunnelOutcome.Ready);
        m.ImageMounted(true, CliError.None);
        m.ServeWebReady();

        m.ServeWebExited(ServeWebLogKind.Other);

        Assert.Equal(SessionFailure.Exited, m.Failure);
    }

    [Fact]
    public void RestartsAreBounded()
    {
        var m = ReadyUsb();
        m.UsbDeviceGone();

        for (var i = 0; i < SessionMachine.MaxRestarts; i++)
        {
            Assert.Equal(SessionAction.MountImage, m.UsbDeviceBack());
            m.ImageMounted(true, CliError.None);
            m.UsbDeviceGone();
        }

        Assert.Equal(SessionAction.None, m.UsbDeviceBack());
        Assert.Equal(SessionFailure.KeepsExiting, m.Failure);
    }

    [Fact]
    public void ReachingReadyResetsTheRestartCount()
    {
        var m = ReadyUsb();
        for (var i = 0; i < SessionMachine.MaxRestarts + 2; i++)
        {
            m.UsbDeviceGone();
            Assert.Equal(SessionAction.MountImage, m.UsbDeviceBack());
            m.ImageMounted(true, CliError.None);
            m.ServeWebReady();
        }

        Assert.Equal(SessionPhase.Ready, m.Phase);
    }

    [Fact]
    public void StopWhileWaitingNeverRestarts()
    {
        var m = ReadyUsb();
        m.UsbDeviceGone();

        Assert.Equal(SessionAction.StopServeWeb, m.Stop());
        Assert.Equal(SessionAction.None, m.UsbDeviceBack());
        Assert.Equal(SessionAction.None, m.ServeWebExited(ServeWebLogKind.Other));
        Assert.Equal(SessionPhase.Stopping, m.Phase);
    }

    [Fact]
    public void StopBeforeServeWebNeedsNoServeWebStop()
    {
        var m = new SessionMachine(CoreDeviceRoute.Wifi);
        m.Start();

        Assert.Equal(SessionAction.None, m.Stop());
        Assert.Equal(SessionAction.None, m.TunnelReady(TunnelOutcome.Ready));
        Assert.Equal(SessionPhase.Stopping, m.Phase);
    }

    [Fact]
    public void LateEventsAfterAFailureChangeNothing()
    {
        var m = new SessionMachine(CoreDeviceRoute.Usb);
        m.Start();
        m.ImageMounted(false, CliError.NotConnected);

        Assert.Equal(SessionAction.None, m.ServeWebReady());
        Assert.Equal(SessionAction.None, m.Stop());
        Assert.Equal(SessionPhase.Failed, m.Phase);
    }
}
```

- [ ] **Step 2: Chạy `SessionMachineTests`, mong đợi FAIL.**

- [ ] **Step 3: Viết `SessionMachine.cs`**

```csharp
using System;

namespace MacExtend.Core.CoreDevice
{
    public enum SessionPhase
    {
        Idle,
        StartingTunnel,
        Preparing,
        Starting,
        Ready,
        Reconnecting,
        WaitingForDevice,
        Stopping,
        Stopped,
        Failed,
    }

    public enum SessionFailure
    {
        None,
        MediaInUse,
        DeviceNotFound,
        NeedsTrust,
        DeviceLocked,
        DeveloperSetup,
        NoInternet,
        NoAppleService,
        StartTimeout,
        TunnelTimeout,
        WindowsSetupMissing,
        AdminDeclined,
        KeepsExiting,
        Exited,
        Other,
    }

    public enum SessionAction
    {
        None,
        StartTunnel,
        MountImage,
        StartServeWeb,
        StopServeWeb,
        WatchUsb,
    }

    public enum TunnelOutcome
    {
        Ready,
        Timeout,
        SetupMissing,
        AdminDeclined,
    }

    /// <summary>
    /// One "Điều khiển trực tiếp" session as a pure state machine: the service tells it what happened and does the
    /// action it returns. Wi-Fi reconnects are serve-web's own (through tunneld). A cable session that loses the device
    /// stops serve-web and waits for the device to come back, restarting at most <see cref="MaxRestarts"/> times
    /// without reaching Ready. After <see cref="Stop"/> nothing restarts.
    /// </summary>
    public sealed class SessionMachine
    {
        public const int MaxRestarts = 3;

        private int _restarts;

        public SessionMachine(CoreDeviceRoute route)
        {
            if (route == CoreDeviceRoute.None) throw new ArgumentException("a session needs a route", nameof(route));
            Route = route;
        }

        public CoreDeviceRoute Route { get; }
        public SessionPhase Phase { get; private set; } = SessionPhase.Idle;
        public SessionFailure Failure { get; private set; }
        public bool StopRequested { get; private set; }

        public bool IsActive => Phase != SessionPhase.Idle && Phase != SessionPhase.Stopped && Phase != SessionPhase.Failed;

        /// <summary>serve-web may still run, so a stop has to go through its cleanup.</summary>
        public bool ServeWebMayRun =>
            Phase == SessionPhase.Starting || Phase == SessionPhase.Ready || Phase == SessionPhase.Reconnecting || Phase == SessionPhase.WaitingForDevice;

        public SessionAction Start()
        {
            if (Phase != SessionPhase.Idle) return SessionAction.None;
            if (Route == CoreDeviceRoute.Wifi)
            {
                Phase = SessionPhase.StartingTunnel;
                return SessionAction.StartTunnel;
            }
            Phase = SessionPhase.Preparing;
            return SessionAction.MountImage;
        }

        public SessionAction TunnelReady(TunnelOutcome outcome)
        {
            if (Phase != SessionPhase.StartingTunnel || StopRequested) return SessionAction.None;
            switch (outcome)
            {
                case TunnelOutcome.Ready:
                    Phase = SessionPhase.Preparing;
                    return SessionAction.MountImage;
                case TunnelOutcome.SetupMissing:
                    return Fail(SessionFailure.WindowsSetupMissing);
                case TunnelOutcome.AdminDeclined:
                    return Fail(SessionFailure.AdminDeclined);
                default:
                    return Fail(SessionFailure.TunnelTimeout);
            }
        }

        public SessionAction ImageMounted(bool ok, CliError error)
        {
            if (Phase != SessionPhase.Preparing || StopRequested) return SessionAction.None;
            if (!ok) return Fail(FromCli(error));
            Phase = SessionPhase.Starting;
            return SessionAction.StartServeWeb;
        }

        public SessionAction ServeWebReady()
        {
            if (Phase != SessionPhase.Starting || StopRequested) return SessionAction.None;
            Phase = SessionPhase.Ready;
            _restarts = 0;
            return SessionAction.None;
        }

        /// <summary>serve-web did not become ready; its job still has to be stopped.</summary>
        public SessionAction ServeWebFailed(ServeWebLogKind lastFatal, bool timedOut)
        {
            if (Phase != SessionPhase.Starting || StopRequested) return SessionAction.None;
            return Fail(timedOut ? SessionFailure.StartTimeout : FromServeWeb(lastFatal), SessionAction.StopServeWeb);
        }

        public SessionAction Log(ServeWebLogKind kind)
        {
            if (StopRequested) return SessionAction.None;
            if (kind == ServeWebLogKind.DeviceDisconnected && Phase == SessionPhase.Ready) Phase = SessionPhase.Reconnecting;
            else if (kind == ServeWebLogKind.DeviceReconnected && Phase == SessionPhase.Reconnecting) Phase = SessionPhase.Ready;
            return SessionAction.None;
        }

        /// <summary>The cable session's device left usbmux while serve-web was starting or running.</summary>
        public SessionAction UsbDeviceGone()
        {
            if (Route != CoreDeviceRoute.Usb || StopRequested) return SessionAction.None;
            if (Phase != SessionPhase.Starting && Phase != SessionPhase.Ready) return SessionAction.None;
            Phase = SessionPhase.WaitingForDevice;
            return SessionAction.StopServeWeb;
        }

        /// <summary>serve-web's job is empty: it exited on its own, or a stop this machine asked for finished.</summary>
        public SessionAction ServeWebExited(ServeWebLogKind lastFatal)
        {
            if (StopRequested) return SessionAction.None;
            if (Phase == SessionPhase.WaitingForDevice) return SessionAction.WatchUsb;
            if (Phase != SessionPhase.Ready && Phase != SessionPhase.Reconnecting) return SessionAction.None;
            if (lastFatal == ServeWebLogKind.MediaInUse) return Fail(SessionFailure.MediaInUse);
            if (Route == CoreDeviceRoute.Usb)
            {
                Phase = SessionPhase.WaitingForDevice;
                return SessionAction.WatchUsb;
            }
            return Fail(SessionFailure.Exited);
        }

        public SessionAction UsbDeviceBack()
        {
            if (Phase != SessionPhase.WaitingForDevice || StopRequested) return SessionAction.None;
            if (++_restarts > MaxRestarts) return Fail(SessionFailure.KeepsExiting);
            Phase = SessionPhase.Preparing;
            return SessionAction.MountImage;
        }

        public SessionAction Stop()
        {
            if (!IsActive) return SessionAction.None;
            var serveWeb = ServeWebMayRun;
            StopRequested = true;
            Phase = SessionPhase.Stopping;
            return serveWeb ? SessionAction.StopServeWeb : SessionAction.None;
        }

        public void StopFinished()
        {
            if (Phase == SessionPhase.Stopping) Phase = SessionPhase.Stopped;
        }

        private SessionAction Fail(SessionFailure failure, SessionAction action = SessionAction.None)
        {
            Failure = failure;
            Phase = SessionPhase.Failed;
            return action;
        }

        public static SessionFailure FromCli(CliError error)
        {
            switch (error)
            {
                case CliError.NotConnected: return SessionFailure.DeviceNotFound;
                case CliError.NeedsTrust:
                case CliError.TrustDenied: return SessionFailure.NeedsTrust;
                case CliError.DeviceLocked: return SessionFailure.DeviceLocked;
                case CliError.DeveloperModeOff:
                case CliError.ServiceUnavailable: return SessionFailure.DeveloperSetup;
                case CliError.NoInternet: return SessionFailure.NoInternet;
                case CliError.NoUsbmux: return SessionFailure.NoAppleService;
                default: return SessionFailure.Other;
            }
        }

        internal static SessionFailure FromServeWeb(ServeWebLogKind kind)
        {
            switch (kind)
            {
                case ServeWebLogKind.MediaInUse: return SessionFailure.MediaInUse;
                case ServeWebLogKind.ServiceUnavailable: return SessionFailure.DeveloperSetup;
                case ServeWebLogKind.DeviceNotFound: return SessionFailure.DeviceNotFound;
                default: return SessionFailure.Other;
            }
        }
    }
}
```

- [ ] **Step 4: Chạy, mong đợi PASS.**

- [ ] **Step 5: Checkpoint** — File: 1 nguồn, 1 test. Toàn bộ test PASS.

### Task C7: `SetupSteps` và `CoreDeviceMessages`

**Files:**
- Create: `src/MacExtend.Core/CoreDevice/SetupSteps.cs`, `src/MacExtend.Core/CoreDevice/CoreDeviceMessages.cs`
- Test: `tests/MacExtend.Core.Tests/CoreDevice/SetupStepsTests.cs`, `tests/MacExtend.Core.Tests/CoreDevice/CoreDeviceMessagesTests.cs`

**Interfaces:**
- Produces: `enum SetupStepId { Components, AppleService, Device, Trust, DeveloperMode, DiskImage, Wifi }`, `enum SetupStatus { Done, Todo, Waiting, Optional }`, `sealed class SetupFacts` (trường public `RuntimeInstalled`, `WindowsSetupDone`, `AppleServiceReachable`, `UsbUdid`, `Trusted` (bool?), `DeveloperMode` (bool?), `DiskImageMounted`, `WifiWanted`, `WifiPaired`), `sealed class SetupStep { SetupStepId Id; SetupStatus Status; string Title; string Detail; }`, `SetupSteps.Evaluate(SetupFacts) : IReadOnlyList<SetupStep>`, `SetupSteps.Ready(IEnumerable<SetupStep>) : bool`; `CoreDeviceMessages.Route(CoreDeviceRoute)`, `.Phase(SessionPhase)`, `.Failure(SessionFailure)`, `.NeedsSetup(SessionFailure) : bool`.

- [ ] **Step 1: Test thất bại**

```csharp
using System.Linq;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class SetupStepsTests
{
    private const string Udid = "00008030-001E185E343B802E";

    private static SetupFacts AllDone() => new SetupFacts
    {
        RuntimeInstalled = true,
        WindowsSetupDone = true,
        AppleServiceReachable = true,
        UsbUdid = Udid,
        Trusted = true,
        DeveloperMode = true,
        DiskImageMounted = true,
    };

    private static SetupStatus Status(SetupFacts f, SetupStepId id) => SetupSteps.Evaluate(f).Single(s => s.Id == id).Status;

    [Fact]
    public void EverythingDoneIsReadyAndWifiStaysOptional()
    {
        var steps = SetupSteps.Evaluate(AllDone());

        Assert.True(SetupSteps.Ready(steps));
        Assert.Equal(SetupStatus.Optional, steps.Single(s => s.Id == SetupStepId.Wifi).Status);
        Assert.Equal(7, steps.Count);
    }

    [Fact]
    public void NothingDoneStartsWithTheComponentsAndTheAppleService()
    {
        var f = new SetupFacts();

        Assert.Equal(SetupStatus.Todo, Status(f, SetupStepId.Components));
        Assert.Equal(SetupStatus.Todo, Status(f, SetupStepId.AppleService));
        Assert.Equal(SetupStatus.Waiting, Status(f, SetupStepId.Device));
        Assert.Equal(SetupStatus.Waiting, Status(f, SetupStepId.Trust));
        Assert.Equal(SetupStatus.Waiting, Status(f, SetupStepId.DeveloperMode));
        Assert.Equal(SetupStatus.Waiting, Status(f, SetupStepId.DiskImage));
        Assert.False(SetupSteps.Ready(SetupSteps.Evaluate(f)));
    }

    [Fact]
    public void EachDeviceStepWaitsForTheOneBefore()
    {
        var f = AllDone();
        f.Trusted = null;
        f.DeveloperMode = null;
        f.DiskImageMounted = false;

        Assert.Equal(SetupStatus.Todo, Status(f, SetupStepId.Trust));
        Assert.Equal(SetupStatus.Waiting, Status(f, SetupStepId.DeveloperMode));
        Assert.Equal(SetupStatus.Waiting, Status(f, SetupStepId.DiskImage));

        f.Trusted = true;
        f.DeveloperMode = false;
        Assert.Equal(SetupStatus.Todo, Status(f, SetupStepId.DeveloperMode));

        f.DeveloperMode = true;
        Assert.Equal(SetupStatus.Todo, Status(f, SetupStepId.DiskImage));
    }

    [Fact]
    public void WifiWantedNeedsPairing()
    {
        var f = AllDone();
        f.WifiWanted = true;

        Assert.Equal(SetupStatus.Todo, Status(f, SetupStepId.Wifi));
        Assert.False(SetupSteps.Ready(SetupSteps.Evaluate(f)));

        f.WifiPaired = true;
        Assert.Equal(SetupStatus.Done, Status(f, SetupStepId.Wifi));
    }

    [Fact]
    public void EveryStepHasVietnameseText()
    {
        foreach (var step in SetupSteps.Evaluate(new SetupFacts()).Concat(SetupSteps.Evaluate(AllDone())))
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Title));
            Assert.False(string.IsNullOrWhiteSpace(step.Detail));
        }
    }
}
```

```csharp
using System;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class CoreDeviceMessagesTests
{
    [Fact]
    public void EveryPhaseAndFailureHasAMessage()
    {
        foreach (SessionPhase phase in Enum.GetValues(typeof(SessionPhase))) Assert.False(string.IsNullOrWhiteSpace(CoreDeviceMessages.Phase(phase)));
        foreach (SessionFailure failure in Enum.GetValues(typeof(SessionFailure)))
        {
            if (failure == SessionFailure.None) continue;
            Assert.False(string.IsNullOrWhiteSpace(CoreDeviceMessages.Failure(failure)));
        }
    }

    [Fact]
    public void TheCameraMessageSaysWhatToClose()
    {
        Assert.Contains("Camera", CoreDeviceMessages.Failure(SessionFailure.MediaInUse));
    }

    [Fact]
    public void SetupCanFixTrustDeveloperModeAndWindows()
    {
        Assert.True(CoreDeviceMessages.NeedsSetup(SessionFailure.NeedsTrust));
        Assert.True(CoreDeviceMessages.NeedsSetup(SessionFailure.DeveloperSetup));
        Assert.True(CoreDeviceMessages.NeedsSetup(SessionFailure.WindowsSetupMissing));
        Assert.True(CoreDeviceMessages.NeedsSetup(SessionFailure.NoAppleService));
        Assert.False(CoreDeviceMessages.NeedsSetup(SessionFailure.MediaInUse));
    }

    [Fact]
    public void RoutesHaveLabels()
    {
        Assert.Equal("Qua cáp", CoreDeviceMessages.Route(CoreDeviceRoute.Usb));
        Assert.Equal("Qua Wi-Fi", CoreDeviceMessages.Route(CoreDeviceRoute.Wifi));
    }
}
```

- [ ] **Step 2: Chạy, mong đợi FAIL.**

- [ ] **Step 3: `SetupSteps.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace MacExtend.Core.CoreDevice
{
    public enum SetupStepId
    {
        Components,
        AppleService,
        Device,
        Trust,
        DeveloperMode,
        DiskImage,
        Wifi,
    }

    public enum SetupStatus
    {
        Done,
        Todo,
        /// <summary>Needs an earlier step first.</summary>
        Waiting,
        Optional,
    }

    /// <summary>What the setup window found; each step is evaluated from these alone.</summary>
    public sealed class SetupFacts
    {
        public bool RuntimeInstalled;
        /// <summary>Scheduled Task and firewall rules in place (always true for a portable copy, which uses neither).</summary>
        public bool WindowsSetupDone;
        public bool AppleServiceReachable;
        /// <summary>The cable device being set up; null when none is plugged in.</summary>
        public string UsbUdid;
        public bool? Trusted;
        public bool? DeveloperMode;
        public bool DiskImageMounted;
        public bool WifiWanted;
        public bool WifiPaired;
    }

    public sealed class SetupStep
    {
        public SetupStep(SetupStepId id, SetupStatus status, string title, string detail)
        {
            Id = id;
            Status = status;
            Title = title;
            Detail = detail;
        }

        public SetupStepId Id { get; }
        public SetupStatus Status { get; }
        public string Title { get; }
        public string Detail { get; }
    }

    public static class SetupSteps
    {
        public static IReadOnlyList<SetupStep> Evaluate(SetupFacts f)
        {
            var components = f.RuntimeInstalled && f.WindowsSetupDone;
            var canSeeDevices = f.RuntimeInstalled && f.AppleServiceReachable;
            var device = canSeeDevices && f.UsbUdid != null;
            var trusted = device && f.Trusted == true;
            var developer = trusted && f.DeveloperMode == true;

            return new[]
            {
                new SetupStep(SetupStepId.Components, components ? SetupStatus.Done : SetupStatus.Todo, "Thành phần điều khiển trực tiếp",
                    components ? "Đã cài." : "Tải khoảng 100 MB và cài một lần. Windows hỏi quyền quản trị đúng một lần."),
                new SetupStep(SetupStepId.AppleService, f.AppleServiceReachable ? SetupStatus.Done : SetupStatus.Todo, "Dịch vụ thiết bị của Apple",
                    f.AppleServiceReachable ? "Windows nói chuyện được với iPhone, iPad qua cáp."
                        : "Cài iTunes (apple.com) hoặc Apple Devices (Microsoft Store), rồi bấm Kiểm tra lại."),
                new SetupStep(SetupStepId.Device, !canSeeDevices ? SetupStatus.Waiting : device ? SetupStatus.Done : SetupStatus.Todo, "Cắm thiết bị",
                    device ? "Đã thấy thiết bị qua cáp." : "Cắm iPhone hoặc iPad bằng cáp và mở khóa màn hình."),
                new SetupStep(SetupStepId.Trust, !device ? SetupStatus.Waiting : trusted ? SetupStatus.Done : SetupStatus.Todo, "Tin cậy máy tính này",
                    trusted ? "Thiết bị đã tin cậy laptop." : "Bấm Kết nối, rồi chọn Tin cậy trên thiết bị và nhập mật mã."),
                new SetupStep(SetupStepId.DeveloperMode, !trusted ? SetupStatus.Waiting : developer ? SetupStatus.Done : SetupStatus.Todo, "Developer Mode",
                    developer ? "Đã bật." : "Bấm Hiện tùy chọn, rồi vào Cài đặt > Quyền riêng tư & Bảo mật > Chế độ nhà phát triển và bật lên. Thiết bị khởi động lại và hỏi xác nhận. Apple cảnh báo khi bật vì chế độ này nới một số lớp bảo vệ."),
                new SetupStep(SetupStepId.DiskImage, !developer ? SetupStatus.Waiting : f.DiskImageMounted ? SetupStatus.Done : SetupStatus.Todo, "Ảnh DDI",
                    f.DiskImageMounted ? "Đã gắn. App tự gắn lại trước mỗi phiên." : "Bấm Gắn ảnh. Lần đầu cần Internet."),
                new SetupStep(SetupStepId.Wifi,
                    !f.WifiWanted ? SetupStatus.Optional : !trusted ? SetupStatus.Waiting : f.WifiPaired ? SetupStatus.Done : SetupStatus.Todo,
                    "Điều khiển qua Wi-Fi",
                    !f.WifiWanted ? "Tùy chọn: bật để điều khiển không cần cáp, khi thiết bị và laptop cùng mạng Wi-Fi loại Riêng tư."
                        : f.WifiPaired ? "Đã ghép nối Wi-Fi." : "Bấm Ghép nối khi đang cắm cáp. Thiết bị không hiện hộp thoại nào."),
            };
        }

        public static bool Ready(IEnumerable<SetupStep> steps) => steps.All(s => s.Status == SetupStatus.Done || s.Status == SetupStatus.Optional);
    }
}
```

- [ ] **Step 4: `CoreDeviceMessages.cs`**

```csharp
namespace MacExtend.Core.CoreDevice
{
    /// <summary>The Vietnamese text the window, the tray and the settings page show for a session.</summary>
    public static class CoreDeviceMessages
    {
        public static string Route(CoreDeviceRoute route) =>
            route == CoreDeviceRoute.Usb ? "Qua cáp" : route == CoreDeviceRoute.Wifi ? "Qua Wi-Fi" : "";

        public static string Phase(SessionPhase phase)
        {
            switch (phase)
            {
                case SessionPhase.StartingTunnel: return "Đang tìm thiết bị qua Wi-Fi...";
                case SessionPhase.Preparing: return "Đang chuẩn bị thiết bị...";
                case SessionPhase.Starting: return "Đang kết nối...";
                case SessionPhase.Ready: return "Đang điều khiển";
                case SessionPhase.Reconnecting: return "Mất kết nối Wi-Fi, đang kết nối lại...";
                case SessionPhase.WaitingForDevice: return "Mất kết nối. Cắm lại cáp để tiếp tục.";
                case SessionPhase.Stopping: return "Đang dừng...";
                case SessionPhase.Stopped: return "Đã dừng";
                case SessionPhase.Failed: return "Không mở được phiên điều khiển";
                default: return "Chưa chạy";
            }
        }

        public static string Failure(SessionFailure failure)
        {
            switch (failure)
            {
                case SessionFailure.MediaInUse: return "Camera, micro hoặc một cuộc gọi đang được dùng trên thiết bị. Đóng Camera, Ghi âm hoặc cuộc gọi rồi thử lại.";
                case SessionFailure.DeviceNotFound: return "Không thấy thiết bị. Cắm cáp, hoặc để thiết bị cùng mạng Wi-Fi và mở khóa màn hình.";
                case SessionFailure.NeedsTrust: return "Thiết bị chưa tin cậy laptop. Mở khóa thiết bị, mở Thiết lập và làm bước Tin cậy.";
                case SessionFailure.DeviceLocked: return "Thiết bị đang khóa. Mở khóa thiết bị rồi thử lại.";
                case SessionFailure.DeveloperSetup: return "Thiết bị chưa sẵn sàng (Developer Mode hoặc ảnh DDI). Mở Thiết lập để làm lại bước này.";
                case SessionFailure.NoInternet: return "Cần Internet để tải ảnh DDI lần đầu. Kiểm tra mạng rồi thử lại.";
                case SessionFailure.NoAppleService: return "Thiếu dịch vụ thiết bị của Apple. Cài iTunes hoặc Apple Devices, rồi thử lại.";
                case SessionFailure.StartTimeout: return "Thiết bị không phản hồi trong 120 giây. Mở khóa thiết bị rồi thử lại.";
                case SessionFailure.TunnelTimeout: return "Không tìm thấy thiết bị qua Wi-Fi trong 30 giây. Kiểm tra: cùng mạng Wi-Fi, mạng là loại Riêng tư, màn hình thiết bị đang mở.";
                case SessionFailure.WindowsSetupMissing: return "Thiếu phần thiết lập Windows cho Wi-Fi. Mở Thiết lập và cài lại thành phần.";
                case SessionFailure.AdminDeclined: return "Cần quyền quản trị để điều khiển qua Wi-Fi. Thử lại và chọn Có khi Windows hỏi, hoặc cắm cáp.";
                case SessionFailure.KeepsExiting: return "Phiên điều khiển dừng liên tục. Rút cáp, mở khóa thiết bị, cắm lại rồi thử lại.";
                case SessionFailure.Exited: return "Phiên điều khiển dừng bất ngờ. Thử lại.";
                case SessionFailure.Other: return "Không mở được phiên điều khiển. Chi tiết ở nhật ký coredevice.log.";
                default: return "";
            }
        }

        /// <summary>Failures the setup window can fix; the window offers "Mở Thiết lập" for them.</summary>
        public static bool NeedsSetup(SessionFailure failure) =>
            failure == SessionFailure.NeedsTrust || failure == SessionFailure.DeveloperSetup
            || failure == SessionFailure.WindowsSetupMissing || failure == SessionFailure.NoAppleService;
    }
}
```

- [ ] **Step 5: Chạy hai lớp test, mong đợi PASS.**

- [ ] **Step 6: Checkpoint** — File: 2 nguồn, 2 test. Toàn bộ test PASS.

### Task C8: Ngăn runtime thứ hai trong cập nhật

**Files:**
- Modify: `src/MacExtend.Core/Updates/UpdateManifest.cs`, `UpdateDecision.cs`, `UpdateApplier.cs`, `UpdateStager.cs`, `FileHash.cs`
- Test: `tests/MacExtend.Core.Tests/Updates/UpdateManifestTests.cs`, `UpdateApplierTests.cs`, Create `tests/MacExtend.Core.Tests/Updates/UpdateStagerStreamTests.cs`

**Interfaces:**
- Produces: `UpdateManifest.CoreDevice : UpdatePackage`, `UpdateManifest.CoreDeviceId : string`, constructor 6 tham số (constructor 4 tham số vẫn còn); `UpdateDecision.NeedsCoreDevice : bool`, `UpdateDecision.For(Version current, string installedRuntimeId, UpdateManifest manifest, string installedCoreDeviceId = null)`; `UpdateApplier` thay `staging\coredevice` thành `{install}\coredevice`; `UpdateStager.Extract(Stream zip, string targetDir)`; `FileHash.Sha256(Stream)`; `UpdateManifest.IsPlainFileName` và `IsSha256` đổi thành `internal static`.

- [ ] **Step 1: Test thất bại**

Thêm vào `UpdateManifestTests`:

```csharp
    private static string WithCoreDevice() => Sample() +
        "coredevice.id=py3.13.9-abc\n" +
        "coredevice.file=MacExtend-1.0.60-coredevice-runtime.zip\n" +
        "coredevice.sha256=" + HashA + "\n" +
        "coredevice.size=95000000\n";

    [Fact]
    public void ParsesTheCoreDeviceRuntimeAndRoundTripsIt()
    {
        var m = UpdateManifest.Parse(WithCoreDevice());

        Assert.Equal("py3.13.9-abc", m.CoreDeviceId);
        Assert.Equal("MacExtend-1.0.60-coredevice-runtime.zip", m.CoreDevice.File);
        Assert.Equal(95000000, m.CoreDevice.Size);
        var again = UpdateManifest.Parse(UpdateManifest.Serialize(m));
        Assert.Equal(m.CoreDeviceId, again.CoreDeviceId);
        Assert.Equal(m.CoreDevice.Sha256, again.CoreDevice.Sha256);
    }

    [Fact]
    public void AnOldManifestHasNoCoreDeviceRuntime()
    {
        var m = UpdateManifest.Parse(Sample());

        Assert.Null(m.CoreDevice);
        Assert.Null(m.CoreDeviceId);
        Assert.DoesNotContain("coredevice", UpdateManifest.Serialize(m));
    }
```

Thêm vào `UpdateDecisionTests`:

```csharp
    private static UpdateManifest WithCoreDevice(string id) => UpdateManifest.Parse(
        "version=1.0.60\napp.file=a.zip\napp.sha256=" + Hash + "\napp.size=1000\n" +
        "runtime.id=rt-2\nruntime.file=r.zip\nruntime.sha256=" + Hash + "\nruntime.size=50000\n" +
        "coredevice.id=" + id + "\ncoredevice.file=c.zip\ncoredevice.sha256=" + Hash + "\ncoredevice.size=90000");

    [Fact]
    public void TheCoreDeviceRuntimeIsReplacedOnlyWhereItIsInstalledAndDiffers()
    {
        var installedOld = UpdateDecision.For(new Version(1, 0, 1), "rt-2", WithCoreDevice("cd-2"), "cd-1");
        Assert.True(installedOld.NeedsCoreDevice);
        Assert.Equal(91000, installedOld.DownloadBytes);

        Assert.False(UpdateDecision.For(new Version(1, 0, 1), "rt-2", WithCoreDevice("cd-2"), "cd-2").NeedsCoreDevice);
        Assert.False(UpdateDecision.For(new Version(1, 0, 1), "rt-2", WithCoreDevice("cd-2"), null).NeedsCoreDevice);
        Assert.False(UpdateDecision.For(new Version(1, 0, 1), "rt-2", Manifest("1.0.60"), "cd-1").NeedsCoreDevice);
    }
```

Thêm vào `UpdateApplierTests` (constructor đã tạo cây install mẫu):

```csharp
    [Fact]
    public void CoreDeviceRuntimeReplacesItsWholeFolder()
    {
        Write(Install, "coredevice/python/python.exe", "old python");
        Write(Install, "coredevice/RUNTIME_ID", "cd-1");
        Write(Staging, "coredevice/python/python.exe", "new python");
        Write(Staging, "coredevice/RUNTIME_ID", "cd-2");

        UpdateApplier.Apply(Staging, Install, _ => { });

        Assert.Equal("new python", Read("coredevice/python/python.exe"));
        Assert.Equal("cd-2", Read("coredevice/RUNTIME_ID"));
        Assert.Equal("old uxplay", Read("uxplay/bin/uxplay.exe"));
        Assert.False(Directory.Exists(Path.Combine(Install, "coredevice.old")));
    }
```

`tests/MacExtend.Core.Tests/Updates/UpdateStagerStreamTests.cs`:

```csharp
using System;
using System.IO;
using System.IO.Compression;
using MacExtend.Core.Updates;

namespace MacExtend.Core.Tests.Updates;

public sealed class UpdateStagerStreamTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "macextend-stager-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static MemoryStream Zip(params (string Name, string Text)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                using (var w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write(text);
            }
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void ExtractsFromAStream()
    {
        using (var zip = Zip(("python/python.exe", "py"), ("RUNTIME_ID", "cd-1")))
        {
            UpdateStager.Extract(zip, _root);
        }

        Assert.Equal("py", File.ReadAllText(Path.Combine(_root, "python", "python.exe")));
    }

    [Fact]
    public void RejectsEntriesOutsideTarget()
    {
        using (var zip = Zip(("../escape.txt", "x")))
        {
            Assert.Throws<InvalidDataException>(() => UpdateStager.Extract(zip, Path.Combine(_root, "t")));
        }
        Assert.False(File.Exists(Path.Combine(_root, "escape.txt")));
    }

    [Fact]
    public void HashesAStreamLikeAFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "abc");
        try
        {
            using (var s = File.OpenRead(path)) Assert.Equal(FileHash.Sha256(path), FileHash.Sha256(s));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
```

- [ ] **Step 2: Chạy `Updates`, mong đợi FAIL** (thành viên chưa có).

- [ ] **Step 3: `UpdateManifest`**

```csharp
        public UpdateManifest(Version version, UpdatePackage app, UpdatePackage runtime, string runtimeId)
            : this(version, app, runtime, runtimeId, null, null)
        {
        }

        public UpdateManifest(Version version, UpdatePackage app, UpdatePackage runtime, string runtimeId, UpdatePackage coreDevice, string coreDeviceId)
        {
            Version = AppVersion.Normalize(version);
            App = app;
            Runtime = runtime;
            RuntimeId = runtime == null ? null : runtimeId;
            CoreDevice = string.IsNullOrEmpty(coreDeviceId) ? null : coreDevice;
            CoreDeviceId = CoreDevice == null ? null : coreDeviceId;
        }

        /// <summary>Python + pymobiledevice3 for "Điều khiển trực tiếp"; optional, only replaced where installed.</summary>
        public UpdatePackage CoreDevice { get; }
        public string CoreDeviceId { get; }
```

`Parse`: trước `return`, thêm:

```csharp
            var coreDevice = Package(values, "coredevice");
            values.TryGetValue("coredevice.id", out var coreDeviceId);
            return new UpdateManifest(version, app, runtime, runtimeId, coreDevice, coreDeviceId);
```

`Serialize`: trước `return sb.ToString();`:

```csharp
            if (m.CoreDevice != null)
            {
                sb.Append("coredevice.id=").Append(m.CoreDeviceId).Append('\n');
                Write(sb, "coredevice", m.CoreDevice);
            }
```

`IsPlainFileName`, `IsSha256`: `private static` thành `internal static`.

- [ ] **Step 4: `UpdateDecision`**

```csharp
    public sealed class UpdateDecision
    {
        private UpdateDecision(bool available, bool needsRuntime, bool needsCoreDevice, long downloadBytes)
        {
            Available = available;
            NeedsRuntime = needsRuntime;
            NeedsCoreDevice = needsCoreDevice;
            DownloadBytes = downloadBytes;
        }

        public bool Available { get; }
        /// <summary>The installed uxplay/GStreamer runtime differs from the release's and must be replaced too.</summary>
        public bool NeedsRuntime { get; }
        /// <summary>The CoreDevice runtime is installed and differs from the release's.</summary>
        public bool NeedsCoreDevice { get; }
        public long DownloadBytes { get; }

        public static UpdateDecision For(Version current, string installedRuntimeId, UpdateManifest manifest, string installedCoreDeviceId = null)
        {
            if (manifest == null || manifest.Version <= AppVersion.Normalize(current)) return new UpdateDecision(false, false, false, 0);
            var runtime = manifest.Runtime != null && !string.Equals(installedRuntimeId, manifest.RuntimeId, StringComparison.Ordinal);
            // optional: never downloaded for a machine that did not install it
            var coreDevice = manifest.CoreDevice != null && !string.IsNullOrEmpty(installedCoreDeviceId)
                             && !string.Equals(installedCoreDeviceId, manifest.CoreDeviceId, StringComparison.Ordinal);
            var bytes = manifest.App.Size + (runtime ? manifest.Runtime.Size : 0) + (coreDevice ? manifest.CoreDevice.Size : 0);
            return new UpdateDecision(true, runtime, coreDevice, bytes);
        }
    }
```

- [ ] **Step 5: `UpdateApplier`**

Thay hằng `RuntimeFolder` bằng danh sách ngăn và tổng quát hóa phần runtime:

```csharp
        private const string Aside = ".old";

        // staging folder -> installed folder; each is replaced as a whole when staged
        private static readonly string[][] RuntimeSlots = { new[] { "runtime", "uxplay" }, new[] { "coredevice", "coredevice" } };
```

Trong `Apply`, bỏ các biến `runtimeSource/runtimeTarget/runtimeAside/runtimeSwapped`; thay bằng:

```csharp
            var swapped = new List<string>();
```

và khối runtime trong `try`:

```csharp
                foreach (var slot in RuntimeSlots)
                {
                    var source = Path.Combine(stagingDir, slot[0]);
                    if (!Directory.Exists(source)) continue;
                    var target = Path.Combine(installDir, slot[1]);
                    if (Directory.Exists(target)) Retry(() => Directory.Move(target, target + Aside));
                    swapped.Add(target);
                    CopyTree(source, target);
                    log(slot[0] == "runtime" ? "runtime replaced" : slot[1] + " runtime replaced");
                }
```

`catch`: `Rollback(movedAside, added, swapped, log);`. Sau `try`: `foreach (var target in swapped) TryDeleteDirectory(target + Aside);` thay cho `TryDeleteDirectory(runtimeAside);`. `CleanupLeftovers`: `foreach (var slot in RuntimeSlots) TryDeleteDirectory(Path.Combine(installDir, slot[1] + Aside));`. `Rollback`:

```csharp
        private static void Rollback(List<string> movedAside, List<string> added, List<string> swapped, Action<string> log)
        {
            foreach (var target in added) TryDeleteFile(target);
            foreach (var target in movedAside)
            {
                TryDeleteFile(target);
                Try(() => Retry(() => File.Move(target + Aside, target)), log);
            }
            foreach (var target in swapped)
            {
                TryDeleteDirectory(target);
                if (Directory.Exists(target + Aside)) Try(() => Retry(() => Directory.Move(target + Aside, target)), log);
            }
        }
```

Sửa doc comment đầu lớp: "staging\runtime\** as the new uxplay folder and staging\coredevice\** as the new coredevice folder".

- [ ] **Step 6: `UpdateStager` và `FileHash`**

```csharp
        public static void Extract(string zipPath, string targetDir)
        {
            using (var stream = File.OpenRead(zipPath)) Extract(stream, targetDir);
        }

        /// <summary>Same checks as <see cref="Extract(string, string)"/>, from an open stream (the caller may have hashed it first).</summary>
        public static void Extract(Stream zipStream, string targetDir)
        {
            var root = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true))
            {
                // ... thân hiện có, giữ nguyên từ foreach kiểm tra đầu tiên
            }
        }
```

(Chuyển nguyên thân hiện có vào overload `Stream`; `ZipFile.OpenRead` thay bằng `new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true)`.)

`FileHash`:

```csharp
        public static string Sha256(string path)
        {
            using (var stream = File.OpenRead(path)) return Sha256(stream);
        }

        /// <returns>Lowercase hex SHA-256 of the rest of <paramref name="stream"/>.</returns>
        public static string Sha256(Stream stream)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
        }
```

- [ ] **Step 7: Chạy `FullyQualifiedName~Updates`, mong đợi PASS (cả test cũ).**

- [ ] **Step 8: Checkpoint** — File: 5 nguồn, 3 test sửa, 1 test mới. Toàn bộ test PASS.

### Task C9: Script thiết lập Windows

**Files:**
- Create: `src/MacExtend.Core/CoreDevice/CoreDeviceSetupScripts.cs`
- Test: `tests/MacExtend.Core.Tests/CoreDevice/CoreDeviceSetupScriptsTests.cs`

**Interfaces:**
- Produces: hằng `TaskPath = @"\MacExtend\"`, `TaskName = "MacExtend CoreDevice tunnel"`, `TaskFullName`, `FirewallGroup = "MacExtend CoreDevice"`, `TunneldArg = "--coredevice-tunneld"`; `RegisterTask(string exePath, string userName, string userProfile)`, `RemoveTask()`, `AddFirewallRules(string pythonPath)`, `RemoveFirewallRules()`, `Query()`, `ParseQuery(string output) : SetupQuery` với `SetupQuery { int Rules; bool Task; }`.

Nếu Task 0 cho thấy `Get-ScheduledTask` không thấy task từ tài khoản thường nhưng `schtasks /Query` thì thấy, `Query()` dùng dòng `$task = if ((& schtasks.exe /Query /TN '\MacExtend\MacExtend CoreDevice tunnel' 2>$null) -and $LASTEXITCODE -eq 0) { 1 } else { 0 }` thay cho dòng `Get-ScheduledTask` dưới đây.

- [ ] **Step 1: Test thất bại**

```csharp
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class CoreDeviceSetupScriptsTests
{
    [Fact]
    public void RegistersAnOnDemandTaskForTheUserWithHighestPrivileges()
    {
        var script = CoreDeviceSetupScripts.RegisterTask(@"C:\Program Files\MacExtend\MacExtend.exe", @"LEGION\thinh", @"C:\Users\thinh");

        Assert.StartsWith("$ErrorActionPreference = 'Stop'", script);
        Assert.Contains("-Execute 'C:\\Program Files\\MacExtend\\MacExtend.exe'", script);
        Assert.Contains("-Argument '--coredevice-tunneld C:\\Users\\thinh'", script);
        Assert.Contains("-UserId 'LEGION\\thinh' -LogonType Interactive -RunLevel Highest", script);
        Assert.Contains("-TaskPath '\\MacExtend\\' -TaskName 'MacExtend CoreDevice tunnel'", script);
        Assert.DoesNotContain("-Trigger", script);
    }

    [Fact]
    public void KeepsUnicodeAndQuotesInTheTaskArgument()
    {
        var script = CoreDeviceSetupScripts.RegisterTask(@"C:\Program Files\MacExtend\MacExtend.exe", @"LEGION\Thịnh O'Hải", @"C:\Users\Thịnh O'Hải");

        Assert.Contains("-Argument '--coredevice-tunneld \"C:\\Users\\Thịnh O''Hải\"'", script);
        Assert.Contains("-UserId 'LEGION\\Thịnh O''Hải'", script);
    }

    [Fact]
    public void FirewallRulesAreScopedToTheRuntimePython()
    {
        var script = CoreDeviceSetupScripts.AddFirewallRules(@"C:\Program Files\MacExtend\coredevice\python\python.exe");

        Assert.Contains("Get-NetFirewallRule -Group 'MacExtend CoreDevice' -ErrorAction SilentlyContinue | Remove-NetFirewallRule", script);
        Assert.Contains("-Protocol UDP -RemoteAddress 'fc00::/7' -Program 'C:\\Program Files\\MacExtend\\coredevice\\python\\python.exe' -Profile Any", script);
        Assert.Contains("-Protocol UDP -LocalPort 5353 -Program 'C:\\Program Files\\MacExtend\\coredevice\\python\\python.exe' -Profile Private,Domain", script);
    }

    [Theory]
    [InlineData("rules=2 task=1\r\n", 2, true)]
    [InlineData("rules=0 task=0", 0, false)]
    [InlineData("", 0, false)]
    [InlineData("garbage", 0, false)]
    public void ReadsTheQuery(string output, int rules, bool task)
    {
        var state = CoreDeviceSetupScripts.ParseQuery(output);

        Assert.Equal(rules, state.Rules);
        Assert.Equal(task, state.Task);
    }
}
```

- [ ] **Step 2: Chạy, mong đợi FAIL.**

- [ ] **Step 3: Viết**

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace MacExtend.Core.CoreDevice
{
    public struct SetupQuery
    {
        public int Rules;
        public bool Task;
    }

    /// <summary>
    /// The PowerShell that sets Windows up for Wi-Fi control (run elevated by MacExtend.exe --coredevice-setup) and
    /// undoes it (--coredevice-remove, the uninstaller). Every value is a single-quoted literal (PowerShellText).
    /// </summary>
    public static class CoreDeviceSetupScripts
    {
        public const string TaskPath = @"\MacExtend\";
        public const string TaskName = "MacExtend CoreDevice tunnel";
        public const string TaskFullName = TaskPath + TaskName;
        public const string FirewallGroup = "MacExtend CoreDevice";
        public const string TunneldArg = "--coredevice-tunneld";

        private const string Stop = "$ErrorActionPreference = 'Stop'";

        /// <summary>
        /// The on-demand task that starts tunneld elevated without a UAC prompt: the user's own account, highest
        /// privileges, only while that user is logged on, no trigger. Its command line is fixed here, so starting it
        /// (all a normal user can do) never runs anything else.
        /// </summary>
        public static string RegisterTask(string exePath, string userName, string userProfile)
        {
            var argument = WindowsCommandLine.Join(new[] { TunneldArg, userProfile });
            return string.Join("\n",
                Stop,
                "$action = New-ScheduledTaskAction -Execute " + PowerShellText.Literal(exePath) + " -Argument " + PowerShellText.Literal(argument),
                "$principal = New-ScheduledTaskPrincipal -UserId " + PowerShellText.Literal(userName) + " -LogonType Interactive -RunLevel Highest",
                "$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew",
                "Register-ScheduledTask -TaskPath " + PowerShellText.Literal(TaskPath) + " -TaskName " + PowerShellText.Literal(TaskName)
                    + " -Action $action -Principal $principal -Settings $settings -Force | Out-Null");
        }

        public static string RemoveTask() =>
            "Get-ScheduledTask -TaskPath " + PowerShellText.Literal(TaskPath) + " -TaskName " + PowerShellText.Literal(TaskName)
            + " -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false";

        /// <summary>
        /// Video arrives as unsolicited UDP on the tunnel adapter (Public, a new network for every tunnel), and Bonjour
        /// answers on private Wi-Fi; both only for the runtime's python.exe. Having rules for that program also keeps
        /// Windows from asking, and from adding its own Block rules for Public (what run 1 ran into).
        /// </summary>
        public static string AddFirewallRules(string pythonPath)
        {
            var group = PowerShellText.Literal(FirewallGroup);
            var program = PowerShellText.Literal(pythonPath);
            return string.Join("\n",
                Stop,
                "Get-NetFirewallRule -Group " + group + " -ErrorAction SilentlyContinue | Remove-NetFirewallRule",
                "New-NetFirewallRule -Group " + group + " -DisplayName 'MacExtend CoreDevice video over tunnel' -Direction Inbound -Action Allow -Protocol UDP -RemoteAddress 'fc00::/7' -Program " + program + " -Profile Any | Out-Null",
                "New-NetFirewallRule -Group " + group + " -DisplayName 'MacExtend CoreDevice Wi-Fi discovery' -Direction Inbound -Action Allow -Protocol UDP -LocalPort 5353 -Program " + program + " -Profile Private,Domain | Out-Null");
        }

        public static string RemoveFirewallRules() =>
            "Get-NetFirewallRule -Group " + PowerShellText.Literal(FirewallGroup) + " -ErrorAction SilentlyContinue | Remove-NetFirewallRule";

        /// <summary>Unelevated check; prints "rules=N task=0|1".</summary>
        public static string Query() => string.Join("\n",
            "$rules = @(Get-NetFirewallRule -Group " + PowerShellText.Literal(FirewallGroup) + " -ErrorAction SilentlyContinue).Count",
            "$task = if (Get-ScheduledTask -TaskPath " + PowerShellText.Literal(TaskPath) + " -TaskName " + PowerShellText.Literal(TaskName) + " -ErrorAction SilentlyContinue) { 1 } else { 0 }",
            "\"rules=$rules task=$task\"");

        public static SetupQuery ParseQuery(string output)
        {
            var m = Regex.Match(output ?? "", @"rules=(\d+) task=([01])", RegexOptions.CultureInvariant);
            if (!m.Success) return new SetupQuery();
            return new SetupQuery
            {
                Rules = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                Task = m.Groups[2].Value == "1",
            };
        }
    }
}
```

- [ ] **Step 4: Chạy, mong đợi PASS.**

- [ ] **Step 5: Checkpoint** — File: 1 nguồn, 1 test. Toàn bộ test PASS.

### Task C10: Tham số cho lệnh ngắn và tunneld

**Files:**
- Modify: `src/MacExtend.Core/CoreDevice/CoreDeviceArguments.cs`
- Test: `tests/MacExtend.Core.Tests/CoreDevice/CoreDeviceArgumentsTunneldTests.cs`

**Interfaces:**
- Produces: `CoreDeviceArguments.Command(params string[] words) : IReadOnlyList<string>` (`-I -X utf8 -u -m pymobiledevice3 <words>`), `CoreDeviceArguments.Tunneld() : IReadOnlyList<string>`, `CoreDeviceArguments.CommandEnvironment(IDictionary inherited, string udid) : IDictionary<string, string>` (udid null thì không đặt `PYMOBILEDEVICE3_UDID`), `CoreDeviceArguments.TunneldEnvironment(IDictionary inherited, string userProfile)`. `Environment(...)` hiện có giữ nguyên hành vi.

- [ ] **Step 1: Test thất bại**

```csharp
using System.Collections;
using System.Linq;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class CoreDeviceArgumentsTunneldTests
{
    [Fact]
    public void TunneldKeepsOnlyRemotePairing()
    {
        Assert.Equal(new[] { "-I", "-X", "utf8", "-u", "-m", "pymobiledevice3", "remote", "tunneld", "--no-usb", "--no-usbmux", "--no-mobdev2" },
                     CoreDeviceArguments.Tunneld());
    }

    [Fact]
    public void CommandsUseTheSameInterpreterFlags()
    {
        Assert.Equal(new[] { "-I", "-X", "utf8", "-u", "-m", "pymobiledevice3", "usbmux", "list", "--usb", "--simple" },
                     CoreDeviceArguments.Command("usbmux", "list", "--usb", "--simple"));
    }

    [Fact]
    public void CommandEnvironmentScrubsUpstreamVariablesAndSetsTheDevice()
    {
        var inherited = new Hashtable { { "PATH", "C:\\Windows" }, { "PYMOBILEDEVICE3_TUNNEL", "x" }, { "FORCE_COLOR", "1" } };

        var env = CoreDeviceArguments.CommandEnvironment(inherited, "00008030-001E185E343B802E");

        Assert.Equal("C:\\Windows", env["PATH"]);
        Assert.Equal("00008030-001E185E343B802E", env["PYMOBILEDEVICE3_UDID"]);
        Assert.Equal("1", env["NO_COLOR"]);
        Assert.DoesNotContain("PYMOBILEDEVICE3_TUNNEL", env.Keys);
        Assert.DoesNotContain("FORCE_COLOR", env.Keys);
        Assert.DoesNotContain("PYMOBILEDEVICE3_UDID", CoreDeviceArguments.CommandEnvironment(inherited, null).Keys);
    }

    [Fact]
    public void SetsTheUserProfile()
    {
        var inherited = new Hashtable { { "USERPROFILE", "C:\\Users\\Admin" }, { "HOME", "C:\\Users\\Admin" } };

        var env = CoreDeviceArguments.TunneldEnvironment(inherited, @"C:\Users\Thịnh O'Hải");

        Assert.Equal(@"C:\Users\Thịnh O'Hải", env["USERPROFILE"]);
        Assert.DoesNotContain("HOME", env.Keys);
        Assert.DoesNotContain("PYMOBILEDEVICE3_UDID", env.Keys);
    }
}
```

- [ ] **Step 2: Chạy, mong đợi FAIL.**

- [ ] **Step 3: Sửa `CoreDeviceArguments`**

Thêm:

```csharp
        /// <summary>A short pymobiledevice3 command with the same interpreter flags as serve-web.</summary>
        public static IReadOnlyList<string> Command(params string[] words)
        {
            var args = new List<string>(InterpreterFlags) { "-m", "pymobiledevice3" };
            args.AddRange(words ?? new string[0]);
            return args;
        }

        /// <summary>
        /// tunneld for Wi-Fi only: its RemotePairing monitor ("--wifi") stays on; USB, usbmux and mobdev2 are off (cable
        /// sessions use serve-web's userspace tunnel, and the mobdev2/lockdown route is refused by iOS 27.2). On Python
        /// 3.13 its tunnels are TCP, which iOS 18.2+ requires.
        /// </summary>
        public static IReadOnlyList<string> Tunneld() => Command("remote", "tunneld", "--no-usb", "--no-usbmux", "--no-mobdev2");

        public static IDictionary<string, string> CommandEnvironment(IDictionary inherited, string udid)
        {
            var env = Scrub(inherited);
            if (udid != null) env[UdidVariable] = udid;
            return env;
        }

        /// <summary>
        /// tunneld runs elevated, possibly as another (administrator) account; pymobiledevice3 reads its RemotePairing
        /// records from Path.home()\.pymobiledevice3, which on Windows is USERPROFILE, so that is the user's own.
        /// </summary>
        public static IDictionary<string, string> TunneldEnvironment(IDictionary inherited, string userProfile)
        {
            var env = Scrub(inherited);
            env.Remove("HOME");
            env["USERPROFILE"] = userProfile;
            return env;
        }
```

Đổi `Environment` để dùng chung phần lọc:

```csharp
        public static IDictionary<string, string> Environment(IDictionary inherited, CoreDeviceOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var env = Scrub(inherited);
            env[UdidVariable] = options.Udid;
            return env;
        }

        /// <summary>
        /// The inherited environment without any PYMOBILEDEVICE3_* variable (any of them could pick another transport or
        /// device) or FORCE_COLOR, with NO_COLOR=1 so status lines and JSON parse.
        /// </summary>
        private static Dictionary<string, string> Scrub(IDictionary inherited)
        {
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (inherited != null)
            {
                foreach (DictionaryEntry entry in inherited)
                {
                    var name = entry.Key as string;
                    var value = entry.Value as string ?? "";
                    if (string.IsNullOrEmpty(name) || name.IndexOf('=', 1) >= 0 || name.IndexOf('\0') >= 0 || value.IndexOf('\0') >= 0) continue;
                    if (name.StartsWith(UpstreamPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(name, "FORCE_COLOR", StringComparison.OrdinalIgnoreCase)) continue;
                    env[name] = value;
                }
            }
            env["NO_COLOR"] = "1";
            return env;
        }
```

- [ ] **Step 4: Chạy `CoreDeviceArguments`, mong đợi PASS (cả `CoreDeviceArgumentsTests` cũ).**

- [ ] **Step 5: Checkpoint** — File: 1 nguồn, 1 test. Toàn bộ test PASS.

### Task C11: `RuntimePackage`

**Files:**
- Create: `src/MacExtend.Core/CoreDevice/RuntimePackage.cs`
- Test: `tests/MacExtend.Core.Tests/CoreDevice/RuntimePackageTests.cs`

**Interfaces:**
- Produces: `RuntimePackage { string Id; string File; string Sha256; long Size; }`, `RuntimePackage.FromMetadata(Func<string, string> get) : RuntimePackage` (null khi thiếu hoặc sai; khóa `CoreDeviceRuntimeId`, `CoreDeviceRuntimeFile`, `CoreDeviceRuntimeSha256`, `CoreDeviceRuntimeSize`), `RuntimePackage.Matches(string sha256) : bool`.

- [ ] **Step 1: Test thất bại**

```csharp
using System.Collections.Generic;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class RuntimePackageTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static Dictionary<string, string> Good() => new Dictionary<string, string>
    {
        ["CoreDeviceRuntimeId"] = "py3.13.9-abc",
        ["CoreDeviceRuntimeFile"] = "MacExtend-1.0.70-coredevice-runtime.zip",
        ["CoreDeviceRuntimeSha256"] = Hash.ToUpperInvariant(),
        ["CoreDeviceRuntimeSize"] = "95000000",
    };

    [Fact]
    public void ReadsTheEmbeddedRuntime()
    {
        var meta = Good();
        var p = RuntimePackage.FromMetadata(k => meta.TryGetValue(k, out var v) ? v : null);

        Assert.Equal("py3.13.9-abc", p.Id);
        Assert.Equal(Hash, p.Sha256);
        Assert.Equal(95000000, p.Size);
        Assert.True(p.Matches(Hash.ToUpperInvariant()));
        Assert.False(p.Matches(new string('0', 64)));
    }

    [Theory]
    [InlineData("CoreDeviceRuntimeId", "")]
    [InlineData("CoreDeviceRuntimeFile", "..\\evil.zip")]
    [InlineData("CoreDeviceRuntimeFile", "dir/x.zip")]
    [InlineData("CoreDeviceRuntimeSha256", "abc")]
    [InlineData("CoreDeviceRuntimeSize", "-1")]
    [InlineData("CoreDeviceRuntimeSize", "lots")]
    public void ALocalBuildOrABadValueHasNoRuntime(string key, string value)
    {
        var meta = Good();
        meta[key] = value;

        Assert.Null(RuntimePackage.FromMetadata(k => meta.TryGetValue(k, out var v) ? v : null));
    }
}
```

- [ ] **Step 2: Chạy, mong đợi FAIL.**

- [ ] **Step 3: Viết**

```csharp
using System;
using System.Globalization;
using MacExtend.Core.Updates;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>
    /// The CoreDevice runtime zip this exact app build was made with. CI embeds it as assembly metadata, so the app
    /// can check a zip it downloaded, or one the user picked from a CI artifact, without asking the network. Local
    /// builds have none.
    /// </summary>
    public sealed class RuntimePackage
    {
        private RuntimePackage(string id, string file, string sha256, long size)
        {
            Id = id;
            File = file;
            Sha256 = sha256;
            Size = size;
        }

        public string Id { get; }
        public string File { get; }
        /// <summary>Lowercase hex.</summary>
        public string Sha256 { get; }
        public long Size { get; }

        public static RuntimePackage FromMetadata(Func<string, string> get)
        {
            var id = get("CoreDeviceRuntimeId");
            var file = get("CoreDeviceRuntimeFile");
            var sha = get("CoreDeviceRuntimeSha256");
            var sizeText = get("CoreDeviceRuntimeSize");
            if (string.IsNullOrWhiteSpace(id) || file == null || !UpdateManifest.IsPlainFileName(file) || sha == null || !UpdateManifest.IsSha256(sha)) return null;
            if (!long.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size <= 0) return null;
            return new RuntimePackage(id.Trim(), file, sha.ToLowerInvariant(), size);
        }

        public bool Matches(string sha256) => string.Equals(Sha256, sha256, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Chạy, mong đợi PASS.**

- [ ] **Step 5: Checkpoint** — File: 1 nguồn, 1 test. Toàn bộ test PASS.

### Task C12: `ServeWebControl`

**Files:**
- Create: `src/MacExtend.Core/CoreDevice/ServeWebControl.cs`
- Test: `tests/MacExtend.Core.Tests/CoreDevice/ServeWebControlTests.cs`

**Interfaces:**
- Produces: `sealed class ServeWebControl : IDisposable` với `ServeWebControl(int port)`, `internal ServeWebControl(int port, HttpMessageHandler handler, TimeSpan timeout)`, `Task HomeAsync(CancellationToken)`, `Task SendTextAsync(string text, CancellationToken)`, `Task<string> GetTextAsync(CancellationToken)` (null khi clipboard thiết bị không có chữ), `internal static string JsonString(string)`. Lỗi: `CoreDeviceClientException`.

Endpoint đã kiểm trong `screen_stream.py`: `POST /button {"name":"home","state":"press"}`; `POST /clipboard {"text": "..."}` trả `{"ok":true}`; `GET /clipboard` trả `{"text": ..., "image": {...}|null}`. Request không có `Origin` được phục vụ (`_refuse_request` chỉ chặn `Origin` khác `Host`).

- [ ] **Step 1: Test thất bại**

```csharp
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class ServeWebControlTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public readonly List<(string Method, string Path, string Body)> Requests = new List<(string, string, string)>();
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Answer = "{\"ok\":true}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync();
            Requests.Add((request.Method.Method, request.RequestUri.AbsolutePath, body));
            return new HttpResponseMessage(Status) { Content = new StringContent(Answer) };
        }
    }

    [Fact]
    public async Task HomePressesTheButton()
    {
        var h = new Handler();
        using (var c = new ServeWebControl(8080, h, TimeSpan.FromSeconds(1))) await c.HomeAsync(CancellationToken.None);

        Assert.Equal(("POST", "/button", "{\"name\":\"home\",\"state\":\"press\"}"), h.Requests[0]);
    }

    [Fact]
    public async Task SendsVietnameseTextAsJson()
    {
        var h = new Handler();
        using (var c = new ServeWebControl(8080, h, TimeSpan.FromSeconds(1))) await c.SendTextAsync("Tiếng Việt \"x\"\n\\", CancellationToken.None);

        var (method, path, body) = h.Requests[0];
        Assert.Equal("POST", method);
        Assert.Equal("/clipboard", path);
        Assert.Equal("Tiếng Việt \"x\"\n\\", ((Dictionary<string, object>)MiniJson.Parse(body))["text"]);
    }

    [Fact]
    public async Task ReadsTheDeviceText()
    {
        var h = new Handler { Answer = "{\"text\": \"xin chào\", \"image\": null}" };
        using (var c = new ServeWebControl(8080, h, TimeSpan.FromSeconds(1)))
        {
            Assert.Equal("xin chào", await c.GetTextAsync(CancellationToken.None));
        }
        Assert.Equal("GET", h.Requests[0].Method);
    }

    [Fact]
    public async Task NoTextIsNull()
    {
        var h = new Handler { Answer = "{\"text\": null, \"image\": {\"type\": \"image/png\", \"size\": 3}}" };
        using (var c = new ServeWebControl(8080, h, TimeSpan.FromSeconds(1)))
        {
            Assert.Null(await c.GetTextAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task AFailedCallThrowsAndTheNextStillWorks()
    {
        var h = new Handler { Status = HttpStatusCode.InternalServerError };
        using (var c = new ServeWebControl(8080, h, TimeSpan.FromSeconds(1)))
        {
            await Assert.ThrowsAsync<CoreDeviceClientException>(() => c.HomeAsync(CancellationToken.None));
            h.Status = HttpStatusCode.OK;
            await c.HomeAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("a\u0001b", "\"a\\u0001b\"")]
    [InlineData("tab\t", "\"tab\\t\"")]
    public void EscapesControlCharacters(string text, string json)
    {
        Assert.Equal(json, ServeWebControl.JsonString(text));
    }
}
```

- [ ] **Step 2: Chạy, mong đợi FAIL.**

- [ ] **Step 3: Viết**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>
    /// The calls the window's toolbar makes besides the viewer: Home and the device clipboard. Unlike CoreDeviceClient
    /// nothing here holds a finger or a key down, so a failed call closes nothing; the next one is tried again.
    /// </summary>
    public sealed class ServeWebControl : IDisposable
    {
        private readonly HttpClient _http;
        private readonly TimeSpan _timeout;

        public ServeWebControl(int port) : this(port, CoreDeviceClient.CreateHandler(), TimeSpan.FromSeconds(5))
        {
        }

        internal ServeWebControl(int port, HttpMessageHandler handler, TimeSpan timeout)
        {
            if (port < CoreDeviceOptions.MinHttpPort || port > CoreDeviceOptions.MaxHttpPort) throw new ArgumentOutOfRangeException(nameof(port));
            _timeout = timeout;
            _http = new HttpClient(handler, disposeHandler: true)
            {
                BaseAddress = new Uri($"http://{CoreDeviceArguments.BindAddress}:{port.ToString(CultureInfo.InvariantCulture)}/"),
                Timeout = Timeout.InfiniteTimeSpan,
            };
            _http.DefaultRequestHeaders.ExpectContinue = false;
        }

        public Task HomeAsync(CancellationToken token) => SendAsync(HttpMethod.Post, "button", "{\"name\":\"home\",\"state\":\"press\"}", token);

        public Task SendTextAsync(string text, CancellationToken token) => SendAsync(HttpMethod.Post, "clipboard", "{\"text\":" + JsonString(text ?? "") + "}", token);

        /// <returns>The text on the device clipboard, or null when it holds none (an image, or nothing).</returns>
        public async Task<string> GetTextAsync(CancellationToken token)
        {
            var body = await SendAsync(HttpMethod.Get, "clipboard", null, token).ConfigureAwait(false);
            if (!MiniJson.TryParse(body, out var value) || !(value is Dictionary<string, object> map)) throw new CoreDeviceClientException("/clipboard answered something that is not JSON");
            return map.TryGetValue("text", out var text) ? text as string : null;
        }

        public void Dispose() => _http.Dispose();

        private async Task<string> SendAsync(HttpMethod method, string path, string json, CancellationToken token)
        {
            using (var timeout = new CancellationTokenSource(_timeout))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token))
            using (var request = new HttpRequestMessage(method, path))
            {
                if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                try
                {
                    using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token).ConfigureAwait(false))
                    {
                        if ((int)response.StatusCode != 200) throw new CoreDeviceClientException($"/{path} answered HTTP {(int)response.StatusCode}");
                        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception e) when (e is OperationCanceledException || e is HttpRequestException)
                {
                    if (token.IsCancellationRequested) throw;
                    throw new CoreDeviceClientException($"/{path} failed: {e.GetBaseException().Message}", e);
                }
            }
        }

        internal static string JsonString(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
```

`internal` constructor được test thấy nhờ `InternalsVisibleTo` đã có trong `MacExtend.Core.csproj`.

- [ ] **Step 4: Chạy, mong đợi PASS.**

- [ ] **Step 5: Checkpoint** — File: 1 nguồn, 1 test. Toàn bộ test PASS.

### Task C13: Cài đặt mới

**Files:**
- Modify: `src/MacExtend.Core/AppSettings.cs`, `src/MacExtend.Core/SettingsStore.cs`
- Test: `tests/MacExtend.Core.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Produces: `AppSettings.DirectWifiDevices : string` (UDID ngăn bởi `;`), `AppSettings.DirectLastDevice : string`, `AppSettings.DirectHintDismissed : bool`, `AppSettings.DirectWindowBounds : string` (`x,y,w,h`); `Clamp()` bỏ UDID không hợp lệ; helper `AppSettings.WifiDevices() : IReadOnlyList<string>`, `AppSettings.SetWifiDevice(string udid, bool on)`.

- [ ] **Step 1: Test thất bại**

Trong `Sample()` thêm `DirectWifiDevices = "00008030-001E185E343B802E", DirectLastDevice = "00008030-001E185E343B802E", DirectHintDismissed = true, DirectWindowBounds = "10,20,520,960",`; trong `AssertSame` thêm bốn `Assert.Equal` tương ứng. Thêm test:

```csharp
    [Fact]
    public void ClampKeepsOnlyValidWifiDevices()
    {
        var s = new AppSettings { DirectWifiDevices = "00008030-001E185E343B802E;junk;;00008030-001E185E343B802E", DirectLastDevice = "junk" };

        s.Clamp();

        Assert.Equal("00008030-001E185E343B802E", s.DirectWifiDevices);
        Assert.Equal("", s.DirectLastDevice);
    }

    [Fact]
    public void WifiDevicesCanBeTurnedOnAndOff()
    {
        var s = new AppSettings();

        s.SetWifiDevice("00008030-001E185E343B802E", true);
        s.SetWifiDevice("00008101-000A1B2C3D4E5F60", true);
        s.SetWifiDevice("00008030-001E185E343B802E", false);

        Assert.Equal(new[] { "00008101-000A1B2C3D4E5F60" }, s.WifiDevices());
    }
```

- [ ] **Step 2: Chạy `SettingsStoreTests`, mong đợi FAIL.**

- [ ] **Step 3: `AppSettings`**

```csharp
        /// <summary>Devices allowed to use "Điều khiển trực tiếp" over Wi-Fi (RemotePairing done), UDIDs joined by ';'.</summary>
        public string DirectWifiDevices { get; set; } = "";
        /// <summary>The device the last direct-control session used; opened again without asking when reachable.</summary>
        public string DirectLastDevice { get; set; } = "";
        /// <summary>The "camera and microphone are unavailable" hint was dismissed.</summary>
        public bool DirectHintDismissed { get; set; }
        /// <summary>Last size and position of the direct-control window, "x,y,width,height" in device-independent pixels.</summary>
        public string DirectWindowBounds { get; set; } = "";

        public IReadOnlyList<string> WifiDevices() =>
            (DirectWifiDevices ?? "").Split(';').Where(CoreDevice.CoreDeviceOptions.IsValidUdid).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        public void SetWifiDevice(string udid, bool on)
        {
            var list = WifiDevices().Where(u => !string.Equals(u, udid, StringComparison.OrdinalIgnoreCase)).ToList();
            if (on && CoreDevice.CoreDeviceOptions.IsValidUdid(udid)) list.Add(udid);
            DirectWifiDevices = string.Join(";", list);
        }
```

(`using System.Collections.Generic; using System.Linq;` ở đầu file.) Trong `Clamp()`:

```csharp
            DirectWifiDevices = string.Join(";", WifiDevices());
            DirectLastDevice = CoreDevice.CoreDeviceOptions.IsValidUdid(DirectLastDevice) ? DirectLastDevice : "";
            DirectWindowBounds = DirectWindowBounds ?? "";
```

- [ ] **Step 4: `SettingsStore`**

Serialize thêm sau `BluetoothControl`:

```csharp
            Line(sb, "DirectWifiDevices", s.DirectWifiDevices);
            Line(sb, "DirectLastDevice", s.DirectLastDevice);
            Line(sb, "DirectHintDismissed", Bool(s.DirectHintDismissed));
            Line(sb, "DirectWindowBounds", s.DirectWindowBounds);
```

Parse thêm:

```csharp
                case "directwifidevices": s.DirectWifiDevices = value; break;
                case "directlastdevice": s.DirectLastDevice = value; break;
                case "directhintdismissed": if (TryBool(value, out var hint)) s.DirectHintDismissed = hint; break;
                case "directwindowbounds": s.DirectWindowBounds = value; break;
```

- [ ] **Step 5: Chạy, mong đợi PASS.**

- [ ] **Step 6: Checkpoint** — File: 2 nguồn, 1 test. Toàn bộ test PASS.

---

## Phase D: phần Windows elevated, runtime và cập nhật

Gate: Task 0 đã chạy. Kiểm tra biên dịch trên Mac: `~/.dotnet/dotnet build MacExtend.sln -c Release` → `Build succeeded.` `0 Warning(s)` `0 Error(s)`.

### Task D1: Elevation, đường dẫn và các chế độ helper

**Files:**
- Create: `src/MacExtend.App/Native/Elevation.cs`, `src/MacExtend.App/Services/CoreDevice/CoreDeviceHelpers.cs`
- Modify: `src/MacExtend.App/MacExtend.App.csproj`, `src/MacExtend.App/AppPaths.cs`, `src/MacExtend.App/App.xaml.cs`, `src/MacExtend.App/Services/UpdateHelper.cs:30`

**Interfaces:**
- Consumes: `CoreDeviceSetupScripts`, `CtrlCHelper`, `CoreDeviceRuntime.InstallFromZip` (Task D3; viết D1 trước với lệnh gọi đó, build sẽ xanh sau D3, hoặc làm D3 trước D1).
- Produces: `Elevation.IsElevated`, `Elevation.IsAdminAccount`, `Elevation.CurrentUserName()`; `AppPaths.CoreDeviceRoot`, `CoreDevicePython`, `CoreDeviceRuntimeIdFile`, `CoreDeviceLog`, `TunneldLog`, `WebViewDataDir`, `PymobiledeviceHome`, `SelfExe`; `CoreDeviceHelpers.TryRun(string[] args, out int exitCode)`, `CoreDeviceHelpers.SetupArg = "--coredevice-setup"`, `RemoveArg = "--coredevice-remove"`, `CoreDeviceHelpers.RunScript(string script, string what, RotatingLog log)`.

- [ ] **Step 1: csproj**

```xml
    <ProjectReference Include="..\MacExtend.CoreDeviceHost\MacExtend.CoreDeviceHost.csproj" />
    <PackageReference Include="Microsoft.Web.WebView2" Version="1.0.3179.45" />
```

và nhóm metadata runtime (CI điền, bản build cục bộ để trống):

```xml
  <ItemGroup>
    <AssemblyMetadata Include="CoreDeviceRuntimeId" Value="$(CoreDeviceRuntimeId)" />
    <AssemblyMetadata Include="CoreDeviceRuntimeFile" Value="$(CoreDeviceRuntimeFile)" />
    <AssemblyMetadata Include="CoreDeviceRuntimeSha256" Value="$(CoreDeviceRuntimeSha256)" />
    <AssemblyMetadata Include="CoreDeviceRuntimeSize" Value="$(CoreDeviceRuntimeSize)" />
  </ItemGroup>
```

Run: `~/.dotnet/dotnet restore src/MacExtend.App`. Nếu NuGet báo không có `1.0.3179.45`, chọn bản stable `1.0.*` mới nhất trên nuget.org và ghi số đó vào csproj.

- [ ] **Step 2: `AppPaths`**

```csharp
        public static string SelfExe => Path.Combine(InstallDir, "MacExtend.exe");

        /// <summary>Python + pymobiledevice3 for "Điều khiển trực tiếp"; installed on first setup, never by the installer.</summary>
        public static string CoreDeviceRoot => Path.Combine(InstallDir, "coredevice");
        public static string CoreDevicePython => Path.Combine(CoreDeviceRoot, "python", "python.exe");
        public static string CoreDeviceRuntimeIdFile => Path.Combine(CoreDeviceRoot, "RUNTIME_ID");
        /// <summary>Classified session status lines only; never keys, clipboard, full UDIDs or raw Python output.</summary>
        public static string CoreDeviceLog => Path.Combine(LogDir, "coredevice.log");
        public static string TunneldLog => Path.Combine(LogDir, "tunneld.log");
        public static string WebViewDataDir => Path.Combine(LocalDir, "WebView2");
        /// <summary>pymobiledevice3's own folder (pair records, RemotePairing records, DDI cache): Path.home() on Windows.</summary>
        public static string PymobiledeviceHome =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pymobiledevice3");
```

- [ ] **Step 3: `Elevation.cs`**

```csharp
using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace MacExtend.Native
{
    /// <summary>Whether this process runs elevated, and whether its account can elevate by itself (an administrator behind UAC).</summary>
    internal static class Elevation
    {
        private const int TokenElevationType = 18;
        private const int TokenElevationTypeLimited = 3;

        public static bool IsElevated
        {
            get
            {
                using (var id = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        /// <summary>
        /// Elevated now, or a split UAC token that elevates without anyone else's password. A standard account is not:
        /// a task "with highest privileges" would run it without administrator rights, so Wi-Fi asks UAC per session.
        /// </summary>
        public static bool IsAdminAccount
        {
            get
            {
                if (IsElevated) return true;
                using (var id = WindowsIdentity.GetCurrent())
                {
                    var buffer = Marshal.AllocHGlobal(sizeof(int));
                    try
                    {
                        return GetTokenInformation(id.Token, TokenElevationType, buffer, sizeof(int), out _)
                               && Marshal.ReadInt32(buffer) == TokenElevationTypeLimited;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
            }
        }

        /// <summary>DOMAIN\name of this process's account: the Scheduled Task's principal.</summary>
        public static string CurrentUserName()
        {
            using (var id = WindowsIdentity.GetCurrent()) return id.Name;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returned);
    }
}
```

- [ ] **Step 4: `CoreDeviceHelpers.cs`**

```csharp
using System;
using System.IO;
using MacExtend.Core;
using MacExtend.Core.CoreDevice;
using MacExtend.CoreDeviceHost;
using MacExtend.Native;

namespace MacExtend.Services.CoreDevice
{
    /// <summary>
    /// MacExtend.exe's helper modes for "Điều khiển trực tiếp", handled before any UI or the single-instance check:
    ///   signal-ctrl-c PID                                         one Ctrl+C to serve-web's console
    ///   --coredevice-setup ZIP|- RESULT USER PROFILE TASK(0|1)    elevated: runtime, firewall rules, task
    ///   --coredevice-remove                                       elevated: task and rules (uninstaller, CI)
    ///   --coredevice-tunneld PROFILE                              elevated: tunneld for Wi-Fi (the task runs this)
    /// </summary>
    internal static class CoreDeviceHelpers
    {
        public const string SetupArg = "--coredevice-setup";
        public const string RemoveArg = "--coredevice-remove";

        public static bool TryRun(string[] args, out int exitCode)
        {
            if (CtrlCHelper.TryRun(args, out exitCode)) return true;
            if (args.Length == 6 && args[0] == SetupArg)
            {
                exitCode = Setup(args[1], args[2], args[3], args[4], args[5] == "1");
                return true;
            }
            if (args.Length == 1 && args[0] == RemoveArg)
            {
                exitCode = Remove();
                return true;
            }
            if (args.Length == 2 && args[0] == CoreDeviceSetupScripts.TunneldArg)
            {
                exitCode = TunneldHelper.Run(args[1]);
                return true;
            }
            exitCode = 0;
            return false;
        }

        private static int Setup(string zip, string resultFile, string userName, string userProfile, bool withTask)
        {
            AppPaths.EnsureDirectories();
            using (var log = new RotatingLog(AppPaths.CoreDeviceLog, 512 * 1024))
            {
                string outcome;
                try
                {
                    if (!Elevation.IsElevated) throw new UnauthorizedAccessException("the setup helper is not elevated");
                    if (zip != "-")
                    {
                        // the package this exe was built with: the hash is checked here, on the handle that is extracted
                        var expected = CoreDeviceRuntime.Package ?? throw new InvalidOperationException("this build carries no runtime metadata");
                        CoreDeviceRuntime.InstallFromZip(zip, expected, AppPaths.CoreDeviceRoot, line => log.WriteLine("[setup] " + line));
                    }
                    RunScript(CoreDeviceSetupScripts.AddFirewallRules(AppPaths.CoreDevicePython), "firewall rules", log);
                    if (withTask) RunScript(CoreDeviceSetupScripts.RegisterTask(AppPaths.SelfExe, userName, userProfile), "scheduled task", log);
                    outcome = "ok";
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is InvalidDataException)
                {
                    outcome = "failed: " + e.Message;
                }
                log.WriteLine("[setup] outcome: " + outcome);
                try
                {
                    File.WriteAllText(resultFile, outcome);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    log.WriteLine("[setup] could not write the result: " + e.Message);
                }
                return outcome == "ok" ? 0 : 1;
            }
        }

        private static int Remove()
        {
            AppPaths.EnsureDirectories();
            using (var log = new RotatingLog(AppPaths.CoreDeviceLog, 512 * 1024))
            {
                var ok = TryScript(CoreDeviceSetupScripts.RemoveTask(), "remove task", log);
                ok &= TryScript(CoreDeviceSetupScripts.RemoveFirewallRules(), "remove firewall rules", log);
                return ok ? 0 : 1;
            }
        }

        public static void RunScript(string script, string what, RotatingLog log)
        {
            var result = PowerShellRunner.RunAsync(script).GetAwaiter().GetResult();
            log.WriteLine("[setup] " + what + ": exit " + result.ExitCode);
            if (!result.Succeeded) throw new InvalidOperationException(what + " failed (exit " + result.ExitCode + ")");
        }

        private static bool TryScript(string script, string what, RotatingLog log)
        {
            try
            {
                RunScript(script, what, log);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}
```

- [ ] **Step 5: App và UpdateHelper**

`App.OnStartup`, ngay sau `base.OnStartup(e);`:

```csharp
            // serve-web's Ctrl+C helper, the elevated setup and the tunneld host: no UI, no receiver
            if (Services.CoreDevice.CoreDeviceHelpers.TryRun(e.Args, out var coreDeviceExit))
            {
                Shutdown(coreDeviceExit);
                return;
            }
```

`UpdateHelper.HelperFiles` thêm `"MacExtend.CoreDeviceHost.dll"` (helper cập nhật chạy từ `%TEMP%` cũng biên dịch `OnStartup`, nên phải có DLL này).

- [ ] **Step 6: Build** — `~/.dotnet/dotnet build MacExtend.sln -c Release` (sau Task D2 và D3 nếu làm theo thứ tự interface). Expected: `Build succeeded.` `0 Warning(s)` `0 Error(s)`.

- [ ] **Step 7: Checkpoint** — File: 2 mới, 4 sửa.

### Task D2: `--coredevice-tunneld`

**Files:**
- Create: `src/MacExtend.App/Services/CoreDevice/TunneldHelper.cs`

**Interfaces:**
- Consumes: `CoreDeviceProcessHost` (`RawLineHandler`, `Start`, `IsEmpty`), `CoreDeviceArguments.Tunneld()`, `CoreDeviceArguments.TunneldEnvironment(...)`, `TunneldLog`, `CoreDeviceOptions.IsWindowsAbsolutePath`, `CoreDeviceHelpers.RunScript`, `CoreDeviceRuntime.IsPortable` (Task D3).
- Produces: `TunneldHelper.Run(string userProfile) : int` (0 khi tunneld thoát bình thường; 2 thiếu runtime; 3 không elevated; 4 hồ sơ không hợp lệ).

- [ ] **Step 1: Viết**

```csharp
using System;
using System.IO;
using System.Threading;
using MacExtend.Core;
using MacExtend.Core.CoreDevice;
using MacExtend.CoreDeviceHost;
using MacExtend.Native;

namespace MacExtend.Services.CoreDevice
{
    /// <summary>
    /// The elevated host of tunneld for Wi-Fi sessions, started by the Scheduled Task (or with UAC on a portable copy or
    /// a standard account). Python runs hidden in a kill-on-close job, so tunneld ends with this process; this process
    /// ends when tunneld does (the app's GET /shutdown, or schtasks /End). One per machine.
    /// </summary>
    internal static class TunneldHelper
    {
        private const string MutexName = @"Global\MacExtend.CoreDeviceTunnel";

        public static int Run(string userProfile)
        {
            AppPaths.EnsureDirectories();
            using (var log = new RotatingLog(AppPaths.TunneldLog, 256 * 1024))
            using (var mutex = new Mutex(true, MutexName, out var first))
            {
                if (!first)
                {
                    log.WriteLine("[tunneld] another tunneld host is running; this one exits");
                    return 0;
                }
                if (!File.Exists(AppPaths.CoreDevicePython))
                {
                    log.WriteLine("[tunneld] runtime missing");
                    return 2;
                }
                if (!Elevation.IsElevated)
                {
                    log.WriteLine("[tunneld] not elevated");
                    return 3;
                }
                if (!CoreDeviceOptions.IsWindowsAbsolutePath(userProfile) || !Directory.Exists(userProfile))
                {
                    log.WriteLine("[tunneld] the user profile argument is not an existing absolute path");
                    return 4;
                }
                if (CoreDeviceRuntime.IsPortable)
                {
                    // a portable copy has no setup step: the rules are made here, in this session's one UAC
                    try
                    {
                        CoreDeviceHelpers.RunScript(CoreDeviceSetupScripts.AddFirewallRules(AppPaths.CoreDevicePython), "firewall rules", log);
                    }
                    catch (InvalidOperationException e)
                    {
                        log.WriteLine("[tunneld] " + e.Message);
                    }
                }

                using (var host = new CoreDeviceProcessHost())
                {
                    host.RawLineHandler = line =>
                    {
                        var kind = TunneldLog.Parse(line);
                        if (kind != TunneldLogKind.Other) log.WriteLine("[tunneld] " + TunneldLog.Describe(kind));
                    };
                    var env = CoreDeviceArguments.TunneldEnvironment(Environment.GetEnvironmentVariables(), userProfile);
                    host.Start(AppPaths.CoreDevicePython, CoreDeviceArguments.Tunneld(), env, false, pid => log.WriteLine("[tunneld] started"));
                    while (!host.IsEmpty) Thread.Sleep(500);
                    log.WriteLine("[tunneld] exited");
                }
                mutex.ReleaseMutex();
                return 0;
            }
        }
    }
}
```

- [ ] **Step 2: Build**, như Task D1 Step 6.

- [ ] **Step 3: Checkpoint** — File: `TunneldHelper.cs`.

### Task D3: `CoreDeviceRuntime`

**Files:**
- Create: `src/MacExtend.App/Services/CoreDevice/CoreDeviceRuntime.cs`
- Modify: `src/MacExtend.App/Services/UpdateService.cs` (thêm `CurrentLabel`)

**Interfaces:**
- Consumes: `RuntimePackage`, `FileHash.Sha256(Stream)`, `UpdateStager.Extract(Stream, string)`, `UpdateHelper.NeedsElevation`, `Elevation`, `CoreDeviceHelpers.SetupArg`.
- Produces: `CoreDeviceRuntime.Package : RuntimePackage`, `IsPortable`, `IsInstalled`, `InstalledId`, `IsCurrent`, `DownloadAsync(IProgress<double>, CancellationToken) : Task<string>` (đường dẫn zip đã kiểm hash), `CheckZip(string path)` (ném `InvalidDataException`), `InstallFromZip(string zip, RuntimePackage expected, string target, Action<string> log)`, `SetupAsync(string zipOrNull) : Task<string>` (`"ok"`, `"cancelled"`, `"failed: ..."`); `UpdateService.CurrentLabel : string`.

- [ ] **Step 1: `UpdateService.CurrentLabel`**

```csharp
        /// <summary>
        /// The release label this build came from (InformationalVersion without "+commit"): "1.0.8-beta.1" for a
        /// pre-release tag, else the 3-part version. Release tags are "v" + this.
        /// </summary>
        public static string CurrentLabel
        {
            get
            {
                var info = typeof(UpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
                var label = info.Split('+')[0].Trim();
                return label.Length > 0 ? label : CurrentVersion.ToString(3);
            }
        }
```

- [ ] **Step 2: Viết `CoreDeviceRuntime.cs`**

```csharp
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MacExtend.Core;
using MacExtend.Core.CoreDevice;
using MacExtend.Core.Updates;
using MacExtend.Native;

namespace MacExtend.Services.CoreDevice
{
    /// <summary>The CoreDevice runtime under {app}\coredevice: what is installed, getting this build's zip, installing it.</summary>
    internal static class CoreDeviceRuntime
    {
        private const int ErrorCancelled = 1223;
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };

        /// <summary>The zip this build was made with (CI metadata); null for a local build.</summary>
        public static RuntimePackage Package { get; } = RuntimePackage.FromMetadata(key =>
            typeof(CoreDeviceRuntime).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value);

        /// <summary>A copy in a folder this account can write: no task, no setup UAC; Wi-Fi asks UAC per session.</summary>
        public static bool IsPortable => !UpdateHelper.NeedsElevation(AppPaths.InstallDir);

        public static string InstalledId
        {
            get
            {
                try
                {
                    return File.Exists(AppPaths.CoreDeviceRuntimeIdFile) ? File.ReadAllText(AppPaths.CoreDeviceRuntimeIdFile).Trim() : null;
                }
                catch (IOException)
                {
                    return null;
                }
            }
        }

        public static bool IsInstalled => File.Exists(AppPaths.CoreDevicePython) && !string.IsNullOrEmpty(InstalledId);

        /// <summary>Installed and the one this build was made with (an older one is replaced by the updater or by setup).</summary>
        public static bool IsCurrent => IsInstalled && (Package == null || InstalledId == Package.Id);

        /// <summary>Downloads this build's runtime zip from its release into %LOCALAPPDATA%\MacExtend\updates\coredevice and checks it.</summary>
        public static async Task<string> DownloadAsync(IProgress<double> progress, CancellationToken cancel)
        {
            var package = Package ?? throw new InvalidOperationException("Bản build cục bộ không có thông tin runtime. Hãy dùng bản do CI build.");
            var dir = Path.Combine(AppPaths.UpdatesDir, "coredevice");
            Directory.CreateDirectory(dir);
            var zip = Path.Combine(dir, package.File);
            if (File.Exists(zip) && package.Matches(FileHash.Sha256(zip))) return zip;

            var part = zip + ".part";
            var url = "https://github.com/" + UpdateService.Repository + "/releases/download/v" + UpdateService.CurrentLabel + "/" + package.File;
            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    throw new FileNotFoundException("Bản phát hành này không có gói runtime trên GitHub. Nếu đây là bản beta từ CI, chọn tệp " + package.File + " trong artifact.");
                }
                response.EnsureSuccessStatusCode();
                using (var input = await response.Content.ReadAsStreamAsync())
                using (var output = File.Create(part))
                {
                    var buffer = new byte[81920];
                    long received = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancel)) > 0)
                    {
                        await output.WriteAsync(buffer, 0, read, cancel);
                        received += read;
                        progress?.Report(Math.Min(1.0, received / (double)package.Size));
                    }
                }
            }
            if (!package.Matches(FileHash.Sha256(part)))
            {
                File.Delete(part);
                throw new InvalidDataException("Tệp runtime tải về bị hỏng (sai mã SHA-256).");
            }
            if (File.Exists(zip)) File.Delete(zip);
            File.Move(part, zip);
            return zip;
        }

        /// <summary>A zip the user picked (from a CI artifact): only this build's own package is accepted.</summary>
        public static void CheckZip(string path)
        {
            var package = Package ?? throw new InvalidOperationException("Bản build cục bộ không có thông tin runtime. Hãy dùng bản do CI build.");
            if (!package.Matches(FileHash.Sha256(path))) throw new InvalidDataException("Tệp này không phải gói runtime của bản MacExtend đang chạy.");
        }

        /// <summary>
        /// Opens the zip once, checks its SHA-256 on that handle (nobody can swap it underneath: write sharing is
        /// denied), extracts it to TARGET.new and swaps it in; the previous runtime comes back if the swap fails.
        /// </summary>
        public static void InstallFromZip(string zipPath, RuntimePackage expected, string target, Action<string> log)
        {
            using (var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!expected.Matches(FileHash.Sha256(stream))) throw new InvalidDataException("runtime zip SHA-256 mismatch");
                stream.Position = 0;
                var fresh = target + ".new";
                var aside = target + ".old";
                if (Directory.Exists(fresh)) Directory.Delete(fresh, true);
                UpdateStager.Extract(stream, fresh);
                if (!File.Exists(Path.Combine(fresh, "python", "python.exe"))) throw new InvalidDataException("runtime zip has no python\\python.exe");
                if (Directory.Exists(aside)) Directory.Delete(aside, true);
                if (Directory.Exists(target)) Directory.Move(target, aside);
                try
                {
                    Directory.Move(fresh, target);
                }
                catch (IOException)
                {
                    if (Directory.Exists(aside) && !Directory.Exists(target)) Directory.Move(aside, target);
                    throw;
                }
                try
                {
                    if (Directory.Exists(aside)) Directory.Delete(aside, true);
                }
                catch (IOException)
                {
                    // a leftover .old is removed by the next setup or update
                }
                log("runtime " + expected.Id + " installed");
            }
        }

        /// <summary>
        /// The one-time setup. Installed copy: one UAC for runtime (when <paramref name="zip"/> is given), firewall
        /// rules and (administrator accounts) the task. Portable copy: the runtime is installed here, without UAC.
        /// </summary>
        public static Task<string> SetupAsync(string zip) => Task.Run(() =>
        {
            if (IsPortable)
            {
                if (zip == null) return "ok";
                try
                {
                    InstallFromZip(zip, Package, AppPaths.CoreDeviceRoot, _ => { });
                    return "ok";
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException)
                {
                    return "failed: " + e.Message;
                }
            }

            var result = Path.Combine(Path.GetTempPath(), "MacExtend-coredevice-" + Guid.NewGuid().ToString("N") + ".result");
            var args = new[]
            {
                CoreDeviceHelpers.SetupArg, zip ?? "-", result, Elevation.CurrentUserName(),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Elevation.IsAdminAccount ? "1" : "0",
            };
            try
            {
                using (var p = Process.Start(new ProcessStartInfo(AppPaths.SelfExe, WindowsCommandLine.Join(args))
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                }))
                {
                    p.WaitForExit();
                }
                return File.Exists(result) ? File.ReadAllText(result).Trim() : "failed: no result from the elevated setup";
            }
            catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
            {
                return "cancelled";
            }
            finally
            {
                try
                {
                    File.Delete(result);
                }
                catch (IOException)
                {
                }
            }
        });
    }
}
```

- [ ] **Step 3: Build**, như Task D1 Step 6.

- [ ] **Step 4: Checkpoint** — File: `CoreDeviceRuntime.cs`, `UpdateService.cs`.

### Task D4: Bộ cập nhật dùng ngăn thứ hai

**Files:**
- Modify: `src/MacExtend.App/Services/UpdateService.cs`, `src/MacExtend.App/Services/UpdateHelper.cs`

**Interfaces:**
- Consumes: `UpdateDecision.For(..., installedCoreDeviceId)`, `UpdateManifest.CoreDevice`, `CoreDeviceRuntime.InstalledId`, `CoreDeviceSetupScripts.TaskFullName`.

- [ ] **Step 1: `UpdateService`**

`CheckAsync`: `UpdateDecision.For(CurrentVersion, InstalledRuntimeId(), manifest, CoreDevice.CoreDeviceRuntime.InstalledId)`.

`DownloadAsync`: sau khối runtime, thêm:

```csharp
            done += check.Decision.NeedsRuntime ? manifest.Runtime.Size : 0;
            var coreDeviceDir = Path.Combine(staging, "coredevice");
            if (check.Decision.NeedsCoreDevice)
            {
                await FetchAsync(manifest, manifest.CoreDevice, staging, "coredevice", counter, cancel);
            }
            else if (Directory.Exists(coreDeviceDir))
            {
                Directory.Delete(coreDeviceDir, true);
            }
```

(Đặt `done += ...` đúng chỗ để tiến trình cộng dồn; dòng `done += manifest.App.Size;` hiện có giữ nguyên.)

- [ ] **Step 2: `UpdateHelper`** dừng tunneld trước khi thay file

Thêm:

```csharp
        /// <summary>tunneld (Scheduled Task, elevated) keeps the CoreDevice runtime's python.exe open; end it before replacing files.</summary>
        private static void EndTunneldTask(RotatingLog log)
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo("schtasks.exe", "/End /TN " + WindowsCommandLine.Quote(Core.CoreDevice.CoreDeviceSetupScripts.TaskFullName))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }))
                {
                    p.WaitForExit(10000);
                    log.WriteLine("[update] schtasks /End: " + p.ExitCode);
                }
            }
            catch (Win32Exception e)
            {
                log.WriteLine("[update] schtasks /End failed: " + e.Message);
            }
        }
```

Gọi `EndTunneldTask(log);` ở đầu `Apply(...)` (trước `UpdateApplier.Apply`).

- [ ] **Step 3: Build**, như Task D1 Step 6.

- [ ] **Step 4: Checkpoint** — File: 2 file.

### Task D5: Bộ gỡ cài đặt và kiểm thử `--coredevice-setup` trên CI

**Files:**
- Modify: `installer/MacExtend.iss`, `.github/workflows/build.yml` (job `app`)
- Create: `tools/coredevice/test-setup.ps1`

- [ ] **Step 1: Inno Setup**

`[UninstallRun]` thêm (trước dòng firewall hiện có):

```ini
Filename: "{app}\MacExtend.exe"; Parameters: "--coredevice-remove"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveCoreDevice"
```

`[UninstallDelete]` thêm:

```ini
Type: filesandordirs; Name: "{app}\coredevice"
Type: filesandordirs; Name: "{app}\coredevice.old"
Type: filesandordirs; Name: "{app}\coredevice.new"
```

- [ ] **Step 2: `test-setup.ps1`**

```powershell
<#
.SYNOPSIS
    CI (elevated windows-latest runner): MacExtend.exe --coredevice-setup with the real runtime zip creates the
    runtime, the firewall group and the task; --coredevice-remove takes the task and rules away again.

.PARAMETER App
    Folder of the built app (build/MacExtend), copied to a temp install folder first.

.PARAMETER Zip
    The coredevice runtime zip of the same build (its SHA-256 is embedded in that MacExtend.exe).
#>
param(
    [Parameter(Mandatory = $true)] [string]$App,
    [Parameter(Mandatory = $true)] [string]$Zip
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$install = Join-Path ([IO.Path]::GetTempPath()) ('MacExtend setup test ' + [guid]::NewGuid().ToString('N').Substring(0, 8))
Copy-Item -LiteralPath (Resolve-Path -LiteralPath $App).Path -Destination $install -Recurse
$exe = Join-Path $install 'MacExtend.exe'
$result = Join-Path $install 'setup.result'
$user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
try {
    $p = Start-Process -FilePath $exe -ArgumentList @('--coredevice-setup', "`"$((Resolve-Path -LiteralPath $Zip).Path)`"", "`"$result`"", "`"$user`"", "`"$env:USERPROFILE`"", '1') -Wait -PassThru
    $outcome = if (Test-Path -LiteralPath $result) { (Get-Content -LiteralPath $result -Raw).Trim() } else { '<none>' }
    if ($p.ExitCode -ne 0 -or $outcome -ne 'ok') { throw "setup exit $($p.ExitCode), outcome '$outcome'" }
    if (-not (Test-Path -LiteralPath (Join-Path $install 'coredevice\python\python.exe'))) { throw 'runtime not installed' }
    $rules = @(Get-NetFirewallRule -Group 'MacExtend CoreDevice' -ErrorAction SilentlyContinue)
    if ($rules.Count -ne 2) { throw "expected 2 firewall rules, found $($rules.Count)" }
    $task = Get-ScheduledTask -TaskPath '\MacExtend\' -TaskName 'MacExtend CoreDevice tunnel' -ErrorAction SilentlyContinue
    if (-not $task) { throw 'task missing' }
    if ($task.Principal.RunLevel -ne 'Highest') { throw "task run level $($task.Principal.RunLevel)" }
    if ($task.Actions[0].Arguments -notmatch '^--coredevice-tunneld ') { throw "task arguments '$($task.Actions[0].Arguments)'" }

    $p = Start-Process -FilePath $exe -ArgumentList '--coredevice-remove' -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "remove exit $($p.ExitCode)" }
    if (@(Get-NetFirewallRule -Group 'MacExtend CoreDevice' -ErrorAction SilentlyContinue).Count -ne 0) { throw 'firewall rules left behind' }
    if (Get-ScheduledTask -TaskPath '\MacExtend\' -TaskName 'MacExtend CoreDevice tunnel' -ErrorAction SilentlyContinue) { throw 'task left behind' }
    Write-Host 'coredevice setup ok'
}
finally {
    & $exe --coredevice-remove | Out-Null
    Remove-Item -LiteralPath $install -Recurse -Force -ErrorAction SilentlyContinue
}
```

- [ ] **Step 3: Bước CI** (job `app`, sau `Build app`)

```yaml
      - name: CoreDevice setup helper (elevated runner)
        shell: pwsh
        run: ./tools/coredevice/test-setup.ps1 -App build/MacExtend -Zip "dist/${{ steps.coredevice.outputs.file }}"
```

Đặt bước này trước `Portable zip`, vì bước đó nén `build/MacExtend`, và script chỉ làm việc trên một bản chép.

- [ ] **Step 4: Checkpoint**

File: `MacExtend.iss`, `build.yml`, `test-setup.ps1`. Mong đợi trên CI: dòng `coredevice setup ok`.

---

## Phase E: phiên điều khiển và giao diện

### Task E1: `CoreDeviceCommands` và `WindowsSetupState`

**Files:**
- Create: `src/MacExtend.App/Services/CoreDevice/CoreDeviceCommands.cs`, `src/MacExtend.App/Services/CoreDevice/WindowsSetupState.cs`

**Interfaces:**
- Produces: `CoreDeviceCommands.UsbDevicesAsync() : Task<IReadOnlyList<string>>`, `DeveloperModeAsync(string udid) : Task<CommandResult>` (dùng `DeviceQueries.ParseBool(r.Stdout)`), `RevealDeveloperModeAsync(string udid)`, `MountAsync(string udid, CoreDeviceRoute route)`, `RemotePairAsync(string udid)`, `AppleServiceReachable() : bool`, `RemotePaired(string udid) : bool`; `WindowsSetupState.UseTask : bool`, `WindowsSetupState.IsDoneAsync() : Task<bool>`.

- [ ] **Step 1: `CoreDeviceCommands.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using MacExtend.Core.CoreDevice;
using MacExtend.CoreDeviceHost;

namespace MacExtend.Services.CoreDevice
{
    /// <summary>The short pymobiledevice3 commands setup and sessions run, each in its own job with its own deadline.</summary>
    internal static class CoreDeviceCommands
    {
        private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(20);
        // waits for the Trust dialog when the device is not paired yet
        private static readonly TimeSpan TrustTimeout = TimeSpan.FromSeconds(120);
        private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(30);
        // the first mount downloads and personalizes the image
        private static readonly TimeSpan MountTimeout = TimeSpan.FromSeconds(180);

        // Apple Mobile Device Service (iTunes, Apple Devices) listens here on Windows
        private const int UsbmuxPort = 27015;

        public static Task<IReadOnlyList<string>> UsbDevicesAsync() => Task.Run(() =>
        {
            var r = Run(null, ListTimeout, "usbmux", "list", "--usb", "--simple");
            return r.Succeeded ? DeviceQueries.ParseUdids(r.Stdout) : (IReadOnlyList<string>)new string[0];
        });

        public static Task<CommandResult> DeveloperModeAsync(string udid) => Task.Run(() => Run(udid, TrustTimeout, "amfi", "developer-mode-status"));

        public static Task<CommandResult> RevealDeveloperModeAsync(string udid) => Task.Run(() => Run(udid, ShortTimeout, "amfi", "reveal-developer-mode"));

        /// <summary>auto-mount needs a tunnel on iOS 17+: the userspace one over the cable, tunneld's over Wi-Fi. Mounted already counts as success.</summary>
        public static Task<CommandResult> MountAsync(string udid, CoreDeviceRoute route) => Task.Run(() =>
            route == CoreDeviceRoute.Wifi
                ? Run(udid, MountTimeout, "mounter", "auto-mount", "--tunnel", udid)
                : Run(udid, MountTimeout, "mounter", "auto-mount", "--userspace"));

        /// <summary>RemotePairing pair-setup over the trusted cable: no dialog on the device; writes remote_&lt;UDID&gt;.plist.</summary>
        public static Task<CommandResult> RemotePairAsync(string udid) => Task.Run(() => Run(udid, ShortTimeout, "lockdown", "remotepairing", "--pair"));

        public static bool RemotePaired(string udid) =>
            udid != null && File.Exists(Path.Combine(AppPaths.PymobiledeviceHome, "remote_" + udid + ".plist"));

        public static bool AppleServiceReachable()
        {
            try
            {
                using (var client = new TcpClient())
                {
                    return client.ConnectAsync("127.0.0.1", UsbmuxPort).Wait(500) && client.Connected;
                }
            }
            catch (AggregateException)
            {
                return false;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        private static CommandResult Run(string udid, TimeSpan timeout, params string[] words) =>
            CoreDeviceProcessHost.RunCommand(AppPaths.CoreDevicePython, CoreDeviceArguments.Command(words),
                CoreDeviceArguments.CommandEnvironment(Environment.GetEnvironmentVariables(), udid), timeout);
    }
}
```

- [ ] **Step 2: `WindowsSetupState.cs`**

```csharp
using System.Threading.Tasks;
using MacExtend.Core.CoreDevice;
using MacExtend.Native;

namespace MacExtend.Services.CoreDevice
{
    /// <summary>Whether Windows' part of Wi-Fi control (task and firewall rules) is in place.</summary>
    internal static class WindowsSetupState
    {
        /// <summary>
        /// The Scheduled Task starts tunneld: installed copy, administrator account. Otherwise every Wi-Fi session asks
        /// UAC (portable copy, standard account, or Task 0 of the plan failed on this machine).
        /// </summary>
        public static bool UseTask => !CoreDeviceRuntime.IsPortable && Elevation.IsAdminAccount;

        public static async Task<bool> IsDoneAsync()
        {
            // a portable copy's per-session elevated helper makes its own rules
            if (CoreDeviceRuntime.IsPortable) return true;
            var result = await PowerShellRunner.RunAsync(CoreDeviceSetupScripts.Query());
            var state = CoreDeviceSetupScripts.ParseQuery(result.Output);
            return state.Rules >= 2 && (state.Task || !UseTask);
        }
    }
}
```

Nếu Task 0 cho FAIL: `UseTask => false;` và ghi chú lý do ở doc comment.

- [ ] **Step 3: Build**, như Task D1 Step 6.

- [ ] **Step 4: Checkpoint** — File: 2 file.

### Task E2: `TunnelController`

**Files:**
- Create: `src/MacExtend.App/Services/CoreDevice/TunnelController.cs`

**Interfaces:**
- Consumes: `TunnelPlan`, `TunneldListing`, `TunnelOutcome`, `WindowsSetupState.UseTask`, `CoreDeviceSetupScripts`.
- Produces: `TunnelController.EnsureAsync(string udid, CancellationToken) : Task<TunnelOutcome>`, `TunnelController.StopAsync() : Task` (chỉ tắt tunneld do chính nó bật).

- [ ] **Step 1: Viết**

```csharp
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MacExtend.Core;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Services.CoreDevice
{
    /// <summary>Gets a tunneld tunnel to one device for a Wi-Fi session, and shuts tunneld down afterwards if this session started it.</summary>
    internal sealed class TunnelController
    {
        public static readonly TimeSpan TunnelTimeout = TimeSpan.FromSeconds(30);
        private const int ErrorCancelled = 1223;

        private static readonly HttpClient Http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(2),
        };
        private static readonly Uri Root = new Uri("http://" + TunneldListing.Host + ":" + TunneldListing.Port + "/");

        private bool _ours;

        public async Task<TunnelOutcome> EnsureAsync(string udid, CancellationToken token)
        {
            var listing = await ListAsync(token);
            var step = TunnelPlan.First(listing != null, listing != null && TunneldListing.HasTunnel(listing, udid));
            if (step == TunnelStep.UseExisting) return TunnelOutcome.Ready;
            if (step == TunnelStep.StartTunneld)
            {
                var started = await Task.Run(() => StartTunneld());
                if (started != TunnelOutcome.Ready) return started;
                _ours = TunnelPlan.ShutdownAfter(step);
            }
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TunnelTimeout)
            {
                await Task.Delay(1000, token);
                var json = await ListAsync(token);
                if (json != null && TunneldListing.HasTunnel(json, udid)) return TunnelOutcome.Ready;
            }
            return TunnelOutcome.Timeout;
        }

        public async Task StopAsync()
        {
            if (!_ours) return;
            _ours = false;
            try
            {
                using (await Http.GetAsync(new Uri(Root, "shutdown"))) { }
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException)
            {
                // tunneld kills itself while answering; no answer is the usual outcome
            }
        }

        private static async Task<string> ListAsync(CancellationToken token)
        {
            try
            {
                using (var response = await Http.GetAsync(Root, token))
                {
                    return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : null;
                }
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException)
            {
                return null;
            }
        }

        private static TunnelOutcome StartTunneld()
        {
            if (WindowsSetupState.UseTask)
            {
                using (var p = Process.Start(new ProcessStartInfo("schtasks.exe", "/Run /TN " + WindowsCommandLine.Quote(CoreDeviceSetupScripts.TaskFullName))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }))
                {
                    p.WaitForExit(10000);
                    return p.ExitCode == 0 ? TunnelOutcome.Ready : TunnelOutcome.SetupMissing;
                }
            }
            try
            {
                var args = WindowsCommandLine.Join(new[] { CoreDeviceSetupScripts.TunneldArg, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) });
                Process.Start(new ProcessStartInfo(AppPaths.SelfExe, args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden })?.Dispose();
                return TunnelOutcome.Ready;
            }
            catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
            {
                return TunnelOutcome.AdminDeclined;
            }
        }
    }
}
```

- [ ] **Step 2: Build**, như Task D1 Step 6.

- [ ] **Step 3: Checkpoint** — File: `TunnelController.cs`.

### Task E3: `CoreDeviceService`

**Files:**
- Create: `src/MacExtend.App/Services/CoreDevice/CoreDeviceService.cs`

**Interfaces:**
- Consumes: `SessionMachine` và các enum, `CoreDeviceProcessHost`, `CoreDeviceOptions`, `CoreDeviceArguments`, `CoreDeviceClient.ReleaseAllAsync`, `CoreDeviceCommands`, `TunnelController`, `CoreDeviceMessages`.
- Produces: `CoreDeviceService(Dispatcher ui)`, `event Action StateChanged` (luôn gọi trên UI thread), `SessionPhase Phase`, `SessionFailure Failure`, `CoreDeviceRoute Route`, `string Udid`, `int Port`, `string ViewerUrl`, `bool IsActive`, `bool LastStopWasForced`, `string StatusText`, `void Start(string udid, CoreDeviceRoute route)`, `Task StopAsync()`, `void Dispose()` (dừng, chờ tối đa 45 giây).

- [ ] **Step 1: Viết**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using MacExtend.Core;
using MacExtend.Core.CoreDevice;
using MacExtend.CoreDeviceHost;

namespace MacExtend.Services.CoreDevice
{
    /// <summary>
    /// Runs one "Điều khiển trực tiếp" session: feeds what happens into a <see cref="SessionMachine"/> on the UI thread
    /// and does the action it returns on the thread pool. The window only reads the state and the viewer URL.
    /// </summary>
    internal sealed class CoreDeviceService : IDisposable
    {
        private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(120);
        private static readonly TimeSpan Grace = TimeSpan.FromSeconds(40);
        private static readonly TimeSpan ReleaseDeadline = TimeSpan.FromSeconds(2);

        private readonly Dispatcher _ui;
        private readonly TunnelController _tunnel = new TunnelController();
        private readonly RotatingLog _log = new RotatingLog(AppPaths.CoreDeviceLog, 512 * 1024);
        private SessionMachine _machine;
        private CoreDeviceProcessHost _host;
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private TaskCompletionSource<bool> _stopped = Completed();
        private int _port;
        private int _usbMisses;

        public CoreDeviceService(Dispatcher ui) => _ui = ui;

        public event Action StateChanged;

        public SessionPhase Phase => _machine?.Phase ?? SessionPhase.Idle;
        public SessionFailure Failure => _machine?.Failure ?? SessionFailure.None;
        public CoreDeviceRoute Route => _machine?.Route ?? CoreDeviceRoute.None;
        public string Udid { get; private set; }
        public int Port => _port;
        public string ViewerUrl => _port == 0 ? null : "http://127.0.0.1:" + _port + "/";
        public bool IsActive => _machine?.IsActive ?? false;
        /// <summary>The last stop had to kill serve-web: its cleanup (camera and microphone release) did not finish.</summary>
        public bool LastStopWasForced { get; private set; }

        public string StatusText => Failure != SessionFailure.None ? CoreDeviceMessages.Failure(Failure) : CoreDeviceMessages.Phase(Phase);

        /// <summary>UI thread. Ignored while a session is active.</summary>
        public void Start(string udid, CoreDeviceRoute route)
        {
            if (IsActive) return;
            Udid = udid;
            LastStopWasForced = false;
            _cts = new CancellationTokenSource();
            _stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _machine = new SessionMachine(route);
            Log("session start, route " + route + ", device …" + udid.Substring(udid.Length - 4));
            Do(_machine.Start());
        }

        /// <summary>UI thread. Completes when serve-web and our tunneld are gone.</summary>
        public Task StopAsync()
        {
            if (_machine == null || !_machine.IsActive)
            {
                _ = _tunnel.StopAsync();
                return _stopped.Task;
            }
            _cts.Cancel();
            Do(_machine.Stop());
            return _stopped.Task;
        }

        public void Dispose()
        {
            try
            {
                var stop = _ui.CheckAccess() ? StopAsync() : (Task)_ui.Invoke(new Func<Task>(StopAsync));
                stop.Wait(TimeSpan.FromSeconds(45));
            }
            catch (AggregateException)
            {
            }
            _host?.Dispose();
            _log.Dispose();
        }

        // ---- the machine's actions ------------------------------------------------------

        private void Do(SessionAction action)
        {
            switch (action)
            {
                case SessionAction.StartTunnel: _ = TunnelAsync(); break;
                case SessionAction.MountImage: _ = MountAsync(); break;
                case SessionAction.StartServeWeb: _ = StartServeWebAsync(); break;
                case SessionAction.StopServeWeb: _ = StopServeWebAsync(); break;
                case SessionAction.WatchUsb: _ = WatchUsbReturnAsync(); break;
            }
            if (_machine.Phase == SessionPhase.Failed)
            {
                Log("failed: " + _machine.Failure);
                if (action != SessionAction.StopServeWeb) _ = FinishAsync();
            }
            else if (_machine.Phase == SessionPhase.Stopping && action == SessionAction.None)
            {
                _ = FinishAsync();
            }
            StateChanged?.Invoke();
        }

        private void Post(Func<SessionAction> step) => _ui.BeginInvoke(new Action(() => Do(step())));

        private async Task TunnelAsync()
        {
            TunnelOutcome outcome;
            try
            {
                outcome = await _tunnel.EnsureAsync(Udid, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            Log("tunnel: " + outcome);
            Post(() => _machine.TunnelReady(outcome));
        }

        private async Task MountAsync()
        {
            var route = _machine.Route;
            var result = await CoreDeviceCommands.MountAsync(Udid, route);
            Log("image mount: " + (result.Succeeded ? "ok" : "failed, " + result.Error + (result.TimedOut ? " (timed out)" : "")));
            Post(() => _machine.ImageMounted(result.Succeeded, result.Error));
        }

        private async Task StartServeWebAsync()
        {
            var route = _machine.Route;
            _port = FreeLoopbackPort();
            var options = new CoreDeviceOptions(AppPaths.CoreDevicePython, Udid,
                route == CoreDeviceRoute.Wifi ? CoreDeviceTransport.Tunnel : CoreDeviceTransport.Userspace, _port);
            var host = new CoreDeviceProcessHost();
            _host = host;
            host.StatusLine += line => _ui.BeginInvoke(new Action(() => OnStatusLine(host, line)));
            try
            {
                host.Start(options.PythonPath, CoreDeviceArguments.ServeWeb(options), CoreDeviceArguments.Environment(Environment.GetEnvironmentVariables(), options), false, _ => { });
            }
            catch (CoreDeviceHostException e)
            {
                Log("serve-web did not start: " + e.Message);
                Post(() => _machine.ServeWebFailed(ServeWebLogKind.Error, false));
                return;
            }
            var failure = await host.WaitReadyAsync(_port, StartupTimeout, _cts.Token, _ => { });
            if (failure == ReadyFailure.None)
            {
                Log("serve-web ready");
                Post(() => _machine.ServeWebReady());
                _ = WatchExitAsync(host);
                if (route == CoreDeviceRoute.Usb) _ = WatchUsbPresenceAsync(host);
                return;
            }
            if (failure == ReadyFailure.ExitedEarly) host.WaitForOutput(TimeSpan.FromSeconds(2));
            Log("serve-web not ready: " + failure + ", " + host.LastFatal);
            Post(() => _machine.ServeWebFailed(host.LastFatal, failure == ReadyFailure.TimedOut));
        }

        private async Task StopServeWebAsync()
        {
            var host = _host;
            var port = _port;
            if (host != null)
            {
                if (port != 0)
                {
                    using (var client = new CoreDeviceClient(port)) await client.ReleaseAllAsync(ReleaseDeadline, force: true);
                }
                var exit = await host.StopAsync(AppPaths.SelfExe, Grace, line => Log(line));
                LastStopWasForced = exit == StopExit.Killed;
                Log("serve-web stopped: " + exit);
                var lastFatal = host.LastFatal;
                host.Dispose();
                _host = null;
                _port = 0;
                // Stopping or Failed: the machine answers None and Do() finishes the session
                Post(() => _machine.ServeWebExited(lastFatal));
                return;
            }
            Post(() => SessionAction.None);
        }

        private async Task FinishAsync()
        {
            await _tunnel.StopAsync();
            _ui.BeginInvoke(new Action(() =>
            {
                _machine.StopFinished();
                Log("session over: " + _machine.Phase);
                _stopped.TrySetResult(true);
                StateChanged?.Invoke();
            }));
        }

        // ---- watchers -------------------------------------------------------------------

        private async Task WatchExitAsync(CoreDeviceProcessHost host)
        {
            while (!host.IsStopping)
            {
                await Task.Delay(500);
                if (host.IsStopping || !host.IsEmpty) continue;
                host.WaitForOutput(TimeSpan.FromSeconds(2));
                Log("serve-web exited on its own: " + host.LastFatal);
                var lastFatal = host.LastFatal;
                _ui.BeginInvoke(new Action(() =>
                {
                    if (_host != host) return;
                    host.Dispose();
                    _host = null;
                    _port = 0;
                    Do(_machine.ServeWebExited(lastFatal));
                }));
                return;
            }
        }

        /// <summary>serve-web over the userspace tunnel never recovers from an unplug: notice the device leaving usbmux.</summary>
        private async Task WatchUsbPresenceAsync(CoreDeviceProcessHost host)
        {
            _usbMisses = 0;
            while (_host == host && !host.IsStopping)
            {
                await Task.Delay(3000);
                var present = (await CoreDeviceCommands.UsbDevicesAsync()).Contains(Udid, StringComparer.OrdinalIgnoreCase);
                _usbMisses = present ? 0 : _usbMisses + 1;
                if (_usbMisses < 2) continue;
                Log("device left usbmux");
                Post(() => _machine.UsbDeviceGone());
                return;
            }
        }

        private async Task WatchUsbReturnAsync()
        {
            while (_machine.Phase == SessionPhase.WaitingForDevice && !_machine.StopRequested)
            {
                await Task.Delay(2000);
                if ((await CoreDeviceCommands.UsbDevicesAsync()).Contains(Udid, StringComparer.OrdinalIgnoreCase))
                {
                    Log("device back on usbmux");
                    Post(() => _machine.UsbDeviceBack());
                    return;
                }
            }
        }

        private void OnStatusLine(CoreDeviceProcessHost host, ServeWebLogLine line)
        {
            if (_host != host || _machine == null) return;
            if (line.Kind == ServeWebLogKind.CodecString) Log("codec " + line.Detail);
            else if (line.Kind != ServeWebLogKind.Other) Log("serve-web: " + line.Kind + (line.Detail != null ? " " + line.Detail : ""));
            Do(_machine.Log(line.Kind));
        }

        private void Log(string line) => _log.WriteLine("[coredevice] " + line);

        private static int FreeLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static TaskCompletionSource<bool> Completed()
        {
            var tcs = new TaskCompletionSource<bool>();
            tcs.SetResult(true);
            return tcs;
        }
    }
}
```

`CoreDeviceProcessHost.LastFatal`, `IsStopping`, `IsEmpty`, `WaitForOutput` là thành viên public sẵn có của host.

- [ ] **Step 2: Build**, như Task D1 Step 6.

- [ ] **Step 3: Checkpoint** — File: `CoreDeviceService.cs`.

### Task E4: Cửa sổ điều khiển (WebView2)

**Files:**
- Create: `src/MacExtend.App/Views/ViewerBridge.cs`, `src/MacExtend.App/Views/CoreDeviceWindow.xaml`, `src/MacExtend.App/Views/CoreDeviceWindow.xaml.cs`

**Interfaces:**
- Consumes: `CoreDeviceService`, `SettingsService`, `ServeWebControl`, `CoreDeviceMessages`, `AppPaths.WebViewDataDir`.
- Produces: `CoreDeviceWindow(CoreDeviceService service, SettingsService settings, Action openSetup, Action retry)`.

Id phần tử viewer đã kiểm trong `resources/serve_web/viewer.html` 11.47.0: `#topbar`, `#left-tray`, `#right-tray`, `#bottom-row`, `#hw-tooltip`, `#help-overlay`, `.hw`, `#stage`, `#stage-wrap`, `#c` (canvas, `tabindex=0`, phím chỉ đi khi canvas có focus), `#offline-overlay span`, `#rotate-left`, `#rotate-right`; localStorage `frameOn`, `zoom`, `tray-left`, `tray-right`, `clipboardSync`.

- [ ] **Step 1: `ViewerBridge.cs`**

```csharp
namespace MacExtend.Views
{
    /// <summary>
    /// Runs in serve-web's viewer page before its own script (AddScriptToExecuteOnDocumentCreatedAsync): no bezel, no
    /// zoom, no clipboard sync, only the canvas and its offline overlay, which says in Vietnamese what to do when the
    /// device's screen is off. Element ids are pymobiledevice3 11.47.0's viewer.html.
    /// </summary>
    internal static class ViewerBridge
    {
        public const string Script = @"(function () {
  try {
    localStorage.setItem('frameOn', 'false');
    localStorage.removeItem('zoom');
    localStorage.setItem('tray-left', 'collapsed');
    localStorage.setItem('tray-right', 'collapsed');
    localStorage.setItem('clipboardSync', 'false');
  } catch (e) {}
  document.addEventListener('DOMContentLoaded', function () {
    var style = document.createElement('style');
    style.textContent = '#topbar,#left-tray,#right-tray,#bottom-row,#hw-tooltip,#help-overlay,.hw{display:none!important}'
      + 'body{padding:0;gap:0;background:#000}#stage::before{display:none}#stage-wrap{padding:0}';
    document.head.appendChild(style);
    document.body.classList.remove('frame-on');
    var span = document.querySelector('#offline-overlay span');
    if (span) span.textContent = 'Đang chờ hình. Nếu màn hình thiết bị đang tắt, hãy mở khóa thiết bị.';
    window.dispatchEvent(new Event('resize'));
  });
})();";

        public const string FocusCanvas = "(function () { var c = document.getElementById('c'); if (c) c.focus(); })();";

        public static string Click(string id) => "(function () { var b = document.getElementById('" + id + "'); if (b) b.click(); })();";
    }
}
```

- [ ] **Step 2: `CoreDeviceWindow.xaml`**

```xml
<Window x:Class="MacExtend.Views.CoreDeviceWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:wv2="clr-namespace:Microsoft.Web.WebView2.Wpf;assembly=Microsoft.Web.WebView2.Wpf"
        Title="Điều khiển trực tiếp"
        Width="520" Height="960" MinWidth="360" MinHeight="480"
        WindowStartupLocation="CenterScreen"
        Background="{DynamicResource Window.Background}"
        Foreground="{DynamicResource Text.Primary}"
        FontFamily="{StaticResource Font.Sans}" FontSize="13.5"
        TextOptions.TextFormattingMode="Display" UseLayoutRounding="True"
        Icon="pack://application:,,,/MacExtend;component/Assets/app.ico">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <!-- toolbar -->
        <Grid Margin="12,8">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <Border x:Name="RouteBadge" Padding="10,3" CornerRadius="10" Background="{DynamicResource Accent.Soft}" HorizontalAlignment="Left" VerticalAlignment="Center">
                <TextBlock x:Name="RouteText" FontSize="12" Foreground="{DynamicResource Accent.SoftText}" />
            </Border>
            <StackPanel Grid.Column="1" Orientation="Horizontal">
                <Button Style="{StaticResource Button.Subtle}" ToolTip="Home" Click="OnHome" Margin="2,0">
                    <TextBlock Style="{StaticResource Text.Icon}" Text="&#xE80F;" />
                </Button>
                <Button Style="{StaticResource Button.Subtle}" ToolTip="Xoay trái" Click="OnRotateLeft" Margin="2,0">
                    <TextBlock Style="{StaticResource Text.Icon}" Text="&#xE7AD;" RenderTransformOrigin="0.5,0.5">
                        <TextBlock.RenderTransform><ScaleTransform ScaleX="-1" /></TextBlock.RenderTransform>
                    </TextBlock>
                </Button>
                <Button Style="{StaticResource Button.Subtle}" ToolTip="Xoay phải" Click="OnRotateRight" Margin="2,0">
                    <TextBlock Style="{StaticResource Text.Icon}" Text="&#xE7AD;" />
                </Button>
                <Button x:Name="TextButton" Style="{StaticResource Button.Subtle}" ToolTip="Văn bản" Click="OnText" Margin="2,0">
                    <TextBlock Style="{StaticResource Text.Icon}" Text="&#xE8C8;" />
                </Button>
                <Button Style="{StaticResource Button.Subtle}" ToolTip="Toàn màn hình (F11)" Click="OnFullscreen" Margin="2,0">
                    <TextBlock x:Name="FullscreenIcon" Style="{StaticResource Text.Icon}" Text="&#xE740;" />
                </Button>
            </StackPanel>
        </Grid>

        <!-- status band: WPF cannot draw over the WebView2 (airspace), so messages sit above it -->
        <Border x:Name="Banner" Grid.Row="1" Visibility="Collapsed" Padding="12,8"
                Background="{DynamicResource Sheet.Background}" BorderBrush="{DynamicResource Sheet.Border}" BorderThickness="0,1">
            <TextBlock x:Name="BannerText" Style="{StaticResource Text.Body}" TextWrapping="Wrap" />
        </Border>

        <wv2:WebView2 x:Name="Viewer" Grid.Row="2" Visibility="Collapsed" DefaultBackgroundColor="Black" />

        <!-- stand-in while there is no picture -->
        <StackPanel x:Name="Placeholder" Grid.Row="2" VerticalAlignment="Center" HorizontalAlignment="Center" MaxWidth="360" Margin="24">
            <TextBlock x:Name="PlaceholderText" Style="{StaticResource Text.Body}" TextWrapping="Wrap" TextAlignment="Center" />
            <Button x:Name="RetryButton" Content="Thử lại" Style="{StaticResource Button.Primary}" Margin="0,16,0,0" HorizontalAlignment="Center" Visibility="Collapsed" Click="OnRetry" />
            <Button x:Name="SetupButton" Content="Mở Thiết lập" Style="{StaticResource Button.Secondary}" Margin="0,8,0,0" HorizontalAlignment="Center" Visibility="Collapsed" Click="OnSetup" />
        </StackPanel>

        <Grid x:Name="Hint" Grid.Row="3" Margin="12,6">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <TextBlock Text="Trong lúc điều khiển, camera và micro của thiết bị không dùng được." Style="{StaticResource Text.Caption}" TextWrapping="Wrap" VerticalAlignment="Center" />
            <Button Grid.Column="1" Content="Đã hiểu" Style="{StaticResource Button.Subtle}" Click="OnDismissHint" />
        </Grid>

        <!-- a Popup is its own window, so it does show above the WebView2 -->
        <Popup x:Name="TextPopup" PlacementTarget="{Binding ElementName=TextButton}" Placement="Bottom" StaysOpen="False" AllowsTransparency="True">
            <Border Style="{StaticResource Sheet}" Padding="12" Width="320">
                <StackPanel>
                    <TextBox x:Name="ClipText" Height="88" TextWrapping="Wrap" AcceptsReturn="True" VerticalScrollBarVisibility="Auto" />
                    <TextBlock x:Name="ClipStatus" Style="{StaticResource Text.Caption}" Margin="0,6,0,0" TextWrapping="Wrap" />
                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,8,0,0">
                        <Button Content="Lấy từ thiết bị" Style="{StaticResource Button.Secondary}" Click="OnGetText" />
                        <Button Content="Gửi tới thiết bị" Style="{StaticResource Button.Primary}" Margin="8,0,0,0" Click="OnSendText" />
                    </StackPanel>
                </StackPanel>
            </Border>
        </Popup>
    </Grid>
</Window>
```

- [ ] **Step 3: `CoreDeviceWindow.xaml.cs`**

```csharp
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MacExtend.Core.CoreDevice;
using MacExtend.Services.CoreDevice;
using MacExtend.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace MacExtend.Views
{
    internal partial class CoreDeviceWindow : Window
    {
        private const string WebView2Download = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

        private readonly CoreDeviceService _service;
        private readonly SettingsService _settings;
        private readonly Action _openSetup;
        private readonly Action _retry;
        private bool _viewerReady;
        private string _loadedUrl;
        private bool _fullscreen;
        private WindowState _restoreState;

        public CoreDeviceWindow(CoreDeviceService service, SettingsService settings, Action openSetup, Action retry)
        {
            _service = service;
            _settings = settings;
            _openSetup = openSetup;
            _retry = retry;
            InitializeComponent();
            RestoreBounds();
            Hint.Visibility = _settings.Current.DirectHintDismissed ? Visibility.Collapsed : Visibility.Visible;
            _service.StateChanged += Refresh;
            Loaded += async (_, __) =>
            {
                await InitViewerAsync();
                Refresh();
            };
            Activated += (_, __) => FocusCanvas();
            PreviewKeyDown += OnKey;
            Closing += (_, __) => SaveBounds();
            Closed += (_, __) =>
            {
                _service.StateChanged -= Refresh;
                _ = _service.StopAsync();
                Viewer.Dispose();
            };
        }

        private async Task InitViewerAsync()
        {
            try
            {
                CoreWebView2Environment.GetAvailableBrowserVersionString();
            }
            catch (WebView2RuntimeNotFoundException)
            {
                ShowPlaceholder("Máy chưa có Microsoft Edge WebView2 Runtime. Tải tại " + WebView2Download + " rồi mở lại cửa sổ này.", false, false);
                return;
            }
            var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewDataDir);
            await Viewer.EnsureCoreWebView2Async(env);
            var s = Viewer.CoreWebView2.Settings;
            s.AreDefaultContextMenusEnabled = false;
            s.AreDevToolsEnabled = false;
            s.IsZoomControlEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsStatusBarEnabled = false;
            s.IsGeneralAutofillEnabled = false;
            s.IsPasswordAutosaveEnabled = false;
            await Viewer.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ViewerBridge.Script);
            Viewer.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
            // only the session's own viewer page (and the blank page between sessions)
            Viewer.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (e.Uri == "about:blank") return;
                if (_loadedUrl == null || !e.Uri.StartsWith(_loadedUrl, StringComparison.Ordinal)) e.Cancel = true;
            };
            Viewer.NavigationCompleted += (_, __) => FocusCanvas();
            Viewer.PreviewKeyDown += OnKey;
            _viewerReady = true;
        }

        private void Refresh()
        {
            RouteText.Text = CoreDeviceMessages.Route(_service.Route);
            RouteBadge.Visibility = _service.Route == CoreDeviceRoute.None ? Visibility.Collapsed : Visibility.Visible;
            var phase = _service.Phase;
            var running = phase == SessionPhase.Ready || phase == SessionPhase.Reconnecting;

            if (running && _viewerReady)
            {
                if (_loadedUrl != _service.ViewerUrl)
                {
                    _loadedUrl = _service.ViewerUrl;
                    Viewer.CoreWebView2.Navigate(_loadedUrl);
                }
                Viewer.Visibility = Visibility.Visible;
                Placeholder.Visibility = Visibility.Collapsed;
                Banner.Visibility = phase == SessionPhase.Reconnecting ? Visibility.Visible : Visibility.Collapsed;
                BannerText.Text = CoreDeviceMessages.Phase(phase);
                return;
            }

            if (_loadedUrl != null && _viewerReady)
            {
                _loadedUrl = null;
                Viewer.CoreWebView2.Navigate("about:blank");
            }
            Viewer.Visibility = Visibility.Collapsed;
            Banner.Visibility = Visibility.Collapsed;
            if (phase == SessionPhase.Failed)
            {
                ShowPlaceholder(CoreDeviceMessages.Failure(_service.Failure), true, CoreDeviceMessages.NeedsSetup(_service.Failure));
            }
            else if (phase == SessionPhase.Stopped || phase == SessionPhase.Idle)
            {
                Close();
            }
            else
            {
                ShowPlaceholder(CoreDeviceMessages.Phase(phase), false, false);
            }
        }

        private void ShowPlaceholder(string text, bool retry, bool setup)
        {
            Placeholder.Visibility = Visibility.Visible;
            PlaceholderText.Text = text;
            RetryButton.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
            SetupButton.Visibility = setup ? Visibility.Visible : Visibility.Collapsed;
        }

        private void FocusCanvas()
        {
            if (_viewerReady && _loadedUrl != null) _ = Viewer.CoreWebView2.ExecuteScriptAsync(ViewerBridge.FocusCanvas);
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.F11) return;
            e.Handled = true;
            ToggleFullscreen();
        }

        private void ToggleFullscreen()
        {
            _fullscreen = !_fullscreen;
            if (_fullscreen)
            {
                _restoreState = WindowState;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Normal;
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = _restoreState;
            }
            FullscreenIcon.Text = _fullscreen ? "\uE73F" : "\uE740";
        }

        private async void OnHome(object sender, RoutedEventArgs e) => await Control(c => c.HomeAsync(CancellationToken.None));

        private void OnRotateLeft(object sender, RoutedEventArgs e) => RunInViewer(ViewerBridge.Click("rotate-left"));

        private void OnRotateRight(object sender, RoutedEventArgs e) => RunInViewer(ViewerBridge.Click("rotate-right"));

        private void OnText(object sender, RoutedEventArgs e)
        {
            ClipStatus.Text = "";
            TextPopup.IsOpen = true;
        }

        private async void OnSendText(object sender, RoutedEventArgs e)
        {
            if (await Control(c => c.SendTextAsync(ClipText.Text, CancellationToken.None))) ClipStatus.Text = "Đã gửi. Nhấn giữ trên thiết bị rồi chọn Dán.";
        }

        private async void OnGetText(object sender, RoutedEventArgs e)
        {
            string text = null;
            if (!await Control(async c => text = await c.GetTextAsync(CancellationToken.None))) return;
            if (string.IsNullOrEmpty(text))
            {
                ClipStatus.Text = "Clipboard của thiết bị không có chữ.";
                return;
            }
            ClipText.Text = text;
            Clipboard.SetText(text);
            ClipStatus.Text = "Đã chép vào clipboard của Windows.";
        }

        private async Task<bool> Control(Func<ServeWebControl, Task> call)
        {
            if (_service.Port == 0) return false;
            try
            {
                using (var control = new ServeWebControl(_service.Port)) await call(control);
                return true;
            }
            catch (CoreDeviceClientException)
            {
                ClipStatus.Text = "Thiết bị chưa phản hồi. Thử lại.";
                return false;
            }
        }

        private void RunInViewer(string script)
        {
            if (_viewerReady && _loadedUrl != null) _ = Viewer.CoreWebView2.ExecuteScriptAsync(script);
        }

        private void OnRetry(object sender, RoutedEventArgs e) => _retry();

        private void OnSetup(object sender, RoutedEventArgs e) => _openSetup();

        private void OnDismissHint(object sender, RoutedEventArgs e)
        {
            Hint.Visibility = Visibility.Collapsed;
            _settings.Current.DirectHintDismissed = true;
            _settings.ScheduleSave();
        }

        private void RestoreBounds()
        {
            var parts = (_settings.Current.DirectWindowBounds ?? "").Split(',');
            if (parts.Length != 4) return;
            var values = new double[4];
            for (var i = 0; i < 4; i++)
            {
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) return;
            }
            // only if it is still on a screen
            var area = SystemParameters.VirtualScreenWidth > 0
                ? new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight)
                : Rect.Empty;
            var bounds = new Rect(values[0], values[1], Math.Max(MinWidth, values[2]), Math.Max(MinHeight, values[3]));
            if (!area.IntersectsWith(bounds)) return;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
        }

        private void SaveBounds()
        {
            if (_fullscreen || WindowState != WindowState.Normal) return;
            _settings.Current.DirectWindowBounds = string.Join(",", new[] { Left, Top, Width, Height }.Select(v => v.ToString("0", CultureInfo.InvariantCulture)));
            _settings.ScheduleSave();
        }
    }
}
```

F11 tới được WPF vì WebView2 chuyển phím chức năng (accelerator key) thành `PreviewKeyDown` của control; cần xác nhận trên máy (bảng D, D4).

- [ ] **Step 4: Build**, như Task D1 Step 6.

- [ ] **Step 5: Checkpoint** — File: 3 file mới.

### Task E5: Cửa sổ thiết lập

**Files:**
- Create: `src/MacExtend.App/ViewModels/CoreDeviceSetupViewModel.cs`, `src/MacExtend.App/Views/CoreDeviceSetupWindow.xaml`, `src/MacExtend.App/Views/CoreDeviceSetupWindow.xaml.cs`

**Interfaces:**
- Consumes: `SetupSteps`, `SetupFacts`, `CoreDeviceRuntime`, `CoreDeviceCommands`, `WindowsSetupState`, `DeviceQueries`, `SettingsService`, `RelayCommand`, `ObservableObject`.
- Produces: `CoreDeviceSetupViewModel(SettingsService settings)` với `ObservableCollection<SetupStepItem> Steps`, `bool Busy`, `double Progress`, `bool ShowProgress`, `string Message`, `bool WifiWanted` (2 chiều), `bool Ready`, `Task RefreshAsync()`, các lệnh trong mỗi `SetupStepItem` (`Action`, `ActionText`); `CoreDeviceSetupWindow(CoreDeviceSetupViewModel vm)`.

- [ ] **Step 1: View model**

```csharp
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MacExtend.Core.CoreDevice;
using MacExtend.Services.CoreDevice;

namespace MacExtend.ViewModels
{
    internal sealed class SetupStepItem
    {
        public SetupStepItem(SetupStep step, string actionText, ICommand action)
        {
            Step = step;
            ActionText = actionText;
            Action = action;
        }

        public SetupStep Step { get; }
        public string Title => Step.Title;
        public string Detail => Step.Detail;
        public SetupStatus Status => Step.Status;
        public string StatusText => Status == SetupStatus.Done ? "Đã xong" : Status == SetupStatus.Todo ? "Cần làm" : Status == SetupStatus.Optional ? "Tùy chọn" : "Chờ bước trước";
        public string ActionText { get; }
        public ICommand Action { get; }
        public bool HasAction => Action != null && Status == SetupStatus.Todo;
    }

    internal sealed class CoreDeviceSetupViewModel : ObservableObject
    {
        private readonly SettingsService _settings;
        private readonly SetupFacts _facts = new SetupFacts();
        private bool _busy;
        private double _progress;
        private bool _showProgress;
        private string _message = "";

        public CoreDeviceSetupViewModel(SettingsService settings)
        {
            _settings = settings;
            RefreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => !Busy);
            PickZipCommand = new RelayCommand(() => _ = PickZipAsync(), () => !Busy);
        }

        public ObservableCollection<SetupStepItem> Steps { get; } = new ObservableCollection<SetupStepItem>();
        public ICommand RefreshCommand { get; }
        public ICommand PickZipCommand { get; }
        public bool Busy { get => _busy; private set { if (Set(ref _busy, value)) CommandManager.InvalidateRequerySuggested(); } }
        public double Progress { get => _progress; private set => Set(ref _progress, value); }
        public bool ShowProgress { get => _showProgress; private set => Set(ref _showProgress, value); }
        public string Message { get => _message; private set => Set(ref _message, value); }
        public bool Ready => SetupSteps.Ready(SetupSteps.Evaluate(_facts));

        public bool WifiWanted
        {
            get => _facts.WifiWanted;
            set
            {
                _facts.WifiWanted = value;
                if (!value && _facts.UsbUdid != null)
                {
                    _settings.Current.SetWifiDevice(_facts.UsbUdid, false);
                    _settings.ScheduleSave();
                }
                Rebuild();
            }
        }

        public async Task RefreshAsync()
        {
            await Run("Đang kiểm tra...", async () =>
            {
                _facts.RuntimeInstalled = CoreDeviceRuntime.IsCurrent;
                _facts.WindowsSetupDone = _facts.RuntimeInstalled && await WindowsSetupState.IsDoneAsync();
                _facts.AppleServiceReachable = CoreDeviceCommands.AppleServiceReachable();
                _facts.UsbUdid = null;
                if (_facts.RuntimeInstalled && _facts.AppleServiceReachable)
                {
                    _facts.UsbUdid = (await CoreDeviceCommands.UsbDevicesAsync()).FirstOrDefault();
                }
                if (_facts.UsbUdid != null)
                {
                    var r = await CoreDeviceCommands.DeveloperModeAsync(_facts.UsbUdid);
                    _facts.Trusted = r.Succeeded ? true : r.Error == CliError.NeedsTrust || r.Error == CliError.TrustDenied || r.Error == CliError.DeviceLocked ? false : (bool?)null;
                    _facts.DeveloperMode = r.Succeeded ? DeviceQueries.ParseBool(r.Stdout) : null;
                    _facts.WifiPaired = CoreDeviceCommands.RemotePaired(_facts.UsbUdid);
                    _facts.WifiWanted |= _settings.Current.WifiDevices().Contains(_facts.UsbUdid, StringComparer.OrdinalIgnoreCase);
                }
            });
        }

        private async Task InstallAsync(string zip)
        {
            await Run("Windows sẽ hỏi quyền quản trị một lần.", async () =>
            {
                var outcome = await CoreDeviceRuntime.SetupAsync(zip);
                Message = outcome == "ok" ? "Đã cài thành phần." : outcome == "cancelled" ? "Đã hủy. Cần quyền quản trị để cài." : "Cài không được: " + outcome;
            });
            await RefreshAsync();
        }

        private async Task DownloadAndInstallAsync()
        {
            string zip = null;
            await Run("Đang tải thành phần...", async () =>
            {
                ShowProgress = true;
                try
                {
                    zip = CoreDeviceRuntime.IsInstalled && CoreDeviceRuntime.IsCurrent ? null
                        : await CoreDeviceRuntime.DownloadAsync(new Progress<double>(p => Progress = p), CancellationToken.None);
                }
                catch (Exception e) when (e is System.IO.IOException || e is InvalidOperationException || e is System.Net.Http.HttpRequestException)
                {
                    Message = e.Message;
                    zip = "";
                }
                finally
                {
                    ShowProgress = false;
                }
            });
            if (zip != "") await InstallAsync(zip);
        }

        private async Task PickZipAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Gói runtime (*-coredevice-runtime.zip)|*-coredevice-runtime.zip" };
            if (dialog.ShowDialog() != true) return;
            try
            {
                CoreDeviceRuntime.CheckZip(dialog.FileName);
            }
            catch (Exception e) when (e is System.IO.InvalidDataException || e is InvalidOperationException || e is System.IO.IOException)
            {
                Message = e.Message;
                return;
            }
            await InstallAsync(dialog.FileName);
        }

        private async Task MountAsync()
        {
            var udid = _facts.UsbUdid;
            await Run("Đang gắn ảnh DDI...", async () =>
            {
                var r = await CoreDeviceCommands.MountAsync(udid, CoreDeviceRoute.Usb);
                _facts.DiskImageMounted = r.Succeeded;
                Message = r.Succeeded ? "" : CoreDeviceMessages.Failure(SessionMachine.FromCli(r.Error));
            });
            Rebuild();
        }

        private async Task RevealAsync()
        {
            var udid = _facts.UsbUdid;
            await Run("Đang hiện tùy chọn trên thiết bị...", async () =>
            {
                var r = await CoreDeviceCommands.RevealDeveloperModeAsync(udid);
                Message = r.Succeeded ? "Vào Cài đặt > Quyền riêng tư & Bảo mật > Chế độ nhà phát triển trên thiết bị. Sau khi máy khởi động lại và bạn xác nhận, bấm Kiểm tra lại." : "Không hiện được tùy chọn. Mở khóa thiết bị rồi thử lại.";
            });
        }

        private async Task PairWifiAsync()
        {
            var udid = _facts.UsbUdid;
            await Run("Đang ghép nối Wi-Fi...", async () =>
            {
                var r = await CoreDeviceCommands.RemotePairAsync(udid);
                _facts.WifiPaired = CoreDeviceCommands.RemotePaired(udid);
                if (_facts.WifiPaired)
                {
                    _settings.Current.SetWifiDevice(udid, true);
                    _settings.ScheduleSave();
                }
                Message = _facts.WifiPaired ? "Đã ghép nối. Thiết bị và laptop cần cùng mạng Wi-Fi loại Riêng tư." : "Ghép nối không được. Mở khóa thiết bị rồi thử lại.";
            });
            Rebuild();
        }

        private async Task Run(string message, Func<Task> work)
        {
            Busy = true;
            Message = message;
            try
            {
                await work();
            }
            finally
            {
                Busy = false;
                Rebuild();
            }
        }

        private void Rebuild()
        {
            Steps.Clear();
            foreach (var step in SetupSteps.Evaluate(_facts))
            {
                switch (step.Id)
                {
                    case SetupStepId.Components: Steps.Add(new SetupStepItem(step, "Tải và cài", new RelayCommand(() => _ = DownloadAndInstallAsync(), () => !Busy))); break;
                    case SetupStepId.AppleService:
                    case SetupStepId.Device: Steps.Add(new SetupStepItem(step, "Kiểm tra lại", RefreshCommand)); break;
                    case SetupStepId.Trust: Steps.Add(new SetupStepItem(step, "Kết nối", RefreshCommand)); break;
                    case SetupStepId.DeveloperMode: Steps.Add(new SetupStepItem(step, "Hiện tùy chọn", new RelayCommand(() => _ = RevealAsync(), () => !Busy))); break;
                    case SetupStepId.DiskImage: Steps.Add(new SetupStepItem(step, "Gắn ảnh", new RelayCommand(() => _ = MountAsync(), () => !Busy))); break;
                    case SetupStepId.Wifi: Steps.Add(new SetupStepItem(step, "Ghép nối", new RelayCommand(() => _ = PairWifiAsync(), () => !Busy))); break;
                }
            }
            Raise(nameof(Ready), nameof(WifiWanted));
        }
    }
}
```

Bước *Kết nối* (Trust) gọi `RefreshAsync`, vì `amfi developer-mode-status` tự hiện hộp thoại Tin cậy (autopair) và chờ tối đa 120 giây. `SessionMachine.FromCli` là `public` (Task C6).

- [ ] **Step 2: Cửa sổ**

`CoreDeviceSetupWindow.xaml`:

```xml
<Window x:Class="MacExtend.Views.CoreDeviceSetupWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Thiết lập điều khiển trực tiếp"
        Width="560" SizeToContent="Height" ResizeMode="NoResize" WindowStartupLocation="CenterScreen"
        Background="{DynamicResource Window.Background}" Foreground="{DynamicResource Text.Primary}"
        FontFamily="{StaticResource Font.Sans}" FontSize="13.5" TextOptions.TextFormattingMode="Display" UseLayoutRounding="True"
        Icon="pack://application:,,,/MacExtend;component/Assets/app.ico">
    <Window.Resources>
        <BooleanToVisibilityConverter x:Key="Visible" />
    </Window.Resources>
    <StackPanel Margin="20">
        <TextBlock Text="Điều khiển trực tiếp" Style="{StaticResource Text.Headline}" />
        <TextBlock Text="Xem và điều khiển iPhone, iPad qua cáp hoặc Wi-Fi, không cần AirPlay. Làm các bước dưới đây một lần." Style="{StaticResource Text.Caption}" TextWrapping="Wrap" Margin="0,4,0,12" />
        <Border Style="{StaticResource Sheet}">
            <ItemsControl ItemsSource="{Binding Steps}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Grid Style="{StaticResource Row}">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <StackPanel Margin="0,0,16,0">
                                <StackPanel Orientation="Horizontal">
                                    <TextBlock Text="{Binding Title}" Style="{StaticResource Text.Label}" />
                                    <TextBlock Text="{Binding StatusText}" Style="{StaticResource Text.Caption}" Margin="8,0,0,0" VerticalAlignment="Center" />
                                </StackPanel>
                                <TextBlock Text="{Binding Detail}" Style="{StaticResource Text.Caption}" TextWrapping="Wrap" Margin="0,2,0,0" />
                            </StackPanel>
                            <Button Grid.Column="1" Content="{Binding ActionText}" Command="{Binding Action}" Style="{StaticResource Button.Secondary}"
                                    VerticalAlignment="Center" Visibility="{Binding HasAction, Converter={StaticResource Visible}}" />
                        </Grid>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </Border>
        <Grid Margin="0,12,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <TextBlock Text="Cho phép điều khiển qua Wi-Fi" Style="{StaticResource Text.Label}" VerticalAlignment="Center" />
            <CheckBox Grid.Column="1" Style="{StaticResource ToggleSwitch}" IsChecked="{Binding WifiWanted}" />
        </Grid>
        <ProgressBar Style="{StaticResource ThinProgress}" Value="{Binding Progress, Mode=OneWay}" Maximum="1" Margin="0,12,0,0"
                     Visibility="{Binding ShowProgress, Converter={StaticResource Visible}}" />
        <TextBlock Text="{Binding Message}" Style="{StaticResource Text.Caption}" TextWrapping="Wrap" Margin="0,8,0,0" />
        <TextBlock Text="Trong lúc điều khiển, camera và micro của thiết bị không dùng được, và màn hình thiết bị phải đang bật." Style="{StaticResource Text.Caption}" TextWrapping="Wrap" Margin="0,8,0,0" />
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
            <Button Content="Chọn tệp runtime..." Command="{Binding PickZipCommand}" Style="{StaticResource Button.Subtle}" Margin="0,0,8,0" />
            <Button Content="Kiểm tra lại" Command="{Binding RefreshCommand}" Style="{StaticResource Button.Secondary}" Margin="0,0,8,0" />
            <Button Content="Đóng" IsCancel="True" Style="{StaticResource Button.Primary}" Click="OnClose" />
        </StackPanel>
    </StackPanel>
</Window>
```

`CoreDeviceSetupWindow.xaml.cs`:

```csharp
using System.Windows;
using MacExtend.ViewModels;

namespace MacExtend.Views
{
    internal partial class CoreDeviceSetupWindow : Window
    {
        public CoreDeviceSetupWindow(CoreDeviceSetupViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            Loaded += async (_, __) => await vm.RefreshAsync();
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
```

- [ ] **Step 3: Build**, như Task D1 Step 6.

- [ ] **Step 4: Checkpoint** — File: 3 file mới.

### Task E6: Mục cài đặt, menu khay, chọn thiết bị và nối dây trong App

**Files:**
- Create: `src/MacExtend.App/Services/CoreDevice/DirectControlUi.cs`, `src/MacExtend.App/Views/DevicePickerWindow.xaml(.cs)`
- Modify: `src/MacExtend.App/App.xaml.cs`, `src/MacExtend.App/ViewModels/MainViewModel.cs`, `src/MacExtend.App/Views/MainWindow.xaml`, `src/MacExtend.App/Tray/TrayIcon.cs`

**Interfaces:**
- Produces: `DirectControlUi(CoreDeviceService service, SettingsService settings)` với `Task OpenAsync()`, `void OpenSetup()`, `void Toggle()`; `MainViewModel` thêm `DirectControlText : string`, `OpenDirectCommand`, `DirectSetupCommand`; `TrayIcon` thêm tham số `CoreDeviceService direct, Action toggleDirect`.

- [ ] **Step 1: `DirectControlUi.cs`**

```csharp
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MacExtend.Core.CoreDevice;
using MacExtend.ViewModels;
using MacExtend.Views;

namespace MacExtend.Services.CoreDevice
{
    /// <summary>Opens a session from the tray or the settings page: setup first if needed, then the device, then the window.</summary>
    internal sealed class DirectControlUi
    {
        private readonly CoreDeviceService _service;
        private readonly SettingsService _settings;
        private CoreDeviceWindow _window;
        private CoreDeviceSetupWindow _setup;

        public DirectControlUi(CoreDeviceService service, SettingsService settings)
        {
            _service = service;
            _settings = settings;
        }

        public void Toggle()
        {
            if (_service.IsActive) _ = _service.StopAsync();
            else _ = OpenAsync();
        }

        public async Task OpenAsync()
        {
            if (_service.IsActive)
            {
                _window?.Activate();
                return;
            }
            if (!CoreDeviceRuntime.IsCurrent || (!CoreDeviceCommands.AppleServiceReachable() && _settings.Current.WifiDevices().Count == 0))
            {
                OpenSetup();
                return;
            }
            var usb = await CoreDeviceCommands.UsbDevicesAsync();
            var wifi = _settings.Current.WifiDevices().Where(CoreDeviceCommands.RemotePaired).ToList();
            var wifiAvailable = wifi.Count > 0 && await WindowsSetupState.IsDoneAsync();
            var udid = TransportChoice.PickDevice(usb, wifi, wifiAvailable, _settings.Current.DirectLastDevice);
            if (udid == null)
            {
                var candidates = TransportChoice.Candidates(usb, wifi, wifiAvailable);
                if (candidates.Count == 0)
                {
                    MessageBox.Show(CoreDeviceMessages.Failure(SessionFailure.DeviceNotFound), "Điều khiển trực tiếp", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var picker = new DevicePickerWindow(candidates, usb);
                if (picker.ShowDialog() != true) return;
                udid = picker.Chosen;
            }
            var route = TransportChoice.Choose(udid, usb, wifi, wifiAvailable);
            _settings.Current.DirectLastDevice = udid;
            _settings.ScheduleSave();
            ShowWindow();
            _service.Start(udid, route);
        }

        public void OpenSetup()
        {
            if (_setup != null)
            {
                _setup.Activate();
                return;
            }
            _setup = new CoreDeviceSetupWindow(new CoreDeviceSetupViewModel(_settings));
            _setup.Closed += (_, __) => _setup = null;
            _setup.Show();
        }

        private void ShowWindow()
        {
            if (_window != null)
            {
                _window.Activate();
                return;
            }
            _window = new CoreDeviceWindow(_service, _settings, OpenSetup, () => { _ = RetryAsync(); });
            _window.Closed += (_, __) => _window = null;
            _window.Show();
        }

        private async Task RetryAsync()
        {
            await _service.StopAsync();
            await OpenAsync();
        }
    }
}
```

`RetryAsync` gọi `OpenAsync` khi cửa sổ cũ còn mở; `ShowWindow` dùng lại cửa sổ đó.

- [ ] **Step 2: `DevicePickerWindow`**

```xml
<Window x:Class="MacExtend.Views.DevicePickerWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Chọn thiết bị" Width="380" SizeToContent="Height" ResizeMode="NoResize" WindowStartupLocation="CenterScreen"
        Background="{DynamicResource Window.Background}" Foreground="{DynamicResource Text.Primary}"
        FontFamily="{StaticResource Font.Sans}" FontSize="13.5">
    <StackPanel Margin="20">
        <TextBlock Text="Có nhiều thiết bị. Chọn thiết bị để điều khiển." Style="{StaticResource Text.Body}" TextWrapping="Wrap" />
        <ListBox x:Name="List" Margin="0,12,0,0" MinHeight="80" DisplayMemberPath="Label" MouseDoubleClick="OnChoose" />
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
            <Button Content="Hủy" IsCancel="True" Style="{StaticResource Button.Secondary}" Margin="0,0,8,0" />
            <Button Content="Điều khiển" IsDefault="True" Style="{StaticResource Button.Primary}" Click="OnChoose" />
        </StackPanel>
    </StackPanel>
</Window>
```

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace MacExtend.Views
{
    internal partial class DevicePickerWindow : Window
    {
        private sealed class Item
        {
            public string Udid { get; set; }
            public string Label { get; set; }
        }

        public DevicePickerWindow(IReadOnlyList<string> candidates, IReadOnlyList<string> usb)
        {
            InitializeComponent();
            // names would need a lockdown session per device; the last digits are enough to tell two devices apart
            List.ItemsSource = candidates.Select(u => new Item
            {
                Udid = u,
                Label = (usb.Contains(u) ? "Qua cáp" : "Qua Wi-Fi") + " · thiết bị …" + u.Substring(u.Length - 6),
            }).ToList();
            List.SelectedIndex = 0;
        }

        public string Chosen { get; private set; }

        private void OnChoose(object sender, RoutedEventArgs e)
        {
            if (!(List.SelectedItem is Item item)) return;
            Chosen = item.Udid;
            DialogResult = true;
        }
    }
}
```

- [ ] **Step 3: App wiring**

Trường mới trong `App`: `private Services.CoreDevice.CoreDeviceService _direct; private Services.CoreDevice.DirectControlUi _directUi;`. Trong `OnStartup`, sau khi tạo `_bluetooth`:

```csharp
            _direct = new Services.CoreDevice.CoreDeviceService(Dispatcher);
            _directUi = new Services.CoreDevice.DirectControlUi(_direct, _settings);
```

`_vm = new MainViewModel(_controller, _bluetooth, _direct, _settings, ExitForUpdate, () => _ = _directUi.OpenAsync(), _directUi.OpenSetup);`

`_tray = new TrayIcon(_controller, _bluetooth, _direct, _directUi.Toggle, ShowMainWindow, ExitApp, ...)` (thêm hai đối số sau `_bluetooth`, giữ nguyên các đối số còn lại).

`OnSessionEnding`: thêm `_direct?.Dispose();` trước `_controller?.ShutdownNow();`. `OnExit`: thêm `_direct?.Dispose();` trước `_bluetooth?.Dispose();`.

- [ ] **Step 4: `MainViewModel`**

Constructor thêm tham số `CoreDeviceService direct, Action openDirect, Action openDirectSetup`; lưu `_direct`; `_direct.StateChanged += () => Raise(nameof(DirectControlText));`; tạo `OpenDirectCommand = new RelayCommand(openDirect);`, `DirectSetupCommand = new RelayCommand(openDirectSetup);`. Thuộc tính:

```csharp
        public ICommand OpenDirectCommand { get; }
        public ICommand DirectSetupCommand { get; }

        public string DirectControlText =>
            _direct.IsActive ? _direct.StatusText
            : CoreDeviceRuntime.IsCurrent ? "Đã thiết lập. Cắm cáp, hoặc dùng Wi-Fi nếu đã bật, rồi bấm Mở điều khiển."
            : "Chưa thiết lập. Cần Developer Mode trên thiết bị, và iTunes hoặc Apple Devices trên Windows.";
```

(`using MacExtend.Services.CoreDevice;`)

- [ ] **Step 5: `MainWindow.xaml`**, ngay sau `</StackPanel>` đóng khối Bluetooth và trước hairline của mục bảo mật:

```xml
                            <Border Style="{StaticResource Hairline}" />
                            <!-- direct control: CoreDevice over the cable or Wi-Fi, its own window, independent of AirPlay -->
                            <Grid Style="{StaticResource Row}">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <StackPanel Margin="0,0,16,0" VerticalAlignment="Center">
                                    <TextBlock Text="Điều khiển trực tiếp" Style="{StaticResource Text.Label}" />
                                    <TextBlock Text="Qua cáp hoặc Wi-Fi, trong cửa sổ riêng. Bấm đúng điểm, giữ, kéo và gõ phím trên iPhone, iPad." Style="{StaticResource Text.Caption}" Margin="0,2,0,0" TextWrapping="Wrap" />
                                    <TextBlock Text="{Binding DirectControlText}" Style="{StaticResource Text.Caption}" Margin="0,6,0,0" TextWrapping="Wrap" />
                                </StackPanel>
                                <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center">
                                    <Button Content="Thiết lập..." Command="{Binding DirectSetupCommand}" Style="{StaticResource Button.Secondary}" Margin="0,0,8,0" />
                                    <Button Content="Mở điều khiển" Command="{Binding OpenDirectCommand}" Style="{StaticResource Button.Primary}" />
                                </StackPanel>
                            </Grid>
```

- [ ] **Step 6: `TrayIcon`**

Thêm trường `private readonly CoreDeviceService _direct; private readonly ToolStripMenuItem _directItem;`. Constructor thêm tham số `CoreDeviceService direct, Action toggleDirect` sau `bluetooth`:

```csharp
            _direct = direct;
            _directItem = new ToolStripMenuItem("", null, (_, __) => toggleDirect());
```

Thêm `_directItem` vào `_menu.Items.AddRange` ngay sau `_control`. `_direct.StateChanged += Update;` (và gỡ trong `Dispose`). Trong `Update()`:

```csharp
            _directItem.Text = _direct.Phase == SessionPhase.Stopping ? "Đang dừng điều khiển trực tiếp..."
                : _direct.IsActive ? "Dừng điều khiển trực tiếp" : "Điều khiển trực tiếp...";
            _directItem.Enabled = _direct.Phase != SessionPhase.Stopping;
            if (_direct.IsActive && _direct.Phase == SessionPhase.Ready) text = "Đang điều khiển trực tiếp (" + CoreDeviceMessages.Route(_direct.Route).ToLowerInvariant() + ")";
```

(dòng cuối đặt trước `var tip = ...`; `using MacExtend.Core.CoreDevice; using MacExtend.Services.CoreDevice;`). Khi phiên vừa dừng và `_direct.LastStopWasForced` là `true`, hiện bong bóng một lần:

```csharp
            if (_direct.Phase == SessionPhase.Stopped && _direct.LastStopWasForced && !_forcedStopShown)
            {
                _forcedStopShown = true;
                _icon.ShowBalloonTip(5000, "Điều khiển trực tiếp đã dừng", "Phiên phải dừng cưỡng bức. Mở Camera trên thiết bị để chắc camera và micro đã trở lại.", ToolTipIcon.None);
            }
            if (_direct.IsActive) _forcedStopShown = false;
```

(trường `private bool _forcedStopShown;`).

- [ ] **Step 7: Build và test**

Run: `~/.dotnet/dotnet build MacExtend.sln -c Release && ~/.dotnet/dotnet test tests/MacExtend.Core.Tests`
Expected: `Build succeeded.` `0 Warning(s)` `0 Error(s)`, và `Passed!  - Failed:     0`.

- [ ] **Step 8: Checkpoint**

File: 3 mới, 4 sửa. CI toàn bộ xanh: *CoreDevice runtime*, *CoreDevice probe*, *MacExtend app* (có `coredevice setup ok`).

---

## Phase F: tài liệu

### Task F1: Bảng D trong `docs/TESTING.md`

**Files:**
- Modify: `docs/TESTING.md` (thêm mục sau *Thử nghiệm C*)

- [ ] **Step 1: Thêm mục**

```markdown
## Thử nghiệm D: Điều khiển trực tiếp (trong app)

Cài đúng bản do CI đóng gói. Bản beta trên nhánh không có release: lấy `MacExtend-<bản>-coredevice-runtime.zip` từ artifact CI rồi dùng *Chọn tệp runtime...* trong cửa sổ thiết lập. Ghi kết quả vào `docs/research/2026-10-29-coredevice-results.md`, mục mới "Trong app".

| # | Phép thử | Tiêu chí |
|---|---|---|
| D1 | Task chạy từ tài khoản thường | `tools/coredevice/spike-task.ps1` (Task 0 của plan) in PASS. Làm trước mọi hàng khác |
| D2 | Thiết lập lần đầu | Tải hoặc chọn runtime, thanh tiến trình chạy; đúng một hộp thoại UAC; sau đó có `{app}\coredevice\python\python.exe`, task `\MacExtend\MacExtend CoreDevice tunnel` và 2 luật nhóm `MacExtend CoreDevice`. Không có hộp thoại firewall nào hiện ra về sau |
| D3 | Các bước thiết bị | Tin cậy, Developer Mode (hiện tùy chọn, bật, máy khởi động lại), gắn ảnh DDI; mỗi bước đổi sang *Đã xong*; Kiểm tra lại không làm hỏng bước đã xong |
| D4 | Phiên qua cáp | Mở điều khiển khi đang cắm cáp: nhãn *Qua cáp*, có hình, chạm/giữ/kéo/phím, Home, xoay trái/phải, gửi và lấy văn bản tiếng Việt, F11 |
| D5 | Phiên qua Wi-Fi | Bật Wi-Fi trong thiết lập (ghép nối qua cáp), rút cáp, cùng mạng Riêng tư: nhãn *Qua Wi-Fi*, có hình trong khoảng 30 giây, không có UAC (bản cài đặt, tài khoản admin) |
| D6 | Đóng cửa sổ | Cửa sổ biến mất ngay; khay hiện "Đang dừng..." rồi về bình thường; Camera và Ghi âm trên thiết bị hoạt động ngay; với Wi-Fi, không còn tiến trình python của tunneld và không còn card `pymobiledevice3-tunnel-*` (`Get-NetAdapter`) |
| D7 | Camera đang mở | Mở Camera trên thiết bị rồi bấm Mở điều khiển: thông báo "Đóng Camera, Ghi âm hoặc cuộc gọi rồi thử lại", có nút Thử lại |
| D8 | Rút cáp giữa phiên | "Mất kết nối. Cắm lại cáp để tiếp tục."; cắm lại thì tự có hình, không phải bấm gì. Rút cáp, đóng cửa sổ, cắm lại: không có phiên nào tự mở |
| D9 | Wi-Fi rớt | Tắt Wi-Fi trên thiết bị: dải thông báo "Đang kết nối lại..."; bật lại: hình tự quay lại, không có thao tác bị gửi lại |
| D10 | Màn hình thiết bị tắt | Khóa thiết bị: lớp chờ hình có chữ tiếng Việt; mở khóa thì có hình lại |
| D11 | Thoát app giữa phiên | Menu khay > Thoát: app thoát trong tối đa khoảng 45 giây; không còn python; camera/micro trở lại |
| D12 | Cập nhật có runtime mới | Bản mới đổi `coredevice.id`: bộ cập nhật tải thêm gói runtime và chỉ hỏi UAC một lần; sau cập nhật phiên vẫn chạy |
| D13 | Gỡ cài đặt | Không còn `{app}\coredevice`, task và luật nhóm `MacExtend CoreDevice` |
| D14 | Bản portable | Không có UAC khi cài runtime; mỗi phiên Wi-Fi hỏi UAC một lần |
| D15 | Tài khoản không phải admin | Thiết lập hỏi mật khẩu admin một lần; mỗi phiên Wi-Fi hỏi lại; cáp không hỏi |
| D16 | Cùng AirPlay và Bluetooth | Phản chiếu AirPlay và điều khiển Bluetooth vẫn chạy như trước khi có và không có phiên điều khiển trực tiếp |
| D17 | Apple Devices thay iTunes | Gỡ iTunes, cài Apple Devices (Microsoft Store): bước dịch vụ Apple và phiên qua cáp vẫn chạy |
| D18 | Không có WebView2 Runtime | Trên máy chưa có runtime (hiếm trên Windows 11): cửa sổ báo và đưa link tải |
| D19 | Hai thiết bị | Cắm iPhone và iPad: hộp chọn thiết bị hiện ra; lần sau mở thẳng thiết bị đã dùng |
```

- [ ] **Step 2: Checkpoint** — File: `docs/TESTING.md`.

### Task F2: README và README của công cụ

**Files:**
- Modify: `README.md` (thay mục "Thử nghiệm: chạm trực tiếp qua CoreDevice"; thêm ghi công), `tools/coredevice/README.md`

- [ ] **Step 1: `README.md`**

Thay mục `### Thử nghiệm: chạm trực tiếp qua CoreDevice` bằng:

```markdown
### Điều khiển trực tiếp (qua cáp hoặc Wi-Fi)

Một cách điều khiển iPhone, iPad khác Bluetooth, trong cửa sổ riêng, không cần AirPlay: bấm đúng điểm, giữ, kéo và gõ phím. Dùng dịch vụ CoreDevice dành cho lập trình viên của Apple, qua [pymobiledevice3](https://github.com/doronz88/pymobiledevice3).

Cần: Developer Mode trên thiết bị (bật một lần), iTunes (apple.com) hoặc Apple Devices (Microsoft Store) trên Windows, và một lần tải thành phần khoảng 100 MB. Windows hỏi quyền quản trị đúng một lần lúc thiết lập. Qua Wi-Fi, thiết bị và laptop phải cùng mạng loại Riêng tư.

1. Mục *Điều khiển* > *Điều khiển trực tiếp* > *Thiết lập...*, làm các bước theo thứ tự.
2. Bấm *Mở điều khiển*, hoặc menu khay > *Điều khiển trực tiếp...*. Đang cắm cáp thì app dùng cáp; không thì dùng Wi-Fi nếu đã bật cho thiết bị đó.

Giới hạn: trong lúc điều khiển, camera và micro của thiết bị không dùng được; màn hình thiết bị phải bật; tiếng vẫn ra loa thiết bị. Nhật ký: `%LOCALAPPDATA%\MacExtend\logs\coredevice.log` và `tunneld.log`, chỉ ghi trạng thái, không ghi phím hay văn bản.
```

Trong mục *Giấy phép và ghi công* thêm:

```markdown
- [pymobiledevice3](https://github.com/doronz88/pymobiledevice3) (GPL-3.0-or-later) và các gói Python đi kèm, cùng [CPython](https://www.python.org) (PSF License): thành phần Điều khiển trực tiếp, tải riêng; giấy phép của từng gói nằm trong `coredevice\python\Lib\site-packages\*.dist-info`.
```

- [ ] **Step 2: `tools/coredevice/README.md`**

- Đoạn đầu: công cụ console vẫn là probe cho người phát triển; app dùng chung runtime và `MacExtend.CoreDeviceHost`.
- Mục *Cài đặt trên Windows*: Python 3.13 x64, lock `requirements-win-x64-py313.lock.txt`.
- Thay khối hướng dẫn venv 3.13 riêng cho tunneld trong mục *5. Wi-Fi* bằng: "Runtime 3.13 dùng chung cho serve-web và tunneld; không cần venv riêng."
- Thêm mục *Runtime của app*: `build-runtime.ps1`, `verify-runtime.ps1`, `runtime-python.txt`, cách bump phiên bản.

- [ ] **Step 3: Checkpoint**

File: 2 README. Kiểm tra: `grep -n "venv313\|requirements-win-x64.lock" README.md tools/coredevice/README.md` không in gì.

---

## Tự rà soát (đã làm khi viết plan)

- **Độ phủ thiết kế:** phần 1 → B1, D1, E3–E6; phần 2 → A1–A3, C8, C11, D3, D4, D5; phần 3 → Task 0, C7, C9, C10, D1, D2, E1, E5; phần 4 → C4–C6, E2, E3, E4, E6; phần 5 → E4, E5, E6, F1, F2. Những chỗ đổi so với thiết kế nằm ở mục *Chỗ plan điều chỉnh*.
- **Tên dùng chung:** `CoreDeviceRoute`, `SessionPhase`, `SessionFailure`, `SessionAction`, `TunnelOutcome`, `CliError`, `CommandResult`, `RuntimePackage`, `CoreDeviceSetupScripts.TaskFullName`, `TunneldArg` được định nghĩa ở Phase B/C và dùng đúng tên ở Phase D/E.
- **Review Focus** có test trong C1, C5, C6, C8, C9, C10, C11.
