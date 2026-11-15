# Kiểm thử

## Thử nghiệm B: điều khiển Bluetooth khi phản chiếu (bản beta)

**Đã thử trước khi tích hợp** (2026-10-28, công cụ ngoài windows-ble-hid v0.6.0 chạy song song MacExtend, iPhone): adapter của laptop thử hỗ trợ peripheral; phím, chuột (AssistiveTouch) và cuộn chạy được; vẫn ổn định khi đang phản chiếu. Lần đầu công cụ báo `HID service 0x1812: RadioNotAvailable` vì Bluetooth của Windows đang tắt. Đây là kết quả của công cụ ngoài, không phải của MacExtend.

**Bản tích hợp trong MacExtend** (từ 1.0.8-beta.1): mọi hàng dưới đây **chưa kiểm chứng**. Trên Mac chỉ chạy được unit test của Core và biên dịch app. Cách dùng xem README, mục *Điều khiển iPhone/iPad bằng chuột và bàn phím*.

Ghi cho mỗi lượt: bản MacExtend, Windows build, model adapter/driver Bluetooth, model thiết bị, iOS/iPadOS, AssistiveTouch, băng tần Wi-Fi và kết quả thực tế. Chạy riêng với iPhone và iPad. Gửi kèm `%LOCALAPPDATA%\MacExtend\logs\bluetooth.log` (chỉ có thay đổi trạng thái) và `uxplay.log` khi có lỗi.

| # | Phép thử | Tiêu chí | Kết quả |
|---|---|---|---|
| B1 | Bật công tắc khi chưa phản chiếu | Dòng Bluetooth báo *Sẵn sàng...*; tắt Bluetooth Windows thì báo *đang tắt*, bật lại thì tự về *Sẵn sàng*; AirPlay không bị ảnh hưởng | Chưa kiểm chứng |
| B2 | Ghép lần đầu | Đang phản chiếu, Cài đặt > Bluetooth trên thiết bị thấy tên laptop, ghép được; app báo *Đã kết nối <thiết bị>* | Chưa kiểm chứng |
| B3 | Bấm để điều khiển | Bấm vào hình: con trỏ Windows ẩn, dải *Đang điều khiển...* hiện rồi mờ sau khoảng 3 giây; bấm, kéo, cuộn và gõ trong Ghi chú đi sang thiết bị; cú bấm đầu không chạm vào thiết bị; bấm vào thanh tiêu đề hay viền cửa sổ không bắt đầu điều khiển | Chưa kiểm chứng |
| B4 | Gõ văn bản | Chữ hoa, Shift/Ctrl/Alt, phím Windows như Command (Cmd+Space, Cmd+H), ký tự đặc biệt, tiếng Việt bằng bàn phím tiếng Việt trên thiết bị (tắt UniKey/EVKey trên Windows) | Chưa kiểm chứng |
| B5 | Cửa sổ, toàn màn hình, xoay | Thử cửa sổ thường và toàn màn hình, dọc/ngang, scale 100/150%; ghi lệch, giật hoặc trôi con trỏ sau khi dừng tay | Chưa kiểm chứng |
| B6 | Trả lại Windows | Ctrl+Alt+Q; Alt+Tab (Windows chuyển cửa sổ ngay); Win+L; Ctrl+Alt+Del rồi Hủy; một cửa sổ khác bật lên trước. Mỗi lần: chuột/phím Windows dùng được ngay, không kẹt phím hay nút trên Windows lẫn trên thiết bị, kể cả khi đang giữ phím hoặc đang kéo | Chưa kiểm chứng |
| B7 | Mất kết nối | Đang điều khiển thì tắt Bluetooth trên thiết bị, ngắt AirPlay, đóng cửa sổ video, tắt công tắc trong app: điều khiển dừng ngay, Windows dùng được ngay | Chưa kiểm chứng |
| B8 | Kết nối lại | Ngừng phản chiếu: sau khoảng 10 giây thiết bị mất bàn phím Bluetooth và có lại bàn phím ảo. Phản chiếu lại: thiết bị tự nối lại, không phải ghép lại (ghi thời gian). Thử cả sau khi khóa/mở thiết bị, tắt/bật Bluetooth, khởi động lại MacExtend và Windows | Chưa kiểm chứng |
| B9 | Đổi nguồn và nhiều thiết bị | iPhone và iPad cùng đã ghép: thiết bị trùng tên với nguồn đang phản chiếu được chọn; không trùng thì app hiện ô chọn. Nguồn mới thay phiên khi đang điều khiển: điều khiển dừng | Chưa kiểm chứng |
| B10 | Dùng liên tục | 30 phút: ghi mất phím, kẹt nút, hình/tiếng giật so với khi tắt công tắc, việc phải ghép lại; lặp B6 sau lượt dài | Chưa kiểm chứng |
| B11 | Menu khay | Khi đã kết nối: mục *Điều khiển <thiết bị>* bắt đầu điều khiển và đưa video lên trước; khi đang điều khiển: *Dừng điều khiển* trả lại Windows | Chưa kiểm chứng |

## Thử nghiệm C: chạm trực tiếp qua CoreDevice (công cụ thử, không thuộc app)

Công cụ `tools/MacExtend.CoreDeviceProbe` chạy `pymobiledevice3 serve-web` (khóa 11.47.0). Cài đặt và lệnh cụ thể ở [tools/coredevice/README.md](../tools/coredevice/README.md); hợp đồng upstream ở [research/2026-10-29-coredevice-probe.md](research/2026-10-29-coredevice-probe.md). Ghi kết quả vào [research/2026-10-29-coredevice-results.md](research/2026-10-29-coredevice-results.md), tách iPhone/iPad và USB userspace / USB tunneld / Wi-Fi. Hàng nào chưa chạy trên thiết bị thật thì giữ **Chưa chạy**.

Thứ tự: chạy serve-web trực tiếp qua USB trước, rồi C7 (camera/micro) và C10–C12 (trả media) ngay, sau đó mới tới probe, tunneld, Wi-Fi, kết nối lại và AirPlay đồng thời.

