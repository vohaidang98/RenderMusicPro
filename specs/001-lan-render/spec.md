# Feature Specification: Render nhiều máy qua LAN (as-built 1.5.1)

**Feature Branch**: `001-lan-render` (dự án không dùng git; chỉ là tên thư mục spec)

**Created**: 2026-11-07

**Status**: As-built (đặc tả lại tính năng đã có trong Render Music Pro 1.5.1 để đối chiếu code)

**Input**: User description: "Render nhiều máy qua LAN: một thư mục share chứa music in, video in, out;
máy GỬI random nhạc/clip/hiệu ứng (giữ lịch sử ít dùng tại một chỗ), ghi phiếu việc với đường dẫn tương
đối theo gốc share, cấp số thứ tự playlist; máy NHẬN có công tắc cho phép nhận bài, chỉ thực thi phiếu,
copy nhạc về máy, đọc clip qua LAN, render cục bộ, đẩy kết quả lên thư mục trung gian; máy gửi tự copy
về thư mục đầu ra, kiểm đủ rồi mới xóa ở share; tự bật chia sẻ Windows khi chọn thư mục; chế độ Audio có
sẵn chưa chia LAN; không bật LAN thì như 1.4.1."

Nguồn sự thật: `.bangiao\ke-hoach.md` (quyết định 1–10), `.bangiao\danh-gia-lan.md`,
`.bangiao\danh-gia-1.5.1.md`, `.bangiao\thay-doi.md`, `.bangiao\ket-qua-test.md`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Chia một lô playlist cho nhiều máy (Priority: P1)

Người dùng ở máy chính (máy GỬI) chọn kho nhạc, kho video, hiệu ứng nằm trong thư mục share, bật
"chia bài", bấm Bắt đầu render N playlist. Máy gửi tự random nội dung từng bài, chia việc qua share,
tự render một phần, và gom đủ N bộ file (mp4 + mp3 + txt) về đúng thư mục đầu ra đã chọn, đánh số liên tục.

**Why this priority**: Đây là giá trị cốt lõi: rút ngắn thời gian render lô lớn bằng máy rảnh trong phòng.

