# Quickstart: kiểm chứng Render LAN

## Tự động (không cần ffmpeg)
```powershell
cd D:\Code\RenderMusicPro; dotnet build RenderMusicPro.csproj -c Release   # 0 lỗi, 0 cảnh báo
cd D:\Code\RenderMusicPro\Tests; dotnet test                               # 38/38 đạt (bản 1.5.1)
```

## Kiểm tay (2 máy A gửi, B nhận)
1. A: RENDER LAN → Chọn thư mục `D:\RenderLAN` (chứa music in, video in, out) → đồng ý bật chia sẻ → nhận `\\A\RenderLAN`.
2. A: chọn `D:\` → phải bị từ chối, không hỏi quyền admin.
3. B: dán `\\A\RenderLAN`, tick "cho phép nhận bài", Lưu. Nút hiện "(NHẬN)".
4. A: tick "chia bài", lô 6 playlist → cả A và B render; đầu ra A đủ 6 bộ; `out\` trống khi xong.
5. B đang giúp → bấm Bắt đầu → câu hỏi đúng (spec US3); thử Không rồi Có.
6. B đang giúp → tắt app B bằng Task Manager → mở lại trong 1 phút → phiếu trả về pending (log B).
7. A bấm Dừng giữa lô → `jobs\cancelled\<lô>` xuất hiện; B dừng bài, không đẩy kết quả.
8. Tắt cả GỬI và NHẬN → render lô thường → giống 1.4.1; (lưu ý FR-060: hiện vẫn chạm share lúc mở app).
9. Tên playlist "Nhạc Chill... Lofi" với chia bài → mọi bài thành công.

Chi tiết danh sách kiểm tay: `.bangiao\ket-qua-test.md` mục "Kiểm thủ công".
