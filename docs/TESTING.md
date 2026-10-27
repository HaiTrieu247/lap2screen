# Kiểm thử

Kiểm thử gồm hai phần tách biệt:

1. **Kiểm tra tự động trên Mac** (người triển khai chạy): unit test phần logic và biên dịch app WPF. Phần này chỉ chứng minh logic và mã biên dịch được, không chứng minh app chạy đúng trên Windows.
2. **Kiểm thử trên máy thật sau CI** (người dùng chạy): cài đúng bản do GitHub Actions đóng gói lên laptop Windows, rồi chạy các bảng T, P và U bên dưới với Mac, iPhone và iPad thật.

Chỉ ghi "đạt" cho một hàng khi đã chạy hàng đó trên thiết bị thật. Hàng chưa chạy hoặc thiếu thiết bị thì ghi **chưa kiểm chứng**.

## Phần 1: Kiểm tra tự động trên Mac

Lần chạy gần nhất: 2026-10-28, macOS, .NET SDK 10.0.403.

| Lệnh | Kết quả | Ghi chú |
|---|---|---|
| `dotnet test tests/MacExtend.Core.Tests` | Đạt: 171/171 | Gồm test log iPhone/iPad, nguồn mới thay phiên không có disconnect, ngắt qua hai mẫu TCP bằng 0 rồi kết nối lại từ nguồn khác, đặt lại bộ đếm khi thay phiên |
| `dotnet build src/MacExtend.App` | Đạt: 0 cảnh báo, 0 lỗi | Chỉ kiểm tra biên dịch C# và XAML; binding và giao diện chỉ kiểm chứng được khi chạy trên Windows |

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
Windows (winver: phiên bản và build), scale màn hình, GPU:
Mac: model, macOS               (không có thì ghi: chưa kiểm chứng)
iPhone: model, iOS              (không có thì ghi: chưa kiểm chứng)
iPad: model, iPadOS             (không có thì ghi: chưa kiểm chứng)
Cài đặt: Độ phân giải (Tự động / Tùy chỉnh W×H Hz), H.265, Khung hình, Âm thanh, PIN,
         Tự chuyển sang màn hình AirPlay, Ẩn con trỏ
