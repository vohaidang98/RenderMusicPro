PHAN QUYET: CAN SUA

# Đánh giá v1.5.1 (duyệt lại các lỗi Render LAN)

Reviewer chỉ đọc code. So sánh với backups\lan-fix-20261107-182542\*.bak.
- `dotnet build -c Release`: 0 cảnh báo, 0 lỗi. `Tests\ dotnet test`: 38/38 đạt.
- File đổi trong lượt này (theo mtime): MainForm.cs, MainForm.LanRender.cs, Services\LanRenderQueue.cs, Services\AppVersion.cs, csproj, .iss, Tests\*. Các file .cs khác giống hệt bản sao lưu. Mã nguồn không bị sửa sau lúc tester chạy (mã 18:31–18:33, Tests 18:36). Không ai sửa ngoài phạm vi. Dòng `Compile Remove Tests` trong csproj là của người điều phối, hợp lệ.

Chỉ còn 1 lỗi Vừa (mục V1), sửa rất rẻ. Các lỗi Nặng cũ đều đã sửa đúng.

## Bảng lỗi cũ

| # | Lỗi cũ | Trạng thái | Ghi chú |
|---|---|---|---|
| 1 | Máy nhận giành phiếu lúc đang bấm Bắt đầu, hai lượt chạy song song | ĐÃ SỬA | `_renderStarting` được bật ngay đầu StartRenderAsync. `LanCanClaim` kiểm tra đủ các cờ. `_lanWorkerBusy` được đặt trên luồng UI trước khi await. `_cts` chỉ bị xóa khi `ReferenceEquals`. Không tìm ra kịch bản nào chồng lượt. |
| 2 | Phiếu mồ côi của máy còn sống không được thu hồi | ĐÃ SỬA | Hàm ReclaimStale dùng Tracker theo đồng hồ của máy gửi, không xét nhịp tim. RecoverAfterRestart trả phiếu `running__<mình>` về hàng chờ. CreateWorkDirectory nay nằm trong try. |
| 3 | Thu kết quả kẹt hoặc đếm sai khi xóa thư mục lỗi | ĐÃ SỬA | Chép xong là tính đã nhận, việc dọn tách riêng và xóa done.json trước. Còn một điểm nhỏ, xem N1. |
| 4 | Tin tên file trong done.json | ĐÃ SỬA | Chỉ chép 3 tên do máy gửi tự tính, có test cho `..\..\x.exe`. |
| 5 | Tin BaseName trong phiếu | ĐÃ SỬA, NHƯNG QUÁ CHẶT | ValidateJob chạy trong TryClaim, phiếu sai vào failed. Quy tắc cấm mọi chuỗi `..` gây lỗi mới V1. |
| 6 | Máy gửi Dừng hoặc chết | ĐÃ SỬA | Ghi `cancelled` rồi mới xóa pending. Máy nhận hủy phiếu khi thấy lô đã hủy, và không giành phiếu khi máy gửi không còn nhịp tim. Rác được dọn ở lô sau. Còn vài điểm nhỏ, xem N2 và N3. |
| 7 | Hai máy cùng ghi out\<jobId> | ĐÃ SỬA | Mỗi máy ghi vào `out\<jobId>__<máy>` và kiểm tra HoldsClaim trước, giữa và trước khi ghi done.json. |
| 8 | `_lanTickBusy` mở khóa sai lúc | ĐÃ SỬA | Chỉ mở trong finally của tick. Phần render chạy tách bằng RunRemoteJobSafeAsync. |
| 9 | Bật chia sẻ: chèn lệnh qua dấu nháy, chia sẻ thư mục quá rộng | ĐÃ SỬA | Đường dẫn truyền bằng base64 UTF-8. IsShareFolderTooBroad được kiểm 2 lần. |
| 10 | Đồng hồ lệch giữa các máy | ĐÃ SỬA | TouchRunning cho mtime tăng dần, nhịp tim có `Seq`, và mọi so sánh đều dùng đồng hồ của máy quan sát. GetAliveMachines vẫn so mtime nhưng chỉ dùng để đếm máy phụ, không dùng để quyết định. |
| Nhẹ | .error.txt ghi trước; lọc lô chính xác; trả phiếu local khi Dừng; dọn publish dở; ValidateLanPaths; log chế độ 3 | ĐÃ SỬA | 3 mục Nhẹ còn lại (Directory.Exists UNC trên luồng UI, copy mp4 không hủy được, CompleteItem) chưa sửa nhưng đã ghi rõ trong thay-doi.md. Chấp nhận được. |

