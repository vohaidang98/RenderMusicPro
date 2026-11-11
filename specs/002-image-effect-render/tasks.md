# Tasks: Chế độ render Ảnh + Hiệu ứng

**Input**: Design documents from `/specs/002-image-effect-render/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ui-contract.md, quickstart.md

**Tests**: xunit cho phần thuần (`EffectPackLibrary`), kiểm tay theo `quickstart.md` cho ffmpeg/UI (constitution V).

## Phase 1: Setup

- [x] T001 Sinh 8 clip mẫu nền đen 1920×1080 vào `Assets/Presets_Effects/00_CoBan_Chung/` bằng `specs/002-image-effect-render/gen-effects.ps1`
- [x] T002 Nhúng `Assets\Presets_Effects\**` làm EmbeddedResource (LogicalName `BuiltInEffects/...`) trong `RenderMusicPro.csproj`

## Phase 2: Foundational

- [x] T003 [P] `Services/EffectPackLibrary.cs`: liệt kê bộ từ nhiều gốc (trùng tên → gốc trước), tên hiển thị tiếng Việt, kiểm tên an toàn, giải tài nguyên nhúng (giữ file trùng dung lượng, `.part` rồi đổi tên, không xóa file lạ)
- [x] T004 [P] `Tests/EffectPackLibraryTests.cs` + link service trong `Tests/RenderMusicPro.Tests.csproj`
- [x] T005 Tách `EncodeOverlayWithFallbackAsync` (NVENC → CPU) từ `RenderEffectVideoAsync` trong `MainForm.Effects.cs`, giữ nguyên lệnh/log

## Phase 3: User Story 1 — Nền ảnh + hiệu ứng (P1) 🎯 MVP

- [x] T006 [US1] `MainForm.ImageEffect.cs`: `txtImageSource`, `cmbBackgroundSource`, `BrowseImageSource()`, `CreateImageDeck()`
- [x] T007 [US1] `RenderImageWithMusicAsync` (file tạm → đổi tên) + `RenderImageEffectVideoAsync` (loop ảnh trong RAM, overlay hiệu ứng đã chuẩn bị, gọi T005)
- [x] T008 [US1] `MainForm.cs`: hàng "Nguồn nền" (combo + 2 ô đường dẫn chuyển đổi)
- [x] T009 [US1] `MainForm.cs`: `ValidateSettings` kiểm kho ảnh khi nền ảnh; `StartRenderCoreAsync` dựng `_imageDeck`, không đòi kho video, không chia LAN (log), dọn ở `finally`
- [x] T010 [US1] `MainForm.cs`: rẽ nhánh nền ảnh tại 2 điểm gọi render (playlist random + Audio có sẵn)

## Phase 4: User Story 2 — Bộ hiệu ứng có sẵn / thư mục riêng / công tắc (P1)

- [x] T011 [US2] `MainForm.ImageEffect.cs`: `cmbEffectSource` (Thư mục riêng + các bộ), `txtEffectPresetsRoot`, `EffectSourceFolder()`, `InitializeEffectPacksAsync()`, `RefreshEffectPacks()` (cả khi mở combo)
- [x] T012 [US2] `chkEffectEnabled` "Bật hiệu ứng" (tắt → `EffectSourceFolder()` rỗng → không hiệu ứng), "Tách nền" bật đúng theo nguồn
- [x] T013 [US2] `MainForm.Effects.cs` `CreateEffectDeck` + `MainForm.LanRender.cs` `ValidateLanPaths` dùng `EffectSourceFolder()`

## Phase 5: User Story 3 — Không ảnh hưởng chế độ cũ + lưu cấu hình (P2)

- [x] T014 [US3] `AppSettings` thêm `BackgroundSourceMode`, `ImageSourcePath`, `EffectEnabled`, `EffectPackName`, `EffectPresetsRoot`; Apply (kẹp giá trị, tự suy gốc bộ từ `EffectSourcePath` cũ) / Capture (preset dùng chung)

## Phase 6: Polish & kiểm chứng

- [x] T015 `dotnet build -c Release` 0 lỗi/không cảnh báo mới; `dotnet test` đạt hết
- [x] T016 Chạy thử đồ thị ffmpeg chế độ ảnh (có/không hiệu ứng, NVENC + CPU) với bộ cơ bản và bộ thật, kiểm bằng ffprobe
- [x] T017 Ghi `.bangiao/thay-doi.md`
- [ ] T018 Kiểm tay giao diện theo `quickstart.md` (cần người dùng chạy app có license)

## Follow-up (ngoài phạm vi)

- Khung Camera (spec riêng): dùng lại tên trường `EnableCameraFrame`/`CameraFramePath`/`CameraFramePosition`; tham khảo `Assets\CameraFrames\Frame_01..04*.png` của bản Full.
- Bộ `11_Den_Disco` riêng khi có clip thật (cơ chế đã tự nhận thư mục mới).
