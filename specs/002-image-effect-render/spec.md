# Feature Specification: Chế độ render Ảnh + Hiệu ứng

**Feature Branch**: `002-image-effect-render`

**Created**: 2026-11-11

**Status**: Implemented (đã chốt các điểm làm rõ từ người dùng, xem mục Clarifications)

**Input**: User description: "sửa cho tôi app render music pro thêm 1 chế độ render ảnh + hiệu ứng. Ảnh thì
sẽ dùng 1 ảnh trong kho ảnh tôi đưa vào để hiệu ứng trong kho hiệu ứng lên trên ảnh tách nền."
Bổ sung sau: "ảnh trong kho ảnh KHÔNG tách nền, chỉ hiệu ứng được tách nền" và "hiệu ứng được chọn từ kho
hiệu ứng có sẵn đi kèm app HOẶC từ thư mục riêng của tôi".

## Clarifications

### Session 2026-11-11

- Q: Ảnh trong kho ảnh có cần tách nền không? → A: Không. Ảnh nền dùng nguyên bản; chỉ hiệu ứng được tách
  nền (xanh/đen) theo cơ chế tách nền hiệu ứng đã có.
- Q: Hiệu ứng lấy từ đâu? → A: Người dùng chọn một trong hai nguồn: (a) kho hiệu ứng có sẵn đi kèm app,
  (b) thư mục hiệu ứng riêng của người dùng (như hiện tại).
- Q: "Kho có sẵn" là gì? → A: Là các BỘ hiệu ứng: mỗi thư mục con của `Presets_Effects` là một bộ (bộ theo
  dòng nhạc thật của người dùng `01_CongGiao_HaoQuang` … `10_Kids_BongBong`, nằm ở
  `<thư mục app>\Assets\Presets_Effects`). Người dùng chọn MỘT bộ; mỗi video random một hiệu ứng trong bộ đó.
- Q: Bộ cơ bản? → A: Thêm một bộ chung `00_CoBan_Chung` gồm 8 loại: nhiễu film, sao lấp lánh, hoa rơi, bokeh
  đơn giản, bụi rơi, tuyết rơi, trái tim bay, đèn disco (clip mẫu tạm, đi kèm app).
- Q: Có tắt được hiệu ứng không? → A: Có công tắc "Bật hiệu ứng" cho cả nền video và nền ảnh; tắt = không ghép
  hiệu ứng (chỉ nền + nhạc).
- Q: Khung Camera (viewfinder/REC/ISO) → A: Tách thành tính năng tiếp theo (spec riêng), không nằm trong bản này.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Render playlist với nền là một ảnh tĩnh + hiệu ứng (Priority: P1)

Người dùng đã có kho ảnh (ảnh nghệ sĩ, ảnh bìa...). Ở thẻ "03 Video & hiệu ứng", người dùng đổi nguồn nền
từ "Kho video" sang "Kho ảnh", chọn thư mục kho ảnh, chọn thư mục hiệu ứng, rồi bấm Bắt đầu như bình thường.
Với mỗi playlist đầu ra, app lấy một ảnh từ kho ảnh làm nền suốt cả video, phủ một hiệu ứng (đã tách nền)
lên trên, ghép với nhạc ON/OFF như các chế độ hiện có. File nhạc mp3/txt đầu ra không thay đổi.

**Why this priority**: Đây là toàn bộ giá trị cốt lõi người dùng yêu cầu.

**Independent Test**: Chọn kho ảnh 3 ảnh, hiệu ứng thư mục riêng 2 hiệu ứng, render 3 playlist; mở từng mp4
thấy ảnh tĩnh làm nền, hiệu ứng chuyển động phủ lên, tiếng nhạc đúng độ dài, 3 video dùng 3 ảnh khác nhau.

**Acceptance Scenarios**:

1. **Given** nguồn nền = Kho ảnh và kho ảnh có N ≥ 1 ảnh hợp lệ, **When** render K playlist, **Then** mỗi
   video dùng đúng 1 ảnh làm nền cho toàn bộ thời lượng, ảnh được giữ tỉ lệ, đặt giữa khung 1920×1080
   (phần thừa là viền đen), và độ dài video bằng độ dài nhạc ON/OFF.
