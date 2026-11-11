# Implementation Plan: Chế độ render Ảnh + Hiệu ứng

**Branch**: `002-image-effect-render` | **Date**: 2026-11-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-image-effect-render/spec.md`

## Summary

Thêm nguồn nền "Kho ảnh" bên cạnh "Kho video": mỗi video đầu ra lấy 1 ảnh (bộ bài `MusicPoolDeck`
ít-dùng-trước), ảnh giữ tỉ lệ trong khung 1920×1080, lặp tĩnh trong RAM (`loop` filter) suốt thời lượng
nhạc, phủ hiệu ứng (đã tách nền qua `GetPreparedEffectAsync` + cache) bằng cùng đồ thị overlay của
`RenderEffectVideoAsync`, mã hóa NVENC → CPU qua một hàm dùng chung tách từ `RenderEffectVideoAsync`.
Thêm nguồn hiệu ứng "Bộ có sẵn": mỗi thư mục con của `Presets_Effects` là một bộ (bộ thật của người dùng
`01_CongGiao_HaoQuang` … `10_Kids_BongBong` ở `<app>\Assets\Presets_Effects` hoặc thư mục gốc người dùng chọn),
cộng bộ `00_CoBan_Chung` (8 clip mẫu: nhiễu film, sao lấp lánh, hoa rơi, bokeh, bụi rơi, tuyết rơi, trái tim bay,
đèn disco) nhúng trong exe và giải ra `%LocalAppData%\RenderMusicPro\Presets_Effects`. Liệt kê/giải/tên hiển thị nằm
trong lớp thuần `Services\EffectPackLibrary` (có test xunit). Chọn một bộ → `CreateEffectDeck` dựng `MusicPoolDeck`
từ thư mục bộ, random mỗi video. Thêm công tắc "Bật hiệu ứng" (tắt → `_effectDeck = null` ở mọi chế độ nền).

## Technical Context

**Language/Version**: C# / .NET 8 (net8.0-windows), WinForms

**Primary Dependencies**: ffmpeg.exe / ffprobe.exe bên ngoài (đã có); không thêm NuGet cho app

**Storage**: settings.json/preset (thêm 5 trường), thư mục kho có sẵn trong thư mục cấu hình, cache hiệu ứng có sẵn

**Testing**: xunit (`Tests\RenderMusicPro.Tests.csproj`, link file Services) + kiểm tay ffmpeg/UI (`quickstart.md`)

**Target Platform**: Windows 10/11 x64, GPU NVIDIA tùy chọn

**Project Type**: desktop-app (WinForms, single-file publish)

**Performance Goals**: Ảnh được scale một lần rồi giữ trong RAM; tốc độ ghép ≥ chế độ video+hiệu ứng hiện có

**Constraints**: Không đổi hành vi khi không dùng lựa chọn mới; exe tự cập nhật chỉ thay 1 file → tài nguyên nhúng

**Scale/Scope**: 1 file partial mới, 1 service mới, sửa nhỏ MainForm.cs/Effects.cs, 8 clip mẫu nhúng (~0,1–0,3 MB mỗi file)

## Constitution Check

| Nguyên tắc | Đánh giá |
|---|---|
| I. An toàn đầu ra | Ghi mp4 qua `CreateTemporaryOutputPath` rồi `File.Move` như luồng cũ; giải kho có sẵn qua file `.part` rồi đổi tên; không xóa file nguồn/không xóa file người dùng thêm vào kho có sẵn. ✅ |
| II. Không làm hỏng chế độ 1 máy | Mặc định Kho video + Thư mục riêng → rẽ nhánh sớm (`_imageDeck == null`, `EffectSourceFolder()` trả về `txtEffectSource`); thân luồng render video không viết lại. Tách phần NVENC/CPU của `RenderEffectVideoAsync` thành hàm dùng chung giữ nguyên lệnh và log. ✅ |
| III. Không tin dữ liệu share | Không thêm dữ liệu share; chế độ ảnh không chia LAN. Tên tài nguyên nhúng được kiểm tra không thoát thư mục đích. ✅ |
| IV. UI không treo | Giải kho và quét kho ảnh chạy `Task.Run`; ffmpeg qua `RunFfmpegAsync` có token Dừng. ✅ |
| V. Logic thuần kiểm chứng được | `EffectPackLibrary` không phụ thuộc MainForm, có test xunit; phần ffmpeg/UI có danh sách kiểm tay trong `quickstart.md` + `.bangiao`. ✅ |
| VI. Tiếng Việt + TDN Media | Chữ mới tiếng Việt có dấu; không đổi thương hiệu. ✅ |

Quy trình "sao lưu vào backups\ / không dùng git" trong constitution: dự án hiện có git (worktree), nhánh riêng thay cho bản sao lưu. Ghi chú, không phải vi phạm thiết kế.

## Project Structure

### Documentation (this feature)

```text
specs/002-image-effect-render/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/ui-contract.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
MainForm.cs                 # AppSettings + Apply/Capture, UI hàng Nền/Hiệu ứng, Validate, rẽ nhánh ở 2 điểm gọi render
MainForm.Effects.cs         # CreateEffectDeck dùng EffectSourceFolder(); tách EncodeOverlayWithFallbackAsync
MainForm.ImageEffect.cs     # MỚI: kho ảnh, nguồn hiệu ứng, kho có sẵn, RenderImageWithMusicAsync / RenderImageEffectVideoAsync
Services/EffectPackLibrary.cs     # MỚI: liệt kê bộ, tên hiển thị, giải tài nguyên nhúng (thuần, có test)
Assets/Presets_Effects/00_CoBan_Chung/*.mp4  # MỚI: 8 clip mẫu, nhúng EmbeddedResource
RenderMusicPro.csproj       # EmbeddedResource Assets\Presets_Effects\**
MainForm.LanRender.cs       # kiểm đường dẫn LAN theo nguồn hiệu ứng đang chọn
Tests/EffectPackLibraryTests.cs + Tests/RenderMusicPro.Tests.csproj (link service mới)
```

**Structure Decision**: Theo đúng kiểu partial class hiện có (`MainForm.<Tính năng>.cs`) và lớp thuần trong `Services\`.

## Complexity Tracking

Không có vi phạm cần biện minh.
