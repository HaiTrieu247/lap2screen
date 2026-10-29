# Điều khiển iPhone/iPad từ laptop Windows khi đang phản chiếu: nghiên cứu khả thi

- Ngày khảo sát: 2026-10-28.
- Phạm vi: chỉ nghiên cứu. MacExtend chưa có tính năng điều khiển, không có nút hay tùy chọn nào cho việc này, và chưa có thử nghiệm nào trên thiết bị thật.
- Câu hỏi: khi iPhone/iPad đang phản chiếu lên laptop Windows qua MacExtend, có thể dùng chuột, bàn phím (hoặc màn hình cảm ứng) của laptop để điều khiển thiết bị, giống iPhone Mirroring trên Mac không?
- Cách xác minh: mã nguồn (mục 2) đọc trực tiếp tại commit được khóa. Tài liệu Apple, Microsoft, Bluetooth SIG, Espressif và Nordic đọc ngày 2026-10-28; trích dẫn tiếng Anh giữ nguyên văn. Mỗi ý trong bảng ở mục 9 ghi rõ là đã xác minh bằng nguồn, suy luận hay chưa thử.

## Kết luận ngắn

| Hướng | Kết luận | Mức chắc chắn |
|---|---|---|
| A. Gửi thao tác ngược lại qua AirPlay | Không làm được. MacExtend và UxPlay không có kênh nào gửi thao tác về thiết bị; Apple cũng giới thiệu AirPlay là cách để xem, không để tương tác. | Mã nguồn: đã xác minh (mục 2). Apple: không có tài liệu kỹ thuật nói rõ giao thức không có kênh ngược, chỉ có câu mô tả ở mục 3. |
| B. Tái tạo iPhone Mirroring của Apple | Không làm được. Tính năng cần Mac chạy macOS Sequoia 15 trở lên và iPhone chạy iOS 18 trở lên, cùng Apple Account; không có giao thức công khai. | Yêu cầu: đã xác minh bằng nguồn Apple. "Không có giao thức công khai": suy luận (không tìm thấy tài liệu). |
| C. Windows tự giả làm chuột/bàn phím Bluetooth (phần mềm) | Có bằng chứng triển khai bên ngoài; nên thử trước. DIS bị giữ bởi Windows không đủ để kết luận hướng này bị chặn. Xem cập nhật mục 10. | Tác giả dự án tham khảo báo đã thử iPhone; MacExtend chưa thử; iPad chưa kiểm chứng. |
| D. Cầu nối phần cứng: Windows gửi qua USB tới một bo mạch đóng vai chuột/bàn phím Bluetooth | Hướng khả thi nhất. Firmware trên bo tự có đủ HID, Battery và Device Information Service. Đạt mức "điều khiển bằng con trỏ": iPad có con trỏ sẵn, iPhone qua AssistiveTouch. Không đạt cảm giác chạm trực tiếp chính xác như iPhone Mirroring. | ESP32-S3 và mẫu firmware Nordic: đã xác minh bằng nguồn. Trải nghiệm với iOS: chưa thử. |
| E. Tự động hóa qua USB/công cụ lập trình viên | Không phù hợp làm tính năng dùng ngay. | Suy luận; chưa thử. |

Đề xuất cập nhật: thử hướng C bằng phần mềm độc lập trước (mục 10), rồi mới tích hợp. Hướng D là phương án dự phòng nếu adapter hoặc driver Windows không đáp ứng. Cả hai đều cần kiểm thử cùng AirPlay trên iPhone và iPad thật.

## 1. Bốn khái niệm cần tách bạch

- **AirPlay phản chiếu màn hình** (thứ MacExtend đang dùng): thiết bị Apple gửi luồng hình và tiếng tới máy nhận. Chiều dữ liệu là thiết bị → máy nhận.
- **iPhone Mirroring của Apple**: Mac hiển thị và điều khiển iPhone. Tính năng của macOS và iOS, chỉ chạy giữa Mac và iPhone.
- **Windows làm HID host**: Windows nhận chuột, bàn phím Bluetooth. Laptop vẫn làm việc này hằng ngày, nhưng không giúp điều khiển iPhone.
- **Windows làm HID peripheral**: Windows tự đóng vai một con chuột hoặc bàn phím Bluetooth để iPhone/iPad kết nối tới. Đây mới là chiều cần cho việc điều khiển, và là chiều khó (mục 4).

