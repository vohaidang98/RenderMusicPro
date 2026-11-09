PHAN QUYET: CAN SUA

# Đánh giá v1.5.0 "Render nhiều máy qua LAN"

Reviewer chỉ đọc code, không sửa. So sánh với backups\lan-render-20261107-165110\*.cs.bak.
Phần diff trong MainForm.cs / MainForm.Features.cs / AppVersion / csproj / iss gọn và đúng (1.5.0 khớp ở cả 3 nơi).
Lỗi nằm chủ yếu ở MainForm.LanRender.cs và Services\LanRenderQueue.cs.

## NẶNG

### 1. Máy NHẬN giành phiếu trong lúc người dùng vừa bấm Bắt đầu render (hai lượt render chạy song song, hỏng biến toàn cục)
- File: MainForm.cs:1636-1666 (preflight chạy khi `_cts == null`, `_isRendering == false`), MainForm.LanRender.cs:77, 91, 699, 764-768, 664-681.
- Kịch bản: máy bật NHẬN (hoặc GỬI+NHẬN) đang rảnh. Người dùng bấm Bắt đầu: `RunPreflightChecksAsync` gọi `ProbeEncodersAsync` và có thể hiện MessageBox cảnh báo (hộp thoại modal, Timer WinForms vẫn chạy). Trong khoảng đó `LanTickAsync` thấy `!_isRendering && _cts == null` nên giành phiếu, `RunRemoteJobAsync` đặt `_cts` và `SetRunning(true)`. Preflight xong, StartRenderAsync ghi đè `_cts = new ...`. Hậu quả:
  - Hai lượt render chạy cùng lúc, dùng chung `_effectDeck`, `_runEffectGreen/Black/BackgroundMode`, `_runFastStart`, `_timingFinalOutput`, `_effectCanvasSize`, `_runDurationSnapshot`, `_renderEstimate`, progress.
  - `RenderJobVideoAsync` lưu `savedDeck` (null lúc rảnh) rồi khôi phục trong finally, nên các video còn lại của lượt render chính mất hiệu ứng.
  - Phiếu LAN kết thúc thì `_cts?.Dispose(); _cts = null; SetRunning(false)`: CTS của lượt chính bị dispose, nút Dừng không còn tác dụng (ObjectDisposedException bị nuốt), nút Bắt đầu sáng lại khi lượt chính vẫn chạy, nên có thể mở thêm lượt thứ ba.
- Gợi ý: thêm cờ `_startPending` hoặc đặt `_isRendering`/cờ "đang khởi động" ngay đầu StartRenderAsync, trước preflight. LanTick kiểm tra cờ này ngay trước khi gọi TryClaim. RunRemoteJobAsync chỉ dispose/xóa `_cts` khi `ReferenceEquals(_cts, myCts)`. Tốt nhất là tách CTS và trạng thái hiệu ứng của phiếu LAN ra biến riêng, không dùng chung.

### 2. Phiếu "mồ côi" của máy nhận vẫn sống không bao giờ được thu hồi, máy gửi chờ mãi
- File: Services\LanRenderQueue.cs:251-253 (`if (alive || fresh) continue;`), MainForm.LanRender.cs:558-577.
- Kịch bản: máy nhận đang render phiếu X thì app crash, bị tắt bằng Task Manager hoặc mất điện, rồi được mở lại trong vòng khoảng 3 phút (máy SSD khởi động lại nhanh, app tự chạy). Nhịp tim mới làm `alive = true`, nên ReclaimStale không trả X về hàng chờ. App mới không biết X và không chạm vào phiếu. Bên máy gửi `running > 0` nên `lostChecks` luôn bị đặt lại về 0, và vòng `while (finished < runIndices.Count)` chờ vô hạn. Cách duy nhất để thoát là bấm Dừng, khi đó mất các bài còn lại.
  - Cùng hậu quả khi `MarkFailed`/`CompleteRunning` lỗi mạng (bị nuốt bởi `catch {}`), hoặc khi `CreateWorkDirectory()` ở dòng 713 ném lỗi nằm ngoài try trong. Trong cả hai trường hợp phiếu nằm lại ở running__MÁY trong khi máy vẫn sống.
