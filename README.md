# MacExtend

Biến laptop Windows thành **màn hình thứ hai cho MacBook** qua AirPlay, không cần cáp và không cài gì thêm trên Mac.

Trên Mac chỉ cần mở **Phản chiếu màn hình** (Screen Mirroring), chọn máy Windows, rồi chọn **Dùng làm màn hình riêng** (Use As Separate Display). Từ đó laptop Windows hoạt động như một màn hình mở rộng: kéo cửa sổ sang, chuột đi qua lại giữa hai màn.

MacExtend dùng [UxPlay](https://github.com/FDH2/UxPlay) làm lõi nhận AirPlay. Phần MacExtend thêm vào là:
- Tự báo đúng độ phân giải và tần số quét của màn laptop cho Mac.
- Cấu hình độ trễ thấp và hiển thị toàn màn hình bằng Direct3D 11.
- Tự xử lý tường lửa và mạng Công cộng.
- Tự khởi động lại khi Windows ngủ dậy, khi đổi Wi-Fi hoặc khi uxplay gặp lỗi.

## Yêu cầu

- Windows 10 hoặc 11, 64-bit (x64).
- Mac chạy macOS 12 Monterey trở lên.
- Hai máy cùng một mạng Wi-Fi hoặc LAN. Mạng khách có chặn thiết bị nói chuyện với nhau sẽ không dùng được.

## Cài đặt

Tải bản mới nhất ở trang **Releases** của repo:

- `MacExtend-x.y.z-setup.exe`: bộ cài. Bộ cài tự thêm quy tắc tường lửa và tạo lối tắt trong Start.
- `MacExtend-x.y.z-portable.zip`: bản chạy ngay. Giải nén rồi mở `MacExtend.exe`. Lần đầu bấm **Cho phép** ở mục Tường lửa trong app.

File chưa được ký số, nên Windows SmartScreen có thể cảnh báo. Bấm **More info** rồi **Run anyway**.

## Dùng lần đầu

1. Mở MacExtend. Bộ nhận tự chạy, biểu tượng nằm ở khay hệ thống.
2. Ở mục **Hệ thống** trong app, đảm bảo cả hai dòng *Tường lửa Windows* và *Mạng* đều có dấu tích xanh. Nếu không, bấm nút bên cạnh để sửa.
3. Trên Mac, mở Trung tâm điều khiển, bấm **Phản chiếu màn hình** và chọn tên máy Windows (ví dụ `MacExtend-LAPTOP-7Q2K`).
4. Nhập mã PIN hiện trong app. Mac chỉ hỏi ở lần đầu.
5. Bấm lại biểu tượng Phản chiếu màn hình, chọn **Dùng làm màn hình riêng**.
6. Muốn sắp xếp vị trí hai màn hình, vào System Settings, mục Displays, rồi chọn Arrange trên Mac.

Đóng cửa sổ MacExtend không tắt bộ nhận; app vẫn chạy ở khay hệ thống. Muốn thoát hẳn, chuột phải vào biểu tượng ở khay và chọn **Thoát MacExtend**.

## Cập nhật

MacExtend tự kiểm tra bản mới trên GitHub: 1 phút sau khi mở, sau đó mỗi 6 giờ. Muốn kiểm tra ngay, bấm **Kiểm tra cập nhật** ở cuối cửa sổ. Khi có bản mới, thanh thông báo hiện ra cùng dung lượng cần tải. Bấm **Cập nhật** thì MacExtend tải bản vá, đóng lại, chép file mới và tự mở lại. Bản cài trong Program Files sẽ có một hộp thoại UAC hỏi quyền quản trị. Không cần gỡ bản cũ.

- Bản vá gồm phần app, khoảng 1 MB. Phần uxplay và GStreamer (vài chục MB) chỉ được tải khi chính phần đó thay đổi.
- Tải hỏng hoặc sai mã SHA-256 thì bản đang dùng không bị đụng tới. Nếu chép file lỗi giữa chừng, các file cũ được khôi phục.
- Nhật ký cập nhật nằm ở `%LOCALAPPDATA%\MacExtend\logs\update.log`.

## Khi Mac kết nối

- Ngay khi Mac bắt đầu chiếu, màn hình Mac tự hiện lên trước mọi cửa sổ và con trỏ chuột Windows được ẩn. Cả hai đều tắt được ở mục Hình ảnh.
- Phím tắt Windows (phím Windows, Alt+Tab, Win+D) và phím Fn (âm lượng, độ sáng) vẫn hoạt động như bình thường.
- Muốn dùng Windows: Alt+Tab sang ứng dụng khác, con trỏ hiện lại ngay. Muốn quay lại màn hình Mac: Alt+Tab về, hoặc bấm biểu tượng MacExtend ở khay (hay chọn **Hiện màn hình Mac** trong menu của nó).

## Cài đặt trong app

| Mục | Ý nghĩa |
|---|---|
| Tên hiện trên Mac | Tên trong menu Phản chiếu màn hình. Dấu tiếng Việt được bỏ, vì uxplay trên Windows chỉ nhận ký tự ASCII. |
| Độ phân giải | *Tự động*: báo đúng kích thước màn laptop, tối đa 1920 × 1080 ở 60 Hz. *Tùy chỉnh*: tự nhập kích thước và tần số; bật H.265 nếu cần lớn hơn 1080p. |
| Khung hình mỗi giây | 60 mượt hơn, 30 nhẹ hơn cho Wi-Fi yếu. |
| Âm thanh | Phát tiếng của Mac trên loa Windows hoặc tắt. |
| Mã PIN | Chặn người khác trong cùng mạng chiếu lên máy bạn. *Quên tất cả* bắt mọi Mac nhập lại mã. |
| Khởi động cùng Windows | Chạy ẩn ở khay hệ thống sau khi đăng nhập. |

Các thay đổi về hình ảnh, âm thanh và PIN áp dụng sau khi khởi động lại bộ nhận. App hiện thông báo kèm nút **Khởi động lại** khi cần.

## Khắc phục sự cố

**Mac không thấy máy Windows**
- Mục Mạng báo *Công cộng*: bấm **Chuyển sang Riêng tư**. Windows chặn tìm kiếm thiết bị trên mạng Công cộng.
- Mục Tường lửa báo chưa được phép hoặc bị chặn: bấm **Cho phép**.
- Phần mềm diệt virus có tường lửa riêng (Kaspersky, ESET, ...): cho phép `uxplay.exe` trong thư mục cài đặt, đường dẫn `uxplay\bin\uxplay.exe`.
- Wi-Fi công cộng hoặc mạng khách thường cô lập các thiết bị với nhau. Dùng mạng nhà hoặc bật hotspot từ điện thoại.

**Hình bị trễ hoặc giật**
- Đặt Khung hình mỗi giây về 30.
- Để hai máy gần router, dùng Wi-Fi 5 GHz, hoặc cắm dây LAN cho laptop Windows.

**Hình bị mờ hoặc chữ nhỏ li ti**
- Trên Mac, vào **System Settings, Displays** và chọn màn hình MacExtend. Ở mục Resolution, chọn đúng **1920 × 1080**. Nếu chỉ thấy các ô "Larger Text ... More Space", bấm **Advanced...** hoặc chuột phải vào khu vực đó, bật **Show all resolutions** rồi chọn 1920 × 1080. Chọn kích thước lớn hơn, ví dụ 2560 × 1440 hoặc 3840 × 2160, thì Mac phải thu nhỏ cả desktop vào luồng 1080p, nên chữ bị nhòe.
- Kiểm tra trên Mac: chạy `system_profiler SPDisplaysDataType` và xem khối của màn AirPlay. Hai dòng **Resolution** và **UI Looks like** đều phải là 1920 x 1080.
- MacExtend tự bật chế độ "Override high DPI scaling: Application" cho `uxplay.exe`, để Windows ở mức scale 125% hoặc 150% không kéo giãn hình. Nếu hình vẫn mờ, chuột phải `uxplay\bin\uxplay.exe`, mở **Properties, Compatibility, Change high DPI settings** và kiểm tra mục này đã được chọn.

**Hình bị đơ ngay khi kết nối**
- Ngắt Phản chiếu màn hình trên Mac rồi kết nối lại, hoặc bấm **Khởi động lại** trong app.

**Xem nhật ký**
- Trong app, bấm **Mở thư mục nhật ký**. File `uxplay.log` nằm ở `%LOCALAPPDATA%\MacExtend\logs`.

## Build từ mã nguồn

### Bằng GitHub Actions (khuyên dùng)

Repo có sẵn workflow `.github/workflows/build.yml` chạy trên máy Windows của GitHub:

1. Push repo lên GitHub.
2. Workflow tự chạy với mỗi lần push vào `main`, hoặc chạy tay ở tab **Actions**, chọn **build**, rồi **Run workflow**.
3. Khi xong, tải artifact `MacExtend-<phiên bản>`. Artifact chứa file `.zip` portable và bộ cài `.exe`.
4. Mỗi lần push vào `main`, workflow tự tạo một GitHub Release `v<phiên bản>`. Release gồm bộ cài, bản portable và các gói cập nhật (`update.ini`, `MacExtend-x.y.z-app.zip`, `MacExtend-x.y.z-runtime.zip`). App trên máy sẽ tìm thấy bản mới qua các gói này.
5. Phiên bản là `<version.txt>.<số thứ tự lần build>`, ví dụ `1.0.57`. Muốn lên `1.1.x` thì sửa `version.txt`. Đẩy tag `vX.Y.Z` cũng tạo release đúng phiên bản `X.Y.Z`. Sau đó hãy nâng `version.txt` cao hơn, để các bản build tiếp theo vẫn được coi là mới hơn.

Workflow gồm ba bước:
- `runtime`: build `uxplay.exe` trong MSYS2 UCRT64 từ commit UxPlay được khóa trong `native/uxplay.ref`. Sau đó gom các plugin GStreamer và DLL cần thiết, rồi kiểm tra bản gom chạy được trên Windows sạch, không có MSYS2.
- `app`: chạy unit test, build app WPF, đóng gói zip và bộ cài Inno Setup.
- `release`: chỉ chạy khi có tag `v*`.

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
docs/TESTING.md         checklist kiểm thử trên máy thật
```

## Giấy phép và ghi công

MacExtend là phần mềm tự do theo giấy phép **GNU GPL v3 hoặc mới hơn** (xem `LICENSE`).

Thành phần đi kèm:
- [UxPlay](https://github.com/FDH2/UxPlay) (GPLv3): lõi nhận AirPlay.
- [GStreamer](https://gstreamer.freedesktop.org) (LGPL) và [FFmpeg](https://ffmpeg.org) (gói MSYS2, LGPL/GPL): giải mã và hiển thị.
- libplist (LGPL), OpenSSL (Apache 2.0).
- Font [Be Vietnam Pro](https://github.com/bettergui/BeVietnamPro) (SIL OFL 1.1).

Văn bản giấy phép của các thành phần nằm trong `uxplay/licenses` và `licenses` trong thư mục cài đặt.

AirPlay, Mac, MacBook và macOS là thương hiệu của Apple Inc. MacExtend không liên kết với và không được Apple bảo trợ.