## Mục 10 mới: ưu tiên việc của mình
Đã kiểm tra, không tìm ra kịch bản nào chồng 2 lượt, kẹt chờ mãi, hoặc làm sai `_cts`/biến hiệu ứng:
- Suốt từ lúc hỏi đến hết lượt render của mình, `_renderStarting` luôn bật, nên tick không giành được phiếu mới (`LanCanClaim`). Bấm Bắt đầu lần nữa thì bị chặn ở dòng `if (_renderStarting) return;`.
- `await _lanWorkerTask` chỉ trả về sau finally của RunRemoteJobSafeAsync. Lúc đó `_cts` đã null (do ReferenceEquals), `_lanWorkerBusy=false`, biến hiệu ứng đã được khôi phục trong finally của RenderJobVideoAsync, và phiếu đã về pending. StartRenderCoreAsync sau đó tự đặt lại `_effectDeck`/`_runEffect*`. Thêm nữa, `_runFastStart=null`.
- Nếu tín hiệu hủy đến đúng lúc PublishResult đang chạy, Task.Run vẫn chạy hết nên kết quả được giữ, không mất. Nếu đến sau đó thì TaskCanceledException kích hoạt ReleaseToPending. Cả hai nhánh đều kết thúc ổn.
- Chọn Không thì không có gì thay đổi. Nếu phiếu tự xong trong lúc hộp thoại đang mở thì chạy luôn.
- Thời gian chờ có giới hạn: chờ giành phiếu tối đa 20 giây, chờ thêm 10 giây, rồi báo lỗi và trả false. Việc await task tự nó kết thúc khi ffmpeg bị kill hoặc lệnh IO hết thời gian chờ.

## Chế độ KHÔNG bật Render LAN
Hành vi giữ nguyên. Việc tách thành StartRenderCoreAsync không đổi thân hàm (diff chỉ thêm vỏ bọc và một dòng log ở chế độ 3 khi LAN bật). PreemptLanWorkerAsync trả true ngay, không có await. Dòng 844 vẫn là `_cts == null` vì `_lanWorkerBusy` luôn false. Nút Dừng và cách đóng app khi đang render (HandleFormClosing → StopRender → Close trong finally) không đổi. Đóng app trong lúc preflight vẫn như cũ. Khác biệt duy nhất (Nhẹ, xem N4): app lúc khởi động đụng vào share nếu đã từng lưu đường dẫn.

## Thương hiệu và phiên bản
1.5.1 khớp ở AppVersion (Current và GitHubTag), csproj (Version 1.5.1, FileVersion 1.5.1.0), .iss (MyAppVersion, MyAppBuildDir Release_v1.5.1). TDN Media có ở tiêu đề cửa sổ, Company/Copyright trong csproj, và MyAppPublisher + AppPublisher trong .iss. File .iss vẫn là UTF-8 không BOM như bản gốc, chuỗi "Chạy RenderMusicPro" còn nguyên.

## Test
Các test hàng chờ là test thật: có đối chứng trước và sau mốc 3 phút/2 phút, có giả lập lệch giờ ±1 ngày và mtime +30 ngày, và harness bắt được file lạ. Tất cả tiêu chí kế hoạch phần LanRenderQueue đều có test. Các mục 1, 8, 10 và "chế độ thường không đổi" nằm ở MainForm nên không có test tự động, chỉ có danh sách kiểm tay. Điều này chấp nhận được vì đã kiểm bằng cách đọc code ở trên. Lưu ý: test `SafeBaseName("a..b", false)` đang khóa cứng đúng quy tắc sai của V1, nên khi sửa V1 phải sửa cả test này.