Log đính kèm:
```

### Mac: màn hình mở rộng

| # | Kiểm tra | Cách làm | Mong đợi | Kết quả |
|---|---|---|---|---|
| T1 | Mac thấy thiết bị | Mở app, trên Mac mở Phản chiếu màn hình | Tên `MacExtend-<tên máy>` xuất hiện trong vòng 10 giây | |
| T2 | Ghép nối bằng PIN | Chọn thiết bị trên Mac | Mac hỏi PIN, nhập mã trong app thì kết nối được | |
| T3 | Màn hình mở rộng | Chọn Dùng làm màn hình riêng | Windows hiện màn nền Mac toàn màn hình; kéo được cửa sổ sang; app báo "Đang nhận AirPlay" kèm tên Mac | |
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
| T22 | Ẩn con trỏ | Khi đang xem màn Mac, di chuột Windows | Không thấy con trỏ Windows; phím Fn âm lượng/độ sáng và phím Windows vẫn chạy | |
| T23 | Quay về Windows | Alt+Tab sang Explorer, rồi bấm biểu tượng MacExtend ở khay | Sau Alt+Tab con trỏ hiện lại và dùng Explorer được; bấm biểu tượng thì màn Mac hiện lại, con trỏ ẩn | |
| T24 | Tắt các tùy chọn | Tắt "Ẩn con trỏ chuột Windows" khi đang xem màn Mac | Con trỏ hiện lại ngay | |

### iPhone và iPad: phản chiếu màn hình

Thao tác trên iPhone: mở Trung tâm điều khiển (iPhone không có nút Home: vuốt xuống từ góc phải trên; iPhone có nút Home: vuốt lên từ cạnh dưới), bấm **Phản chiếu màn hình**, chọn tên máy Windows. Muốn dừng: mở lại Phản chiếu màn hình, chọn **Dừng phản chiếu**. Chỉ dùng nội dung không có DRM (ví dụ ảnh và video tự quay trong ứng dụng Ảnh); ứng dụng phim trả phí có thể chặn hình.

| # | Kiểm tra | Cách làm | Mong đợi | Kết quả |
|---|---|---|---|---|
| P1 | iPhone thấy thiết bị | Mở app ở trạng thái Sẵn sàng kết nối, trên iPhone mở Phản chiếu màn hình | Tên `MacExtend-<tên máy>` xuất hiện trong vòng 10 giây | |
| P2 | PIN đúng, sai và lần sau | Bật Yêu cầu mã PIN, khởi động lại bộ nhận. Chọn thiết bị, nhập sai mã; chọn lại, nhập đúng mã. Dừng phản chiếu rồi kết nối lại | Mã sai: không kết nối được. Mã đúng: kết nối; app báo "Đang nhận AirPlay", tên iPhone hiện trong app và khay. Lần sau: ghi lại iPhone có hỏi PIN nữa hay không | |
| P3 | Dọc, ngang và xoay | Mở ứng dụng Ảnh ở hướng dọc, xoay ngang rồi xoay lại; lặp ba lần. Theo dõi PID `uxplay.exe` trong Task Manager | Hình giữ đúng tỉ lệ ở cả hai hướng, có viền khi cần, không bị cắt hay kéo giãn; không mất kết nối; PID không đổi (bộ nhận không khởi động lại) | |
| P4 | Âm thanh | Phát video có tiếng trong ứng dụng Ảnh. Sau đó tắt Âm thanh trong app, khởi động lại bộ nhận, kết nối lại và phát lại | Lần đầu tiếng phát ra trên Windows; sau khi tắt thì không. Ghi lại độ lệch tiếng và hình quan sát được | |
| P5 | Dừng, kết nối lại, Quên tất cả | Chọn Dừng phản chiếu trên iPhone; kết nối lại. Với PIN bật, bấm Quên tất cả rồi kết nối lại | Sau khi dừng, cửa sổ video đóng và app về "Sẵn sàng kết nối" trong vài giây; kết nối lại được; sau Quên tất cả iPhone phải nhập PIN | |
| P6 | Focus, con trỏ, Alt+Tab, khay | Mở vài cửa sổ trên Windows, kết nối iPhone. Di chuột; Alt+Tab sang Explorer; bấm biểu tượng MacExtend ở khay; mở menu khay | Màn hình AirPlay tự lên trước mọi cửa sổ khi có hình; con trỏ ẩn khi đang xem; sau Alt+Tab con trỏ hiện và dùng Explorer được; bấm khay thì màn AirPlay hiện lại; menu có "Hiện màn hình AirPlay" | |
| P7 | Chuyển tuần tự Mac, iPhone, Mac | Kết nối Mac (Dùng làm màn hình riêng), ngắt trên Mac. Kết nối iPhone, dừng phản chiếu. Kết nối lại Mac | Mỗi lần đều kết nối được mà không cần khởi động lại bộ nhận; tên nguồn trong app và khay đúng thiết bị đang phát; Mac vẫn mở rộng desktop | |
| P8 | iPhone thay phiên Mac | Khi Mac đang Dùng làm màn hình riêng, từ iPhone chọn máy Windows mà không ngắt Mac. Sau đó dừng iPhone và kết nối lại Mac | Mac mất màn phụ; app và khay đổi sang tên iPhone; khi Tự chuyển sang màn hình AirPlay đang bật, cửa sổ video mới hiện lên trước mọi cửa sổ; con trỏ ẩn đúng trên cửa sổ video mới (cursor shield bám cửa sổ mới). Mac kết nối lại được và mở rộng desktop | |
| P9 | Tùy chỉnh 1080p với H.265 | Đặt Độ phân giải Tùy chỉnh 1920 × 1080, 60 Hz, bật Dùng H.265, khởi động lại bộ nhận. Kết nối iPhone, xoay vài lần | Ghi lại: có hình không, giật hay lỗi, dòng codec trong `uxplay.log`. Nếu lỗi, quay về Tự động và ghi chú | |
| P10 | Đổi hướng dẫn trong phiên | Khi iPhone đang kết nối, mở app, bấm qua lại giữa Mac và iPhone, iPad nhiều lần. Theo dõi PID `uxplay.exe` | Chỉ bước 1 và bước 4 đổi nội dung; PID không đổi; không hiện thông báo "Thay đổi có hiệu lực sau khi khởi động lại"; phiên không bị ngắt. Mở lại app thì hướng dẫn về Mac | |
| P11 | Theme, scale, tên dài | Đổi Windows giữa Sáng và Tối; đặt scale 125% rồi 150%; đổi tên iPhone (Cài đặt, Cài đặt chung, Giới thiệu, Tên) thành chuỗi khoảng 40 ký tự rồi kết nối | Hướng dẫn và cài đặt không bị cắt chữ; tên nguồn trong khung minh họa xuống tối đa hai dòng và kết thúc bằng dấu "…"; màu đúng theme | |
| P12 | iPad | Lặp nhanh P1 đến P4 với iPad | Kết quả như iPhone. Không có iPad thì ghi chưa kiểm chứng | |
| P13 | Bản đóng gói trên Windows sạch | Cài bản CI trên máy Windows không có MSYS2, chạy P1 đến P3 | Chạy bình thường; không báo thiếu DLL; `uxplay.log` không có lỗi thiếu plugin GStreamer | |

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

Lỗi tìm thấy ở Phần 2 (không thấy thiết bị, xoay, codec, thay phiên, cursor shield, ...) được ghi thành tác vụ sửa riêng, kèm `uxplay.log`, cài đặt và bước tái hiện. Sau khi có kết quả thật, ghi các phiên bản iOS/iPadOS đã kiểm thử vào mục Yêu cầu trong `README.md`.
