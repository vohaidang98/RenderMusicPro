# Research: Chế độ render Ảnh + Hiệu ứng

## R1. Cách lặp ảnh tĩnh làm nền trong ffmpeg

- **Decision**: `-framerate 30 -i <ảnh>` + trong đồ thị: `scale=1920:1080:force_original_aspect_ratio=decrease:force_divisible_by=2,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,setsar=1,format=yuv420p,loop=loop=-1:size=1:start=0,setpts=N/(30*TB)`, giới hạn bằng `-t <thời lượng>`.
- **Rationale**: Giống tinh thần comment hiện có trong `RenderEffectVideoAsync` ("giữ khung hình đã giải mã trong RAM, không đọc lại PNG mỗi khung"): ảnh chỉ được giải mã và scale một lần, filter `loop` nhân khung trong RAM. Dùng `-framerate 30` như nhánh ảnh hiệu ứng hiện có. Đã thử thực tế (ffmpeg N-125365): NVENC và libx264 đều ra 1920×1080, 30 fps, 300 khung/10 s, tiếng khớp 10,000 s; ảnh dọc 1200×1600 được căn giữa có viền đen.
- **Alternatives**: `-loop 1 -i ảnh` (giải mã + scale lại mỗi khung, chậm hơn); dùng kích thước ảnh gốc (ảnh lẻ/không chuẩn làm hỏng NVENC và YouTube).

## R2. Tầng mã hóa

- **Decision**: Tách đoạn NVENC → CPU của `RenderEffectVideoAsync` thành `EncodeOverlayWithFallbackAsync` (giữ nguyên lệnh, cờ `_nvencAvailable`, `TryDelete`, log). Chế độ ảnh gọi thẳng hàm này. Tầng "giải mã nền CUDA" không áp dụng (không có video nền để giải mã).
- **Rationale**: Tránh nhân bản logic dự phòng; hành vi video+hiệu ứng không đổi.

## R3. Đóng gói kho hiệu ứng có sẵn

- **Decision**: Bộ = thư mục con của `Presets_Effects` (đúng cấu trúc bản Full người dùng đang dùng: `<app>\Assets\Presets_Effects\NN_Ten\*.mp4`, 1920×1080 nền đen h264 30 fps). Bộ thật đọc tại chỗ (không chép vào repo). Bộ cơ bản `00_CoBan_Chung` nhúng `Assets\Presets_Effects\**` làm `EmbeddedResource` (LogicalName `BuiltInEffects/<đường dẫn>`), lúc mở app `EffectPackLibrary.Extract` ghi ra `%LocalAppData%\RenderMusicPro\Presets_Effects`, chỉ ghi file thiếu/khác dung lượng, qua `.part` rồi đổi tên.
- **Rationale**: Bản phát hành là `PublishSingleFile`, bộ cài Inno chỉ chép `RenderMusicPro.exe`, tự cập nhật chỉ thay exe → file rời cạnh exe sẽ không tới máy người dùng. Thư mục `Assets` hiện chỉ chứa icon, không có cơ chế chép ra output. Nhúng là cách duy nhất đi cùng mọi kênh phát hành. ffmpeg cần đường dẫn file thật nên phải giải ra đĩa.
- **Alternatives**: `Content CopyToOutputDirectory` (không vào single-file/bộ cài/tự cập nhật); tải từ mạng (thêm phụ thuộc mạng).

## R4. Nội dung mẫu

- **Decision**: 8 clip 1920×1080, 8 s, 30 fps, nền đen, sinh bằng ffmpeg `geq` (script `specs/002-image-effect-render/gen-effects.ps1`), tên file theo kiểu bộ thật: film_grain, sparkle_stars, falling_petals, bokeh_simple, falling_dust, snow_fall, flying_hearts, disco_lights. Chuyển động có chu kỳ chia hết 8 s nên lặp gần như liền mạch. Nền đen → "Tự nhận diện" chọn tách nền đen (độ sáng thành alpha) đã có.
- **Rationale**: Người dùng chỉ cần cơ chế chạy thật; tuyển chọn nội dung thật là việc khác.

## R5. Render LAN

- **Decision**: Chế độ ảnh không chia LAN, ghi log và render cục bộ (như "Audio có sẵn").
- **Rationale**: Phiếu việc LAN chỉ có danh sách clip; thêm ảnh cần đổi định dạng phiếu + máy nhận → ngoài phạm vi, rủi ro constitution III.

## R6. Chọn ảnh

- **Decision**: `MusicPoolDeck.Take(1, rng)` — cân bằng số lần dùng, tránh lặp bài trước; không lặp tới khi dùng hết.
