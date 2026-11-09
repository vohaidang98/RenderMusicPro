# Research: Render nhiều máy qua LAN (as-built 1.5.1)

Các quyết định đã chốt trong `.bangiao\ke-hoach.md`; ghi lại dạng Decision/Rationale/Alternatives.

## R1. Kênh phối hợp
- **Decision**: chỉ dùng một thư mục SMB share; không server, không cổng mạng.
- **Rationale**: người dùng đã có share; không đụng tường lửa ngoài nhóm "Chia sẻ tệp và máy in".
- **Alternatives**: HTTP/TCP giữa các máy (phải mở cổng, khám phá máy); dịch vụ đám mây (phụ thuộc Internet).

## R2. Giành phiếu
- **Decision**: `File.Move(pending, running\<phiếu>__<máy>, overwrite:false)`.
- **Rationale**: nguyên tử trên NTFS/SMB; tên file mang luôn chủ sở hữu.
- **Alternatives**: file khóa riêng (2 bước, có cửa sổ tranh chấp).

## R3. Phát hiện chết không phụ thuộc đồng hồ máy khác
- **Decision**: `LanChangeTracker` ghi "lần cuối thấy dấu vân tay đổi" theo đồng hồ máy quan sát; phiếu: mtime+size, chạm mỗi 5s với mtime tăng dần; nhịp tim: mtime+size+Seq+Utc. Thu hồi phiếu > 3 phút không đổi; máy gửi chết > 2 phút.
- **Rationale**: đồng hồ LAN không NTP lệch vài phút; so trực tiếp mtime với giờ máy mình sai (danh-gia-lan mục 10).
- **Alternatives**: so mtime với UtcNow (sai khi lệch giờ); file mốc của máy gửi (phức tạp hơn).
- **Hệ quả đã biết**: lần đầu thấy một khóa được coi là "vừa đổi" → máy nhận mới mở app có thể giành 1 phiếu của máy gửi đã chết (N3).

## R4. Thu kết quả
- **Decision**: máy nhận đẩy vào `out\<phiếu>__<máy>\`, done.json sau cùng; máy gửi chỉ chép 3 tên tự tính, so dung lượng, tính đã nhận rồi dọn (xóa done.json trước).
- **Rationale**: chống ghi đè giữa 2 máy (mục 7), chống path traversal (mục 4), không kẹt khi dọn lỗi (mục 3).
- **Hệ quả đã biết**: xóa done.json xong mà xóa thư mục lỗi thì không còn ai thử dọn lại trong lô (N1).

## R5. Hủy lô
- **Decision**: file `jobs\cancelled\<lô>` ghi trước khi xóa pending; máy nhận kiểm trước/sau khi giành và mỗi tick khi đang render.
- **Alternatives**: chỉ xóa pending (máy nhận vẫn render tiếp — lỗi 1.5.0 mục 6).

## R6. Bật chia sẻ
- **Decision**: script PowerShell `-EncodedCommand`, tên/đường dẫn qua base64 UTF-8; Everyone theo SID; chặn thư mục quá rộng.
- **Hệ quả đã biết**: đường dẫn có dấu cách + `\` cuối làm icacls lỗi (N7); quyền Modify cho toàn share (kể cả kho nguồn) — chờ hỏi (FR-055).

## R7. Chống chồng lượt render
- **Decision**: cờ `_renderStarting` bật đầu StartRenderAsync; `LanCanClaim()` chặn khi bất kỳ cờ bận nào; `_lanWorkerBusy` đặt trên luồng UI trước await; `_cts` của phiếu chỉ giải phóng khi `ReferenceEquals`.

## R8. Ưu tiên việc của mình
- **Decision**: nút Bắt đầu bật khi đang giúp; hỏi Có/Không; Có → hủy CTS phiếu, `await _lanWorkerTask`, chờ thêm ≤ 10s, rồi mới StartRenderCoreAsync.

## Mục chưa giải quyết (chuyển thành [NEEDS CLARIFICATION] trong spec)
- FR-041 thời gian tạm ngưng nhận bài sau khi Dừng tay; FR-054 kiểm toàn vẹn kết quả; FR-055 phạm vi quyền ghi share.
  Theo yêu cầu lượt này: không hỏi người dùng; giả định tạm = giữ hành vi hiện tại.