2. **Given** K ≤ N, **When** render K playlist, **Then** không ảnh nào bị dùng hai lần; khi K > N, ảnh chỉ
   lặp lại sau khi mọi ảnh đã được dùng (ưu tiên ảnh ít dùng).
3. **Given** có hiệu ứng nền xanh/đen và bật "Tách nền", **When** render, **Then** hiệu ứng được tách nền
   (dùng lại cache tách nền hiện có) và phủ giữa khung lên ảnh; ảnh nền KHÔNG bị tách nền.
4. **Given** máy có GPU NVIDIA, **When** render, **Then** ưu tiên mã hóa NVENC; nếu NVENC lỗi thì tự chuyển
   sang CPU và vẫn ra video.
5. **Given** nguồn nền = Kho ảnh và ô hiệu ứng thư mục riêng để trống, **When** render, **Then** video vẫn
   được tạo với ảnh tĩnh + nhạc (không hiệu ứng).
6. **Given** chế độ nhạc "Audio có sẵn", **When** nguồn nền = Kho ảnh, **Then** mỗi audio cũng nhận một ảnh
   nền + hiệu ứng như trên.

---

### User Story 2 - Chọn bộ hiệu ứng có sẵn hoặc thư mục riêng, bật/tắt hiệu ứng (Priority: P1)

Ở hàng "Hiệu ứng", người dùng chọn "Thư mục riêng" (như hiện nay) hoặc một bộ có sẵn, ví dụ
"Bộ 07 · Pop Ballad – Đám cưới" hay "Bộ 00 · Cơ bản – chung". Mỗi video random một hiệu ứng trong bộ đó.
Công tắc "Bật hiệu ứng" tắt hẳn việc ghép hiệu ứng. Áp dụng cho cả nền video lẫn nền ảnh.

**Why this priority**: Người dùng yêu cầu rõ ràng; dùng lại kho bộ hiệu ứng theo dòng nhạc đã có.

**Independent Test**: Chọn "Bộ 08 · Mùa đông – Giáng sinh", render 2 playlist → log "Bộ hiệu ứng: …", mỗi video có
hiệu ứng lấy từ thư mục bộ đó. Tắt "Bật hiệu ứng" → video không có hiệu ứng.

**Acceptance Scenarios**:

1. **Given** thư mục gốc các bộ có N thư mục con chứa hiệu ứng, **When** mở danh sách nguồn hiệu ứng, **Then**
   thấy "Thư mục riêng" + N bộ (tên thân thiện tiếng Việt) + bộ cơ bản đi kèm app.
2. **Given** người dùng thả thêm thư mục bộ mới (ví dụ `11_Den_Disco`) có file hiệu ứng, **When** mở lại danh
   sách, **Then** bộ mới hiện ra, không cần sửa app.
3. **Given** đã chọn một bộ, **When** render K video, **Then** mỗi video lấy 1 hiệu ứng trong bộ, cân bằng số lần dùng.
4. **Given** "Bật hiệu ứng" tắt, **When** render, **Then** không ghép hiệu ứng (nền video giữ stream copy như cũ;
   nền ảnh chỉ ảnh + nhạc).
5. **Given** nguồn = Thư mục riêng và bật hiệu ứng, **Then** hành vi giống hệt bản trước.
6. **Given** tắt/mở app hoặc lưu/nạp preset, **Then** nhớ nguồn nền, kho ảnh, bộ đã chọn, thư mục gốc các bộ,
   công tắc hiệu ứng.

---
### User Story 3 - Không ảnh hưởng chế độ cũ (Priority: P2)

Người dùng không đổi gì (nguồn nền = Kho video, hiệu ứng = Thư mục riêng) thì app chạy y như bản trước.

**Why this priority**: Nguyên tắc II của constitution — tính năng mới không được làm hỏng luồng cũ.

**Independent Test**: Với cấu hình cũ, render 2 playlist; log, file đầu ra và thời gian tương đương bản trước.

**Acceptance Scenarios**:

1. **Given** settings.json cũ không có các trường mới, **When** mở app, **Then** mặc định Kho video +
   Thư mục riêng, đúng hành vi cũ.

### Edge Cases