| # | Phép thử | Tiêu chí |
|---|---|---|
| C1 | Hình | Viewer hiện hình trên Edge và Chrome (HEVC qua WebCodecs); ghi trình duyệt, GPU/driver, chuỗi codec. Thời gian từ lúc chạy tới *sẵn sàng* và tới khung hình đầu |
| C2 | Chạm, giữ, kéo, Home | Bấm vào hình chạm đúng điểm; giữ mở menu ngữ cảnh; kéo cuộn danh sách và nhấc khi thả; chuột phải hoặc nút Home về màn hình chính |
| C3 | Nhả | Kéo rồi thả ngoài hình, Alt+Tab khi đang giữ, đổi tab, thu nhỏ trình duyệt: không còn ngón tay hay phím bị giữ trên thiết bị |
| C4 | Bàn phím | Chữ, số, Shift, Backspace, Enter, mũi tên, Cmd (phím Windows) + Space/H; ghi phím tắt nào bị Windows giữ lại |
| C5 | Tiếng Việt, clipboard | UniKey/EVKey tắt, bàn phím tiếng Việt trên iOS: gõ được dấu. Văn bản tiếng Việt đầy đủ qua *Send to device* rồi dán. Ghi riêng nếu bật UniKey (chỉ khảo sát) |
| C6 | Xoay | Xoay thiết bị và nút xoay của viewer: hình đúng hướng, chạm vẫn đúng điểm ở cả dọc và ngang |
| C7 | Camera, micro | Thứ tự 1: mở Camera/Ghi âm/cuộc gọi video trước rồi chạy serve-web (ghi thông báo lỗi). Thứ tự 2: serve-web đang chạy rồi mở Camera, Ghi âm, cuộc gọi video (ghi hình đen, tiếng câm, cuộc gọi có tiếng không) |
| C8 | Âm thanh | Loa và tai nghe của thiết bị, và âm thanh AirPlay của MacExtend: trước, trong và sau phiên; với CoreDevice riêng, AirPlay riêng và cả hai cùng lúc |
| C9 | Lệnh API probe | Đóng viewer, chạy `tap 32768 32768`, `hold`, `drag`, `key 0x04`, `release`, `home` ở hướng dọc: thiết bị phản hồi đúng, không kẹt |
| C10 | Trả media sau `stop` | Probe báo stopped trong 40 giây; `get-media-stream-server-status`, Camera và Ghi âm hoạt động, chỉ báo chia sẻ màn hình tắt |
| C11 | Trả media sau Ctrl+C | Như C10, với Ctrl+C trong cửa sổ probe; thêm một lần Ctrl+C thứ hai khi đang dừng |
| C12 | Sau kill cưỡng bức | Kill probe bằng Task Manager, đóng cửa sổ probe, và để hết 40 giây: không còn tiến trình Python hay cổng mở; ghi media có bị giữ không và bước phục hồi nào hiệu quả (chạy lại serve-web rồi dừng đúng cách, cuối cùng mới khởi động lại thiết bị) |
| C13 | Mất kết nối | Rút cáp, khóa thiết bị, tắt Wi-Fi khi đang chạy: userspace cần chạy lại probe; tunneld tự nối lại hình; không có thao tác bị gửi lại |
| C14 | Độ trễ | Ít nhất 20 thao tác mỗi transport; ghi cách đo (ví dụ quay video 240 fps cả tay và màn hình) và trung vị/lớn nhất |
| C15 | Cùng AirPlay | MacExtend đang phản chiếu qua AirPlay, đồng thời probe: hai hình, âm thanh, chạm, và khi dừng probe AirPlay vẫn chạy |

## Thử nghiệm D: Điều khiển trực tiếp (trong app)

Tính năng nằm trong app (mục *Điều khiển* > *Điều khiển trực tiếp*), khác với công cụ probe ở bảng C. Cài đúng bản do CI đóng gói. Bản beta trên nhánh không có release: lấy `MacExtend-<bản>-coredevice-runtime.zip` từ artifact CI rồi dùng *Chọn tệp runtime...* trong cửa sổ thiết lập. Ghi kết quả vào [research/2026-10-29-coredevice-results.md](research/2026-10-29-coredevice-results.md), mục mới "Trong app", tách iPhone và iPad.

Mọi hàng dưới đây **chưa kiểm chứng**: trên Mac chỉ chạy được unit test của Core và biên dịch app. Gửi kèm `%LOCALAPPDATA%\MacExtend\logs\coredevice.log` khi có lỗi (log chỉ ghi trạng thái, không ghi phím, văn bản clipboard, UDID đầy đủ hay output thô của Python).

Quy ước: **bản cài** là bản cài bằng bộ cài vào `C:\Program Files\MacExtend` (hoặc `Program Files (x86)`), chạy bằng tài khoản Windows có quyền quản trị. Chỉ bản cài như vậy có tác vụ định kỳ (Scheduled Task); bản portable và bản cài vào thư mục khác dùng chung đường không có tác vụ (D26, D28). Làm D1 trước, rồi D2 đến D7, rồi các nhóm còn lại.

Thiết lập:

| # | Phép thử | Tiêu chí |
|---|---|---|
| D1 | Task chạy từ tài khoản thường | `tools/coredevice/spike-task.ps1` in PASS: `-Create -User "$env:USERDOMAIN\$env:USERNAME"` ở PowerShell quản trị, `-Run` ở PowerShell thường, `-Remove` ở PowerShell quản trị. Làm trước mọi hàng khác |
| D2 | Thiết lập lần đầu (bản cài) | Cửa sổ thiết lập hiện chip và nút sau *Đang kiểm tra...*. *Tải và cài* (hoặc *Chọn tệp runtime...* với bản beta): thanh tiến trình và phần trăm chạy; đúng **một** hộp thoại UAC; sau đó có `{app}\coredevice\python\python.exe`, `pythonw.exe` và `RUNTIME_ID` (không có `coredevice.new`, `coredevice.old`), tác vụ `\MacExtend\MacExtend CoreDevice tunnel` (chạy `pythonw.exe` của runtime, quyền cao nhất, không có trigger), và đúng 4 luật tường lửa nhóm `MacExtend CoreDevice` (python.exe và pythonw.exe, mỗi cái một luật UDP vào từ `fc00::/7` và một luật UDP 5353 chỉ mạng Riêng tư). Không có hộp thoại firewall nào hiện ra về sau |
| D3 | Chọn tệp runtime, hủy và thiếu | Bản beta: chọn tệp zip sai thì báo "Tệp này không phải gói runtime..." trước khi hỏi UAC; chọn Không ở UAC thì báo "Đã hủy..."; mất mạng khi tải thì báo kiểm tra Internet. Xóa một luật tường lửa hoặc tác vụ nhưng giữ runtime: hiện nút *Cài lại*, bấm thì chỉ một UAC và mọi thứ về đủ |
| D4 | Các bước thiết bị | *Kết nối* (thiết bị khóa rồi mở khóa, hộp thoại Tin cậy hiện trên thiết bị), *Hiện tùy chọn* rồi bật Developer Mode (thiết bị khởi động lại), *Gắn ảnh* (lần đầu có thể mất vài phút): mỗi bước đổi sang *Đã xong*. *Kiểm tra lại* và mở cửa sổ thiết lập lần nữa không làm hỏng bước đã xong; sau khi khởi động lại thiết bị, bước ảnh hiện *Cần làm* lại. Bước ảnh chưa xong không chặn việc mở điều khiển (mỗi phiên tự gắn ảnh trước khi chạy) |
| D5 | Trust và iOS 27 | Thiết bị chạy iOS 27: sau *Gắn ảnh* bước ảnh thành *Đã xong* và vẫn *Đã xong* sau *Kiểm tra lại* hoặc mở cửa sổ thiết lập mới (`coredevice.log` có dòng `[setup] image list: ok, mounted yes`). Bước ảnh chưa xong không chặn mở điều khiển. Hộp thoại Tin cậy có thể được chờ tới 120 giây; trong lúc đó hàng Tin cậy hiện *Đang làm* |
| D6 | Dịch vụ Apple | Khi chưa có Apple Mobile Device Service (chưa cài iTunes hay Apple Devices): bước dịch vụ Apple là *Cần làm* và các bước sau không chạy; cài xong, *Kiểm tra lại* chuyển sang *Đã xong* |
| D7 | Trang cài đặt và menu khay | Công tắc *Điều khiển trực tiếp* tắt sẵn. Bật khi chưa thiết lập: cửa sổ thiết lập tự mở, dòng trạng thái "Chưa thiết lập..."; đóng cửa sổ thiết lập thì trạng thái đổi ngay. Đã thiết lập, chưa có gì phản chiếu: "Đang chờ iPhone hoặc iPad phản chiếu." Menu khay: *Bật điều khiển trực tiếp* / *Tắt điều khiển trực tiếp* đổi theo công tắc ở trang cài đặt và ngược lại; khi đang điều khiển, dòng đầu menu là "Đang điều khiển <tên>". Mac phản chiếu thì không có phiên nào chạy. Các mục Bluetooth không đổi. Giao diện đúng ở nền Sáng và Tối, scale 100/125/150% |
| D8 | Đóng cửa sổ thiết lập giữa chừng | Đóng cửa sổ thiết lập khi đang có hộp thoại UAC hoặc đang gắn ảnh, rồi mở lại: đúng bước đó hiện *Đang làm* kèm thông báo, không có UAC thứ hai. Đóng cửa sổ khi đang hiện "Đang dừng phiên...", rồi mở lại: bước đó hiện *Đang làm* cho tới khi việc dừng xong, và không có UAC nào theo sau |

