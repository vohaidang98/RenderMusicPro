# UI Contract: thẻ "03 Video & hiệu ứng"

| Hàng | Cột 0 | Cột 1–3 |
|---|---|---|
| 1 | Nhãn **Nguồn nền** | Combo `cmbBackgroundSource` ["Kho video", "Kho ảnh"] + ô đường dẫn: `txtVideoSource` (Kho video) hoặc `txtImageSource` (Kho ảnh, nút 📁 → `BrowseImageSource()`). Chỉ hiện một ô. |
| 2 | Nhãn **Hiệu ứng** | Combo `cmbEffectSource` ["Thư mục riêng", "Bộ 00 · Cơ bản – chung", "Bộ 01 · Công giáo – Hào quang", …] + ô đường dẫn: `txtEffectSource` (Thư mục riêng, như cũ) hoặc `txtEffectPresetsRoot` (thư mục gốc các bộ, nút 📁 → `BrowseEffectPresetsRoot()`). Danh sách bộ quét lại mỗi lần mở combo. |
| 3–4 | Không đổi (Có nhạc/Im lặng, Tự lặp video — mờ khi nền ảnh). |
| 5 | `chkEffectEnabled` "Bật hiệu ứng" + "Tách nền" + Tự nhận diện/Nền xanh/Nền đen + cache (như cũ). |

- "Tách nền" bật khi: bật hiệu ứng VÀ (đang chọn một bộ, hoặc Thư mục riêng có đường dẫn).
- Tắt "Bật hiệu ứng" → combo nguồn + ô đường dẫn hiệu ứng mờ, render không ghép hiệu ứng.
- Log bắt đầu: `✓ Kho ảnh: {n} ảnh · mỗi video dùng 1 ảnh làm nền.` · `✓ Bộ hiệu ứng: {tên} · {n} hiệu ứng, random mỗi video.` · `ℹ Hiệu ứng đang tắt; video không ghép hiệu ứng.` · `ℹ Chế độ nền ảnh chưa chia bài LAN; render hết trên máy này.`
- Log mỗi mục: `  ✦ Ảnh nền: {tên} · giữ tỉ lệ trong khung 1920×1080.` rồi các log hiệu ứng/mã hóa như chế độ video + hiệu ứng.