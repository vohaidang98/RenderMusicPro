# Kết quả test 1.5.1 (Tester)

## Lệnh và kết quả
- `cd D:\Code\RenderMusicPro\Tests; dotnet test` -> `Passed! - Failed: 0, Passed: 38, Skipped: 0, Total: 38`. Không có lỗi.
- `cd D:\Code\RenderMusicPro; dotnet build RenderMusicPro.csproj -c Release` -> 0 Warning, 0 Error (csproj đã có `Compile Remove="Tests\**"`).

## Cấu trúc
- `Tests\RenderMusicPro.Tests.csproj` (xunit 2.9.2, net8.0-windows), link `Services\LanRenderQueue.cs` và `Services\AppVersion.cs`. Không tham chiếu dự án chính.
- `Tests\LanQueueTests.cs`. Dùng share tạm trong `Path.GetTempPath()`. Hai máy giả lập: PCA (gửi) và PC_B (nhận), mỗi máy có đồng hồ giả riêng. Test tự dọn thư mục tạm.

## Phủ
- Giành phiếu: 2 máy cùng giành 1 phiếu, chỉ 1 được. ClaimNext lấy mỗi phiếu một lần. BaseName `..\evil` vào failed kèm .error.txt.
- Thu hồi: sau >3 phút không đổi thì thu hồi dù nhịp tim máy nhận vẫn đổi. Đối chứng: <3 phút thì chưa thu hồi. TouchRunning đều thì không thu hồi, với đồng hồ máy nhận lệch +1 và -1 ngày. Ngừng chạm thì bị thu hồi. mtime đặt +30 ngày mà không chạm thì vẫn bị thu hồi.
- RecoverAfterRestart: trả đúng phiếu của máy mình, không đụng phiếu máy khác.
- PublishResult: đẩy vào `out\<jobId>__PC_B\` kèm done.json. Trả false và không đẩy gì khi mất phiếu.
- CollectResult: chép đúng 3 tên. done.json có `..\..\x.exe` và `strange.exe` thì bị bỏ qua, không tạo file nào ngoài video/music. Sai kích thước, thiếu tên hoặc baseName không an toàn đều ném lỗi, không tạo file. TryCleanupResult thất bại (FileStream FileShare.None) trả false, dữ liệu đã chép còn nguyên; mở khóa rồi dọn lại thành công.
- Hủy lô: cờ cancelled (ghi riêng, pending còn nguyên) chặn ClaimNext và TryClaim. CancelBatch xóa pending. Máy gửi nhịp tim không đổi >2 phút thì không giành; ghi lại nhịp tim thì giành được. Máy gửi chưa có file nhịp tim thì không giành.
- Quy tắc thuần: IsSafeBaseName ("1 - Playlist" đạt; `..\x`, `a/b`, `C:x`, rỗng, null, `a..b` bị từ chối). IsShareFolderTooBroad (`D:\`, `C:\`, Windows, System32, Program Files, Program Files (x86), hồ sơ người dùng bị chặn; `D:\Share\TDN` được phép). BelongsToBatch không nhầm PC với PC_X.
- Chống xanh giả: (1) các đối chứng trước/sau mốc 3 phút và 2 phút ở trên; (2) `SnapshotHarnessDetectsStrayFiles_NotFalseGreen` chứng minh cách kiểm "không tạo file lạ" thật sự bắt được file x.exe khi nó xuất hiện; (3) cờ hủy được ghi trong khi pending còn nguyên nên chính cờ mới chặn giành.

## Lỗi sản phẩm phát hiện
Không có.

## Kiểm thủ công (không tự động hóa được UI)
Mục 10 (ưu tiên việc của mình):
1. Bật "nhận bài" ở máy B, máy A gửi lô. Khi B đang render giúp, nút Bắt đầu của B phải bấm được.
2. Bấm Bắt đầu ở B: phải hiện hộp "Máy đang render giúp máy {Sender}: {BaseName}. Dừng bài đang giúp để render việc của bạn? (Bài sẽ được trả về hàng chờ cho máy khác làm.)".
3. Chọn Không: bài đang giúp chạy tiếp, không đổi gì.
4. Chọn Có: bài giúp dừng, phiếu quay về `jobs\pending`, thư mục tạm bị xóa, rồi lượt render của B mới bắt đầu. Hai lượt không chồng nhau (xem CPU/ffmpeg chỉ một job).
5. Trong lúc B đang chờ/hỏi, B không được giành phiếu mới (kiểm log).

Lỗi 1 (chạy song song):
6. Bấm Bắt đầu ở máy nhận ngay khi tick sắp giành phiếu (có phiếu pending, đang ở hộp thoại preflight của StartRender). Không được có 2 render cùng lúc, không giành phiếu khi hộp thoại đang mở.
7. Khi đang giúp, bấm Dừng: phiếu về pending, `_cts` được dọn, không lỗi ở Dispose.
8. Lúc nhả phiếu, kiểm tra biến hiệu ứng được khôi phục: render thường sau đó dùng đúng hiệu ứng đã chọn.