Phiên qua cáp:

| # | Phép thử | Tiêu chí |
|---|---|---|
| D9 | Phiên qua cáp | Bật công tắc, cắm cáp, phản chiếu iPhone: trạng thái qua "Đang tìm thiết bị...", "Đang chuẩn bị thiết bị...", "Đang kết nối..." rồi "Đang điều khiển qua cáp..." với dấu kiểm xanh; trên hình hiện dòng gợi ý vài giây. Bấm là chạm đúng điểm, giữ mở menu ngữ cảnh, kéo cuộn danh sách và vuốt giữa các trang Home theo đúng tay; chuột phải và Ctrl+H là Home. Ở cửa sổ thường, phóng to, toàn màn hình (Alt+Enter) và khi cửa sổ rộng hơn hình (dải đen hai bên): điểm chạm vẫn đúng; bấm vào dải đen không chạm gì |
| D10 | Bàn phím | Chữ, số, Shift, Backspace, Enter, mũi tên trong ô tìm kiếm: đúng; giữ một phím thì thiết bị tự lặp. Ctrl+A, Ctrl+C, Ctrl+X, Ctrl+Z làm như Command trên iOS; Ctrl+V dán văn bản tiếng Việt đầy đủ từ laptop vào chỗ con trỏ; Ctrl+mũi tên lên/xuống đổi âm lượng; Alt+Tab khi đang giữ phím thì thiết bị không còn phím nào bị giữ; Alt+F4 đóng cửa sổ AirPlay. Tiếng Việt với UniKey/EVKey tắt và bàn phím tiếng Việt của iOS: gõ được dấu. Bật UniKey: ghi lại kết quả (chỉ khảo sát) |
| D11 | Chọn thiết bị | Một thiết bị: điều khiển ngay. iPhone và iPad cùng cắm cáp, iPhone phản chiếu: app tự chọn iPhone (cùng tên hoặc cùng model), không hỏi. Hai thiết bị cùng tên và cùng model: trạng thái "Có nhiều thiết bị..." và nút *Chọn thiết bị...* ở trang cài đặt và menu khay; hộp chọn xếp cáp trước, chọn bằng mũi tên/Enter/Esc/nhấp đúp; chọn xong thì phiên chạy, lần sau thiết bị đó tự được chọn cho tên AirPlay ấy. Không thấy thiết bị nào: trạng thái báo không thấy thiết bị, kèm nút *Thử lại* |

Phiên qua Wi-Fi:

| # | Phép thử | Tiêu chí |
|---|---|---|
| D12 | Bật Wi-Fi cho thiết bị | Công tắc *Cho phép điều khiển qua Wi-Fi* tắt và có gợi ý "Cắm iPhone..." khi chưa cắm thiết bị. Cắm cáp, bật: *Ghép nối* (RemotePairing qua cáp, không hiện hộp thoại Tin cậy), thiết bị vào danh sách Wi-Fi; tắt thì thiết bị rời danh sách |
| D13 | Phiên Wi-Fi (bản cài, tài khoản admin) | Rút cáp, thiết bị và laptop cùng mạng Riêng tư: nhãn *Qua Wi-Fi*, có hình trong khoảng 30 giây, không có UAC. Lúc chạy có tiến trình `pythonw.exe` của tunneld (khởi động qua tác vụ) và card `pymobiledevice3-tunnel-*` trong `Get-NetAdapter`. Nếu đang cắm cáp thì phiên dùng cáp |
| D14 | Wi-Fi bị thiếu phần Windows | Bản cài, thiết bị đã ghép, rút cáp, xóa một luật tường lửa của nhóm hoặc tác vụ, rồi phản chiếu: trạng thái báo thiếu phần thiết lập Windows cho Wi-Fi. *Thiết lập...* rồi cài lại thành phần (đúng một UAC); đóng cửa sổ thiết lập thì phiên Wi-Fi tự mở. Cổng 127.0.0.1:49151 đang do chương trình khác dùng: báo cổng bị dùng, không có tiến trình nào của chương trình khác bị tắt |
| D15 | Tunneld có sẵn và tunneld không tắt | Tunneld chạy tay từ trước (`pmd3 remote tunneld`, quyền quản trị): phiên Wi-Fi dùng lại nó và khi dừng app **không** tắt nó. Bản cài: tác vụ không tắt khi nhận `/shutdown` (ví dụ giả bằng cách chặn lệnh) thì sau khoảng 10 giây app chạy `schtasks /End` và cổng 49151 đóng; app chỉ tắt tunneld do chính nó bật |
| D16 | Dừng khi UAC đang hiện | Bản portable: phản chiếu khi không cắm cáp để hộp UAC của Wi-Fi hiện ra, rồi tắt công tắc khi hộp còn mở. Chọn Có sau đó: tunneld không bị bỏ sót (không còn `pythonw.exe` sau khi dừng, hoặc app tắt nó); chọn Không: báo bị từ chối, không có phiên nào mở |

Dừng và trả camera, micro:

| # | Phép thử | Tiêu chí |
|---|---|---|
| D17 | Hết phản chiếu và tắt công tắc | Dừng phản chiếu trên iPhone: phiên còn 5 phút (phản chiếu lại trong lúc đó thì điều khiển được ngay, không có *Đang kết nối điều khiển...* lâu), sau đó dừng. Tắt công tắc giữa phiên: dừng ngay. Sau khi dừng, Camera và Ghi âm trên thiết bị hoạt động, không còn biểu tượng chia sẻ màn hình. Với Wi-Fi: không còn `pythonw.exe` của tunneld, cổng 49151 đã đóng và card `pymobiledevice3-tunnel-*` không còn. Tắt rồi bật lại ngay khi vẫn phản chiếu: "Đang dừng phiên trước..." rồi phiên mới chạy |
| D18 | Camera và micro theo nhu cầu | Đang phản chiếu, điều khiển sẵn sàng, chưa đưa chuột vào hình: Camera có hình, Ghi âm thu được tiếng, không có biểu tượng chia sẻ màn hình. Đưa chuột vào hình rồi bấm: chạm đúng; Camera đen trong lúc đó. Không đụng vào hình 20 giây: Camera và Ghi âm hoạt động lại (`coredevice.log` có `InputStreamClosed idle`). Đang gọi FaceTime hoặc đang mở Camera rồi bấm vào hình: trên hình hiện "Camera hoặc micro đang được dùng trên thiết bị..." (`InputStreamRefused` trong log), không có thao tác nào bị gửi muộn sau khi đóng ứng dụng đó |
| D19 | Thoát app giữa phiên | Menu khay > Thoát MacExtend: khay hiện "Đang dừng..." và menu trống; qua cáp app thoát trong tối đa khoảng 45 giây, qua Wi-Fi trong khoảng 70 giây; không còn python; Camera và Ghi âm trở lại. Không có phiên: thoát nhanh như trước. Đăng xuất hoặc tắt Windows khi đang có phiên: không treo. Phiên không dừng sạch trong 40 giây: một thông báo "Điều khiển trực tiếp đã dừng" |

