# Contract: Giao thức file trên thư mục share (phiên bản phiếu 1)

Hợp đồng giữa các bản app trên các máy. Đổi bất kỳ mục nào → tăng `LanJob.CurrentVersion`.

| Đường dẫn (tương đối gốc share) | Ai ghi | Ai đọc | Quy tắc |
|---|---|---|---|
| `_RenderLAN\jobs\pending\<lô>_<NNNNNN>.json` | máy gửi (ghi tạm + Move) | mọi máy nhận | đường dẫn bên trong tương đối; thứ tự giành = theo tên |
| `_RenderLAN\jobs\running\<phiếu>__<máy>.json` | máy giành (Move từ pending) | máy gửi (ReclaimStale) | chủ chạm mtime mỗi 5s, mtime tăng dần |
| `_RenderLAN\jobs\failed\<phiếu>.json` + `.error.txt` | máy nhận | máy gửi (TakeFailed, xóa sau đọc) | .error.txt ghi TRƯỚC .json |
| `_RenderLAN\jobs\cancelled\<lô>` | máy gửi | mọi máy | có file = lô hủy; ghi trước khi xóa pending |
| `_RenderLAN\machines\<máy>.json` | mỗi máy, mỗi 5s | mọi máy | Seq tăng dần; không so Utc giữa máy |
| `out\<phiếu>__<máy>\{BaseName}.mp4/.mp3/.txt` | máy nhận (qua `.part`) | máy gửi | máy gửi chỉ đọc 3 tên tự tính |
| `out\<phiếu>__<máy>\done.json` | máy nhận, SAU CÙNG | máy gửi | thiếu done.json = chưa xong |

Ngưỡng: chạm/nhịp tim 5s; thu hồi 3 phút; máy gửi sống 2 phút; đếm máy hiển thị 45s; dọn 7 ngày.
Ràng buộc tên: `<máy>` đã làm sạch ký tự cấm và `__`; BaseName theo IsSafeBaseName.
