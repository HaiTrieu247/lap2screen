# Kiểm thử trên máy thật

Unit test chỉ phủ phần logic. Những mục dưới đây cần một laptop Windows và một Mac thật. Ghi kết quả vào cột cuối: đạt, lỗi, hoặc ghi chú. Khi lỗi, đính kèm file `%LOCALAPPDATA%\MacExtend\logs\uxplay.log`.

Chuẩn bị: cài bản mới bằng `MacExtend-x.y.z-setup.exe`. Hai máy dùng chung Wi-Fi nhà. Mạng trên Windows đặt là Riêng tư.

| # | Kiểm tra | Cách làm | Mong đợi | Kết quả |
|---|---|---|---|---|
| T1 | Mac thấy thiết bị | Mở app, trên Mac mở Phản chiếu màn hình | Tên `MacExtend-<tên máy>` xuất hiện trong vòng 10 giây | |
| T2 | Ghép nối bằng PIN | Chọn thiết bị trên Mac | Mac hỏi PIN, nhập mã trong app thì kết nối được | |
| T3 | Màn hình mở rộng | Chọn Dùng làm màn hình riêng | Windows hiện màn nền Mac toàn màn hình; kéo được cửa sổ sang; app báo "Đang là màn hình phụ" kèm tên Mac | |
| T4 | Độ phân giải | Đặt Windows scale 125% hoặc 150%. Trên Mac vào Displays, chọn 1920 × 1080 cho màn AirPlay; chạy `system_profiler SPDisplaysDataType` | Resolution và UI Looks like đều là 1920 x 1080; chữ trên màn Windows nét như trên màn gốc | |
| T5 | Độ trễ | Kéo cửa sổ qua lại, gõ chữ trên màn Windows | Trễ khoảng 100 đến 250 ms, không giật kéo dài | |
| T6 | Âm thanh | Phát nhạc trên Mac, sau đó tắt Âm thanh trong app và khởi động lại bộ nhận | Lần đầu tiếng phát ra trên Windows; sau khi tắt thì không | |
| T7 | Lần kết nối sau | Ngắt rồi kết nối lại | Không hỏi PIN nữa | |
| T8 | Quên Mac | Bấm Quên tất cả rồi kết nối lại | Mac hỏi PIN lại | |
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
