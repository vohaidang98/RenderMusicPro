# Feature Specification: Khung Camera phủ lên video

**Feature Branch**: `002-image-effect-render` (làm chung nhánh theo yêu cầu; thư mục spec riêng vì là tính năng riêng)

**Created**: 2026-11-11

**Status**: Implemented

**Input**: "Thêm khung Camera: góc chữ L vàng ở 4 góc như khung ngắm máy ảnh, huy hiệu logo tròn góc trên trái, chữ REC +
chấm đỏ + biểu tượng pin góc trên phải, dòng thông số kiểu 1/500 · F4.0 · ISO 800 ở dưới; nhiều kiểu khung để chọn;
có nút bật/tắt."

## Clarifications

### Session 2026-11-11

- Q: Tên trường cấu hình? → A: Dùng đúng các trường đã có trong settings.json của người dùng: `EnableCameraFrame` (bool),
  `CameraFramePath` (string), `CameraFramePosition` (int).
- Q: File khung mẫu của bản Full (`Assets\CameraFrames\Frame_01..04*.png`)? → A: Chỉ là file giữ chỗ 480×270 gần trống;
  vẽ lại 4 khung thật 1920×1080, giữ đúng 4 tên.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Gắn khung camera lên video đầu ra (Priority: P1)

Người dùng tích "Khung camera", chọn kiểu khung (ví dụ "03 · Máy quay (REC)") và vị trí, bấm Bắt đầu. Mọi video đầu ra
có khung phủ trên cùng, bất kể nền là video hay ảnh, có hay không có hiệu ứng.

**Independent Test**: Bật khung, render 1 playlist nền video không hiệu ứng và 1 playlist nền ảnh + hiệu ứng → cả hai
video có khung ở lớp trên cùng, hiệu ứng nằm dưới khung.

**Acceptance Scenarios**:

1. **Given** khung bật, **When** render nền video có hiệu ứng, **Then** khung phủ trên hiệu ứng, video giữ độ phân giải nền.
2. **Given** khung bật, nền video không hiệu ứng, **When** render, **Then** hình được mã hóa lại (GPU ưu tiên) để gắn khung.
3. **Given** khung bật, nền ảnh, **When** render, **Then** khung phủ trên ảnh + hiệu ứng ở khung 1920×1080.
4. **Given** khung tắt, **Then** hành vi giống hệt bản trước (nền video không hiệu ứng vẫn stream copy).

### User Story 2 - Chọn kiểu và vị trí khung (Priority: P1)

Danh sách kiểu khung gồm 4 khung có sẵn + mọi file PNG người dùng thả vào thư mục khung; nút "Thư mục khung" mở thư mục đó.
"Vị trí" chọn khoảng cách khung tới mép: Sát mép / Cách mép vừa (3%) / Cách mép xa (6%).

**Acceptance Scenarios**:

1. **Given** thả `Frame_05_Abc.png` vào thư mục khung, **When** mở danh sách, **Then** thấy "05 · Abc".
2. **Given** tắt/mở app hoặc lưu/nạp preset, **Then** nhớ bật/tắt, file khung và vị trí.

### Edge Cases

- Bật khung nhưng file khung đã bị xóa → báo lỗi trước khi render: "Đã bật Khung camera nhưng không tìm thấy file khung…".
- Video nền khác 16:9 → khung được co theo đúng kích thước video (có thể hơi giãn), không cắt.
- Chia bài LAN → khung chưa áp dụng cho lô đó, ghi log cảnh báo (phiếu LAN không mang thông tin khung).
- PNG không trong suốt → che video; trách nhiệm người dùng khi tự thêm khung.

## Requirements *(mandatory)*

- **FR-001**: MUST có công tắc "Khung camera" (mặc định tắt) lưu ở `EnableCameraFrame`.
- **FR-002**: MUST có danh sách kiểu khung = PNG trong `<app>\Assets\CameraFrames`, `%LocalAppData%\RenderMusicPro\CameraFrames`
  (nơi giải 4 khung nhúng trong exe) và thư mục của khung đã lưu; đường dẫn khung chọn lưu ở `CameraFramePath`.
- **FR-003**: `CameraFramePosition` MUST là mức cách mép 0/1/2 (giá trị lạ kẹp về khoảng hợp lệ).
- **FR-004**: Khung MUST là lớp trên cùng của video (sau nền + hiệu ứng), không qua bước tách nền, áp cho nền video và nền ảnh.
- **FR-005**: Mã hóa khi có khung MUST dùng cùng chuỗi dự phòng GPU → CPU hiện có.
- **FR-006**: 4 khung có sẵn: 01 Tối giản (chỉ góc L trắng + dấu ngắm), 02 Streamer neon (góc neon cyan, huy hiệu TDN, nhãn LIVE),
  03 Máy quay (góc vàng, huy hiệu TDN, REC + chấm đỏ + pin, 1/500 · F4.0 · ISO 800, 4K · 30P), 04 Sang trọng vàng (viền đôi vàng,
  hoa văn kim cương, không chi tiết máy quay).
- **FR-007**: Khi khung tắt, lệnh ffmpeg của mọi chế độ MUST giữ nguyên như trước (constitution II).

## Success Criteria *(mandatory)*

- **SC-001**: Bật khung và render trong dưới 30 giây thao tác (3 lần bấm).
- **SC-002**: 100% video đầu ra khi bật khung có khung ở đúng vị trí, thời lượng hình = tiếng.
- **SC-003**: Thêm kiểu khung mới không cần sửa app (thả PNG vào thư mục).

## Assumptions

- "Position" được hiểu là khoảng cách khung tới mép. Lý do: khung là ảnh phủ toàn khung 16:9 (kể cả file giữ chỗ cũ
  480×270 cũng 16:9), nên "góc" không có nghĩa; chỉ số kiểu khung đã có `CameraFramePath`; còn lại hợp lý nhất là
  khung sát mép hay thụt vào để không bị nền tảng (YouTube/TikTok) che. Nếu người dùng muốn nghĩa khác, chỉ cần đổi
  bảng `FramePositionNames`/`CameraFrameLayout`.
- Huy hiệu logo dùng chữ "TDN" (thương hiệu TDN Media), chữ thông số là cố định.
- Khung được vẽ bằng System.Drawing (`specs/003-camera-frame/gen-camera-frames.ps1`), không dùng tài nguyên ngoài.

## Out of Scope

- Khung động (chấm REC nhấp nháy, đồng hồ chạy), logo ảnh thật của người dùng, chia bài LAN có khung.
