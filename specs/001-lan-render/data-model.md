# Data Model: Render nhiều máy qua LAN

Nguồn: `Services\LanRenderQueue.cs` dòng 17–81.

## LanJob (phiếu việc) — `_RenderLAN\jobs\pending\<JobId>.json`
| Trường | Ý nghĩa | Kiểm tra |
|---|---|---|
| Version | =1 | khác 1 → failed (ValidateJob) |
| JobId, BatchId | `<lô>_<6 số>` | KHÔNG tin nội dung; lấy theo tên file đã giành |
| Sender, Index | máy gửi, số thứ tự | Sender chỉ để hiển thị; máy gửi thật lấy từ BatchId |
| BaseName | `{số} - {playlist}` | IsSafeBaseName |
| PlaylistName | ghi vào txt | — |
| OnSeconds, OffSeconds, PlannedDuration | tham số nhạc | — |
| Tracks[] {Category, Path}, Clips[] {Path, Duration}, Effect? | đường dẫn tương đối share | Resolve không thoát share |
| EffectGreen, EffectBackgroundMode, FastStart | tham số render | — |

## LanMachineStatus — `_RenderLAN\machines\<máy>.json`
Machine, Accept, CurrentJob, AppVersion, Utc, Seq (tăng dần). Chỉ dùng làm dấu vân tay + hiển thị.

## LanDone — `out\<JobId>__<máy>\done.json`
JobId, Machine, Files[] {Name, Size}, FinishedUtc. Tên trong Files bị bỏ qua; chỉ Size của 3 tên tự tính được dùng để so.

## LanSettings — `<SettingsDirectory>\lan-render.json`
ShareRoot, Distribute, AcceptJobs.

## Trạng thái phiếu

```text
(máy gửi Enqueue) → pending
pending --TryClaim (File.Move nguyên tử)--> running\<id>__<máy>
running --ValidateJob sai / lỗi 2 lần--> failed (+ .error.txt ghi trước)
running --PublishResult ok--> (out\<id>__<máy>\done.json) → CompleteRunning (xóa running)
running --Dừng / lỗi ngoài dự kiến / RecoverAfterRestart--> pending (hoặc xóa nếu lô đã hủy)
running(máy khác) --không đổi > 3 phút (đồng hồ máy gửi), chưa có done.json--> pending (ReclaimStale)
pending của lô đã hủy --CancelBatch--> xóa
out\<id>__<máy> --CollectResult ok--> "đã nhận" → TryCleanupResult (done.json trước, rồi thư mục)
```

## Lô
`yyyyMMdd-HHmmss-<máy gửi>`; `cancelled\<lô>` = đã hủy; dọn sau 7 ngày (CleanupOwnOldBatches).
