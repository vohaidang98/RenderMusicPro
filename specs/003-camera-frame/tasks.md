# Tasks: Khung Camera phủ lên video

- [x] T001 Vẽ 4 khung PNG 1920×1080 trong suốt (`specs/003-camera-frame/gen-camera-frames.ps1` → `Assets/CameraFrames/`)
- [x] T002 Nhúng `Assets\CameraFrames\*.png` (LogicalName `BuiltInFrames/…`) trong `RenderMusicPro.csproj`
- [x] T003 [P] `Services/EffectPackLibrary.cs`: `CameraFrameFile`, `ListCameraFrames`, `FrameDisplayName`, `FramePositionNames`, `CameraFrameLayout`
- [x] T004 [P] Test xunit cho T003 trong `Tests/EffectPackLibraryTests.cs`
- [x] T005 [US1] `MainForm.CameraFrame.cs`: `CameraFrameInputArgs`, `ApplyCameraFrame`, `RenderCameraFrameVideoAsync`
- [x] T006 [US1] Nối khung vào `RenderEffectVideoAsync` (CUDA + CPU, input 3) và `RenderImageEffectVideoAsync` (input sau nhạc)
- [x] T007 [US1] `RenderVideoWithMusicAsync`: có khung + không hiệu ứng → `RenderCameraFrameVideoAsync` thay stream copy
- [x] T008 [US1] `StartRenderCoreAsync`: `_runCameraFrame` (lỗi rõ nếu thiếu file), log, bỏ khung khi chia LAN, dọn ở `finally`
- [x] T009 [US2] UI hàng "Khung camera" (công tắc, kiểu, vị trí, nút Thư mục khung); thẻ 03 cao thêm 36px
- [x] T010 [US2] Settings/preset: `EnableCameraFrame`, `CameraFramePath`, `CameraFramePosition`; giải khung nhúng khi mở app
- [x] T011 Build Release 0 lỗi; `dotnet test` đạt; chạy thử 3 đồ thị ffmpeg (CUDA+NVENC, CPU, NVENC) kiểm bằng ffprobe
- [ ] T012 Người dùng xem tận mắt 4 khung trên video thật và kiểm tay giao diện