## VẤN ĐỀ MỚI

### VỪA

#### V1. Tên playlist có ".." (vd "Nhạc Chill... Lofi") làm MỌI bài ở chế độ chia LAN bị lỗi, trong khi 1.5.0 vẫn chạy được
- File: Services\LanRenderQueue.cs:182 (`if (baseName.Contains("..")) return false;`), được gọi ở MainForm.LanRender.cs:746 (PrepareJobAudioAsync), LanRenderQueue.cs:276 (ValidateJob trong TryClaim) và :728 (CollectResult).
- Kịch bản: playlistName chỉ đi qua `SanitizeFileName` (MainForm.cs:3083), hàm này chỉ cắt dấu chấm ở cuối nên "…" ở giữa tên vẫn còn. BaseName thành `1 - Nhạc Chill... Lofi`. Hậu quả:
  - Máy nhận giành phiếu thì phiếu bị đẩy vào failed với lý do "Tên bài không hợp lệ".
  - Bài máy gửi tự làm ném InvalidDataException, sau 2 lần thử thì ghi lỗi.
  - Kết quả cả lô bằng 0. Chế độ thường vẫn render bình thường với cùng tên đó.
  - Dấu "..." trong tên playlist tiếng Việt rất hay gặp.
- Gợi ý: không cấm `..` ở giữa tên, vì tên đã không được chứa `\ / :` và ký tự cấm thì `..` không thể thoát khỏi thư mục. Chỉ cần chặn tên đúng bằng `.` hoặc `..`, hoặc so `baseName == SanitizeFileName(baseName)` như kế hoạch mục 4. Có thể cân nhắc bỏ giới hạn 200 ký tự (chế độ thường không giới hạn), hoặc kiểm tra tên ngay ở máy gửi trước khi lên phiếu để báo một lần thay vì lỗi từng bài. Sửa test `a..b` thành true và thêm case `"1 - Chill... Lofi"`.