- Kho ảnh không tồn tại hoặc không có ảnh hỗ trợ → báo lỗi tiếng Việt trước khi chạy, không bắt đầu render.
- Nguồn nền = Kho ảnh nhưng kho video trống/không tồn tại → vẫn chạy (không đòi kho video).
- Ảnh hỏng/không đọc được → mục đó lỗi, cơ chế thử lại/bỏ qua theo mục hiện có xử lý; lô vẫn tiếp tục.
- Ảnh rất lớn (ví dụ 6000×4000) hoặc ảnh dọc → thu nhỏ giữ tỉ lệ vào khung 1920×1080, không méo.
- Ảnh PNG có vùng trong suốt → vùng trong suốt hiển thị đen (ảnh dùng nguyên bản, không tách nền).
- Bật Render LAN (chia bài) khi nguồn nền = Kho ảnh → báo trong log "chế độ ảnh chưa chia bài LAN" và render
  hết trên máy này (giống chế độ Audio có sẵn hiện nay).
- Bộ có sẵn + chia bài LAN với nền video → bộ phải nằm trong thư mục share (kiểm tra đường dẫn LAN hiện có báo lỗi nếu không).
- Bộ đã lưu nhưng thư mục gốc chưa sẵn sàng (ổ E: chưa cắm) → combo về "Thư mục riêng" nhưng vẫn nhớ tên bộ;
  khi thư mục có lại thì tự chọn lại.
- Không giải được bộ cơ bản ra đĩa → ghi app log; các bộ khác vẫn dùng được.
- Nhấn Dừng giữa lúc ghép → dừng ngay, không để lại file mp4 dở mang tên thật.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Thẻ "03 Video & hiệu ứng" MUST có lựa chọn nguồn nền: "Kho video" (mặc định) hoặc "Kho ảnh".
- **FR-002**: Khi chọn "Kho ảnh", MUST có ô đường dẫn + nút chọn thư mục kho ảnh, cùng kiểu với các ô thư mục
  hiện có; ô kho video ẩn đi (giá trị vẫn giữ).
- **FR-003**: Kho ảnh MUST nhận các định dạng ảnh .png, .jpg, .jpeg, .bmp, .webp, .tif, .tiff (quét cả thư mục con
  như kho hiệu ứng).
- **FR-004**: Mỗi video đầu ra MUST dùng đúng một ảnh; cách chọn MUST cân bằng số lần dùng và tránh lặp ảnh
  liền kề (cùng cơ chế "ít dùng trước" đang dùng cho nhạc/video/hiệu ứng).
- **FR-005**: Ảnh nền MUST được dùng nguyên bản (không tách nền), giữ tỉ lệ, căn giữa khung 1920×1080, 30 khung/giây,
  kéo dài đúng thời lượng nhạc ON/OFF.
- **FR-006**: Nếu có hiệu ứng, MUST phủ hiệu ứng lên ảnh theo đúng cách hiện có (căn giữa, giữ kích thước gốc,
  lặp đến hết video, tách nền xanh/đen theo tùy chọn "Tách nền" + "Tự nhận diện/Nền xanh/Nền đen" và cache).
- **FR-007**: Mã hóa MUST ưu tiên GPU NVIDIA và tự rơi về CPU khi GPU không dùng được, như ghép hiệu ứng hiện có.
- **FR-008**: MUST có lựa chọn nguồn hiệu ứng: "Thư mục riêng" (mặc định, hành vi cũ) hoặc MỘT bộ có sẵn; mỗi
  thư mục con (có ít nhất 1 file ảnh/video) của thư mục gốc các bộ là một bộ, tự hiện khi thêm thư mục.
- **FR-009**: Thư mục gốc các bộ: mặc định `<thư mục app>\Assets\Presets_Effects`, người dùng đổi được bằng nút 📁;
  ngoài ra luôn gộp bộ cơ bản `00_CoBan_Chung` nhúng trong exe (giải ra thư mục cấu hình, chỉ ghi lại khi khác,
  không xóa file người dùng thêm). Trùng tên bộ → ưu tiên thư mục người dùng chọn.