Mất kết nối:

| # | Phép thử | Tiêu chí |
|---|---|---|
| D20 | Rút cáp giữa phiên | "Mất kết nối. Cắm lại cáp để tiếp tục." trong khoảng 6 đến 9 giây (không có dòng "cable presence check: no answer" trong `coredevice.log`); cắm lại thì tự có hình trong tối đa khoảng 45 giây, không phải bấm gì. Rút cáp, đóng cửa sổ, cắm lại: không có phiên nào tự mở. Trong lúc phiên cáp chạy, không có `python.exe` mới được bật mỗi vài giây |
| D21 | Wi-Fi rớt | Phiên qua Wi-Fi, tắt Wi-Fi trên thiết bị: AirPlay cũng dừng. Bật lại trong 10 giây và phản chiếu lại: điều khiển trở lại, không có thao tác bị gửi lại. Quá 10 giây: phiên dừng, lần phản chiếu sau mở phiên mới. Phiên qua cáp khi Wi-Fi của thiết bị rớt rồi trở lại: ghi lại AirPlay và phiên ra sao |
| D22 | Màn hình thiết bị tắt | Khóa thiết bị khi đang điều khiển: ghi lại AirPlay và trạng thái. Mở khóa: điều khiển được lại, hoặc trạng thái báo lỗi kèm *Thử lại* và *Thử lại* chạy được |

Cập nhật, gỡ cài đặt và bản đặc biệt:

| # | Phép thử | Tiêu chí |
|---|---|---|
| D24 | Cập nhật có runtime mới | Bản đã thiết lập, bản mới đổi `coredevice.id`: dung lượng tải gồm cả gói runtime, thanh tiến trình chạy đến hết, chỉ một UAC. Khi bấm Cập nhật lúc có phiên, phiên dừng trước rồi bộ cập nhật mới chạy; tunneld của tác vụ cũng được dừng; app mở lại bản mới và phiên chạy được. Gói sai SHA-256 thì cập nhật bị từ chối. Cùng `coredevice.id` hoặc chưa cài runtime: không tải thêm gì |
| D25 | Gỡ cài đặt | Chạy trình gỡ: không còn `{app}\coredevice`, tác vụ và luật nhóm `MacExtend CoreDevice` |
| D26 | Bản portable | Thiết lập: cài runtime vào `coredevice` cạnh `MacExtend.exe` không có UAC; không tạo tác vụ; ghép nối Wi-Fi hoặc phiên Wi-Fi đầu tiên hỏi UAC một lần để mở tường lửa; mỗi phiên Wi-Fi hỏi UAC một lần để chạy tunneld (chọn Không thì báo bị từ chối); phiên cáp không hỏi. Cập nhật runtime không cần UAC |
| D27 | Bản portable thất bại hoặc ở thư mục chỉ đọc | Bản portable: phiên Wi-Fi không lên thì mở tường lửa lại bằng đúng một UAC bổ sung, không lặp lại. Portable đặt trong thư mục chỉ đọc: *Tải và cài* báo bằng tiếng Việt rằng cần cài MacExtend bằng bộ cài vào Program Files hoặc giải nén vào thư mục có quyền ghi, không hiện chữ tiếng Anh của hệ thống |
| D28 | Bản cài ngoài Program Files | Cài bằng bộ cài vào thư mục khác (ví dụ `C:\MacExtend`): *Tải và cài* không tạo tác vụ, runtime cài không cần UAC; hành xử Wi-Fi như D21. Bản cài vào `Program Files (x86)` thì cũng có tác vụ như D2 |
| D29 | Tài khoản không phải admin | Đăng nhập tài khoản Windows thường: chỉ chạy qua cáp. Công tắc Wi-Fi tắt, bước Wi-Fi và gợi ý ghi "Điều khiển qua Wi-Fi cần tài khoản Windows có quyền quản trị; tài khoản này chỉ dùng được qua cáp."; không UAC cho Wi-Fi. Thiết lập lần đầu hỏi mật khẩu quản trị một lần; phiên cáp không hỏi |
| D30 | Apple Devices thay iTunes | Gỡ iTunes, cài Apple Devices (Microsoft Store): bước dịch vụ Apple và phiên qua cáp vẫn chạy; ghi phiên Wi-Fi có chạy không |
| D32 | Cùng AirPlay và Bluetooth | Phản chiếu AirPlay và điều khiển Bluetooth vẫn chạy như trước khi có và không có phiên điều khiển trực tiếp; âm thanh AirPlay không đổi |
| D33 | Nhật ký sạch | `coredevice.log` sau các hàng trên: không có UDID đầy đủ (dòng `[direct] #… device` chỉ có bốn ký tự cuối), phím, văn bản clipboard hay dòng output thô của Python. Kiểm bằng `Select-String` các mẫu đó |
| D34 | Dừng phiên Wi-Fi khi UAC đang hiện | Bản portable (hoặc bản cài ngoài Program Files): phản chiếu khi không cắm cáp để hộp UAC của tunneld hiện ra, rồi tắt công tắc (hoặc chọn *Tắt điều khiển trực tiếp* ở menu khay) **trước** khi trả lời UAC. Phiên dừng; trả lời Có sau đó không để lại tunneld: `Get-NetTCPConnection -LocalPort 49151 -State Listen` trống sau tối đa 40 giây, và không có UAC thứ hai |
| D35 | Con lăn và touchpad | Qua cáp và qua Wi-Fi, trên hình AirPlay của một trang chữ trong Safari và danh sách trong Cài đặt: một nấc cuộn đi một đoạn ngắn và dừng (không trôi theo đà); lăn xuống thì nội dung đi lên như trên Windows, không bấm nhầm mục dưới con trỏ; *Lines to scroll at a time* lớn hơn thì mỗi nấc đi xa hơn. Lăn liên tục thì cuộn tiếp; ghi lại có khựng giữa các nấc không. Lăn dài một mạch không mở Trung tâm điều khiển, Trung tâm thông báo hay về Home. Shift + lăn và cuộn ngang hai ngón trên touchpad cuộn ngang (ảnh trong ứng dụng Ảnh). Đang giữ chuột trái kéo thì lăn không ảnh hưởng thao tác kéo. Sau khi xoay ngang, lăn xuống vẫn đẩy nội dung lên theo hình đang thấy |

Trên hình AirPlay:

