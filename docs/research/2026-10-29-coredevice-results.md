# CoreDevice probe: kết quả thử nghiệm

- Cập nhật: 2026-10-29.
- Phép thử định nghĩa ở [docs/TESTING.md](../TESTING.md), *Thử nghiệm C*. Cách chạy ở [tools/coredevice/README.md](../../tools/coredevice/README.md). Hợp đồng upstream ở [2026-10-29-coredevice-probe.md](2026-10-29-coredevice-probe.md).
- Chưa có thiết bị thật trong lượt triển khai, nên mọi ô phần cứng là **Chưa chạy**. HTTP 200, cổng đã nhả hoặc cây tiến trình đã hết không thay thế kiểm tra trên thiết bị.

## Kiểm tra không cần thiết bị

| Phép thử | Nơi chạy | Kết quả |
|---|---|---|
| Unit test Core (tham số, môi trường, client, log) | macOS, .NET 10 | Đạt, 2026-10-29 |
| Build probe net48 | macOS, .NET 10 SDK | Đạt (chỉ biên dịch) |
| `setup.ps1` hai lần, `pip check`, phiên bản, `serve-web --help` | job *CoreDevice probe*, windows-latest | Đạt, 2026-10-29: pymobiledevice3 11.47.0, CPython 3.12 x64 |
| Unit test Core, build solution, build probe net48 | job *CoreDevice probe*, windows-latest | Đạt, 2026-10-29 |
| Smoke probe net48 với serve-web giả, venv thật | job *CoreDevice probe*, windows-latest | Đạt, 2026-10-29: cả 19 kịch bản trong `SCENARIOS` |

Lượt CI: [run 40826987621](https://github.com/HaiTrieu247/lap2screen/actions/runs/40826987621), commit 6430ad9 trên `beta/bluetooth`, chạy tay (`workflow_dispatch`). Artifact `coredevice-probe-6430ad9`.

## Môi trường của mỗi lượt

Ghi cho mỗi lượt: Windows build, Python và mã commit của lock, model thiết bị và iOS/iPadOS, Apple service (iTunes cổ điển hay Apple Devices), trình duyệt và phiên bản, GPU và driver, transport, thời gian tới *sẵn sàng*. Không ghi UDID, tên thiết bị, địa chỉ mạng.

## Ma trận

Mỗi ô ghi Đạt / Không đạt / Chưa chạy, kèm ghi chú ngắn. Cột Wi-Fi dùng tunneld.

| # | iPhone USB userspace | iPhone USB tunneld | iPhone Wi-Fi | iPad USB userspace | iPad USB tunneld | iPad Wi-Fi |
|---|---|---|---|---|---|---|
| C1 serve-web trực tiếp, hình HEVC (Edge, Chrome) | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |
| C2 chạm, giữ, kéo, Home qua viewer | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |
| C3 nhả khi rời hình, mất focus, đổi tab | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |
| C4 bàn phím, phím tắt | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |
| C5 tiếng Việt (UniKey/EVKey tắt), clipboard | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |
| C6 xoay màn hình qua viewer | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |
| C7 camera, ghi âm, cuộc gọi video (hai thứ tự) | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |
| C8 loa/tai nghe và âm thanh AirPlay trước/trong/sau | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |
| C9 lệnh API probe: tap, hold, drag, key, release, home | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |
| C10 media được trả sau `stop` | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |
| C11 media sau Ctrl+C | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |
| C12 media sau kill cưỡng bức và probe bị đóng | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |
| C13 mất kết nối, kết nối lại | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |
| C14 độ trễ (ít nhất 20 thao tác) | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |
| C15 chạy cùng AirPlay của MacExtend | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy | Chưa chạy |

## Kết luận hiện tại

Chưa đủ bằng chứng để quyết định thay Bluetooth. Đã có: đường triển khai xác minh từ mã nguồn, công cụ thử, và CI không cần thiết bị đã chạy xanh trên Windows. Còn thiếu toàn bộ phép thử phần cứng ở trên, trước hết C1, C7 và C10–C12.
