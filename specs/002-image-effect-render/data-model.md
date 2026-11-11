# Data Model: Chế độ render Ảnh + Hiệu ứng

## AppSettings (settings.json + preset) — trường mới

| Trường | Kiểu | Mặc định | Ý nghĩa |
|---|---|---|---|
| `BackgroundSourceMode` | int | 0 | 0 = Kho video, 1 = Kho ảnh. Giá trị lạ → kẹp về 0..1. |
| `ImageSourcePath` | string | "" | Thư mục kho ảnh. |
| `EffectEnabled` | bool | true | Công tắc "Bật hiệu ứng". |
| `EffectPackName` | string | "" | Tên thư mục bộ đang chọn; "" = Thư mục riêng (`EffectSourcePath`). |
| `EffectPresetsRoot` | string | "" | Thư mục gốc các bộ; "" = tự suy (cha của `EffectSourcePath` nếu tên là `Presets_Effects`, nếu không thì `<app>\Assets\Presets_Effects`). |

File settings cũ thiếu trường → mặc định → hành vi cũ.

## Kho ảnh (runtime)

- File ảnh (.png .jpg .jpeg .bmp .webp .tif .tiff) quét đệ quy; `_imageDeck : MusicPoolDeck?` chỉ khác null
  trong lượt render nền ảnh; xóa ở `finally`.

## Bộ hiệu ứng (`EffectPack(Name, Folder)`)

- Gốc quét theo thứ tự: `EffectPresetsRoot` → `<app>\Assets\Presets_Effects` → `%LocalAppData%\RenderMusicPro\Presets_Effects`
  (nơi giải bộ nhúng). Trùng tên → giữ bộ ở gốc đứng trước. Thư mục con không có file ảnh/video → không liệt kê.
- Tên hiển thị: `NN · <tên thân thiện>` (bảng tên tiếng Việt cho 00–10, còn lại thay `_` bằng khoảng trắng).
- Tài nguyên nhúng: LogicalName `BuiltInEffects/00_CoBan_Chung/<file>.mp4`; giải ra nếu thiếu/khác dung lượng,
  qua `.part` rồi đổi tên; không xóa file khác.