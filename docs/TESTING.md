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