| # | Phép thử | Tiêu chí |
|---|---|---|
| D37 | Hướng màn hình | iPhone: trong một ứng dụng xoay được (Safari, Ảnh), xoay ngang về bên trái rồi về bên phải: chạm đúng điểm ở cả hai phía sau tối đa khoảng 2 giây; `coredevice.log` có dòng `[direct] orientation LandscapeHomeRight` hoặc `LandscapeHomeLeft` đúng phía. Lật thẳng từ ngang trái sang ngang phải: sau lần bấm đầu tiên, các lần bấm sau đúng điểm. iPad: thêm dọc ngược. Ghi lại phía nút Home (hoặc thanh Home) ứng với từng dòng log |
| D38 | Kích thước hình | `uxplay.log` có dòng `raop_rtp_mirror width_source = ...` lúc bắt đầu phản chiếu và sau mỗi lần xoay. Chạm vào nội dung sát bốn góc hình đúng chỗ ở cửa sổ hẹp, rộng và toàn màn hình |
| D39 | Thiết bị khác chiếm AirPlay | Đang điều khiển iPhone, iPad phản chiếu vào (iPhone bị đá ra): phiên iPhone dừng, phiên cho iPad chạy, chạm trên hình đi tới iPad chứ không tới iPhone |
| D40 | Cùng Bluetooth | Bật cả *Điều khiển thiết bị bằng chuột và bàn phím* và *Điều khiển trực tiếp*: khi điều khiển trực tiếp sẵn sàng, bấm vào hình là chạm, không bắt đầu phiên Bluetooth; một phiên Bluetooth đang chạy thì kết thúc khi điều khiển trực tiếp sẵn sàng |
| D41 | Con trỏ | Khi đang điều khiển, con trỏ chuột luôn hiện trên hình kể cả khi bật *Ẩn con trỏ chuột Windows*; tắt công tắc thì ẩn con trỏ hoạt động lại |
| D42 | Lần bấm đầu sau khi nghỉ | Sau hơn 20 giây không thao tác: đưa chuột vào hình rồi bấm ngay, và bấm khi chuột đã nằm sẵn trên hình. Ghi lại cú bấm đến thiết bị sau bao lâu và dòng `InputStreamOpened <giây>` trong `coredevice.log`; cú bấm không bị mất |
| D43 | Đang kết nối | Bật công tắc rồi phản chiếu: trên hình hiện "Đang kết nối điều khiển..." đến khi điều khiển sẵn sàng; bấm lúc đó không làm gì và không bắt đầu phiên Bluetooth. Ghi lại thời gian từ lúc phản chiếu đến lúc dòng chữ đổi thành gợi ý điều khiển, và số giây giữa các dòng `[direct]`, `session start`, `image mount` và `serve-web ready` trong `coredevice.log` |
| D44 | Màn hình khóa | Khóa thiết bị khi đang phản chiếu, mở lên màn hình nhập mật khẩu: hình AirPlay chỉ có các chấm (iOS ẩn bàn phím số). Bấm vào hình rồi gõ mật khẩu bằng phím số của laptop: thiết bị mở khóa. Nếu không được, ghi lại; thử bấm đúng chỗ các phím số |
| D45 | Chạm lâu hơn 20 giây | Đưa chuột vào hình, đợi 30 giây không bấm, rồi bấm: chạm đúng. Kéo, cuộn và bấm liên tục hơn một phút: lần nào cũng tới thiết bị. Bàn phím chạy được không chứng minh gì: iOS nhận phím cả khi luồng đã bị thiết bị cắt (thiết bị cắt luồng sau 20 giây không có RTCP receiver report) |

Kiểm thử gồm hai phần tách biệt:

1. **Kiểm tra tự động trên Mac** (người triển khai chạy): unit test phần logic và biên dịch app WPF. Phần này chỉ chứng minh logic và mã biên dịch được, không chứng minh app chạy đúng trên Windows.
2. **Kiểm thử trên máy thật sau CI** (người dùng chạy): cài đúng bản do GitHub Actions đóng gói lên laptop Windows, rồi chạy các bảng T, P và U bên dưới với Mac, iPhone và iPad thật.

Chỉ ghi "đạt" cho một hàng khi đã chạy hàng đó trên thiết bị thật. Hàng chưa chạy hoặc thiếu thiết bị thì ghi **chưa kiểm chứng**.

## Phần 1: Kiểm tra tự động trên Mac

Lần chạy gần nhất: 2026-10-28, macOS, .NET SDK 10.0.403.

| Lệnh | Kết quả | Ghi chú |
|---|---|---|
| `dotnet test tests/MacExtend.Core.Tests -c Release` | Đạt: 244/244 | Gồm test log iPhone/iPad, nguồn mới thay phiên không có disconnect, ngắt qua hai mẫu TCP bằng 0 rồi kết nối lại từ nguồn khác. Thêm: cài đặt Fullscreen (mặc định, file cũ, giá trị lỗi, hoa thường), tham số `-vs` cho hai chế độ và không còn `-fs`, nhận dòng log đóng cửa sổ video, ngắt ngay khi đóng cửa sổ, phép thu nhỏ cửa sổ cho vừa vùng làm việc. Thêm: cài đặt AlwaysOnTop (mặc định, file cũ, giá trị lỗi, hoa thường), bật/tắt ghim không đổi tham số uxplay, quy tắc ghim/bỏ ghim cửa sổ video |
| `dotnet build src/MacExtend.App -c Release` | Đạt: 0 cảnh báo, 0 lỗi | Chỉ kiểm tra biên dịch C# và XAML. Nhận biết toàn màn hình, thu nhỏ cửa sổ, đưa cửa sổ lên trước, ghim trên cùng, menu khay, cursor shield và binding chỉ kiểm chứng được khi chạy trên Windows (bảng W) |

## Phần 2: Kiểm thử trên máy thật sau CI

Chuẩn bị:
- Lấy bản từ lần chạy CI tương ứng với commit cần kiểm (artifact `MacExtend-<phiên bản>` hoặc GitHub Release), cài bằng `MacExtend-x.y.z-setup.exe`.
- Các thiết bị dùng chung Wi-Fi nhà. Mạng trên Windows đặt là Riêng tư.
- Khi một hàng lỗi, đính kèm `%LOCALAPPDATA%\MacExtend\logs\uxplay.log`, cài đặt đang dùng và các bước tái hiện.

Ghi thông tin phiên bản cho mỗi lần kiểm thử:

```
Ngày:
MacExtend (dòng phiên bản ở chân cửa sổ app, gồm cả uxplay/runtime):
Bản CI hoặc commit:
uxplay và GStreamer thực tế (file uxplay\VERSION.txt trong thư mục cài đặt):
Windows (winver: phiên bản và build), GPU:
Màn hình: số màn hình; với từng màn: độ phân giải, scale (100/125/150 %), màn chính hay phụ,
          vị trí so với màn chính (bên phải/trên: tọa độ dương; bên trái/dưới: tọa độ âm)
Mac: model, macOS               (không có thì ghi: chưa kiểm chứng)
iPhone: model, iOS              (không có thì ghi: chưa kiểm chứng)
iPad: model, iPadOS             (không có thì ghi: chưa kiểm chứng)
Cài đặt: Độ phân giải (Tự động / Tùy chỉnh W×H Hz), H.265, Khung hình, Âm thanh, PIN,
         Mở AirPlay ở chế độ toàn màn hình, Giữ cửa sổ AirPlay luôn ở trên cùng, Tự chuyển sang màn hình AirPlay, Ẩn con trỏ
Log đính kèm:
```

### Mac: màn hình mở rộng