## 2. Pipeline hiện tại có kênh gửi thao tác về thiết bị không?

Không. Đã đọc mã nguồn MacExtend trong repo này và UxPlay tại commit được khóa trong `native/uxplay.ref` ([28ed70a](https://github.com/FDH2/UxPlay/tree/28ed70a9876fe3f5864479f4e0d07abe4678383a), UxPlay 1.74):

- MacExtend chỉ khởi động `uxplay.exe` với tham số dựng sẵn (`src/MacExtend.Core/UxPlayArguments.cs`), đọc log của nó (`UxPlayLogParser`) và đếm kết nối TCP của tiến trình (`TcpConnections`). Không có đoạn mã nào gửi dữ liệu tới thiết bị.
- Trong UxPlay, sự kiện bàn phím trên cửa sổ video chỉ được dùng tại chỗ để bật/tắt toàn màn hình (F11, Alt+Return) trong `renderers/video_renderer.c`, và khối này chỉ được biên dịch khi CMake tìm thấy X11 (`X_DISPLAY_FIX` trong `renderers/CMakeLists.txt`). Không có mã chuyển sự kiện chuột hay bàn phím tới thiết bị.
- Tùy chọn `-dacp` chỉ ghi ra file mã DACP của nguồn phát (điều khiển phát nhạc kiểu remote khi truyền âm thanh), không liên quan đến thao tác trên màn hình phản chiếu. Bit tính năng "Hangdog Remote Control" trong `uxplay.cpp` chỉ nằm trong một khối chú thích, không được bật.

Vì vậy, muốn điều khiển phải có một kênh riêng, độc lập với AirPlay. Kênh iOS mở cho thiết bị ngoài là chuột/bàn phím chuẩn HID qua Bluetooth (hoặc USB).

## 3. iPhone Mirroring của Apple

Nguồn: [Apple Support 120421, "Use your iPhone from your Mac"](https://support.apple.com/en-us/120421).

- Yêu cầu: "Your Mac is using macOS Sequoia 15 or later and is a Mac with Apple silicon or Mac with the Apple T2 Security Chip", "Your iPhone is using iOS 18 or later", "signed in to the same Apple Account using two-factor authentication", "Your iPhone is locked and near your Mac". Thêm: "iPhone Mirroring is currently unavailable in the European Union."
- Cách tương tác: "Use your Mac mouse or trackpad to click wherever you would tap on your iPhone", và "scroll or swipe with your mouse or trackpad, and use your Mac keyboard to type".
- Về AirPlay, cùng trang viết: "To simply view but not interact with your iPhone screen or its streamed video on an AirPlay-compatible device ... you can use AirPlay instead." Apple mô tả AirPlay là để xem, không để tương tác. Đây là mô tả sản phẩm, không phải đặc tả giao thức.
- Trang không ghi nguyên văn "chỉ dành cho Mac", nhưng mọi yêu cầu đều là Mac và macOS. Không tìm thấy giao thức hay SDK công khai để máy khác làm việc này; tái tạo nó đòi hỏi giả lập giao thức riêng của Apple, nằm ngoài phạm vi (không dùng private API, không jailbreak).
- Kết luận: Windows không thể có "iPhone Mirroring" đúng nghĩa. Thứ có thể đạt là phản chiếu qua AirPlay (đã có) cộng với một kênh điều khiển riêng (mục 4 đến 6).

## 4. Windows tự đóng vai chuột/bàn phím Bluetooth

1. **Dịch vụ bị hạn chế.** [Microsoft, Bluetooth GATT Server](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-server) (bản ngày 2026-05-27): "The following Services are reserved by the system and cannot be published at this time:" gồm Device Information Service (DIS), Generic Attribute Profile Service (GATT), Generic Access Profile Service (GAP), Scan Parameters Service (SCP). "Attempting to create a blocked service will result in BluetoothError.DisabledByPolicy being returned from the call to CreateAsync." HID Service (0x1812) không được nhắc tới: không nằm trong danh sách cấm, nhưng cũng không được nói là dùng được.
2. **HID over GATT cần DIS.** [Bluetooth SIG, HID over GATT Profile 1.0](https://www.bluetooth.com/specifications/specs/hid-over-gatt-profile-1-0/), [bản HTML của đặc tả](https://www.bluetooth.com/specifications/specs/html/?src=HOGP_v1.0/out/en/index-en.html), mục 3: "The HID Device shall have one or more instances of the HID Service, one or more instances of the Battery Service, one instance of the Device Information Service, and optionally the Scan Parameters Service". Bảng 3.1 ghi HID Service, Battery Service, Device Information Service đều bắt buộc (M). Mục 2.1: "The HID Device shall be a GATT server."
   - Giới hạn này không tự chứng minh thiết bị không thể hoạt động: ứng dụng không tự đăng DIS khác với toàn bộ máy không có DIS. Có ghi nhận bên ngoài về DIS do hệ thống cung cấp, xem mục 10. Mức tuân thủ chuẩn và khả năng hoạt động trên máy đích vẫn phải kiểm tra riêng.
3. **Capability và package identity.** [GattServiceProvider](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattserviceprovider) ghi "App capabilities | bluetooth", có từ Windows 10 Creators Update (10.0.15063.0). Trang GATT Server viết "You must declare the "bluetooth" capability in *Package.appxmanifest*." [App capability declarations](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations): "*Most* scenarios for app capabilities are relevant only to apps that have package identity." [WinRT APIs not supported in desktop apps](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-api-desktop-app-support) không liệt kê `Windows.Devices.Bluetooth` trong nhóm cần package identity, nhưng tự ghi "The list might not be comprehensive."
   - Hệ quả: MacExtend là app WPF .NET Framework 4.8 không đóng gói. Không có tài liệu Microsoft nào nói `GattServiceProvider` chạy được hay không chạy được trong trường hợp này: **chưa chứng minh**, phải thử. Nếu cần package identity thì phải đóng gói MSIX hoặc "packaging with external location".
4. **Adapter.** [BluetoothAdapter.IsPeripheralRoleSupported](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothadapter.isperipheralrolesupported?view=winrt-28000): "Gets a boolean indicating if the adapater supports LowEnergy peripheral role." Tài liệu chỉ nói theo từng adapter; việc phụ thuộc driver là suy luận. Không thể hứa trước cho mọi laptop.
5. **Descriptor, ghép nối, mã hóa, kết nối lại.** Thiết bị HID cần report descriptor đúng (chuột: nút, chuyển động tương đối, cuộn; bàn phím: mã phím), ghép nối có mã hóa (bonding) và kết nối lại được sau khi tắt mở. Trên một GATT server tự viết, các phần này phải tự làm; chưa thử.

Kết luận hướng C cập nhật: giới hạn API là có thật, nhưng chưa đủ để loại hướng phần mềm. Bằng chứng mới ở mục 10 khiến hướng này đáng làm thử trước; khả năng chạy từ app net48 hiện tại và hỗ trợ từng adapter vẫn chưa được MacExtend kiểm chứng.

## 5. Con trỏ trên iPhone và iPad

- iPhone: [Apple Support 111775, "How to use a pointer device with AssistiveTouch on your iPhone, iPad, or iPod touch"](https://support.apple.com/en-us/111775). Ghép nối qua "Select AssistiveTouch > Devices, then select Bluetooth Devices", rồi "With your pointer connected, turn on AssistiveTouch." Trang không ghi phiên bản iOS tối thiểu.
- iPad: [Apple Support 108938](https://support.apple.com/en-us/108938): "Connecting a Bluetooth accessory to your iPad requires iPadOS 13.4 or later." Ghép nối qua Cài đặt > Bluetooth, không cần AssistiveTouch.
- Chuột HID gửi chuyển động **tương đối** và iOS tự áp tốc độ con trỏ (suy luận từ chuẩn HID chuột, chưa thử). Muốn con trỏ trên thiết bị trùng chỗ con trỏ Windows trên cửa sổ video phải tự hiệu chỉnh (ví dụ đẩy con trỏ về góc rồi đi tới) và vẫn có thể lệch, nhất là khi có viền đen (letterbox) hay khi xoay dọc/ngang.
- Chuẩn HID có digitizer (tọa độ tuyệt đối), nhưng không tìm thấy nguồn nào xác nhận iOS/iPadOS nhận digitizer Bluetooth như màn hình cảm ứng. Xếp vào **chưa thử**.
- Bắt và thả chuột: khi chuyển thao tác sang thiết bị, chuột Windows phải bị "bắt" vào cửa sổ video và luôn có cách thả an toàn (phím tắt, Alt+Tab, mất focus, ngắt AirPlay).

## 6. Cầu nối phần cứng USB sang Bluetooth LE HID

Ý tưởng: Windows gửi lệnh chuột/phím qua USB (serial) tới một bo mạch nhỏ; bo mạch là chuột + bàn phím BLE HID thật, firmware tự có HID Service, Battery Service và DIS. Cách này tránh xung đột DIS và chuyện đóng gói của hướng C, vì Windows chỉ cần nói chuyện USB.

- **ESP32-S3** (đã xác minh bằng nguồn Espressif):
  - [Trang sản phẩm](https://www.espressif.com/en/products/socs/esp32-s3): "Bluetooth 5 (LE) connectivity".
  - [ESP-ADF](https://docs.espressif.com/projects/esp-adf/en/latest/solution-center/bluetooth-audio.html): "ESP32-S3 supports Bluetooth LE only, and does not support Classic Bluetooth or LE Audio." BLE HID là đủ cho iOS; không cần Bluetooth Classic.
  - [USB device stack](https://docs.espressif.com/projects/esp-usb/en/latest/esp32s3/usb_device.html): "USB Full-Speed (USB 1.1) peripheral with internal PHY", "Multiple supported device classes (CDC, HID, MIDI, MSC…)".
  - [USB Serial/JTAG](https://docs.espressif.com/projects/esp-idf/en/stable/esp32s3/api-guides/usb-serial-jtag-console.html): "ESP32-S3 contains a USB Serial/JTAG Controller". Đủ để Windows gửi lệnh qua cổng serial.
- **nRF52840 và nRF Connect SDK**:
  - Mẫu ["Bluetooth: Peripheral HIDS mouse"](https://github.com/nrfconnect/sdk-nrf) (`samples/bluetooth/peripheral_hids_mouse`): "This sample exposes the HID GATT Service"; `prj.conf` bật `CONFIG_BT_HIDS=y`, `CONFIG_BT_BAS=y`, `CONFIG_BT_DIS=y`, tức là đủ ba dịch vụ HOGP yêu cầu.
  - Ở bản phát hành v3.4.1 (2026-09-17), `sample.yaml` có `nrf52840dk/nrf52840`. Nhưng một commit sau đó trên nhánh phát triển, "samples: bluetooth: remove nrf52 support from HID samples" (2026-09), đã bỏ hỗ trợ nRF52 khỏi các mẫu HID. Nếu chọn nRF52840 phải ghim SDK ở v3.4.1 hoặc tự duy trì firmware.
  - Thông số phần cứng nRF52840 (Bluetooth LE, USB 2.0 full speed) **chưa xác minh** được: trang Nordic trả lỗi 403 khi khảo sát.
- Vì mẫu Nordic sắp bỏ nRF52, ESP32-S3 là lựa chọn đầu tiên cho prototype.
- Chi phí cho người dùng: phải mua và cắm thêm một thiết bị, nạp firmware, ghép nối Bluetooth với iPhone/iPad một lần.
- Mức tương tác dự kiến: như dùng chuột/bàn phím Bluetooth thật với iPhone (qua AssistiveTouch) hoặc iPad, không phải chạm trực tiếp.

## 7. Tự động hóa qua USB và công cụ lập trình viên

Các công cụ kiểm thử giao diện iOS (ví dụ XCUITest của Apple và các công cụ dựa trên nó) điều khiển được thiết bị bằng tọa độ, nhưng cần ghép nối "tin cậy máy tính", bật chế độ nhà phát triển, ký ứng dụng chạy kèm bằng tài khoản nhà phát triển và bộ công cụ của Apple. Đây là suy luận từ cách các công cụ này hoạt động, chưa thử, và không tương đương một tính năng bật lên là dùng được. Không đề xuất đi theo hướng này.

## 8. Đề xuất prototype (chưa làm trong tác vụ này)

Mục tiêu: trả lời bằng thực nghiệm câu hỏi "chuột và bàn phím của laptop điều khiển được iPhone/iPad đang phản chiếu tới mức nào".

1. Phần cứng: một bo ESP32-S3 (hoặc nRF52840 với nRF Connect SDK v3.4.1), nạp firmware chuột + bàn phím BLE HID có DIS và Battery Service, nhận lệnh từ Windows qua USB serial.
2. Phía Windows: một chương trình thử nghiệm riêng, không nằm trong MacExtend, gửi chuyển động tương đối, nút bấm, cuộn và phím khi con trỏ đang ở trên cửa sổ video.
3. Thử trên cả iPhone (AssistiveTouch bật) và iPad (iPadOS 13.4 trở lên), trong khi AirPlay đang chạy:
   - ghép nối, mã hóa, kết nối lại sau khi tắt/mở bo và sau khi thiết bị khóa màn hình;
   - bấm, kéo, cuộn, gõ chữ (kể cả bố cục phím và tiếng Việt);
   - đo độ lệch giữa con trỏ Windows trên cửa sổ video và con trỏ trên thiết bị, với viền đen và khi xoay;
   - bắt và thả chuột an toàn: phím tắt để thoát, thả khi Alt+Tab, khi mất focus, khi ngắt AirPlay, không bao giờ kẹt chuột Windows.
4. Ghi phiên bản iOS/iPadOS, model, bo mạch, firmware và kết quả từng mục. Chỉ khi các mục trên đạt mới bàn đến việc đưa vào MacExtend.

Nếu vẫn muốn thử hướng C (không cần phần cứng), làm thành một thí nghiệm riêng nhỏ: gọi `BluetoothAdapter.IsPeripheralRoleSupported`, đăng HID Service bằng `GattServiceProvider` từ một app không đóng gói, xem có lỗi hay không, rồi xem iPhone/iPad có thấy và ghép nối được không.

## 9. Bảng bằng chứng

| Nhận định | Trạng thái | Nguồn |
|---|---|---|
| MacExtend và UxPlay không có kênh gửi thao tác về thiết bị | Đã xác minh bằng mã nguồn | Repo này; UxPlay `28ed70a`: `uxplay.cpp`, `renderers/video_renderer.c`, `renderers/CMakeLists.txt` |
| Apple mô tả AirPlay là để xem, không tương tác | Đã xác minh bằng nguồn (mô tả sản phẩm, không phải đặc tả giao thức) | Apple 120421 |
| iPhone Mirroring cần macOS 15+, iOS 18+, cùng Apple Account; chưa có ở EU | Đã xác minh bằng nguồn | Apple 120421 |
| Không có giao thức công khai cho iPhone Mirroring | Suy luận (không tìm thấy tài liệu) | |
| Windows giữ DIS, GAP, GATT, SCP; tạo các dịch vụ này trả về DisabledByPolicy | Đã xác minh bằng nguồn | Microsoft, Bluetooth GATT Server |
| HOGP bắt buộc HID Service, Battery Service, DIS | Đã xác minh bằng nguồn | Bluetooth SIG, HOGP 1.0 mục 3, bảng 3.1 |
| HID Service đăng từ app có hoạt động với iOS | Dự án bên ngoài báo thành công; MacExtend chưa thử | Xem mục 10 |
| GATT server cần capability `bluetooth` trong Package.appxmanifest | Đã xác minh bằng nguồn | Microsoft, GATT Server; GattServiceProvider |
| GATT server chạy được từ WPF .NET Framework 4.8 không đóng gói | Chưa chứng minh (tài liệu không nói) | Microsoft, capability declarations; WinRT APIs not supported in desktop apps |
| Hỗ trợ vai trò peripheral tùy adapter | Đã xác minh bằng nguồn; phụ thuộc driver là suy luận | Microsoft, IsPeripheralRoleSupported |
| iPhone dùng chuột qua AssistiveTouch | Đã xác minh bằng nguồn | Apple 111775 |
| iPad nhận phụ kiện Bluetooth, cần iPadOS 13.4+ | Đã xác minh bằng nguồn | Apple 108938 |
| Chuột HID chỉ cho chuyển động tương đối, gây lệch con trỏ | Suy luận; chưa thử | |
| iOS nhận digitizer tuyệt đối qua Bluetooth | Chưa thử, không có nguồn | |
| ESP32-S3: chỉ Bluetooth LE, có USB device (HID, CDC) và USB Serial/JTAG | Đã xác minh bằng nguồn | Espressif |
| Mẫu Nordic HIDS mouse có HIDS, BAS, DIS; v3.4.1 hỗ trợ nRF52840, nhánh phát triển đã bỏ | Đã xác minh bằng nguồn | nrfconnect/sdk-nrf |
| Thông số Bluetooth LE và USB của nRF52840 | Chưa xác minh (trang Nordic lỗi 403) | |
| Công cụ tự động hóa iOS cần chế độ nhà phát triển và bộ công cụ Apple | Suy luận | |

## Những điểm chưa thử trong khảo sát ban đầu

- Chưa chạy bất kỳ thử nghiệm Bluetooth nào, trên Windows hay iOS.
- Chưa biết adapter/driver Bluetooth của laptop mục tiêu có hỗ trợ vai trò peripheral hay không.
- Chưa biết phiên bản iOS/iPadOS mục tiêu.
- Chưa xác nhận iOS/iPadOS có nhận digitizer tuyệt đối qua Bluetooth hay không.
- Chưa thử firmware nào trên ESP32-S3 hay nRF52840.
- Chưa đọc bản PDF của đặc tả HOGP và phần cuối bản HTML; chưa đọc được datasheet ESP32-S3 (PDF không đọc được) và trang thông số nRF52840.

## 10. Cập nhật: thử Bluetooth HID bằng phần mềm trước

Bổ sung ngày 2026-10-28. Đây là kết quả đọc nguồn công khai, chưa phải kết quả chạy trên máy của người dùng. Những kết luận ưu tiên phần cứng ở bản khảo sát đầu được thay bằng đề xuất dưới đây.

### Bằng chứng mới

[windows-ble-hid](https://github.com/abhishek-raj/windows-ble-hid) triển khai bàn phím/chuột BLE trên Windows bằng `GattServiceProvider`. Tác giả báo iPhone nhận thao tác, chuột cần AssistiveTouch; iPadOS và kết nối lại trên iPhone chưa được thử. Dự án có CLI chẩn đoán `BleHid.Cli.exe --diagnose`, cần Windows build 19041+ và adapter hỗ trợ peripheral. Đây là báo cáo của tác giả, chưa được chúng tôi tái hiện.

[DEVELOPMENT.md](https://github.com/abhishek-raj/windows-ble-hid/blob/main/DEVELOPMENT.md) ghi nhận DIS do Windows cung cấp trong bảng GATT đo được. Vì vậy, việc ứng dụng bị cấm tự đăng DIS không đủ để bác bỏ hướng này; ghi nhận đó cũng chưa chứng minh mọi máy đều tuân thủ HOGP.

[Project Core tham khảo](https://github.com/abhishek-raj/windows-ble-hid/blob/main/src/BleHid.Core/BleHid.Core.csproj) nhắm `net8.0-windows10.0.22000.0`, hỗ trợ tối thiểu build 19041. MacExtend hiện là `net48`, nên không thể thêm trực tiếp DLL này làm tham chiếu. Các liên kết trên trỏ nhánh main đang thay đổi; khi dùng mã phải khóa commit, đọc giấy phép và lưu bản tương ứng.

### Cách thử ít thay đổi nhất

1. Trên Windows, dùng CLI chẩn đoán ở trên; lưu model adapter, driver và bản Windows. Kiểm tra peripheral bằng API [IsPeripheralRoleSupported](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothadapter.isperipheralrolesupported). Số phiên bản Bluetooth tự nó chưa trả lời được câu hỏi này.
2. Chạy một giao diện của công cụ tham khảo, ghép nối từ thiết bị Apple, rồi thử bàn phím và chuột riêng trước. iPhone bật [AssistiveTouch](https://support.apple.com/en-us/111775); iPad ghép chuột qua [Cài đặt Bluetooth](https://support.apple.com/en-us/108938).
3. Giữ công cụ chạy, bật Phản chiếu màn hình tới MacExtend. Chọn thiết bị nhận input trong công cụ và bật capture. Dùng phím thoát capture được công cụ công bố, `Ctrl+Alt+Q`, để lấy lại chuột/phím Windows.
4. Thử bảng B trong TESTING.md trên từng thiết bị. Chỉ chuyển sang tích hợp sau khi có kết quả.

Đây là phép thử chạy hai chương trình song song; MacExtend chưa có nút Bluetooth. Chưa có bằng chứng ở lần khảo sát này rằng tổ hợp đó đã chạy ổn cùng AirPlay. Apple cũng [ghi nhận phụ kiện Bluetooth có thể ảnh hưởng Wi-Fi trên một số iPad](https://support.apple.com/en-us/108938), nên phải đo độ trễ và giật hình khi bật cả hai.

### Kiến trúc đề xuất cho repo này

```text
iPhone/iPad ── AirPlay qua Wi-Fi/LAN ──> uxplay.exe ──> cửa sổ video
     ^
     └── Bluetooth LE HID ── bộ điều khiển riêng <── chuột/phím Windows
                                    ^
                                    └── IPC cục bộ ── MacExtend WPF
```

Phần dưới là thiết kế đề xuất dựa trên mã repo, chưa được triển khai:

- Thêm helper Windows dùng .NET hiện đại để quản lý BLE HID. WPF net48 trao đổi trạng thái và lệnh qua named pipe giới hạn cho người dùng hiện tại. Chưa nâng framework toàn bộ app chỉ để thử Bluetooth.
- Thêm `InputController` ở `src/MacExtend.App/Services`, quản lý trạng thái: tắt, không hỗ trợ, đang ghép nối, sẵn sàng, đang điều khiển. AirPlay và Bluetooth có vòng đời độc lập; khởi động lại uxplay không cần khởi động lại helper.
- Dùng `AirPlayPresenter` và `VideoWindow` để nhận biết cửa sổ video còn sống/đang ở trước. Video thuộc tiến trình uxplay nên chỉ thêm sự kiện chuột/phím vào MainWindow WPF sẽ không bắt được thao tác trên video.
- Người dùng chọn đích Bluetooth rõ ràng. Không mặc định thiết bị BLE vừa ghép chính là nguồn AirPlay: repo chưa có cách đối chiếu danh tính hai kết nối. Khi nguồn AirPlay đổi, dừng capture và yêu cầu chọn lại đích.
- Bản đầu dùng chuột tương đối, phím, nút và cuộn. Chọn chế độ điều khiển rõ ràng rồi ẩn chuột Windows trong vùng video, theo dõi con trỏ thiết bị trên hình phản chiếu. Không hứa bấm đúng tọa độ hoặc đa điểm; con trỏ có thể chịu gia tốc và hút vào thành phần UI.
- Rà lại `CursorShield`: hiện nó là lớp phủ toàn màn hình nhận hit test để giấu chuột, không chuyển tiếp thao tác. Cần một nơi duy nhất sở hữu capture để tránh lớp phủ làm mất input hoặc chặn cách thoát.
- Capture phải thả ngay khi mất focus, đóng video, AirPlay ngắt, BLE ngắt hoặc helper lỗi. Có phím thoát xử lý tại máy Windows; không phụ thuộc gửi BLE thành công. Gửi trạng thái nhả toàn bộ phím/nút nếu đường truyền còn hoạt động. Alt+Tab và phím chuyển toàn màn hình cần được dành cho máy Windows theo thiết kế hiện có.
- Thêm cài đặt bật/tắt, chọn thiết bị, trạng thái kết nối, phím thoát; mặc định tắt. Chỉ đóng gói helper vào CI/installer/updater sau khi kiểm thử. Bản Windows cũ hơn build 19041 vẫn nhận AirPlay và báo Bluetooth chưa hỗ trợ nếu dùng nền tảng tham khảo.

### Tiêu chí quyết định

Ưu tiên prototype phần mềm vì có thể thử bằng Bluetooth sẵn trên laptop. Nếu không có peripheral hoặc không ghép nối/nhận input ổn định trên thiết bị đích, chuyển sang cầu USB–BLE ở mục 6. Việc tích hợp hoàn chỉnh chỉ được coi là đạt khi cả điều khiển, phản chiếu và cách lấy lại chuột/phím Windows hoạt động cùng nhau trên thiết bị thật.

### Đã tích hợp (1.0.8-beta.1)

Thử nghiệm ngoài đạt B1–B3 trên iPhone, nên phần điều khiển đã được đưa vào MacExtend, khác thiết kế ở trên một điểm: không có helper riêng. .NET Framework 4.8 gọi được API GATT server của WinRT qua gói `Microsoft.Windows.SDK.Contracts`, nên app WPF tự đóng vai bàn phím/chuột và bắt chuột/phím trên một luồng riêng. Cách này bỏ được pipe, tiến trình thứ hai và runtime .NET đi kèm. Nhận định "net48 không dùng được" ở mục trên chỉ đúng với việc tham chiếu thẳng DLL net8 của dự án tham khảo. Mã nằm ở `src/MacExtend.App/Services/Bluetooth/` và `src/MacExtend.Core/Bluetooth/`; bảng kiểm thử B trong TESTING.md đã đổi theo bản tích hợp.

## 11. Bổ sung 2026-10-29: chạm trực tiếp qua CoreDevice

Mục 7 đánh giá công cụ lập trình viên là "không phù hợp". Đánh giá đó chưa xét `developer core-device display serve-web` của pymobiledevice3: dịch vụ CoreDevice mà Xcode dùng để phản chiếu, đã có sẵn chạm theo tọa độ (nhấn, kéo, nhả), phím vật lý, nút Home và clipboard mà không cần ký ứng dụng chạy kèm. Phân biệt rõ hai loại bằng chứng:

- Đã xác minh bằng mã nguồn pymobiledevice3 11.47.0 (tag `v11.47.0`, commit `f7b034ec`, chứa bản sửa cleanup Windows `d700d8e`): hợp đồng HTTP, tọa độ HID 0..65535, cách viewer nhả chạm/phím, trình tự dừng và trả phiên media, giới hạn camera/micro, ý nghĩa `--no-audio`. Chi tiết ở [2026-10-29-coredevice-probe.md](2026-10-29-coredevice-probe.md).
- Chưa thử trên phần cứng: độ mượt, giải mã HEVC trên trình duyệt Windows, iPad, Wi-Fi, ảnh hưởng tới camera/micro/âm thanh và AirPlay. Kết quả ghi ở [2026-10-29-coredevice-results.md](2026-10-29-coredevice-results.md).

Điều kiện người dùng vẫn nặng hơn Bluetooth: tin cậy máy tính, bật Developer Mode, mount Developer Disk Image sau mỗi lần khởi động lại thiết bị, Python đi kèm, và không dùng được camera/micro khi phiên đang chạy. Vì vậy Bluetooth (mục 10) vẫn là đường điều khiển của sản phẩm; CoreDevice chỉ có công cụ thử riêng `tools/MacExtend.CoreDeviceProbe`, không đóng gói trong app.