- Gợi ý: máy nhận còn sống luôn chạm phiếu của mình mỗi 5 giây, nên chỉ cần điều kiện "phiếu không được chạm quá 3 phút" để thu hồi, không cần kiểm tra nhịp tim. Hoặc thu hồi khi nhịp tim của máy đó có `CurrentJob != jobId`. Lúc khởi động, máy nhận nên trả về hàng chờ mọi phiếu running__<chính nó>.

### 3. Thu thập kết quả có thể kẹt vòng lặp vô hạn hoặc đếm nhầm thành lỗi khi xóa thư mục trên share thất bại giữa chừng
- File: Services\LanRenderQueue.cs:327-347, MainForm.LanRender.cs:458-472, 562-573.
- Kịch bản: các file đã copy về đầu ra xong, sau đó `Directory.Delete(dir, true)` xóa lần lượt `1 - X.mp3` rồi gặp `1 - X.mp4` đang bị khóa (Defender quét file mới ghi, Explorer preview, máy khác đang mở). IOException được bắt ở dòng 469, nên `outstanding` vẫn giữ X. Lần lặp sau `done.json` vẫn còn nhưng mp3 đã mất, nên CollectResult ném "File kết quả trên share chưa đủ" ở mọi lần lặp sau. Kết quả là log báo lỗi mỗi 3 giây và lô không bao giờ kết thúc. Nếu ngược lại `done.json` bị xóa trước còn thư mục rỗng xóa lỗi, thì sau 15 giây X bị ghi "Mất phiếu việc" dù file đã nằm trong đầu ra.
- Gợi ý: tách việc copy và việc dọn. Copy và kiểm tra dung lượng xong thì coi như đã nhận: gọi `outstanding.Remove` và tăng `finished`. Sau đó đổi tên `done.json` thành `collected` (hoặc xóa nó trước tiên), rồi dọn thư mục theo kiểu cố gắng hết sức. Lần sau gặp thư mục không còn done.json thì chỉ dọn.

### 4. Bảo mật: máy gửi tin tên file trong done.json, có thể bị ghi file tùy ý (chạy code)
- File: MainForm.LanRender.cs:460-462, Services\LanRenderQueue.cs:331-344.
- Kịch bản: share được cấp Everyone:Modify (dòng 317-318), nên bất kỳ máy hoặc người dùng nào trong LAN cũng ghi được `out\<jobId>\done.json` (jobId đọc được từ jobs\pending). Đặt `Name = "..\\..\\AppData\\Roaming\\Microsoft\\Windows\\Start Menu\\Programs\\Startup\\a.exe"`, chép kèm file có đúng dung lượng khai báo. `Path.Combine(musicOutput, file)` thoát ra khỏi thư mục đầu ra, và máy gửi tự copy file đó vào Startup.
- Gợi ý: chỉ chấp nhận đúng 3 tên `job.BaseName + .mp4/.mp3/.txt` mà máy gửi tự tính ra (không lấy tên từ done.json), hoặc ít nhất kiểm tra `Path.GetFileName(name) == name` và đuôi file nằm trong danh sách cho phép.

## VỪA

### 5. Máy nhận tin BaseName trong phiếu việc, có thể ghi file ra ngoài thư mục làm việc
- File: MainForm.LanRender.cs:610-611, 720-722, 686-687.
- Kịch bản: phiếu do bất kỳ ai ghi vào pending với `BaseName = "..\\..\\..\\Users\\Public\\Desktop\\x"` hoặc một đường dẫn tuyệt đối. Khi đó mp3/txt/mp4 bị ghi ra ngoài thư mục tạm của máy nhận. Nội dung txt chứa PlaylistName và tên bài do kẻ tấn công chọn.
- Gợi ý: trên máy nhận chạy `SanitizeFileName(job.BaseName)` và từ chối phiếu nếu kết quả khác giá trị gốc. Nên kiểm tra `job.Version` luôn.