| # | Kiểm tra | Cách làm | Mong đợi | Kết quả |
|---|---|---|---|---|
| T1 | Mac thấy thiết bị | Mở app, trên Mac mở Phản chiếu màn hình | Tên `MacExtend-<tên máy>` xuất hiện trong vòng 10 giây | |
| T2 | Ghép nối bằng PIN | Chọn thiết bị trên Mac | Mac hỏi PIN, nhập mã trong app thì kết nối được | |
| T3 | Màn hình mở rộng | Chọn Dùng làm màn hình riêng (cài đặt mặc định: toàn màn hình tắt) | Windows hiện màn nền Mac trong cửa sổ video có thanh tiêu đề, vừa vùng làm việc; kéo được cửa sổ Mac sang; app báo "Đang nhận AirPlay" kèm tên Mac | |
| T4 | Độ phân giải | Đặt Windows scale 125% hoặc 150%. Trên Mac vào Displays, chọn 1920 × 1080 cho màn AirPlay; chạy `system_profiler SPDisplaysDataType` | Resolution và UI Looks like đều là 1920 x 1080; chữ trên màn Windows nét như trên màn gốc | |
| T5 | Độ trễ | Kéo cửa sổ qua lại, gõ chữ trên màn Windows | Trễ khoảng 100 đến 250 ms, không giật kéo dài | |
| T6 | Âm thanh | Phát nhạc trên Mac, sau đó tắt Âm thanh trong app và khởi động lại bộ nhận | Lần đầu tiếng phát ra trên Windows; sau khi tắt thì không | |
| T7 | Lần kết nối sau | Ngắt rồi kết nối lại | Không hỏi PIN nữa | |
| T8 | Quên thiết bị | Bấm Quên tất cả rồi kết nối lại | Mac hỏi PIN lại | |
| T9 | Thoát khi đang kết nối | Khi đang là màn hình phụ, chọn Thoát MacExtend ở khay | Màn phụ trên Mac biến mất trong vài giây; tên thiết bị biến khỏi menu Phản chiếu; không còn `uxplay.exe` trong Task Manager | |
| T10 | Ngủ và thức | Cho Windows ngủ (Sleep), đánh thức | Sau khoảng 10 giây Mac lại thấy thiết bị và kết nối được | |
| T11 | Đổi Wi-Fi | Chuyển Windows sang Wi-Fi khác rồi quay lại | Bộ nhận tự khởi động lại; Mac thấy thiết bị | |
| T12 | Mạng Công cộng | Đặt Wi-Fi của Windows thành Công cộng, mở app | Mục Mạng báo Công cộng; bấm Chuyển sang Riêng tư, xác nhận UAC thì dấu tích xanh xuất hiện | |
| T13 | Tường lửa bị chặn | Trong Windows Defender Firewall, đặt quy tắc của uxplay.exe thành Block | Mục Tường lửa báo bị chặn; bấm Cho phép thì sửa được | |
| T14 | Màn hình luôn sáng | Đặt Windows tắt màn sau 1 phút, giữ kết nối 3 phút | Màn Windows không tắt khi đang kết nối; tắt bình thường sau khi ngắt | |
| T15 | Theme | Đổi Windows giữa Sáng và Tối khi app đang mở | Cửa sổ và menu khay đổi màu theo, không cần mở lại | |
| T16 | Khởi động cùng Windows | Bật tùy chọn, đăng xuất rồi đăng nhập | App chạy ẩn ở khay, Mac thấy thiết bị | |
| T17 | Tên có dấu | Đặt tên "Máy của Thịnh" | Mac thấy "May cua Thinh" | |
| T18 | Mở app lần hai | Khi app đang chạy, mở lại từ Start | Cửa sổ đang có hiện lên, không có bộ nhận thứ hai | |
| T19 | Bản portable | Giải nén zip vào thư mục có dấu, ví dụ `C:\Users\Thịnh\Tải về\MacExtend`, rồi chạy | Chạy bình thường; T1 đến T3 đạt | |
| T20 | MacExtend bị tắt đột ngột | Khi đang là màn hình phụ, dùng Task Manager kết thúc `MacExtend.exe` | `uxplay.exe` cũng biến mất ngay; Mac mất màn phụ trong khoảng 10 giây; mở lại MacExtend thì chỉ có một bộ nhận | |
| T21 | Tự nhảy sang màn Mac | Mở vài cửa sổ khác trên Windows, kết nối từ Mac và chọn Dùng làm màn hình riêng | Màn hình Mac tự hiện lên trước mọi cửa sổ, không cần bấm | |
| T22 | Ẩn con trỏ | Khi đang xem màn Mac trong cửa sổ, di chuột Windows qua hình, thanh tiêu đề, viền. Nhấn Alt+Enter sang toàn màn hình rồi di chuột | Ở cửa sổ: con trỏ Windows luôn hiện. Ở toàn màn hình: không thấy con trỏ Windows; phím Fn âm lượng/độ sáng và phím Windows vẫn chạy | |
| T23 | Quay về Windows | Ở toàn màn hình, Alt+Tab sang Explorer, rồi bấm biểu tượng MacExtend ở khay | Sau Alt+Tab con trỏ hiện lại và dùng Explorer được; bấm biểu tượng thì màn Mac hiện lại, con trỏ ẩn | |
| T24 | Tắt các tùy chọn | Tắt "Ẩn con trỏ chuột Windows" khi đang xem màn Mac toàn màn hình | Con trỏ hiện lại ngay | |

### iPhone và iPad: phản chiếu màn hình

Thao tác trên iPhone: mở Trung tâm điều khiển (iPhone không có nút Home: vuốt xuống từ góc phải trên; iPhone có nút Home: vuốt lên từ cạnh dưới), bấm **Phản chiếu màn hình**, chọn tên máy Windows. Muốn dừng: mở lại Phản chiếu màn hình, chọn **Dừng phản chiếu**. Chỉ dùng nội dung không có DRM (ví dụ ảnh và video tự quay trong ứng dụng Ảnh); ứng dụng phim trả phí có thể chặn hình.

