<!--
Sync Impact Report
- Version change: (template, chưa có) → 1.0.0
- Principles added: I. An toàn dữ liệu đầu ra; II. Không làm hỏng chế độ 1 máy;
  III. Không tin dữ liệu từ share; IV. UI không treo, luôn dừng được; V. Logic thuần kiểm chứng được;
  VI. Tiếng Việt và thương hiệu TDN Media
- Added sections: Ràng buộc kỹ thuật; Quy trình và cổng chất lượng; Governance
- Removed sections: none
- Templates: plan-template.md / spec-template.md / tasks-template.md đọc constitution lúc chạy,
  không cần sửa (✅ không đụng).
- Follow-up TODOs: none
-->
# Render Music Pro Constitution

## Core Principles

### I. An toàn dữ liệu đầu ra

- File đầu ra (mp4/mp3/txt) chỉ được ghi vào thư mục đầu ra người dùng đã chọn, qua file tạm
  (`.part`/temp) rồi đổi tên; không bao giờ để lại file dở dang mang tên thật.
- Một bài chỉ được tính "đã xong" khi đủ file và dung lượng khớp; dọn rác trên share là việc
  riêng, lỗi dọn KHÔNG được làm sai số đếm hay xóa kết quả đã nhận.
- Không xóa dữ liệu nguồn (kho nhạc, kho video, hiệu ứng) trong bất kỳ luồng nào.

Lý do: người dùng làm media hàng loạt; mất/hỏng file đầu ra là lỗi nặng nhất.

### II. Không làm hỏng chế độ 1 máy

- Khi KHÔNG bật Render LAN (không GỬI, không NHẬN), hành vi render MUST giống bản 1.4.1/1.5.0:
  cùng luồng, cùng file, cùng thông báo; không đụng share, không thêm hộp thoại.
- Tính năng mới MUST được bọc sau công tắc riêng và rẽ nhánh sớm; thân luồng render thường
  không được viết lại.

Lý do: đa số lượt dùng là 1 máy; tính năng mới không được trả giá bằng sự ổn định cũ.

### III. Không tin dữ liệu từ share

- Mọi thứ đọc từ thư mục share (phiếu việc, done.json, tên file, nhịp tim, mtime) là dữ liệu
  không tin cậy: MUST kiểm tra trước khi dùng (tên an toàn, đường dẫn không thoát khỏi share,
  phiên bản phiếu, dung lượng).
- Tên file đích MUST do chính máy ghi tự tính, không lấy từ dữ liệu share.
- Không so đồng hồ giữa các máy; phán đoán "còn sống/đã chết" dùng đồng hồ của máy quan sát.
- Lệnh chạy quyền quản trị MUST không ghép chuỗi do người dùng/share cung cấp (dùng base64).

Lý do: share LAN thường cấp Everyone:Modify; bất kỳ máy nào trong mạng cũng ghi được.

### IV. UI không treo, luôn dừng được

- IO mạng/đĩa và ffmpeg MUST chạy ngoài luồng UI (Task.Run/async); luồng UI chỉ cập nhật giao diện.
- Nút Dừng MUST luôn có tác dụng; không có vòng chờ vô hạn không thoát được.
- Không bao giờ có hai lượt render chạy chồng nhau trên một máy.

Lý do: app WinForms chạy hàng giờ; treo UI hoặc chồng lượt làm hỏng biến toàn cục và đầu ra.

### V. Logic thuần kiểm chứng được

- Quy tắc hàng chờ, thu hồi, kiểm tra tên/đường dẫn MUST nằm trong lớp Services không phụ
  thuộc MainForm, nhận đồng hồ qua tham số (`Func<DateTime>`), và có test xunit không cần ffmpeg.
- Phần không tự động hóa được (UI, ffmpeg) MUST có danh sách kiểm tay trong `.bangiao`.

### VI. Tiếng Việt và thương hiệu TDN Media

- Mọi chữ hiển thị cho người dùng (nút, log, hộp thoại, lỗi) bằng tiếng Việt có dấu, ngắn, rõ việc
  cần làm.
- App mang thương hiệu TDN Media: tiêu đề cửa sổ, Company/Copyright trong csproj, Publisher trong
  bộ cài. File `.iss` giữ UTF-8 không BOM.

## Ràng buộc kỹ thuật

- C# .NET 8 WinForms, chạy Windows 10/11; ffmpeg.exe bên ngoài.
- Phối hợp nhiều máy chỉ qua MỘT thư mục share (SMB), không mở cổng mạng, không server trung tâm.
- Giành phiếu bằng `File.Move(..., overwrite:false)` (nguyên tử trên NTFS/SMB).
- Phiên bản đồng bộ ở `Services\AppVersion.cs`, `RenderMusicPro.csproj`, `RenderMusicPro_Setup.iss`.

## Quy trình và cổng chất lượng

- Mỗi bản: kế hoạch (`.bangiao\ke-hoach.md`) → code → đánh giá đọc code → test → ghi `thay-doi.md`.
- Cổng: `dotnet build -c Release` 0 lỗi, không thêm cảnh báo; `Tests\ dotnet test` đạt hết.
- Sao lưu trước khi sửa vào `backups\`; dự án không dùng git.
- Không đưa lên `D:\Code\CAP_NHAT` hay tạo bộ cài khi chưa được người dùng duyệt.

## Governance

- Constitution này đứng trên các thói quen khác của dự án; mọi kế hoạch/đánh giá MUST đối chiếu
  6 nguyên tắc trên.
- Sửa đổi: ghi lý do vào Sync Impact Report, tăng phiên bản theo semver (MAJOR: bỏ/đổi nghĩa
  nguyên tắc; MINOR: thêm nguyên tắc/mục; PATCH: sửa chữ).
- Mỗi lượt đánh giá (Reviewer) kiểm tra tuân thủ và ghi vi phạm vào báo cáo đánh giá.

**Version**: 1.0.0 | **Ratified**: 2026-11-07 | **Last Amended**: 2026-11-07
