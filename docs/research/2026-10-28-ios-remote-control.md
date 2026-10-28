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
| C. Windows tự giả làm chuột/bàn phím Bluetooth (phần mềm) | Chưa chứng minh được, có xung đột đã xác minh: HID over GATT bắt buộc có Device Information Service, còn Windows không cho ứng dụng tự đăng dịch vụ này. Thêm hai điểm chưa rõ: chạy từ app WPF không đóng gói được không, và adapter có hỗ trợ vai trò peripheral không. | Xung đột DIS: đã xác minh bằng nguồn Microsoft và Bluetooth SIG. Hoạt động thực tế với iOS: chưa thử. |
| D. Cầu nối phần cứng: Windows gửi qua USB tới một bo mạch đóng vai chuột/bàn phím Bluetooth | Hướng khả thi nhất. Firmware trên bo tự có đủ HID, Battery và Device Information Service. Đạt mức "điều khiển bằng con trỏ": iPad có con trỏ sẵn, iPhone qua AssistiveTouch. Không đạt cảm giác chạm trực tiếp chính xác như iPhone Mirroring. | ESP32-S3 và mẫu firmware Nordic: đã xác minh bằng nguồn. Trải nghiệm với iOS: chưa thử. |
| E. Tự động hóa qua USB/công cụ lập trình viên | Không phù hợp làm tính năng dùng ngay. | Suy luận; chưa thử. |

Đề xuất: nếu muốn đi tiếp, làm một prototype riêng theo hướng D (mục 8) trên cả iPhone và iPad trước khi hứa hẹn tính năng.

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
   - Hệ quả: một ứng dụng Windows chỉ có thể đăng HID Service và Battery Service, không thể tự đăng DIS, nên **chưa thể tuân thủ HOGP**. Windows có tự cung cấp DIS của hệ thống để thay vào hay không, và iOS có chấp nhận thiết bị như vậy hay không: chưa có nguồn, chỉ biết được bằng thử nghiệm.
3. **Capability và package identity.** [GattServiceProvider](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattserviceprovider) ghi "App capabilities | bluetooth", có từ Windows 10 Creators Update (10.0.15063.0). Trang GATT Server viết "You must declare the "bluetooth" capability in *Package.appxmanifest*." [App capability declarations](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations): "*Most* scenarios for app capabilities are relevant only to apps that have package identity." [WinRT APIs not supported in desktop apps](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-api-desktop-app-support) không liệt kê `Windows.Devices.Bluetooth` trong nhóm cần package identity, nhưng tự ghi "The list might not be comprehensive."
   - Hệ quả: MacExtend là app WPF .NET Framework 4.8 không đóng gói. Không có tài liệu Microsoft nào nói `GattServiceProvider` chạy được hay không chạy được trong trường hợp này: **chưa chứng minh**, phải thử. Nếu cần package identity thì phải đóng gói MSIX hoặc "packaging with external location".
4. **Adapter.** [BluetoothAdapter.IsPeripheralRoleSupported](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothadapter.isperipheralrolesupported?view=winrt-28000): "Gets a boolean indicating if the adapater supports LowEnergy peripheral role." Tài liệu chỉ nói theo từng adapter; việc phụ thuộc driver là suy luận. Không thể hứa trước cho mọi laptop.
5. **Descriptor, ghép nối, mã hóa, kết nối lại.** Thiết bị HID cần report descriptor đúng (chuột: nút, chuyển động tương đối, cuộn; bàn phím: mã phím), ghép nối có mã hóa (bonding) và kết nối lại được sau khi tắt mở. Trên một GATT server tự viết, các phần này phải tự làm; chưa thử.

Kết luận hướng C: có một xung đột đã xác minh (DIS bắt buộc nhưng bị Windows giữ) và hai điểm chưa rõ (đóng gói, adapter). Không nên chọn làm hướng chính.

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
| HID Service đăng từ app có hoạt động với iOS | Chưa thử | |
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

## 10. Chưa thử

- Chưa chạy bất kỳ thử nghiệm Bluetooth nào, trên Windows hay iOS.
- Chưa biết adapter/driver Bluetooth của laptop mục tiêu có hỗ trợ vai trò peripheral hay không.
- Chưa biết phiên bản iOS/iPadOS mục tiêu.
- Chưa xác nhận iOS/iPadOS có nhận digitizer tuyệt đối qua Bluetooth hay không.
- Chưa thử firmware nào trên ESP32-S3 hay nRF52840.
- Chưa đọc bản PDF của đặc tả HOGP và phần cuối bản HTML; chưa đọc được datasheet ESP32-S3 (PDF không đọc được) và trang thông số nRF52840.