| # | Kiểm tra | Cách làm | Mong đợi | Kết quả |
|---|---|---|---|---|
| P1 | iPhone thấy thiết bị | Mở app ở trạng thái Sẵn sàng kết nối, trên iPhone mở Phản chiếu màn hình | Tên `MacExtend-<tên máy>` xuất hiện trong vòng 10 giây | |
| P2 | PIN đúng, sai và lần sau | Bật Yêu cầu mã PIN, khởi động lại bộ nhận. Chọn thiết bị, nhập sai mã; chọn lại, nhập đúng mã. Dừng phản chiếu rồi kết nối lại | Mã sai: không kết nối được. Mã đúng: kết nối; app báo "Đang nhận AirPlay", tên iPhone hiện trong app và khay. Lần sau: ghi lại iPhone có hỏi PIN nữa hay không | |
| P3 | Dọc, ngang và xoay | Mở ứng dụng Ảnh ở hướng dọc, xoay ngang rồi xoay lại; lặp ba lần, một lần ở cửa sổ và một lần ở toàn màn hình. Theo dõi PID `uxplay.exe` trong Task Manager | Hình giữ đúng tỉ lệ ở cả hai hướng, có viền khi cần, không bị cắt hay kéo giãn; không mất kết nối; PID không đổi (bộ nhận không khởi động lại). Cửa sổ không bắt buộc tự đổi kích thước theo hướng xoay: ghi lại cửa sổ có đổi hay không | |
| P4 | Âm thanh | Phát video có tiếng trong ứng dụng Ảnh. Sau đó tắt Âm thanh trong app, khởi động lại bộ nhận, kết nối lại và phát lại | Lần đầu tiếng phát ra trên Windows; sau khi tắt thì không. Ghi lại độ lệch tiếng và hình quan sát được | |
| P5 | Dừng, kết nối lại, Quên tất cả | Chọn Dừng phản chiếu trên iPhone; kết nối lại. Với PIN bật, bấm Quên tất cả rồi kết nối lại | Sau khi dừng, cửa sổ video đóng và app về "Sẵn sàng kết nối" trong vài giây; kết nối lại được; sau Quên tất cả iPhone phải nhập PIN | |
| P6 | Focus, con trỏ, Alt+Tab, khay | Mở vài cửa sổ trên Windows, kết nối iPhone. Nhấn Alt+Enter sang toàn màn hình, di chuột; Alt+Tab sang Explorer; bấm biểu tượng MacExtend ở khay; mở menu khay | Màn hình AirPlay tự lên trước mọi cửa sổ khi có hình; con trỏ ẩn khi đang xem toàn màn hình; sau Alt+Tab con trỏ hiện và dùng Explorer được; bấm khay thì màn AirPlay hiện lại; menu có "Hiện màn hình AirPlay" | |
| P7 | Chuyển tuần tự Mac, iPhone, Mac | Kết nối Mac (Dùng làm màn hình riêng), ngắt trên Mac. Kết nối iPhone, dừng phản chiếu. Kết nối lại Mac | Mỗi lần đều kết nối được mà không cần khởi động lại bộ nhận; tên nguồn trong app và khay đúng thiết bị đang phát; Mac vẫn mở rộng desktop | |
| P8 | iPhone thay phiên Mac | Khi Mac đang Dùng làm màn hình riêng, từ iPhone chọn máy Windows mà không ngắt Mac. Sau đó dừng iPhone và kết nối lại Mac | Mac mất màn phụ; app và khay đổi sang tên iPhone; khi Tự chuyển sang màn hình AirPlay đang bật, cửa sổ video mới hiện lên trước mọi cửa sổ; con trỏ ẩn đúng trên cửa sổ video mới (cursor shield bám cửa sổ mới). Mac kết nối lại được và mở rộng desktop | |
| P9 | Tùy chỉnh 1080p với H.265 | Đặt Độ phân giải Tùy chỉnh 1920 × 1080, 60 Hz, bật Dùng H.265, khởi động lại bộ nhận. Kết nối iPhone, xoay vài lần | Ghi lại: có hình không, giật hay lỗi, dòng codec trong `uxplay.log`. Nếu lỗi, quay về Tự động và ghi chú | |
| P10 | Đổi hướng dẫn trong phiên | Khi iPhone đang kết nối, mở app, bấm qua lại giữa Mac và iPhone, iPad nhiều lần. Theo dõi PID `uxplay.exe` | Chỉ bước 1 và bước 4 đổi nội dung; PID không đổi; không hiện thông báo "Thay đổi có hiệu lực sau khi khởi động lại"; phiên không bị ngắt. Mở lại app thì hướng dẫn về Mac | |
| P11 | Theme, scale, tên dài | Đổi Windows giữa Sáng và Tối; đặt scale 125% rồi 150%; đổi tên iPhone (Cài đặt, Cài đặt chung, Giới thiệu, Tên) thành chuỗi khoảng 40 ký tự rồi kết nối | Hướng dẫn và cài đặt không bị cắt chữ; tên nguồn trong khung minh họa xuống tối đa hai dòng và kết thúc bằng dấu "…"; màu đúng theme | |
| P12 | iPad | Lặp nhanh P1 đến P4 với iPad | Kết quả như iPhone. Không có iPad thì ghi chưa kiểm chứng | |
| P13 | Bản đóng gói trên Windows sạch | Cài bản CI trên máy Windows không có MSYS2, chạy P1 đến P3 | Chạy bình thường; không báo thiếu DLL; `uxplay.log` không có lỗi thiếu plugin GStreamer | |

### Cửa sổ video: đổi kích thước, toàn màn hình, đóng

Chưa chạy hàng nào trong bảng này: cần bản CI trên Windows thật cùng Mac, iPhone, iPad. Chạy với cả ba nguồn khi có; thiếu thiết bị nào thì ghi chưa kiểm chứng cho thiết bị đó. Ghi PID `uxplay.exe` (Task Manager, tab Details) trước và sau mỗi hàng.

Giới hạn đã biết: uxplay chạy ở chế độ nhận DPI theo hệ thống (system DPI aware), không theo từng màn hình. Trên màn hình có scale khác màn hình chính, Windows (DWM) co giãn ảnh của cửa sổ video nên hình có thể mờ. Bản này không đổi cách uxplay nhận DPI; ở W13 ghi lại mức mờ như một quan sát, không tính là đạt hay lỗi của bản này. Riêng vị trí, độ phủ và con trỏ ở toàn màn hình trên màn khác scale phải ghi số đo thật.

