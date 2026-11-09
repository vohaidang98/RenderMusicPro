# Implementation Plan: Render nhiều máy qua LAN (as-built 1.5.1)

**Branch**: `001-lan-render` | **Date**: 2026-11-07 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-lan-render/spec.md`

**Note**: Kế hoạch "as-built": mô tả thiết kế đã có trong code 1.5.1 (xem `.bangiao\ke-hoach.md`
quyết định 1–10) và chỉ ra phần còn thiếu so với spec. Không có code mới trong lượt này.

## Summary

Phối hợp nhiều máy chỉ qua một thư mục SMB share: máy gửi lên phiếu JSON (đường dẫn tương đối),
máy nhận giành phiếu bằng đổi tên file nguyên tử, render cục bộ, đẩy kết quả vào thư mục riêng theo
máy với done.json ghi sau cùng; máy gửi chép đúng 3 tên tự tính về đầu ra rồi dọn. Phát hiện máy/phiếu
chết bằng "lần cuối thấy giá trị đổi" theo đồng hồ của máy quan sát (LanChangeTracker). Logic thuần
nằm ở `Services\LanRenderQueue.cs` (test được), phần UI/ffmpeg ở `MainForm.LanRender.cs`.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (net8.0-windows), WinForms

**Primary Dependencies**: ffmpeg.exe (bên ngoài), System.Text.Json, PowerShell (SmbShare, icacls, NetFirewall) cho bật chia sẻ

**Storage**: File trên share SMB (`_RenderLAN\jobs\*`, `_RenderLAN\machines`, `out\`); cài đặt `lan-render.json` trong SettingsDirectory

**Testing**: xunit 2.9.2 (`Tests\RenderMusicPro.Tests.csproj`, link file `Services\LanRenderQueue.cs`), 38 test; UI kiểm tay

**Target Platform**: Windows 10/11 x64, máy trong cùng LAN, mạng Private

**Project Type**: desktop-app (WinForms, một exe)

**Performance Goals**: tick 5 giây; máy nhận rảnh nhận phiếu ≤ 10 giây; máy gửi vòng thu kết quả 3 giây

**Constraints**: không mở cổng mạng; không so đồng hồ giữa máy; UI không treo; chế độ 1 máy không đổi

**Scale/Scope**: 2–6 máy, lô tới vài trăm playlist; video ghép stream copy (file mp4 hàng GB)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Nguyên tắc | Kết quả | Ghi chú |
|---|---|---|
| I. An toàn dữ liệu đầu ra | PASS | Chép qua `.part`, so dung lượng, tính đã nhận trước khi dọn (FR-015/016) |
| II. Không làm hỏng chế độ 1 máy | PASS có lưu ý | Rẽ nhánh `LanDistributeEnabled`; nhưng InitLan vẫn chạm share khi đã lưu đường dẫn mà tắt LAN (N4 → FR-060) |
| III. Không tin dữ liệu từ share | PASS có lưu ý | ValidateJob, Resolve, 3 tên tự tính, base64; kết quả chỉ kiểm dung lượng (FR-054 chờ hỏi); quyền share rộng (FR-055 chờ hỏi) |
| IV. UI không treo, luôn dừng được | PASS có lưu ý | Còn IO đồng bộ trên luồng UI (LanRender.cs:245, 686) và copy mp4 không hủy được (FR-062/063) |
| V. Logic thuần kiểm chứng được | PASS | LanRenderQueue nhận `Func<DateTime>`; 38 test; mục 1/8/10 kiểm tay |
| VI. Tiếng Việt + TDN Media | PASS | Tiêu đề, csproj, iss đã có |

Không có vi phạm cần biện minh. Re-check sau Phase 1: không đổi.

## Project Structure

### Documentation (this feature)

```text
specs/001-lan-render/
├── plan.md              # File này
├── research.md          # Phase 0: quyết định thiết kế đã chốt
├── data-model.md        # Phase 1: thực thể + trạng thái phiếu
├── quickstart.md        # Phase 1: cách kiểm chứng
├── contracts/
│   └── share-protocol.md  # Giao thức file trên share (hợp đồng giữa các máy)
├── checklists/          # requirements.md + 4 checklist miền
├── analysis.md          # /speckit-analyze
├── convergence.md       # /speckit-converge
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
D:\Code\RenderMusicPro\
├── MainForm.cs                 # StartRenderAsync (vỏ + _renderStarting), StartRenderCoreAsync (rẽ nhánh LAN), tiêu đề
├── MainForm.LanRender.cs       # cài đặt, tick, bật chia sẻ, máy gửi (RenderLanBatchAsync), máy nhận (RunRemoteJobAsync), ưu tiên việc của mình
├── Services\LanRenderQueue.cs  # hàng chờ thuần: giành/thu hồi/hủy/đẩy/thu kết quả, quy tắc tên, LanChangeTracker
├── Services\AppVersion.cs, RenderMusicPro.csproj, RenderMusicPro_Setup.iss  # phiên bản + thương hiệu
└── Tests\LanQueueTests.cs      # xunit cho LanRenderQueue
```

**Structure Decision**: Giữ một dự án WinForms; logic giao thức tách vào `Services\LanRenderQueue.cs`
(không phụ thuộc MainForm) để test được; phần còn lại là partial class của MainForm.

## Complexity Tracking

Không có vi phạm constitution cần biện minh.
