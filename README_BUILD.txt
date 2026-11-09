Render Music Pro — Build phát hành trên Windows

Yêu cầu:
- .NET 8 SDK
- Inno Setup 6 (ISCC.exe)

Tạo release:
1. Xác nhận Services\AppVersion.cs và RenderMusicPro_Setup.iss đang cùng phiên bản.
2. Chạy PowerShell tại thư mục dự án:
   .\build-release.ps1
3. Mở thư mục Release_v<version>.

Tải lên GitHub Releases:
- Tag phải dùng định dạng v<version>, ví dụ v1.2.7.
- Tải RenderMusicPro.exe với đúng tên này để app nhận diện asset Windows.
- Tải RenderMusicPro_Setup_v<version>.exe để người dùng cài mới/cài đè.
- Có thể tải kèm RELEASE_NOTES.md và SHA256SUMS.txt.
- Gói ZIP chứa các tệp phát hành trên.

Ứng dụng kiểm tra GitHub, tải `RenderMusicPro.exe`, xác minh file, rồi thay EXE sau khi app đóng và tự khởi động lại. Nếu thư mục cài đặt cần quyền quản trị, Windows sẽ hiện hộp thoại UAC.
Người dùng các phiên bản trước 1.1.16 cần cài bộ cài 1.1.16 một lần; các bản sau đó có thể cập nhật tự động trong app.
Bộ cài Inno Setup cài chương trình vào Program Files và tạo shortcut Start Menu/Desktop.

Hiệu ứng (tùy chọn): chọn thư mục ảnh/video green screen. Để trống để bỏ qua.
Mỗi video đầu ra lấy một hiệu ứng, căn giữa và giữ kích thước gốc; phần ngoài khung bị cắt.
Hiệu ứng lặp đến hết video, âm thanh hiệu ứng bị bỏ. Ghép hiệu ứng cần mã hóa lại hình.

Cache lâu dài mặc định: D:\RenderMusicPro\EffectCache (tự tạo, không phụ thuộc thư mục cài app).
PreparedEffects chứa ảnh/video alpha đã tách. Metadata/media-v1.json chứa thời lượng/kích thước.
Cache được kiểm tra theo đường dẫn, dung lượng và thời gian sửa file gốc. Xóa cache sẽ tạo/đo lại khi cần.
Không có/không ghi được ổ D thì app hỏi vị trí khác, không tự chuyển sang ổ C.
Cache video alpha có thể lớn; chỉ tạo khi hiệu ứng thực sự được dùng.

Thời gian render: đồng hồ chạy theo Stopwatch, cập nhật giao diện 1 lần/giây.
ETA chỉ là ước tính, điều chỉnh theo tiến độ FFmpeg và các video đã hoàn thành; không đo media thêm.

Nền hiệu ứng: mặc định Tự nhận diện; có thể chọn tay Nền xanh hoặc Nền đen.
Nhận diện và cache alpha được lưu theo từng file trong cache ổ D. Nền đen giữ ánh sáng mờ.