| # | Kiểm tra | Cách làm | Mong đợi | Kết quả |
|---|---|---|---|---|
| W1 | Cửa sổ mặc định | Toàn màn hình tắt. Kết nối, rồi kéo thanh tiêu đề, kéo từng cạnh và góc, thu nhỏ, phóng to, khôi phục | Có thanh tiêu đề; mọi thao tác chạy; hình giữ tỉ lệ, có viền đen khi khung khác tỉ lệ, không bị cắt hay kéo giãn; tiếng không ngắt; PID không đổi | |
| W2 | Vừa màn hình lần đầu | Độ phân giải Tự động, laptop 1920 × 1080 ở 100 %. Kết nối; ghi vị trí và kích thước cửa sổ khi vừa mở và sau 2 giây. Đổi kích thước bằng tay, chờ 5 giây | Cửa sổ mở to hơn màn hình được thu nhỏ một lần cho vừa vùng làm việc, nằm giữa, không đè thanh taskbar. Cửa sổ nhỏ hơn màn hình không bị phóng to. Sau khi tự đổi kích thước, cửa sổ giữ nguyên kích thước và vị trí đã chọn | |
| W3 | Alt+Enter lặp | Trong phiên ở cửa sổ, nhấn Alt+Enter vào/ra toàn màn hình ít nhất năm lần. Mở `%APPDATA%\MacExtend\settings.ini` trước và sau | Mỗi lần đều chuyển đúng; PID, kết nối và tiếng giữ nguyên; dòng `Fullscreen=` trong `settings.ini` không đổi | |
| W4 | Mở sẵn toàn màn hình | Bật "Mở AirPlay ở chế độ toàn màn hình", Khởi động lại bộ nhận, kết nối. Nhấn Alt+Enter, chờ 2 giây, rồi lặp vào/ra năm lần | Phiên mở toàn màn hình; Alt+Enter đầu tiên ra được cửa sổ có thanh tiêu đề, cửa sổ được thu nhỏ vừa vùng làm việc nếu to hơn; các lần sau chuyển đúng; PID, kết nối, tiếng giữ nguyên | |
| W5 | Đổi lựa chọn trong phiên | Khi đang kết nối, bật rồi tắt "Mở AirPlay ở chế độ toàn màn hình". Sau đó để bật, bấm Khởi động lại | Chỉ hiện thông báo khởi động lại, phiên không bị ngắt. Sau khi khởi động lại, phiên kế tiếp mở toàn màn hình và thông báo biến mất | |
| W6 | Con trỏ theo chế độ | Ẩn con trỏ bật. Ở cửa sổ thường và cửa sổ phóng to: di chuột qua hình, thanh tiêu đề, viền, taskbar, ứng dụng khác. Sang toàn màn hình. Rồi lần lượt: Alt+Tab, thu nhỏ, Alt+Enter ra cửa sổ, ngắt từ thiết bị, Dừng trong app, đóng cửa sổ | Ở cửa sổ thường và phóng to con trỏ luôn hiện và bấm được mọi chỗ. Chỉ ẩn khi toàn màn hình và cửa sổ video ở trước. Mỗi thao tác ở bước cuối làm con trỏ hiện lại trong khoảng 0,3 giây | |
| W7 | Phóng to không phải toàn màn hình | Phóng to cửa sổ video bằng nút trên thanh tiêu đề | Vẫn còn thanh tiêu đề; con trỏ vẫn hiện; taskbar vẫn dùng được | |
| W8 | Đóng bằng X và Alt+F4 | Ghi PID. Đóng cửa sổ video bằng X; kết nối lại; đóng bằng Alt+F4 | Thiết bị bị ngắt; app về "Sẵn sàng kết nối" trong tối đa 6 giây; con trỏ hiện. `uxplay.log` có dòng `GStreamer error (video): ... Output window was closed` và `[MacExtend] video window closed by user`, không có `uxplay exited` hay `restarting uxplay`. PID không đổi. Nếu `uxplay.exe` thoát: ghi **không đạt**, kèm phiên bản uxplay/GStreamer, đoạn log và PID trước/sau, báo lại để xử lý ở vòng sau | |
| W9 | Đóng và kết nối lại tám lần | Lặp tám phiên, mỗi phiên dưới 30 giây: kết nối, đóng cửa sổ, kết nối lại. Làm với từng thiết bị có | Không lần nào báo lỗi "Không chạy được" hay khởi động lại bộ nhận; PID không đổi; mỗi cửa sổ mới được đưa lên trước (khi Tự chuyển bật) và vừa màn hình. Ghi từng thiết bị: tự kết nối lại hay phải chọn lại máy Windows | |
| W10 | Ngắt khi không có log đóng | Khi đang xem, chọn Dừng phản chiếu trên iPhone (hoặc ngắt trên Mac), không đụng vào cửa sổ trên Windows | App về "Sẵn sàng kết nối" trong tối đa 6 giây nhờ kiểm tra kết nối TCP, dù `uxplay.log` không có dòng `[MacExtend] video window closed by user`; cửa sổ video đóng và con trỏ hiện lại. Ở W8, nếu dòng đó xuất hiện sau khi app đã về Sẵn sàng thì đường TCP đã ngắt trước: vẫn đạt, ghi lại | |
| W11 | Phím đầu tiên | Mở ứng dụng khác ở trước, kết nối (Tự chuyển bật); ngay khi hình hiện, nhấn Alt+Enter. Sau đó Alt+Tab đi, bấm biểu tượng khay, nhấn Alt+Enter lần nữa | Cả hai lần Alt+Enter đều chuyển chế độ ngay. Không có menu hệ thống bật lên, phím đầu tiên không bị nuốt (dấu hiệu menu mode: nhấn mũi tên xuống thì mở menu Restore/Move/Size). Nếu gặp, ghi lại bước | |
| W12 | Màn hình phụ cùng scale | Hai màn cùng scale. Đưa cửa sổ sang màn phụ (Win+Shift+mũi tên), nhấn Alt+Enter. Lặp với màn phụ đặt bên phải (tọa độ dương) và bên trái hoặc phía trên (tọa độ âm) | Toàn màn hình phủ kín đúng màn phụ; con trỏ ẩn trên màn đó và hiện lại khi sang màn chính; Alt+Enter ra lại cửa sổ trên màn phụ | |
| W13 | Màn hình khác scale | Màn chính 100 %, màn phụ 150 % (và ngược lại). Ở cửa sổ và toàn màn hình trên từng màn | Ghi thực tế, không mặc định là đạt: hình nét hay mờ; ở toàn màn hình, tọa độ cửa sổ video so với tọa độ màn hình (dùng công cụ như Spy++ hoặc ảnh chụp), có phủ kín không, viền đen, vùng con trỏ bị ẩn có trùng màn hình không | |
| W14 | Ghim trên cùng | Ở cửa sổ thường, bật "Giữ cửa sổ AirPlay luôn ở trên cùng". Bấm vào Explorer hoặc trình duyệt đang nằm chồng lên cửa sổ video, kéo nó đè lên video, gõ chữ trong đó. Lặp với cửa sổ video phóng to | Cửa sổ video luôn nằm trên ứng dụng kia; ứng dụng kia vẫn nhận chuột và bàn phím; con trỏ hiện; hình và tiếng không ngắt; PID không đổi | |
| W15 | Bật/tắt trong phiên | Khi đang xem, tắt rồi bật công tắc trong app. Sau đó mở menu khay, chọn "Tắt luôn ở trên cùng", mở lại menu, chọn "Bật luôn ở trên cùng". Xem `%APPDATA%\MacExtend\settings.ini` | Mỗi lần có hiệu lực ngay (khoảng 0,3 giây); không hiện thông báo khởi động lại; phiên không ngắt. Chữ trong menu khay luôn khớp công tắc trong app; dòng `AlwaysOnTop=` đổi theo. Menu khay chỉ có mục này khi đang kết nối | |
| W16 | Ghim và toàn màn hình | Ghim bật. Alt+Enter sang toàn màn hình, Alt+Tab sang Explorer. Alt+Tab về, Alt+Enter ra cửa sổ, rồi bấm sang ứng dụng khác | Ở toàn màn hình, Alt+Tab sang Explorer được và Explorer hiện lên trên video. Ra cửa sổ thường thì ghim tự bật lại trong khoảng 0,3 giây: video nằm trên ứng dụng khác | |
| W17 | Ghim qua thu nhỏ và phiên mới | Ghim bật. Thu nhỏ rồi khôi phục cửa sổ video; Alt+Tab đi rồi bấm biểu tượng khay; đóng cửa sổ video rồi kết nối lại; cho thiết bị khác thay phiên (như P8) | Sau mỗi bước cửa sổ video đang mở vẫn ghim. Khi phiên kết thúc không còn cửa sổ nào của uxplay bị ghim sót lại | |
| W18 | Mặc định không ghim | Cài mới, hoặc dùng `settings.ini` cũ không có dòng `AlwaysOnTop` | Công tắc tắt; bấm sang ứng dụng khác thì ứng dụng đó che cửa sổ video như trước | |

## Cập nhật

| # | Kiểm tra | Cách làm | Mong đợi | Kết quả |
|---|---|---|---|---|
| U1 | Không có mạng | Ngắt Wi-Fi, bấm Kiểm tra cập nhật | Chân trang báo "Không kiểm tra được cập nhật: không kết nối được GitHub."; app vẫn chạy bình thường | |
| U2 | Có bản mới | Cài bản N, push một commit để CI tạo bản N+1, bấm Kiểm tra cập nhật | Thanh "Có bản mới N+1" với dung lượng khoảng 1 MB; khay hệ thống báo một lần | |
| U3 | Cập nhật Program Files | Bấm Cập nhật, chọn Yes ở UAC | App đóng, mở lại và báo "Đã cập nhật lên bản N+1"; Installed apps của Windows hiện N+1 | |
| U4 | Hủy UAC | Bấm Cập nhật, chọn No ở UAC | App mở lại bản N, báo "Cập nhật đã bị hủy" | |
| U5 | Runtime đổi | Làm một bản CI có gói MSYS2 khác (hoặc sửa gst-elements.txt), cập nhật | Dung lượng tải gồm cả runtime (vài chục MB); sau cập nhật Mac vẫn kết nối được | |
| U6 | Bản portable | Giải nén zip vào thư mục người dùng, cập nhật | Không có hộp thoại UAC; app mở lại ở bản mới | |

## Khi có lỗi

Lỗi tìm thấy ở Phần 2 (không thấy thiết bị, xoay, codec, thay phiên, cursor shield, cửa sổ video, DPI, ...) được ghi thành tác vụ sửa riêng, kèm `uxplay.log`, cài đặt và bước tái hiện. Sau khi có kết quả thật, ghi các phiên bản iOS/iPadOS đã kiểm thử vào mục Yêu cầu trong `README.md`.