**Independent Test**: 1 máy gửi + 1 máy nhận cùng share; lô 6 playlist; kiểm thư mục đầu ra có đủ 6 bộ,
tên `{số} - {tên playlist}`, không trùng số, share `out\` sạch sau khi xong.

**Acceptance Scenarios**:

1. **Given** máy gửi bật chia bài và một máy nhận đang rảnh, **When** bấm Bắt đầu lô 6 bài,
   **Then** cả hai máy cùng render, máy gửi nhận đủ 6 bộ file vào thư mục đầu ra và báo "máy khác N".
2. **Given** kho video nằm ngoài thư mục share, **When** bấm Bắt đầu, **Then** app dừng trước khi
   render và báo rõ thư mục nào không nằm trong share.
3. **Given** không có máy nhận nào, **When** bấm Bắt đầu, **Then** máy gửi tự render hết lô.
4. **Given** một số bài đã có file đầu ra, **When** bấm Bắt đầu, **Then** các bài đó bị bỏ qua như chế độ thường.

---

### User Story 2 - Máy rảnh nhận bài render giúp (Priority: P1)

Người dùng ở máy phụ bật "cho phép nhận bài", dán đường dẫn share. Khi máy rảnh, app tự nhận từng
phiếu, render cục bộ và đẩy kết quả lên share; không cần thao tác thêm.

**Why this priority**: Không có máy nhận thì không có tính năng.

**Independent Test**: Đặt sẵn 1 phiếu hợp lệ trên share, bật nhận bài ở máy phụ; trong vòng 10 giây máy
nhận giành phiếu, render, đẩy kết quả vào thư mục riêng theo máy, có done.json.

**Acceptance Scenarios**:

1. **Given** máy nhận rảnh và có phiếu chờ, **When** tick kế tiếp chạy, **Then** máy nhận giành đúng một phiếu.
2. **Given** phiếu có tên bài hoặc đường dẫn không an toàn, **When** máy nhận giành, **Then** phiếu bị
   chuyển sang lỗi kèm lý do, không render.
3. **Given** máy gửi đã hủy lô hoặc đã chết, **When** máy nhận quét, **Then** không giành phiếu của lô đó.
4. **Given** máy nhận đang render thì phiếu bị thu hồi hoặc lô bị hủy, **Then** máy nhận dừng/bỏ kết
   quả, không đẩy lên share.

---

### User Story 3 - Ưu tiên việc của mình (Priority: P2)

Người dùng ở máy đang render giúp muốn dùng máy cho việc riêng: bấm Bắt đầu, được hỏi có dừng bài
đang giúp không; chọn Có thì bài giúp trả về hàng chờ và lượt của mình chạy, không chồng hai lượt.

**Why this priority**: Không có thì người dùng tắt nhận bài, mất máy phụ.

**Independent Test**: Kiểm tay theo `.bangiao\ket-qua-test.md` mục 1–5.

**Acceptance Scenarios**:

1. **Given** máy đang giúp máy A bài X, **When** bấm Bắt đầu, **Then** hiện câu hỏi
   "Máy đang render giúp máy A: X. Dừng bài đang giúp để render việc của bạn? (Bài sẽ được trả về hàng chờ cho máy khác làm.)".
2. **When** chọn Không, **Then** bài giúp chạy tiếp, không đổi gì.
3. **When** chọn Có, **Then** bài giúp dừng, phiếu về hàng chờ, thư mục tạm bị xóa, rồi mới chạy lượt của mình.

---

### User Story 4 - Tự bật chia sẻ Windows (Priority: P2)

Ở máy gửi, chọn thư mục cục bộ chưa được chia sẻ: app hỏi, xin quyền quản trị, bật chia sẻ, mở tường
lửa, rồi đưa đường dẫn mạng (đã copy sẵn) để dán vào máy nhận.

**Independent Test**: Chọn `D:\Share\TDN` chưa share → đồng ý → nhận đường dẫn `\\MÁY\TDN`.
Chọn `D:\` → bị từ chối, không chạy lệnh.

**Acceptance Scenarios**:

1. **Given** thư mục chưa share, **When** chọn và đồng ý, **Then** thư mục được share và đường dẫn UNC được copy.
2. **Given** gốc ổ / Windows / Program Files / hồ sơ người dùng, **When** chọn, **Then** từ chối kèm lý do.

---

### User Story 5 - Lỗi, mất mạng, tắt máy giữa chừng không làm kẹt lô (Priority: P1)

Máy nhận crash/mất mạng, máy gửi bấm Dừng hoặc mất điện, file trên share bị khóa khi dọn: lô vẫn kết
thúc (đủ bài hoặc báo lỗi rõ), không chờ vô hạn, không đếm sai, không mất file đã nhận.

**Independent Test**: Test tự động `Tests\LanQueueTests.cs` (thu hồi, khởi động lại, dọn lỗi, hủy lô).

**Acceptance Scenarios**:

1. **Given** máy nhận ngừng chạm phiếu quá 3 phút, **Then** máy gửi trả phiếu về hàng chờ dù nhịp tim máy đó còn đổi.
2. **Given** máy nhận mở lại app, **Then** mọi phiếu đang làm dở của chính nó được trả về hàng chờ.
3. **Given** dọn thư mục kết quả trên share thất bại, **Then** bài vẫn được tính đã nhận, dữ liệu đầu ra còn nguyên.
4. **Given** máy gửi bấm Dừng, **Then** lô bị đánh dấu hủy, máy nhận không giành/không đẩy thêm.

---

### User Story 6 - Không bật LAN thì như cũ (Priority: P1)

Người dùng không dùng LAN thấy app hoạt động y như 1.4.1.

**Independent Test**: Tắt GỬI và NHẬN; render lô thường; so log/đầu ra với 1.4.1; không có truy cập share.

### Edge Cases

- Hai máy cùng giành một phiếu cùng lúc → chỉ một máy thắng.
- Máy nhận lệch giờ ±1 ngày hoặc mtime phiếu ở tương lai → không thu hồi nhầm phiếu đang chạm; phiếu không chạm vẫn bị thu hồi.
- Máy nhận crash rồi mở lại trong < 3 phút (nhịp tim lại sống) → phiếu cũ vẫn được trả về.
- done.json chứa tên `..\..\x.exe` hoặc tên lạ → bị bỏ qua, không file nào ngoài 3 tên hợp lệ được tạo.
- Tên playlist có "..." (vd "Nhạc Chill... Lofi") → hợp lệ, không bị từ chối.
- Tên máy là tiền tố của máy khác (`PC` và `PC_X`) → không nhầm lô.
- Bấm Bắt đầu đúng lúc máy nhận sắp giành phiếu / đang mở hộp thoại kiểm tra trước khi render → không chồng hai lượt.
- Máy gửi Dừng khi đang render phiếu của chính nó → phiếu không nằm lại ở running.
- Máy nhận bị thu hồi phiếu rồi có mạng lại → kết quả của nó bị bỏ, không ghi đè kết quả máy khác.
- Lô có máy gửi đã chết từ lâu nhưng file nhịp tim còn → máy nhận vừa mở app không nên giành bài.
- Đồng hồ lệch làm đếm "máy đang hoạt động" sai → chỉ ảnh hưởng số phiếu lên trước, không làm kẹt lô.
- Đường dẫn gõ tay có dấu cách và `\` cuối (`D:\My Share\`) → bật chia sẻ phải vẫn thành công.
- Share UNC tới máy đang tắt → UI không được treo.
- Chế độ "Audio có sẵn" khi đang bật chia bài → render cục bộ, ghi log báo không chia LAN.

## Requirements *(mandatory)*

### Functional Requirements

**Cài đặt và chia sẻ**

- **FR-001**: Hệ thống MUST cho cấu hình một thư mục share gốc và hai công tắc độc lập GỬI (chia bài) / NHẬN (cho phép nhận bài), lưu bền giữa các lần mở app.
- **FR-002**: Hệ thống MUST chặn bật GỬI hoặc NHẬN khi chưa chọn thư mục share, kèm thông báo.
- **FR-003**: Khi chọn (hoặc gõ) thư mục cục bộ chưa được chia sẻ, hệ thống MUST hỏi rồi tự bật chia sẻ Windows (share, quyền ghi, tường lửa) với quyền quản trị, và đưa đường dẫn mạng đã copy sẵn cho máy nhận.
- **FR-004**: Hệ thống MUST từ chối tự chia sẻ thư mục quá rộng (gốc ổ, Windows, Program Files, ProgramData, Users, hồ sơ người dùng, Desktop/Documents/Downloads gốc) mà không chạy lệnh nào.
- **FR-005**: Tên/đường dẫn truyền vào lệnh quản trị MUST không thể chèn lệnh (mọi ký tự, kể cả dấu nháy Unicode) và lệnh MUST thành công với mọi đường dẫn hợp lệ (có dấu cách, có `\` cuối).
- **FR-006**: Hộp thoại MUST có nút Kiểm tra cho biết ghi được vào share, các máy đang hoạt động (rảnh/đang render/không nhận) và số phiếu chờ.
- **FR-007**: Nút RENDER LAN MUST hiển thị trạng thái GỬI / NHẬN / GỬI+NHẬN.

**Máy gửi**

- **FR-010**: Khi chia bài, hệ thống MUST kiểm kho nhạc đang dùng, kho video và (nếu dùng) kho hiệu ứng nằm trong thư mục share; sai thì dừng trước khi render và nêu thư mục sai.
- **FR-011**: Máy gửi MUST tự random nhạc, clip, hiệu ứng (lịch sử "ít dùng" giữ tại máy gửi) và tự cấp tên bài `{số bắt đầu + i} - {tên playlist}`.
- **FR-012**: Phiếu việc MUST chỉ chứa đường dẫn tương đối theo gốc share, có số phiên bản, và đủ tham số để máy nhận tái tạo đúng bài (ON/OFF, hiệu ứng, chế độ nền, faststart, tên playlist, thời lượng clip).
- **FR-013**: Máy gửi MUST lên phiếu dần theo số máy nhận đang rảnh (không lên hết lô một lần) để lịch sử ít dùng vẫn đúng thứ tự.
- **FR-014**: Máy gửi MUST tự giành và render phiếu của chính lô mình, ghi thẳng ra thư mục đầu ra.
- **FR-015**: Khi thu kết quả máy khác, máy gửi MUST chỉ chép đúng 3 tên do nó tự tính (`{tên}.mp4` → thư mục video, `{tên}.mp3` và `{tên}.txt` → thư mục nhạc), bỏ qua mọi tên khác, so dung lượng với done.json, chép qua file tạm.
- **FR-016**: Chép xong và khớp dung lượng thì bài MUST được tính "đã nhận" ngay; dọn thư mục trên share là việc riêng, lỗi dọn MUST không đổi số đếm và MUST được thử lại cho tới khi sạch.
- **FR-017**: Máy gửi MUST ghi nhận bài lỗi do máy khác báo (kèm lý do) vào danh sách lỗi của lượt.
- **FR-018**: Máy gửi MUST trả về hàng chờ phiếu của máy khác không được chạm quá 3 phút (đo bằng đồng hồ máy gửi), bất kể nhịp tim máy đó, trừ khi kết quả đã đẩy xong; lỗi IO thì ghi log và thử lại vòng sau.
- **FR-019**: Nếu phiếu không còn ở đâu (chờ, đang làm, thư mục kết quả) qua 5 lần kiểm liên tiếp, máy gửi MUST ghi lỗi "mất phiếu" và kết thúc lô, không chờ vô hạn.
- **FR-020**: Khi máy gửi Dừng, hệ thống MUST ghi dấu hủy lô TRƯỚC khi trả/xóa bất kỳ phiếu nào của lô, và báo số bài đang render ở máy khác.
- **FR-021**: Máy gửi MUST phát nhịp tim suốt thời gian lô chạy.
- **FR-022**: Khi bắt đầu lô mới, máy gửi MUST dọn dấu hủy quá 7 ngày và thư mục kết quả của các lô cũ của chính nó đã hủy hoặc quá 7 ngày.
- **FR-023**: Khi mở lại app, máy gửi MUST hủy các lô cũ do chính nó gửi (còn phiếu chờ/đang làm), và việc này MUST không bao giờ hủy nhầm lô mới vừa bắt đầu.
- **FR-024**: Máy gửi MUST hiển thị tiến độ theo số bài xong, số bài máy khác làm, số lỗi, và ước tính thời gian còn lại hợp lý.

**Máy nhận**

- **FR-030**: Máy nhận MUST chỉ giành phiếu khi: bật nhận bài, license hợp lệ, có ffmpeg, không đang render, không đang khởi động render, không đang làm phiếu khác.
- **FR-031**: Mỗi phiếu MUST chỉ được một máy giành thành công.
- **FR-032**: Trước khi render, máy nhận MUST kiểm phiếu: phiên bản hỗ trợ, tên bài an toàn (không `\ / :`, ký tự cấm, không chỉ gồm dấu chấm; cho phép "..." ở giữa), mọi đường dẫn nằm trong share; sai thì chuyển phiếu sang lỗi kèm lý do.
- **FR-033**: Máy nhận MUST không giành phiếu của lô đã hủy, hoặc lô mà máy gửi không có nhịp tim thay đổi trong 2 phút gần nhất (đo bằng đồng hồ máy nhận, kể cả ngay sau khi máy nhận vừa mở app).
- **FR-034**: Máy nhận MUST copy nhạc về máy, đọc clip qua LAN, render cục bộ trong thư mục tạm theo đúng tham số của phiếu, rồi xóa thư mục tạm.
- **FR-035**: Máy nhận MUST đẩy kết quả vào thư mục riêng `out\<phiếu>__<máy>`, ghi done.json sau cùng, và kiểm còn giữ phiếu + lô chưa hủy trước, giữa và trước khi ghi done.json; mất phiếu thì bỏ kết quả và dọn phần đã đẩy.
- **FR-036**: Máy nhận MUST chạm phiếu đang làm và phát nhịp tim (có bộ đếm tăng dần) mỗi 5 giây.
- **FR-037**: Đang render mà lô bị hủy, hoặc phiếu mất 3 lần kiểm liên tiếp, máy nhận MUST dừng bài đó và không đẩy kết quả.
- **FR-038**: Lỗi render MUST thử lại 1 lần; lần 2 lỗi thì báo lỗi kèm lý do cho máy gửi; lỗi ngoài dự kiến thì trả phiếu về hàng chờ.
- **FR-039**: Khi mở app, bật nhận bài hoặc đổi share, máy nhận MUST trả về hàng chờ mọi phiếu đang làm dở của chính nó (thử lại nếu share chưa truy cập được).
- **FR-040**: Người dùng bấm Dừng khi đang giúp: phiếu MUST trả về hàng chờ, CTS/giao diện được dọn sạch.
- **FR-041**: Sau khi người dùng tự bấm Dừng bài đang giúp, máy nhận SHOULD không tự giành lại phiếu ngay (tạm ngưng nhận một thời gian). [NEEDS CLARIFICATION: tạm ngưng bao lâu — 2 phút, 10 phút, hay tắt hẳn nhận bài đến khi bật lại?]
- **FR-042**: Khi đang giúp, nút Bắt đầu MUST bấm được; bấm thì hỏi đúng câu ở US3; Có → hủy bài giúp, CHỜ dọn xong rồi mới chạy lượt của mình; Không → giữ nguyên.
- **FR-043**: Trong lúc chờ dừng bài đang giúp (hoặc chờ biết máy vừa giành phiếu hay không), giao diện MUST hiện trạng thái "Đang dừng bài đang giúp..." trong vòng 1 giây; phiếu vừa giành trong lúc người dùng đang bấm Bắt đầu SHOULD được trả lại mà không hỏi.

**Đồng thời và an toàn**

- **FR-050**: Trên một máy MUST không bao giờ có hai lượt render chạy chồng (kể cả trong lúc hộp thoại kiểm tra trước khi render đang mở).
- **FR-051**: Các vòng quét LAN (tick) MUST không chạy chồng nhau và không giành hai phiếu cùng lúc.
- **FR-052**: Sau khi xong/dừng phiếu LAN, trạng thái hiệu ứng/faststart của lượt render thường MUST được khôi phục đúng.
- **FR-053**: Phán đoán phiếu/máy còn sống MUST không so đồng hồ giữa các máy.
- **FR-054**: Kết quả nhận về SHOULD được kiểm tính toàn vẹn trước khi đưa vào thư mục đầu ra. [NEEDS CLARIFICATION: chỉ so dung lượng (như hiện tại) là đủ, hay cần kiểm thời lượng/ffprobe hoặc mã băm do máy nhận ghi?]
- **FR-055**: Quyền mạng của thư mục share SHOULD đủ hẹp để máy lạ trong LAN không sửa/xóa được kho nguồn. [NEEDS CLARIFICATION: giữ Everyone:Modify cho toàn share (hiện tại), hay chỉ cấp ghi cho `_RenderLAN` và `out`, còn music in / video in chỉ đọc?]

**Tương thích và trải nghiệm**

- **FR-060**: Khi không bật GỬI và NHẬN, app MUST hoạt động như 1.4.1 và MUST không truy cập thư mục share (kể cả lúc khởi động).
- **FR-061**: Chế độ "Audio có sẵn" MUST không chia LAN, render trên máy này và ghi log thông báo khi đang bật chia bài.
- **FR-062**: Mọi truy cập share/đĩa MUST chạy ngoài luồng UI; UI MUST không treo khi share không truy cập được.
- **FR-063**: Bấm Dừng MUST có tác dụng trong vòng vài giây, kể cả khi đang chép file kết quả lớn.
- **FR-064**: Mọi sự kiện LAN (nhận bài, thu hồi, lỗi, hủy, dọn) MUST được ghi log tiếng Việt; lỗi lặp lại MUST không spam log.
- **FR-065**: Bản 1.5.1 MUST mang thương hiệu TDN Media (tiêu đề cửa sổ, Company/Copyright, Publisher bộ cài) và phiên bản 1.5.1 khớp ở mọi nơi.

### Key Entities

- **Thư mục share**: gốc chứa `music in`, `video in`, `out`, `_RenderLAN\jobs\{pending,running,failed,cancelled}`, `_RenderLAN\machines`.
- **Lô (batch)**: một lần bấm Bắt đầu ở máy gửi; mã `yyyyMMdd-HHmmss-<máy gửi>`.
- **Phiếu việc (job)**: một playlist; mã `<lô>_<6 số>`; chứa tên bài, danh sách nhạc/clip/hiệu ứng (tương đối), tham số render, phiên bản.
- **Quyền giành phiếu (claim)**: file phiếu ở `running\<phiếu>__<máy>`; mtime được chạm định kỳ.
- **Nhịp tim máy**: `machines\<máy>.json` (nhận bài?, phiếu đang làm, phiên bản app, bộ đếm Seq).
- **Kết quả**: `out\<phiếu>__<máy>\` gồm mp4/mp3/txt + done.json (tên, dung lượng).
- **Dấu hủy lô**: `jobs\cancelled\<lô>`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Lô 20 playlist trên 3 máy tương đương xong trong ≤ 45% thời gian so với 1 máy.
- **SC-002**: 100% lô kết thúc (đủ bài hoặc có danh sách lỗi) mà không cần người dùng can thiệp, kể cả khi 1 máy nhận bị tắt ngang; thời gian phát hiện máy chết ≤ 4 phút.
- **SC-003**: 0 file được tạo ngoài thư mục đầu ra / thư mục tạm, với mọi nội dung phiếu hoặc done.json do bên thứ ba ghi.
- **SC-004**: 0 trường hợp hai lượt render chạy chồng trên một máy trong toàn bộ danh sách kiểm tay.
- **SC-005**: Số bài báo "đã tạo" luôn bằng số bộ file đủ 3 file trong thư mục đầu ra (sai lệch 0).
- **SC-006**: Khi không bật LAN, kết quả và log của lô mẫu giống 1.4.1 (ngoài tiêu đề/phiên bản), và không có truy cập share.
- **SC-007**: Từ lúc chọn Có ở câu hỏi ưu tiên đến lúc lượt của mình bắt đầu ≤ 15 giây; giao diện phản hồi ≤ 1 giây.
- **SC-008**: Máy nhận bắt đầu xử lý phiếu chờ trong ≤ 10 giây khi rảnh.
- **SC-009**: Thao tác giao diện không bị đứng quá 1 giây khi share bị mất kết nối.

## Phụ lục: thay đổi 1.5.2 → 1.5.10 (viết ngược lại từ code, 8/11/2026)

Các mục dưới đây KHÔNG có trong bản đặc tả gốc (viết cho 1.5.1); bổ sung để khớp code thật sau khi
người dùng yêu cầu tách 3 thư mục, thêm kho cố định, dò tìm UDP, tự khởi động cùng Windows.

### User Story 7 - Tách 3 thư mục share độc lập (Priority: P1, thay FR-001)

Thay vì MỘT thư mục gốc chứa cả music in/video in/out, hệ thống dùng BA thư mục độc lập: Nhạc vào,
Video vào, Ra. Kho nhạc đang dùng phải nằm trong Nhạc vào; kho video + hiệu ứng phải nằm trong Video
vào; Ra chỉ dùng trao đổi phiếu việc/kết quả (không cần chứa gì sẵn). Cấu hình `ShareRoot` một-thư-mục
cũ (≤1.5.1) tự chuyển thành cả 3 khi nạp, không phá cấu hình người dùng đã có.

- **FR-001b** (thay FR-001): Hệ thống MUST cho cấu hình RIÊNG BIỆT `MusicRoot`/`VideoRoot`/`OutRoot`,
  lưu bền trong `lan-render.json`; field `ShareRoot` cũ chỉ đọc 1 lần để tự chuyển sang 3 field mới.
- **FR-012b** (thay 1 phần FR-012): Đường dẫn trong phiếu việc MUST tương đối theo ĐÚNG gốc của nó —
  nhạc tương đối theo `MusicRoot`, clip/hiệu ứng tương đối theo `VideoRoot` (không còn một gốc chung).

### User Story 8 - Kho cố định trên ổ D cho máy GỬI (Priority: P1)

Máy chủ kho bấm 1 nút "Tạo/Dùng kho cố định trên ổ D": hệ thống tự tạo
`D:\Cache_Render_Music_Pro\{Music_In\{SIEU-HAY,HAY,Kho-Nhac,KHO}, Video_In, Out}`, tự chia sẻ cả 3
(SMB + quyền Everyone:Change + tường lửa "File and Printer Sharing"), tự điền 4 ô kho nhạc chính +
Nhạc vào/Video vào/Ra, tự tick "Máy GỬI: chia bài", tự Lưu. CHỈ bấm ở máy giữ nội dung thật; máy khác
muốn giúp render KHÔNG bấm nút này.

- **FR-070**: Hệ thống MUST tự tạo đủ cấu trúc thư mục cố định (4 kho nhạc + Video_In + Out) khi bấm nút này.
- **FR-071**: Hệ thống MUST tự chia sẻ cả 3 thư mục cố định với quyền đọc+ghi cho mạng LAN, không hỏi xác nhận riêng từng thư mục (chỉ còn UAC bắt buộc của Windows, không bỏ được).
- **FR-072**: Sau khi tạo+chia sẻ xong, hệ thống MUST tự bật "chia bài" và tự lưu cấu hình mà không cần thao tác Lưu tay thêm.

### User Story 9 - Máy NHẬN tự kết nối (Priority: P1)

Máy muốn giúp render bấm "Dò tìm máy chủ kho trên mạng" (ưu tiên) hoặc "Kết nối tới máy chủ kho" (gõ
tay tên máy, dự phòng khi dò tìm không ra). Tìm/đọc được đủ 3 thư mục của máy chủ kho thì hệ thống tự
tick "cho phép nhận bài" + "tự khởi động cùng Windows" + tự Lưu, không cần thao tác gì thêm.

- **FR-080**: Máy chủ kho (đã bật "chia bài") MUST tự phát quảng bá tên máy qua UDP broadcast (cổng 47991) mỗi nhịp tim (~5 giây) để máy khác dò được, không cần biết trước IP/tên máy.
- **FR-081**: Máy muốn nhận MUST có cách dò tìm tự động (lắng nghe UDP vài giây, liệt kê máy tìm thấy, cho chọn nếu nhiều máy) VÀ cách gõ tay tên máy làm dự phòng khi mạng chặn UDP broadcast.
- **FR-082**: Dò tìm/kết nối thành công (đọc được đủ 3 thư mục) MUST tự bật "cho phép nhận bài" + "tự khởi động cùng Windows" + tự lưu, không cần bấm Lưu tay.
- **FR-083**: Hệ thống MUST cho tự khởi động cùng Windows (ẩn/thu nhỏ, không giành focus) qua Task Scheduler mức người dùng (không cần quyền quản trị để BẬT/TẮT tính năng này, dù Windows vẫn có thể tự hỏi quyền mạng lần đầu nghe UDP).

**Lệch so với ràng buộc gốc trong `plan.md` ("không mở cổng mạng")**: FR-080/081 đưa UDP broadcast/listen
(cổng 47991) vào hệ thống — ĐÂY LÀ THAY ĐỔI RÀNG BUỘC so với bản 1.5.1, không phải lỗi, nhưng cần người
dùng biết: lần đầu bấm "Dò tìm" trên 1 máy, Windows có thể tự hỏi "Cho phép ứng dụng này giao tiếp qua
mạng?" (không phải UAC/quyền quản trị, chỉ là xác nhận tường lửa bình thường) — bấm Cho phép (Private
network) là xong, không cần hỏi lại. Gõ tay ("Kết nối tới máy chủ kho") không bị ảnh hưởng vì không mở
cổng nghe nào.

## Assumptions

- Mọi máy dùng cùng phiên bản app (phiếu phiên bản 1) và đều có license hợp lệ, ffmpeg riêng.
- Share là SMB trên Windows (NTFS) nên đổi tên file là nguyên tử; NAS khác chưa được cam kết.
- Kho video trong share đã được chuẩn hóa nên ghép bằng stream copy; khác biệt GPU giữa các máy chỉ ảnh hưởng bài có hiệu ứng (chấp nhận).
- Ngưỡng: chạm phiếu/nhịp tim 5 giây, thu hồi 3 phút, máy gửi chết 2 phút, dọn rác 7 ngày, "mất phiếu" 5 lần kiểm (~15 giây) — theo kế hoạch 1.5.1; quy tắc "bỏ qua lô có máy gửi chết quá 10 phút" gộp vào quy tắc 2 phút (thay-doi.md, Lệch kế hoạch).
- LAN tin cậy vừa phải (văn phòng/studio); không có mã hóa hay xác thực ngoài quyền Windows share.
- Chế độ "Audio có sẵn" ngoài phạm vi chia LAN ở bản này.
- Không cần giao diện xem/sửa hàng chờ; người dùng chỉ thấy log và tiến độ.
- Máy nhận dùng tham số mã hóa cố định trong app (mp3 q2, AAC 384k) nên đầu ra đồng nhất giữa các máy.
