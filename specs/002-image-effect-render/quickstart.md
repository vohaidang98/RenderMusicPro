# Quickstart / kiểm thử: Chế độ render Ảnh + Hiệu ứng

## Tự động

```powershell
dotnet build RenderMusicPro.csproj -c Release      # 0 lỗi, không thêm cảnh báo
dotnet test Tests\RenderMusicPro.Tests.csproj       # gồm BuiltInEffectLibraryTests
```

## Kiểm tay (cần ffmpeg, license)

Chuẩn bị: thư mục `KhoAnh` 3 ảnh (1 ngang .jpg, 1 dọc .png, 1 .webp), thư mục hiệu ứng riêng có 1 clip nền xanh.

1. **Nền ảnh + hiệu ứng riêng**: Nền = Kho ảnh → chọn `KhoAnh`; Hiệu ứng = Thư mục riêng; render 3 playlist.
   Kỳ vọng: 3 mp4 1920×1080, mỗi video một ảnh khác nhau, hiệu ứng đã tách nền phủ giữa khung, thời lượng
   hình = tiếng (ffprobe), log có "Ảnh nền: …", "mã hóa NVIDIA NVENC" hoặc "CPU".
2. **Bộ có sẵn**: mở combo Hiệu ứng → thấy "Bộ 00 · Cơ bản – chung" và (nếu gốc là thư mục Presets_Effects của bản Full)
   "Bộ 01 · Công giáo – Hào quang" … "Bộ 10 · Thiếu nhi – Bong bóng". Chọn "Bộ 00 · Cơ bản – chung", render 3 playlist.
   Kỳ vọng: `%LocalAppData%\RenderMusicPro\Presets_Effects\00_CoBan_Chung` có 8 clip; log "Bộ hiệu ứng: 00 · Cơ bản – chung
   · 8 hiệu ứng", "Nền hiệu ứng: đen (tự nhận diện)", mỗi video một hiệu ứng khác nhau. Tạo thư mục `11_Den_Disco` chứa 1 mp4
   trong thư mục gốc → mở lại combo thấy "Bộ 11 · Den Disco".
3. **Không hiệu ứng**: bỏ chọn "Bật hiệu ứng" (hoặc Thư mục riêng để trống) → nền ảnh: ảnh tĩnh + nhạc; nền video: stream copy như cũ.
4. **Audio có sẵn + nền ảnh**: mỗi audio ra 1 video nền ảnh.
5. **Lỗi**: kho ảnh không tồn tại → hộp lỗi trước khi chạy; kho ảnh rỗng → "Kho ảnh không có ảnh…".
6. **Dừng** giữa lúc ghép → dừng ngay, không có mp4 dở mang tên thật.
7. **Hồi quy**: Nền = Kho video + Thư mục riêng → kết quả/log như bản trước. Bật chia bài LAN + nền ảnh → log
   "Chế độ nền ảnh chưa chia bài LAN; render hết trên máy này."
8. Tắt/mở app, Lưu/Nạp preset → giữ Nền, Kho ảnh, Nguồn hiệu ứng.