- **FR-010**: Các lựa chọn mới MUST được lưu trong cấu hình và preset.
- **FR-014**: MUST có công tắc "Bật hiệu ứng" (mặc định bật); tắt thì không ghép hiệu ứng ở mọi chế độ nền.
- **FR-011**: Mọi chữ hiển thị/log mới MUST bằng tiếng Việt có dấu, cùng giọng văn các log hiện có.
- **FR-012**: Khi nguồn nền = Kho ảnh và bật chia bài LAN, MUST render cục bộ và ghi log thông báo.
- **FR-013**: Khi không đổi lựa chọn mới, hành vi render MUST giống bản trước (constitution II).

### Key Entities

- **Kho ảnh**: thư mục người dùng chọn; chứa ảnh nền. Mỗi lượt render dựng một "bộ bài" ảnh để chia đều.
- **Nguồn hiệu ứng**: Thư mục riêng (đường dẫn người dùng) hoặc một Bộ hiệu ứng.
- **Bộ hiệu ứng**: thư mục con của thư mục gốc `Presets_Effects`; tên thư mục `NN_Ten_Bo`, hiển thị "NN · Tên thân thiện".
- **Bộ cơ bản `00_CoBan_Chung`**: 8 clip mẫu nhúng trong exe, giải ra `%LocalAppData%\RenderMusicPro\Presets_Effects`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Người dùng chuyển sang chế độ Ảnh + Hiệu ứng và bắt đầu render trong dưới 1 phút, tối đa 3 thao tác
  mới (chọn Kho ảnh, chọn thư mục, chọn nguồn hiệu ứng).
- **SC-002**: 100% video đầu ra ở chế độ ảnh có thời lượng hình khớp thời lượng tiếng (lệch ≤ 0,5 giây).
- **SC-003**: Với K playlist và N ảnh, số lần dùng của hai ảnh bất kỳ chênh nhau không quá 1.
- **SC-004**: Chọn "Bộ 00 · Cơ bản – chung" trên máy mới cài lần đầu render được ngay, không cần chuẩn bị thư mục hiệu ứng.
- **SC-005**: Cấu hình cũ (không đổi gì) cho kết quả và log giống bản trước.

## Assumptions

- "Ảnh tách nền" trong câu gốc mô tả hiệu ứng được tách nền phủ lên ảnh; người dùng đã xác nhận ảnh nền
  không tách nền.
- Khung đầu ra cố định 1920×1080 cho chế độ ảnh (cùng khung dự phòng hiện có của app), 30 khung/giây.
- Chọn ảnh dùng lại cơ chế "bộ bài" ít-dùng-trước sẵn có (không lặp tới khi dùng hết); ghép ảnh–hiệu ứng là
  độc lập ngẫu nhiên, không cần cặp cố định.
- Bộ theo dòng nhạc là dữ liệu thật của người dùng (bản Full: `E:\MUSIC\Tool\RenderMusicPro_Full_v15_Release\Assets\Presets_Effects`);
  không chép vào repo (bộ 06 nặng 67 MB), app đọc tại chỗ. Nếu settings cũ có thư mục riêng nằm trong
  `...\Presets_Effects\<bộ>` thì thư mục cha được dùng làm gốc các bộ.
- Bộ cơ bản chỉ là clip mẫu tạm 1920×1080 nền đen sinh bằng ffmpeg; nội dung thật thay sau bằng cách chép đè file
  cùng tên hoặc thêm file vào thư mục bộ.
- Vì bản phát hành là một file exe duy nhất (bộ cài + tự cập nhật chỉ chép exe), bộ cơ bản được nhúng
  trong exe và giải ra thư mục cấu hình của app.
- Bộ "đèn disco" riêng chưa có trên đĩa; đã có clip disco trong bộ cơ bản, thả thư mục `11_Den_Disco` là thành bộ riêng.
- Render LAN (chia bài nhiều máy) cho chế độ ảnh nằm ngoài phạm vi bản này, giống "Audio có sẵn".
- Lựa chọn nguồn hiệu ứng áp dụng cho cả chế độ nền video hiện có.

## Out of Scope

- **Khung Camera**: đã làm ở tính năng riêng `specs/003-camera-frame` (cùng nhánh).
- Chia bài LAN cho nền ảnh.