### 6. Máy gửi bấm Dừng hoặc bị crash: các máy nhận vẫn render tiếp phiếu "chết" và rác dồn lên share\out
- File: MainForm.LanRender.cs:591-598 (chỉ xóa pending), :95-104 (máy nhận giành mọi phiếu của mọi lô).
- Kịch bản 1: máy gửi Dừng. Những phiếu đang chạy ở máy khác vẫn render đến hết (tốn hàng chục phút), rồi đẩy kết quả vào `out\<batchId>_xxx`. Lô đó không còn ai thu, và lô mới có batchId khác nên các thư mục này nằm lại mãi.
- Kịch bản 2: máy gửi mất điện. Pending còn nguyên, không ai xóa, và mọi máy nhận cứ render hết lô cho một máy gửi đã chết, làm đầy ổ share.
- Gợi ý: khi Dừng, ghi file `_RenderLAN\cancel\<batchId>`. Trong tick, máy nhận thấy file này thì hủy phiếu đang chạy. Máy nhận chỉ giành phiếu khi nhịp tim của `job.Sender` còn sống (máy gửi có nhịp tim suốt lô vì `_lanBatchRunning`). Máy gửi lúc khởi động nên dọn `out\*` và pending của các lô cũ do chính nó tạo.

### 7. Máy nhận sống lại sau khi bị thu hồi, hai máy cùng ghi out\<jobId>
- File: Services\LanRenderQueue.cs:289-307, :212-215.
- Kịch bản: máy nhận A mất share hơn 3 phút (wifi chập chờn) nhưng vẫn render tiếp phần đã đọc được. Máy gửi thu hồi phiếu và máy B giành lại. A có mạng lại thì `TouchRunning` lỗi bị nuốt, A vẫn `PublishResult` vào cùng `out\<jobId>`. Nếu máy gửi đang thu kết quả của B và `Directory.Delete`, thư mục bị A tạo lại cùng done.json mới. Thư mục này không còn trong `outstanding` nên thành rác vĩnh viễn, và nếu xen kẽ đúng lúc thì CollectResult gặp file bị A ghi đè.
- Gợi ý: trước khi publish và trong lúc render, máy nhận kiểm tra `File.Exists(RunningPath(jobId))`, mất phiếu thì bỏ. Đẩy kết quả vào `out\<jobId>__<máy>\`, hoặc khi publish dùng `Directory.CreateDirectory` kèm kiểm tra done.json đã có thì dừng.

### 8. Cờ `_lanTickBusy` bị mở khóa sai lúc, hai tick chồng nhau có thể giành 2 phiếu cùng lúc
- File: MainForm.LanRender.cs:109 và 117.
- Kịch bản: tick A chạy phiếu, đã đặt `_lanTickBusy=false`. Tick B đang chờ heartbeat hoặc liệt kê pending trên share chậm. Phiếu của A xong, và `finally` của A đặt `_lanTickBusy=false` trong khi B vẫn đang chạy. Tick C vào, nên B và C cùng thấy `_lanWorkerBusy == false` và mỗi tick giành một phiếu. Hai `RunRemoteJobAsync` chạy song song, cùng hậu quả như mục 1. Phiếu của tick chạy trước không còn được TouchRunning vì `_lanCurrentJob` bị ghi đè.
- Gợi ý: đặt `_lanWorkerBusy = true` ngay trong tick trước khi `await` TryClaim, và chỉ tick nào còn giữ khóa mới được mở `_lanTickBusy` (dùng biến cục bộ `releasedEarly`).

### 9. Tự bật chia sẻ: chèn ký tự vào PowerShell elevated qua dấu nháy Unicode, và không chặn thư mục quá rộng
- File: MainForm.LanRender.cs:311, 317-318.
- Kịch bản a: PowerShell coi ‘ ’ ‚ ‛ (U+2018 đến U+201B) cũng là dấu nháy đơn. Thư mục tên `Tú’s share` (dấu nháy "thông minh" rất hay gặp khi dán tên) làm vỡ chuỗi, nên lệnh lỗi hoặc phần còn lại của tên chạy thành lệnh với quyền admin. `Ps()` chỉ nhân đôi dấu `'` ASCII.
- Kịch bản b: người dùng chọn `D:\` hoặc `C:\Users\<tên>`. App share cho Everyone quyền Change và chạy `icacls ... (OI)(CI)M` lan xuống toàn bộ cây thư mục: chạy rất lâu và cấp quyền sửa/xóa cho cả mạng trên toàn ổ đĩa. Không có cảnh báo và không có cách hoàn tác.
- Gợi ý: truyền đường dẫn vào script qua base64 (`[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('...'))`) thay vì ghép chuỗi. Từ chối gốc ổ, thư mục hệ thống và thư mục hồ sơ người dùng. Nên tạo riêng thư mục con làm share. Cân nhắc chỉ cấp quyền ghi cho `_RenderLAN` và `out`, còn kho nhạc/video chỉ cấp quyền đọc.

### 10. Đồng hồ lệch giữa các máy làm sai phán đoán "phiếu còn tươi"
- File: Services\LanRenderQueue.cs:199, 214 (`SetLastWriteTimeUtc(DateTime.UtcNow)` theo đồng hồ máy NHẬN), :252 (so với `DateTime.UtcNow` của máy GỬI).
- Kịch bản: máy nhận chậm giờ hơn 3 phút (máy không đồng bộ NTP, hay gặp trong LAN không có Internet). Phiếu vừa giành đã bị coi là cũ, nên chỉ còn điều kiện nhịp tim bảo vệ. Nếu sửa mục 2 theo hướng chỉ xét độ tươi của phiếu thì sẽ thu hồi nhầm phiếu của máy đang sống. Nếu máy nhận nhanh giờ, phiếu của máy chết không bao giờ bị coi là cũ.
- Gợi ý: đừng đặt mtime theo đồng hồ máy khách. Hãy ghi lại nội dung file (thêm bộ đếm) để server share tự đặt thời gian, hoặc so sánh với mtime của một file mốc do chính máy gửi vừa ghi lên share.

## NHẸ

- MainForm.LanRender.cs:530-548: máy gửi bấm Dừng trong lúc đang render phiếu của chính mình. OCE ném ra qua RunItemWithRetryAsync nên bỏ qua `CompleteRunning`, và phiếu đã được lấy ra khỏi `local` nên finally không trả lại. Kết quả là `running\<jobId>__MÁYGỬI.json` nằm lại vĩnh viễn (chỉ là rác, không gây kẹt).
- Services\LanRenderQueue.cs:233-235 và 264-276: MoveRunningTo chuyển .json trước rồi mới ghi .error.txt. Nếu TakeFailed đọc vào giữa hai bước thì lý do thành "Lỗi không rõ" và còn sót file .error.txt mồ côi. Nên ghi .error.txt trước.
- Services\LanRenderQueue.cs:289-307: publish lỗi ở lần 2 thì `out\<jobId>` chứa file .part hoặc file dở dang, và không ai dọn.
- MainForm.LanRender.cs:190, 244, 281, 314: bộ lọc `batchId + "_*"`. Nếu tên máy là `PC` và có máy khác tên `PC_X` tạo lô trong cùng giây, lô của PC sẽ bị nhầm sang thu hồi, xóa hoặc lấy lỗi phiếu của PC_X. Nên dùng tách chính xác (`jobId[..^7] == batchId`).
- MainForm.LanRender.cs:151, 261: `Directory.Exists` chạy trên luồng UI với đường dẫn UNC tới máy đang tắt, có thể treo UI 20-30 giây.
- MainForm.LanRender.cs:460: `Task.Run(..., token)` không hủy được thao tác copy mp4 lớn đang chạy, nên bấm Dừng phải chờ copy xong.
- MainForm.cs:1709-1721: chế độ 3 (audio có sẵn) bỏ qua LAN mà không báo gì. ValidateLanPaths (LanRender.cs:415) bắt kho hiệu ứng phải nằm trong share kể cả khi không dùng hiệu ứng (`_effectDeck == null`).
- MainForm.LanRender.cs:465: `CompleteItem` được gọi cho bài của máy khác mà không có `BeginItem`, nên ước tính thời gian còn lại bị lệch.

## Đã kiểm tra, không thấy lỗi
- Giành phiếu bằng `File.Move(..., false)`: nguyên tử trên NTFS/SMB, nên không có hai máy cùng giành thành công một phiếu ở thời điểm claim.
- Ghi done.json sau cùng và kiểm tra dung lượng ở cả hai chiều: máy gửi không copy file đang ghi dở.
- `Resolve()` chặn được `..`, đường dẫn tuyệt đối và `C:foo` thoát khỏi share.
- Thời lượng: clip được chọn dư 3 giây và máy nhận lặp clip nếu nhạc dài hơn dự tính. `-t` cắt đúng theo nhạc.
- Biến hiệu ứng giữa lúc lên phiếu và lúc render trên máy gửi: chạy tuần tự, có lưu và khôi phục, nên không chồng nhau (trừ các trường hợp ở mục 1 và mục 8).
- Nút RENDER LAN nằm trong outputCard, bị khóa khi đang render. Phiên bản 1.5.0 khớp ở AppVersion, csproj và iss.