### NHẸ
- N1. Services\LanRenderQueue.cs:756-765 + MainForm.LanRender.cs:572-585: TryCleanupResult xóa done.json trước. Nếu sau đó xóa thư mục thất bại, ListDone không còn liệt kê thư mục này (vì nó chỉ lấy thư mục có done.json), nên câu "thử lại mỗi vòng" trong thay-doi.md không đúng. Rác nằm lại đến khi CleanupOwnOldBatches dọn ở lô sau (khi lô đã hủy) hoặc sau 7 ngày. Không làm sai số đếm. Gợi ý: ở mỗi vòng, dọn thêm các thư mục trong `ListResultJobIds(batchId)` không còn trong outstanding.
- N2. MainForm.LanRender.cs:683-687 và 721-730: lúc Dừng, phiếu local được `ReleaseToPending` (đưa về pending) TRƯỚC `CancelBatch`. Trong khoảng hở đó máy nhận có thể giành lại phiếu, render vài giây rồi mới thấy cờ hủy. Chỉ phí công. Riêng dòng 686 còn gọi IO share đồng bộ trên luồng UI. Gợi ý: khi `finished < runIndices.Count` thì CancelBatch trước, rồi mới release (ReleaseToPending sẽ tự xóa phiếu khi lô đã hủy).
- N3. LanRenderQueue.cs:386 + LanChangeTracker.Observe: lần đầu thấy một khóa thì coi là "vừa đổi". Vì vậy máy nhận vừa mở app vẫn giành 1 phiếu của máy gửi đã chết từ lâu (nếu file nhịp tim còn), sau đó mới bỏ qua. Mỗi lần khởi động mất tối đa 1 bài. Gợi ý: lần đầu thấy khóa thì chưa giành, chờ thấy nhịp tim đổi ít nhất 1 lần (khoảng 5–10 giây).
- N4. MainForm.LanRender.cs:58: InitLan gọi RecoverLanAfterRestartAsync mỗi khi `ShareRoot` khác rỗng, kể cả khi đã tắt cả GỬI lẫn NHẬN. Kết quả là app vẫn tạo thư mục `_RenderLAN` và đụng share (UNC) lúc khởi động. Việc này chạy nền nên không treo UI, nhưng là thay đổi nhỏ khi LAN "tắt". Gợi ý: chỉ gọi khi `_lan.AcceptJobs || _lan.Distribute`.
- N5. Bản phục hồi phiếu dở dang có thể chạy song song: bản fire-and-forget của InitLan, bản của tick và bản của RenderLanBatchAsync (dòng 58, 156, 522). Nếu bản của InitLan chậm (share UNC/NAS) và người dùng bấm Bắt đầu ngay, bản cũ chạy `RecoverAfterRestart(includeOwnBatches)` có thể gọi `ListPending` đúng lúc lô MỚI đã có phiếu, và hủy luôn lô mới. Rất khó xảy ra. Gợi ý: giữ một `_lanRecoverTask` duy nhất và await nó trong RenderLanBatchAsync. Trong RecoverAfterRestart, bỏ qua các lô tạo sau thời điểm app khởi động.
- N6. MainForm.LanRender.cs:190: bấm Bắt đầu đúng lúc tick đang giành phiếu thì UI im lặng tối đa 20 giây, rồi vẫn hỏi "Dừng bài đang giúp?" cho một phiếu vừa giành xong. Sau khi bấm Có cũng không có dòng chữ "Đang dừng bài đang giúp..." trong lúc chờ. Gợi ý: hiện lblCurrent khi chờ. Nếu phiếu được giành trong lúc `_renderStarting` thì tự trả phiếu luôn, không hỏi.
- N7. MainForm.LanRender.cs:427: `icacls $sharePath`. Nếu người dùng gõ tay đường dẫn có dấu cách và kết thúc bằng `\` (vd `D:\My Share\`), PowerShell bọc nó là `"D:\My Share\"`, và dấu `\` cuối làm hỏng dấu nháy khi truyền cho chương trình ngoài. Hậu quả: icacls lỗi và app báo bật chia sẻ thất bại. Đây không phải lỗ bảo mật. Gợi ý: `TrimEnd('\\')` trước khi mã hóa base64.
- (Lỗi có sẵn từ 1.5.0, không phải hồi quy) Máy nhận bấm Dừng thì phiếu trả về pending, nhưng tick có thể giành lại đúng phiếu đó sau khoảng 5 giây vì vẫn đang bật nhận bài. Nên tạm ngưng nhận bài vài phút sau khi người dùng tự bấm Dừng.

## Đã kiểm tra, không thấy lỗi
- Không có đường nào để RunRemoteJobAsync bắt đầu khi `_renderStarting`/`_isRendering`/`_cts` đang bật. Từ LanCanClaim đến `_lanWorkerBusy=true` không có await xen giữa.
- ReclaimStale không thu hồi phiếu đã có done.json. Tracker được Prune đúng cách. Lỗi IO được ghi log rồi thử lại. Trường hợp lệch giờ ±1 ngày đều có test.
- Thứ tự trong PublishResult (HoldsClaim → copy .part → so dung lượng → HoldsClaim/lô hủy → done.json) đúng. Lỗi giữa chừng thì thư mục dở dang bị xóa.
- TryClaim không tin JobId/BatchId trong nội dung phiếu, và kiểm lô đã hủy cả trước lẫn sau khi giành.
- Chế độ thường không đổi: diff MainForm.cs chỉ gồm tiêu đề, dòng 844, vỏ StartRenderAsync và một dòng log ở chế độ 3 khi LAN bật.
