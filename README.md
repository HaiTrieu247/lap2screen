# MacExtend

Biến laptop Windows thành **màn hình mở rộng cho Mac** và **bộ nhận phản chiếu màn hình iPhone/iPad** qua AirPlay, không cần cáp và không cài gì thêm trên thiết bị Apple.

Trên Mac chỉ cần mở **Phản chiếu màn hình** (Screen Mirroring), chọn máy Windows, rồi chọn **Dùng làm màn hình riêng** (Use As Separate Display). Từ đó laptop Windows hoạt động như một màn hình mở rộng: kéo cửa sổ sang, chuột đi qua lại giữa hai màn.

Trên iPhone hoặc iPad, mở **Phản chiếu màn hình** trong Trung tâm điều khiển và chọn máy Windows. Màn hình và âm thanh của thiết bị hiện trên laptop.

MacExtend dùng [UxPlay](https://github.com/FDH2/UxPlay) làm lõi nhận AirPlay. Phần MacExtend thêm vào là:
- Tự báo đúng độ phân giải và tần số quét của màn laptop cho nguồn phát.
- Cấu hình độ trễ thấp, hiển thị bằng Direct3D 11 trong cửa sổ đổi được kích thước hoặc toàn màn hình.
- Tự xử lý tường lửa và mạng Công cộng.
- Tự khởi động lại khi Windows ngủ dậy, khi đổi Wi-Fi hoặc khi uxplay gặp lỗi.

## Yêu cầu

- Windows 10 hoặc 11, 64-bit (x64).
- Nguồn phát là Mac: macOS 12 Monterey trở lên.
- Nguồn phát là iPhone hoặc iPad: có mục Phản chiếu màn hình trong Trung tâm điều khiển. Phiên bản iOS/iPadOS đã kiểm thử: chưa có kết quả thiết bị thật.
- Máy Windows và thiết bị phát cùng một mạng Wi-Fi hoặc LAN. Mạng khách có chặn thiết bị nói chuyện với nhau sẽ không dùng được.

## Cài đặt

Tải bản mới nhất ở trang [Releases](https://github.com/HaiTrieu247/MacExtend/releases/latest):

- `MacExtend-x.y.z-setup.exe`: bộ cài. Bộ cài tự thêm quy tắc tường lửa và tạo lối tắt trong Start.
- `MacExtend-x.y.z-portable.zip`: bản chạy ngay. Giải nén rồi mở `MacExtend.exe`. Lần đầu bấm **Cho phép** ở mục Tường lửa trong app.

File chưa được ký số, nên Windows SmartScreen có thể cảnh báo. Bấm **More info** rồi **Run anyway**.

## Dùng lần đầu

Chuẩn bị trên Windows:
1. Mở MacExtend. Bộ nhận tự chạy, biểu tượng nằm ở khay hệ thống.
2. Ở mục **Hệ thống** trong app, đảm bảo cả hai dòng *Tường lửa Windows* và *Mạng* đều có dấu tích xanh. Nếu không, bấm nút bên cạnh để sửa.

Mục **Kết nối thiết bị** trong app có hai lựa chọn **Mac** và **iPhone, iPad** để xem đúng các bước cho thiết bị của bạn.

Từ Mac:
1. Mở Trung tâm điều khiển (Control Center, ở góc phải thanh menu), bấm **Phản chiếu màn hình** và chọn tên máy Windows (ví dụ `MacExtend-LAPTOP-7Q2K`).
2. Nếu Mac hỏi, nhập mã PIN hiện trong app.
3. Bấm lại biểu tượng Phản chiếu màn hình, chọn **Dùng làm màn hình riêng**.
4. Muốn sắp xếp vị trí hai màn hình, vào System Settings, mục Displays, rồi chọn Arrange trên Mac.

Từ iPhone hoặc iPad:
1. Mở Trung tâm điều khiển. iPhone không có nút Home và iPad: vuốt xuống từ góc phải trên. iPhone có nút Home: vuốt lên từ cạnh dưới.
2. Bấm **Phản chiếu màn hình** (Screen Mirroring) và chọn tên máy Windows.
3. Nếu thiết bị hỏi, nhập mã PIN hiện trong app.
4. Muốn dừng: mở lại Phản chiếu màn hình, chọn **Dừng phản chiếu**.

Đóng cửa sổ MacExtend không tắt bộ nhận; app vẫn chạy ở khay hệ thống. Muốn thoát hẳn, chuột phải vào biểu tượng ở khay và chọn **Thoát MacExtend**.

## Cập nhật

MacExtend tự kiểm tra bản mới trên GitHub: 1 phút sau khi mở, sau đó mỗi 6 giờ. Muốn kiểm tra ngay, bấm **Kiểm tra cập nhật** ở cuối cửa sổ. Khi có bản mới, thanh thông báo hiện ra cùng dung lượng cần tải. Bấm **Cập nhật** thì MacExtend tải bản vá, đóng lại, chép file mới và tự mở lại. Bản cài trong Program Files sẽ có một hộp thoại UAC hỏi quyền quản trị. Không cần gỡ bản cũ.

- Bản vá gồm phần app, khoảng 1 MB. Phần uxplay và GStreamer (vài chục MB) chỉ được tải khi chính phần đó thay đổi.
- Tải hỏng hoặc sai mã SHA-256 thì bản đang dùng không bị đụng tới. Nếu chép file lỗi giữa chừng, các file cũ được khôi phục.
- Nhật ký cập nhật nằm ở `%LOCALAPPDATA%\MacExtend\logs\update.log`.

## Khi thiết bị kết nối

- Mặc định, hình ảnh mở trong một cửa sổ có thanh tiêu đề. Kéo thanh tiêu đề để di chuyển, kéo cạnh hoặc góc để đổi kích thước, thu nhỏ và phóng to như cửa sổ bình thường. Hình luôn giữ đúng tỉ lệ; khi khung cửa sổ khác tỉ lệ hình (ví dụ iPhone dựng dọc), hai bên có viền đen.
- Nếu cửa sổ mở ra lớn hơn màn hình, MacExtend thu nhỏ nó cho vừa vùng làm việc một lần. Sau đó kích thước và vị trí bạn chọn được giữ nguyên trong suốt phiên.
- Nhấn **Alt+Enter** khi đang ở cửa sổ video để chuyển giữa cửa sổ và toàn màn hình. Muốn mỗi phiên mở sẵn toàn màn hình, bật **Mở AirPlay ở chế độ toàn màn hình** ở mục Hình ảnh.
- Muốn cửa sổ video luôn nằm trước các ứng dụng khác, ví dụ vừa xem iPhone vừa gõ trong ứng dụng khác, bật **Giữ cửa sổ AirPlay luôn ở trên cùng** ở mục Hình ảnh, hoặc chọn **Bật luôn ở trên cùng** trong menu khay khi đang kết nối. Mặc định tắt, có hiệu lực ngay. Ở toàn màn hình cửa sổ không được ghim, để Alt+Tab vẫn sang được ứng dụng khác; khi về lại cửa sổ thường, ghim tự bật lại.
- Khi có hình ảnh, màn hình AirPlay tự hiện lên trước mọi cửa sổ. Con trỏ chuột Windows chỉ được ẩn khi video đang toàn màn hình và ở trước; trong cửa sổ thường con trỏ luôn hiện để kéo và đổi kích thước. Cả hai đều tắt được ở mục Hình ảnh.
- Đóng cửa sổ video (nút X hoặc Alt+F4) là kết thúc phiên phản chiếu: thiết bị bị ngắt, app về *Sẵn sàng kết nối*, bộ nhận vẫn chạy. Muốn xem tiếp, kết nối lại từ thiết bị; có thể phải chọn lại máy Windows trong Phản chiếu màn hình.
- Phím tắt Windows (phím Windows, Alt+Tab, Win+D) và phím Fn (âm lượng, độ sáng) vẫn hoạt động như bình thường.
- Muốn dùng Windows: Alt+Tab sang ứng dụng khác, con trỏ hiện lại ngay. Muốn quay lại màn hình AirPlay: Alt+Tab về, hoặc bấm biểu tượng MacExtend ở khay (hay chọn **Hiện màn hình AirPlay** trong menu của nó).

## Điều khiển iPhone/iPad bằng chuột và bàn phím (beta)

Khi iPhone hoặc iPad đang phản chiếu, laptop có thể đóng vai bàn phím và chuột Bluetooth cho thiết bị đó. Hình và tiếng vẫn đi qua AirPlay; chỉ thao tác đi qua Bluetooth.

Cần: Bluetooth của laptop đóng vai bàn phím được (Bluetooth LE ở chế độ peripheral; nhiều adapter có, một số không) và Bluetooth của Windows đang bật.

1. Bật **Điều khiển thiết bị bằng chuột và bàn phím** ở mục Điều khiển. Dòng Bluetooth bên dưới báo máy có dùng được không.
2. Phản chiếu từ iPhone hoặc iPad như bình thường. Laptop chỉ hiện ra như bàn phím Bluetooth trong lúc đang phản chiếu, và thêm khoảng 10 giây sau khi ngừng.
3. Lần đầu: trong lúc đang phản chiếu, trên thiết bị vào **Cài đặt > Bluetooth**, chọn tên laptop. iPhone cần bật **Cài đặt > Trợ năng > Cảm ứng > AssistiveTouch** để hiện con trỏ chuột; iPad không cần.
4. Bấm vào hình trong cửa sổ video để bắt đầu điều khiển. Con trỏ Windows ẩn đi; di chuột, bấm, cuộn và gõ phím đều đi sang thiết bị. Nhìn con trỏ của thiết bị trên hình để di. Phím Windows đóng vai phím Command.
5. Nhấn **Ctrl+Alt+Q** để trả chuột và bàn phím cho Windows. Alt+Tab, chuyển sang cửa sổ khác, khóa máy, đóng video hoặc mất kết nối cũng trả lại ngay.

Menu khay có mục **Điều khiển** / **Dừng điều khiển** khi đang phản chiếu. Nếu có hơn một thiết bị nối Bluetooth và không thiết bị nào trùng tên với thiết bị đang phản chiếu, chọn thiết bị ở mục Điều khiển.

Con trỏ chạy theo kiểu chuột thật: bấm vào một điểm trên hình không làm con trỏ của thiết bị nhảy tới đó. Tốc độ con trỏ chỉnh ở Cài đặt > Trợ năng > Điều khiển con trỏ trên thiết bị. Phần Bluetooth dựa trên [windows-ble-hid](https://github.com/abhishek-raj/windows-ble-hid) (MIT). Nhật ký nằm ở `%LOCALAPPDATA%\MacExtend\logs\bluetooth.log` và chỉ ghi thay đổi trạng thái, không ghi phím đã gõ.

## Điều khiển trực tiếp (qua cáp hoặc Wi-Fi)

Một cách điều khiển iPhone, iPad khác Bluetooth, trong cửa sổ riêng, không cần AirPlay: bấm đúng điểm, giữ, kéo, cuộn bằng con lăn chuột hoặc touchpad theo cài đặt cuộn của Windows (Shift + lăn để cuộn ngang) và gõ phím, kể cả tiếng Việt. Dùng dịch vụ CoreDevice dành cho lập trình viên của Apple (cộng đồng dịch ngược), qua [pymobiledevice3](https://github.com/doronz88/pymobiledevice3). Tính năng còn mới: chưa được thử trên máy thật, xem bảng D trong [docs/TESTING.md](docs/TESTING.md).

Cần:
- Developer Mode trên thiết bị, bật một lần. Tùy chọn này nằm ở **Cài đặt > Quyền riêng tư & Bảo mật > Chế độ nhà phát triển** và chỉ hiện sau khi cửa sổ thiết lập chạy bước *Hiện tùy chọn*. Bật nó thì thiết bị khởi động lại và hỏi xác nhận; cửa sổ thiết lập hướng dẫn từng bước này.
- iTunes (tải từ apple.com) hoặc Apple Devices (Microsoft Store) trên Windows. iTunes từ apple.com là lựa chọn đã dùng để thử; Apple Devices chưa được xác nhận chạy được, nhất là khi điều khiển qua Wi-Fi (hàng D30 trong [docs/TESTING.md](docs/TESTING.md)).
- Một gói runtime (Python và pymobiledevice3) tải về lần đầu thiết lập. Bản beta không có release thì chọn tệp `MacExtend-<bản>-coredevice-runtime.zip` từ artifact CI bằng nút *Chọn tệp runtime...*.
- Qua Wi-Fi: thiết bị và laptop cùng mạng loại Riêng tư, và tài khoản Windows có quyền quản trị. Tài khoản thường chỉ dùng được qua cáp.

Cách dùng:

1. Mục *Điều khiển* > *Điều khiển trực tiếp* > *Thiết lập...*, làm các bước theo thứ tự. Với bản cài vào Program Files, Windows hiện hộp thoại hỏi quyền quản trị một lần (bấm Có): lần đó cài thành phần, mở tường lửa và tạo một tác vụ để sau này bật kết nối Wi-Fi mà không hỏi lại. Bản portable và bản cài vào thư mục khác cài thành phần không cần quyền quản trị, nhưng hỏi quyền một lần để mở tường lửa và một lần mỗi phiên Wi-Fi.
2. Muốn dùng Wi-Fi, cắm cáp rồi bật *Cho phép điều khiển qua Wi-Fi* cho thiết bị đó (ghép nối một lần qua cáp).
3. Bấm *Mở điều khiển*, hoặc menu khay > *Điều khiển trực tiếp...*. Đang cắm cáp thì app dùng cáp; không thì dùng Wi-Fi nếu đã bật cho thiết bị đó.

Nếu danh sách có từ hai thiết bị trở lên, app hiện bảng chọn trước khi kết nối, kể cả khi đã lưu máy dùng lần cuối. Mỗi thiết bị hiện sáu ký tự cuối UDID và cách kết nối; cùng một máy có cả cáp và Wi-Fi chỉ hiện một lần và dùng cáp. Danh sách Wi-Fi dựa trên các máy đã thiết lập, chưa xác nhận máy đang có mặt.

Giới hạn:
- Trong lúc điều khiển, camera và micro của thiết bị không dùng được; phiên cũng không mở được khi một ứng dụng đang giữ camera hoặc micro.
- Màn hình thiết bị phải bật thì mới có hình. Tiếng vẫn ra loa của thiết bị, không đi về laptop.
- Cửa sổ cần Microsoft Edge WebView2 Runtime (đã có sẵn trên hầu hết máy Windows 11).

Nhật ký ở `%LOCALAPPDATA%\MacExtend\logs\coredevice.log`, chỉ ghi trạng thái, không ghi phím hay văn bản đã gõ. Công cụ probe cho người phát triển nằm ở [tools/coredevice/README.md](tools/coredevice/README.md), kết quả thử ở [docs/research/2026-10-29-coredevice-results.md](docs/research/2026-10-29-coredevice-results.md).

## Cài đặt trong app

| Mục | Ý nghĩa |
|---|---|
| Tên hiện trên thiết bị Apple | Tên trong menu Phản chiếu màn hình của Mac, iPhone và iPad. Dấu tiếng Việt được bỏ, vì uxplay trên Windows chỉ nhận ký tự ASCII. |
| Độ phân giải | Kích thước MacExtend yêu cầu; nguồn phát có thể chọn kích thước khác. *Tự động*: báo đúng kích thước màn laptop, tối đa 1920 × 1080 ở 60 Hz. *Tùy chỉnh*: tự nhập kích thước và tần số; bật H.265 nếu cần lớn hơn 1080p. |
| Khung hình mỗi giây | 60 mượt hơn, 30 nhẹ hơn cho Wi-Fi yếu. |
| Mở AirPlay ở chế độ toàn màn hình | *Tắt* (mặc định): mở trong cửa sổ kéo và đổi kích thước được. *Bật*: mở toàn màn hình. Lựa chọn này chỉ quyết định cách mở của các phiên sau khi khởi động lại bộ nhận; trong lúc xem, Alt+Enter luôn chuyển được và không làm đổi cài đặt. |
| Giữ cửa sổ AirPlay luôn ở trên cùng | *Tắt* (mặc định). *Bật*: cửa sổ video luôn nằm trước các ứng dụng khác, kể cả khi bạn đang dùng ứng dụng đó. Có hiệu lực ngay, không cần khởi động lại bộ nhận; không áp dụng khi toàn màn hình. Khi đang kết nối, bật/tắt được cả từ menu khay. |
| Âm thanh | Phát tiếng của nguồn phát (Mac, iPhone, iPad) trên loa Windows hoặc tắt. |
| Mã PIN | Chặn người khác trong cùng mạng chiếu lên máy bạn. *Quên tất cả* xóa danh sách thiết bị đã ghép nối; nếu bật PIN, thiết bị phải nhập lại mã khi ghép nối lại. |
| Điều khiển thiết bị bằng chuột và bàn phím | *Tắt* (mặc định). *Bật*: laptop đóng vai bàn phím và chuột Bluetooth khi iPhone/iPad đang phản chiếu. Có hiệu lực ngay. Xem mục trên. |
| Khởi động cùng Windows | Chạy ẩn ở khay hệ thống sau khi đăng nhập. |

Cấu hình hình ảnh và âm thanh dùng chung cho mọi nguồn phát. Với iPhone và iPad, nên để Độ phân giải ở *Tự động* khi bắt đầu; *Tùy chỉnh* và H.265 trên điện thoại chưa được kiểm chứng.

Các thay đổi về hình ảnh, âm thanh và PIN áp dụng sau khi khởi động lại bộ nhận. App hiện thông báo kèm nút **Khởi động lại** khi cần. Riêng **Giữ cửa sổ AirPlay luôn ở trên cùng**, **Tự chuyển sang màn hình AirPlay** và **Ẩn con trỏ chuột Windows** có hiệu lực ngay.

## Giới hạn

- Mỗi lúc chỉ có một phiên AirPlay. Một kết nối mới được chấp nhận sẽ thay phiên đang chạy, kể cả khi Mac đang dùng máy này làm màn hình mở rộng.
- Bật Yêu cầu mã PIN để hạn chế nguồn lạ. Thiết bị đã ghép nối vẫn có thể thay phiên mà không cần nhập lại mã. Khi PIN tắt, mọi thiết bị cùng mạng đều kết nối được.
- Chỉ hỗ trợ Phản chiếu màn hình. Nút AirPlay bên trong ứng dụng (gửi video thẳng tới bộ nhận) và nội dung có DRM, ví dụ phim trong các ứng dụng trả phí, có thể không hiện hình.
- Độ khớp giữa tiếng và hình khi phản chiếu từ iPhone/iPad chưa được kiểm chứng trên thiết bị thật.
- Điều khiển bằng chuột và bàn phím qua Bluetooth đang ở dạng beta: công cụ tham khảo đã chạy được với iPhone trên laptop thử, còn bản tích hợp trong MacExtend và iPad chưa được kiểm thử trên thiết bị thật. Chưa có bấm đúng điểm trên hình, cử chỉ nhiều ngón, màn hình cảm ứng Windows hay phím media.
- uxplay chạy ở chế độ nhận DPI theo hệ thống (system DPI aware), không theo từng màn hình. Khi cửa sổ video nằm trên màn hình có mức scale khác màn hình chính, Windows co giãn ảnh của cửa sổ nên hình có thể mờ. Bản này không đổi cách uxplay nhận DPI. Toàn màn hình trên màn hình có scale khác màn hình chính chưa có kết quả kiểm thử thật.

## Khắc phục sự cố

**Thiết bị Apple không thấy máy Windows**
- Mục Mạng báo *Công cộng*: bấm **Chuyển sang Riêng tư**. Windows chặn tìm kiếm thiết bị trên mạng Công cộng.
- Mục Tường lửa báo chưa được phép hoặc bị chặn: bấm **Cho phép**.
- Phần mềm diệt virus có tường lửa riêng (Kaspersky, ESET, ...): cho phép `uxplay.exe` trong thư mục cài đặt, đường dẫn `uxplay\bin\uxplay.exe`.
- Wi-Fi công cộng hoặc mạng khách thường cô lập các thiết bị với nhau. Dùng mạng nhà hoặc bật hotspot từ điện thoại.

**Hình bị trễ hoặc giật**
- Đặt Khung hình mỗi giây về 30.
- Để các thiết bị gần router, dùng Wi-Fi 5 GHz, hoặc cắm dây LAN cho laptop Windows.

**Hình bị mờ hoặc chữ nhỏ li ti khi dùng Mac**
- Trên Mac, vào **System Settings, Displays** và chọn màn hình MacExtend. Ở mục Resolution, chọn đúng **1920 × 1080**. Nếu chỉ thấy các ô "Larger Text ... More Space", bấm **Advanced...** hoặc chuột phải vào khu vực đó, bật **Show all resolutions** rồi chọn 1920 × 1080. Chọn kích thước lớn hơn, ví dụ 2560 × 1440 hoặc 3840 × 2160, thì Mac phải thu nhỏ cả desktop vào luồng 1080p, nên chữ bị nhòe.
- Kiểm tra trên Mac: chạy `system_profiler SPDisplaysDataType` và xem khối của màn AirPlay. Hai dòng **Resolution** và **UI Looks like** đều phải là 1920 x 1080.
- MacExtend tự bật chế độ "Override high DPI scaling: Application" cho `uxplay.exe`, để Windows ở mức scale 125% hoặc 150% không kéo giãn hình. Nếu hình vẫn mờ, chuột phải `uxplay\bin\uxplay.exe`, mở **Properties, Compatibility, Change high DPI settings** và kiểm tra mục này đã được chọn.
- Dùng nhiều màn hình: chế độ trên chỉ khớp với mức scale của màn hình chính. Nếu cửa sổ video nằm trên màn hình có scale khác, hình có thể mờ. Kéo cửa sổ về màn hình chính, hoặc đặt cùng mức scale cho các màn hình.

**Đóng cửa sổ video thì thiết bị bị ngắt**
- Đây là cách hoạt động bình thường: đóng cửa sổ là kết thúc phiên. Bộ nhận vẫn chạy; kết nối lại từ thiết bị để xem tiếp. Muốn tạm ẩn hình mà không ngắt, bấm nút thu nhỏ hoặc Alt+Tab sang ứng dụng khác.

**Hình bị đơ ngay khi kết nối**
- Ngắt Phản chiếu màn hình trên thiết bị (trên iPhone/iPad: chọn **Dừng phản chiếu**) rồi kết nối lại, hoặc bấm **Khởi động lại** trong app.

**Xem nhật ký**
- Trong app, bấm **Mở thư mục nhật ký**. File `uxplay.log` nằm ở `%LOCALAPPDATA%\MacExtend\logs` và ghi lại phiên của mọi nguồn phát. Khi báo lỗi, gửi kèm file này, tên và phiên bản hệ điều hành của thiết bị phát.

## Build từ mã nguồn

### Bằng GitHub Actions (khuyên dùng)

Repo có sẵn workflow `.github/workflows/build.yml` chạy trên máy Windows của GitHub:

1. Push repo lên GitHub.
2. Workflow tự chạy với mỗi lần push vào `main`, hoặc chạy tay ở tab **Actions**, chọn **build**, rồi **Run workflow**.
3. Khi xong, tải artifact `MacExtend-<phiên bản>`. Artifact chứa file `.zip` portable và bộ cài `.exe`.
4. Mỗi lần push vào `main`, workflow tự tạo một GitHub Release `v<phiên bản>` ở repo công khai [HaiTrieu247/MacExtend](https://github.com/HaiTrieu247/MacExtend) (biến `RELEASE_REPOSITORY` trong workflow). Việc đăng release cần secret `RELEASES_TOKEN`: một fine-grained token chỉ cấp cho repo đó, quyền Contents đọc và ghi. Release gồm bộ cài, bản portable và các gói cập nhật (`update.ini`, `MacExtend-x.y.z-app.zip`, `MacExtend-x.y.z-runtime.zip`). App trên máy sẽ tìm thấy bản mới qua các gói này.
5. Phiên bản là `<version.txt>.<số thứ tự lần build>`, ví dụ `1.0.57`. Muốn lên `1.1.x` thì sửa `version.txt`. Đẩy tag `vX.Y.Z` cũng tạo release đúng phiên bản `X.Y.Z`. Sau đó hãy nâng `version.txt` cao hơn, để các bản build tiếp theo vẫn được coi là mới hơn.

Workflow gồm ba bước:
- `runtime`: build `uxplay.exe` trong MSYS2 UCRT64 từ commit UxPlay được khóa trong `native/uxplay.ref`. Sau đó gom các plugin GStreamer và DLL cần thiết, rồi kiểm tra bản gom chạy được trên Windows sạch, không có MSYS2.
- `app`: chạy unit test, build app WPF, đóng gói zip và bộ cài Inno Setup.
- `release`: chạy khi push vào `main` hoặc đẩy tag `v*`, đăng release lên repo công khai ở trên.

### Trên máy

- Phần logic (`src/MacExtend.Core`) và test chạy được trên mọi hệ điều hành có .NET 10 SDK: `dotnet test tests/MacExtend.Core.Tests`.
- App WPF biên dịch được cả trên macOS/Linux để kiểm tra lỗi (`dotnet build src/MacExtend.App`), nhưng chỉ chạy được trên Windows.
- Muốn tự build uxplay trên Windows: cài [MSYS2](https://www.msys2.org), mở terminal UCRT64, cài các gói trong workflow, rồi chạy `bash native/build-uxplay.sh && bash native/bundle-runtime.sh`. Chép `dist/uxplay` vào cạnh `MacExtend.exe` thành thư mục `uxplay`.

### Cấu trúc

```
native/                 build và gom uxplay (MSYS2), danh sách plugin GStreamer, script kiểm tra
src/MacExtend.Core/     logic thuần: cài đặt, độ phân giải, tham số uxplay, đọc log, chính sách khởi động lại
src/MacExtend.App/      app WPF: cửa sổ, khay hệ thống, chạy uxplay, tường lửa, mạng, ngủ/thức
tests/                  unit test cho Core
installer/              script Inno Setup
tools/make_icons.py     sinh các file .ico (Python thuần)
tools/coredevice/       runtime Python khóa phiên bản của Điều khiển trực tiếp, script build và test CI, probe cho người phát triển
tools/MacExtend.CoreDeviceProbe/  công cụ console thử CoreDevice (cho người phát triển, không thuộc app)
src/MacExtend.CoreDeviceHost/    mã dùng chung giữa app và probe để chạy lệnh và dựng dòng lệnh
docs/TESTING.md         checklist kiểm thử trên máy thật
docs/research/          báo cáo nghiên cứu khả thi (chưa phải tính năng)
```

## Giấy phép và ghi công

MacExtend là phần mềm tự do theo giấy phép **GNU GPL v3 hoặc mới hơn** (xem `LICENSE`).

Thành phần đi kèm:
- [UxPlay](https://github.com/FDH2/UxPlay) (GPLv3): lõi nhận AirPlay.
- [GStreamer](https://gstreamer.freedesktop.org) (LGPL) và [FFmpeg](https://ffmpeg.org) (gói MSYS2, LGPL/GPL): giải mã và hiển thị.
- libplist (LGPL), OpenSSL (Apache 2.0).
- Font [Be Vietnam Pro](https://github.com/bettergui/BeVietnamPro) (SIL OFL 1.1).
- [pymobiledevice3](https://github.com/doronz88/pymobiledevice3) (GPL-3.0-or-later) và các gói Python đi kèm, cùng [CPython](https://www.python.org) 3.13 (PSF License): thành phần Điều khiển trực tiếp, tải riêng khi thiết lập; giấy phép của từng gói nằm trong `coredevice\python\Lib\site-packages\*.dist-info`.
- [Microsoft Edge WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) SDK 1.0.4258.31 (giấy phép Microsoft, bản sao trong `licenses`): hiện hình của Điều khiển trực tiếp.
- Phần bàn phím và chuột Bluetooth chuyển thể từ [windows-ble-hid](https://github.com/abhishek-raj/windows-ble-hid) (MIT), commit b33a004.

Văn bản giấy phép của các thành phần nằm trong `uxplay/licenses` và `licenses` trong thư mục cài đặt.

AirPlay, Mac, MacBook, macOS, iPhone, iPad và iPadOS là thương hiệu của Apple Inc. MacExtend không liên kết với và không được Apple bảo trợ.
