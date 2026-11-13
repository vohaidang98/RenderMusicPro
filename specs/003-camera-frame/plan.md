# Implementation Plan: Khung Camera phủ lên video

**Branch**: `002-image-effect-render` | **Date**: 2026-11-11 | **Spec**: [spec.md](./spec.md)

## Summary

Khung là PNG trong suốt, thêm làm input ffmpeg CUỐI CÙNG (`-framerate 30 -i khung.png`, sau nhạc nên chỉ số audio cũ
không đổi). Hàm `ApplyCameraFrame` đổi nhãn đầu ra `[outv]` của đồ thị hiện có thành `[preframe]` rồi nối
`[N:v:0]scale=W':H',format=rgba[cameraframe];[preframe][cameraframe]overlay=x:y:...repeatlast=1,format=yuv420p[outv]`.
Áp vào: `RenderEffectVideoAsync` (cả tầng CUDA và CPU), `RenderImageEffectVideoAsync`, và nhánh mới
`RenderCameraFrameVideoAsync` cho nền video không hiệu ứng (thay stream copy khi có khung). Mã hóa qua
`EncodeOverlayWithFallbackAsync`. Danh sách khung + tên hiển thị + bố cục cách mép nằm trong `Services\EffectPackLibrary`
(thuần, có test). 4 khung nhúng trong exe (`BuiltInFrames/…`) và giải ra `%LocalAppData%\RenderMusicPro\CameraFrames`
cùng lúc với bộ hiệu ứng cơ bản.

## Technical Context

**Language/Version**: C# / .NET 8 WinForms · **Dependencies**: ffmpeg (đã có) · **Testing**: xunit + kiểm tay
**Constraints**: khung tắt → lệnh ffmpeg y như trước; khung là lớp UI, không qua tách nền.

## Constitution Check

| Nguyên tắc | Đánh giá |
|---|---|
| I | Ghi qua file tạm như cũ; giải khung qua `.part`. ✅ |
| II | Mặc định tắt; `CameraFrameInputArgs()` rỗng và `ApplyCameraFrame` trả nguyên đồ thị khi tắt. ✅ |
| III | Không đọc dữ liệu share; LAN không gắn khung (log). ✅ |
| IV | Giải file nền `Task.Run`; ffmpeg async có Dừng. ✅ |
| V | `ListCameraFrames`, `FrameDisplayName`, `CameraFrameLayout` có test xunit. ✅ |
| VI | Chữ tiếng Việt; huy hiệu "TDN". ✅ |

## Project Structure

```text
MainForm.CameraFrame.cs            # MỚI: UI, settings, ApplyCameraFrame, RenderCameraFrameVideoAsync
MainForm.Effects.cs                # RenderEffectVideoAsync nối khung (CUDA + CPU)
MainForm.ImageEffect.cs            # RenderImageEffectVideoAsync nối khung; giải khung nhúng
MainForm.cs                        # AppSettings 3 trường cũ; hàng "Khung camera"; nhánh stream copy; bắt đầu/kết thúc lượt
Services/EffectPackLibrary.cs      # CameraFrameFile, ListCameraFrames, FrameDisplayName, CameraFrameLayout
Assets/CameraFrames/Frame_0[1-4]_*.png  # 4 khung 1920×1080 (gen-camera-frames.ps1)
RenderMusicPro.csproj              # EmbeddedResource BuiltInFrames/
Tests/EffectPackLibraryTests.cs    # test khung
```
