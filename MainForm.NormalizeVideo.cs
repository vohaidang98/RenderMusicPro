using RenderMusicPro.Services;

namespace RandomVideoDangVH;

// "CHUẨN HÓA VIDEO": chọn 1 thư mục kho (quét đệ quy mọi thư mục con bên trong), đo từng video (dùng
// chung cache + hàm đo với VideoCheck ở MainForm.VideoCheck.cs — video đã đo & đúng chuẩn rồi thì KHÔNG
// đo lại). Video sai chuẩn app (không phải 1920x1080 / H.264 / .mp4, hoặc hình ngắn hơn tiếng) thì tự
// chuyển đổi rồi THAY THẾ đúng vị trí cũ trong kho (xóa bản gốc sau khi xác nhận bản mới đọc tốt). Video
// lỗi/không đọc được thì CHỈ BỎ QUA, không đụng tới — tránh mất nhầm video chỉ vì app không đọc được.
public sealed partial class MainForm
{
    private readonly RoundedButton btnNormalize = new();
    private CancellationTokenSource? _normalizeCts;

    private static bool IsVideoStandard(string file, VideoCheckEntry e) =>
        e.Error.Length == 0 && e.Width == 1920 && e.Height == 1080 &&
        string.Equals(e.Codec, "h264", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Path.GetExtension(file), ".mp4", StringComparison.OrdinalIgnoreCase) &&
        !(e.VideoDuration > 0 && e.FileDuration - e.VideoDuration > 0.15) &&
        (e.VideoDuration > 0 ? e.VideoDuration : e.FileDuration) >= 0.5;

    /// <summary>Quét đệ quy, bỏ qua từng thư mục con không đọc được thay vì làm hỏng cả lượt quét.</summary>
    private static IEnumerable<string> SafeEnumerateFilesRecursive(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            string dir = stack.Pop();
            string[] files = Array.Empty<string>();
            string[] subdirs = Array.Empty<string>();
            try { files = Directory.GetFiles(dir); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
            try { subdirs = Directory.GetDirectories(dir); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
            foreach (string f in files) yield return f;
            foreach (string d in subdirs) stack.Push(d);
        }
    }

    private void ShowNormalizeDialog()
    {
        using var form = new Form
        {
            Text = "Chuẩn hóa kho video",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false,
            ClientSize = new Size(680, 460),
            BackColor = Card, ForeColor = TextMain, Font = Font
        };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(16), BackColor = Color.Transparent };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        form.Controls.Add(grid);

        // Không cho chọn thư mục tự do nữa: quét thẳng vào thư mục "Video vào" đã cấu hình ở RENDER LAN
        // (thư mục share LAN cố định), để kho video luôn đúng 1 chỗ, không lệch khỏi phần chia sẻ LAN.
        var title = new Label
        {
            Text = "Quét thẳng thư mục Video vào (Render LAN) — tự quét toàn bộ thư mục con bên trong",
            AutoSize = true, Margin = new Padding(0, 0, 0, 4)
        };
        grid.Controls.Add(title, 0, 0); grid.SetColumnSpan(title, 2);
        var txtFolder = new TextBox
        {
            Text = _lan.VideoRoot, ReadOnly = true, Dock = DockStyle.Fill,
            BackColor = Input, ForeColor = TextMuted, BorderStyle = BorderStyle.FixedSingle
        };
        var btnOpenLanSettings = new Button { Text = "RENDER LAN...", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, ForeColor = TextMain };
        btnOpenLanSettings.Click += (_, _) =>
        {
            ShowLanDialog();
            txtFolder.Text = _lan.VideoRoot;
        };
        grid.Controls.Add(txtFolder, 0, 1);
        grid.Controls.Add(btnOpenLanSettings, 1, 1);

        var hint = new Label
        {
            Text = "Chuẩn app dùng: 1920x1080, mã H.264, định dạng .mp4. Video đúng chuẩn (đã đo rồi) sẽ KHÔNG đo lại. " +
                "Video sai chuẩn: tự chuyển đổi rồi thay vào đúng chỗ cũ (xóa bản gốc). Video lỗi/không đọc được: chỉ bỏ qua, không đụng vào.",
            AutoSize = true, ForeColor = TextMuted, MaximumSize = new Size(640, 0), Margin = new Padding(0, 4, 0, 8)
        };
        grid.Controls.Add(hint, 0, 2); grid.SetColumnSpan(hint, 2);

        var logBox = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
            BackColor = Input, ForeColor = TextMain, BorderStyle = BorderStyle.FixedSingle
        };
        grid.Controls.Add(logBox, 0, 3); grid.SetColumnSpan(logBox, 2);

        var lblSummary = new Label { Text = "Sẵn sàng.", AutoSize = true, ForeColor = TextMuted, Margin = new Padding(0, 8, 0, 4) };
        grid.Controls.Add(lblSummary, 0, 4); grid.SetColumnSpan(lblSummary, 2);

        void Log(string text)
        {
            if (logBox.IsDisposed) return;
            logBox.AppendText((logBox.TextLength > 0 ? Environment.NewLine : "") + text);
        }

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, BackColor = Color.Transparent };
        var btnClose = new Button { Text = "Đóng", Width = 96, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = TextMain };
        var btnStopNorm = new Button { Text = "Dừng", Width = 96, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = TextMain, Enabled = false };
        var btnRun = new Button { Text = "Bắt đầu chuẩn hóa", Width = 160, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = TextMain };
        buttons.Controls.Add(btnClose); buttons.Controls.Add(btnStopNorm); buttons.Controls.Add(btnRun);
        grid.Controls.Add(buttons, 0, 5); grid.SetColumnSpan(buttons, 2);
        form.CancelButton = btnClose;

        bool running = false;
        btnStopNorm.Click += (_, _) => { btnStopNorm.Enabled = false; _normalizeCts?.Cancel(); };
        btnClose.Click += (_, _) => form.Close();
        form.FormClosing += (_, e) => { if (running) e.Cancel = true; };

        btnRun.Click += async (_, _) =>
        {
            string folder = _lan.VideoRoot;
            if (folder.Length == 0 || !Directory.Exists(folder))
            {
                ShowError("Chưa cấu hình (hoặc chưa tạo) thư mục \"Video vào\" trong RENDER LAN.\n\n" +
                    "Bấm nút \"RENDER LAN...\" ở trên, dùng \"Tạo/Dùng kho cố định trên ổ D\" hoặc tự chọn thư mục Video vào rồi quay lại đây.");
                return;
            }
            running = true;
            btnRun.Enabled = false; btnOpenLanSettings.Enabled = false; btnStopNorm.Enabled = true; btnClose.Enabled = false;
            logBox.Clear();
            using var cts = new CancellationTokenSource();
            _normalizeCts = cts;
            try
            {
                await RunNormalizeAsync(folder, Log, s => { if (!lblSummary.IsDisposed) lblSummary.Text = s; }, cts.Token);
            }
            catch (OperationCanceledException) { Log("■ Đã dừng theo yêu cầu."); }
            catch (Exception ex) { Log("✗ Lỗi: " + FirstLine(ex.Message)); AppLog.Write("Chuẩn hóa video lỗi: " + ex); }
            finally
            {
                _normalizeCts = null;
                running = false;
                btnRun.Enabled = true; btnOpenLanSettings.Enabled = true; btnStopNorm.Enabled = false; btnClose.Enabled = true;
            }
        };

        form.ShowDialog(this);
        _normalizeCts?.Cancel();
    }

    private async Task RunNormalizeAsync(string rootFolder, Action<string> log, Action<string> setSummary, CancellationToken token)
    {
        _ffmpegPath ??= FindFfmpeg();
        if (_ffmpegPath == null) throw new InvalidOperationException("Không tìm thấy ffmpeg.exe.");
        string ffprobe = Path.Combine(Path.GetDirectoryName(_ffmpegPath)!, "ffprobe.exe");
        if (!File.Exists(ffprobe)) throw new InvalidOperationException("Không tìm thấy ffprobe.exe cạnh ffmpeg.exe.");

        LoadVideoCheckCache();
        List<string> files = await Task.Run(() =>
            SafeEnumerateFilesRecursive(rootFolder).Where(IsVideoFile).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(), token);

        log($"Tìm thấy {files.Count:N0} video trong kho (đã quét toàn bộ thư mục con).");
        int scanned = 0, already = 0, fixedCount = 0, skipped = 0;
        void UpdateSummary() => setSummary($"Đã quét {scanned:N0}/{files.Count:N0} · đã chuẩn sẵn {already:N0} · đã sửa {fixedCount:N0} · lỗi/bỏ qua {skipped:N0}");

        foreach (string file in files)
        {
            token.ThrowIfCancellationRequested();
            scanned++;
            if (!File.Exists(file)) { UpdateSummary(); continue; }

            var info = new FileInfo(file);
            VideoCheckEntry? cached;
            lock (_videoCheckCache) _videoCheckCache.TryGetValue(file, out cached);
            VideoCheckEntry entry;
            if (cached != null && cached.FileLength == info.Length && cached.LastWriteUtc == info.LastWriteTimeUtc)
                entry = cached;
            else
            {
                entry = await ProbeVideoCheckAsync(ffprobe, file, token);
                lock (_videoCheckCache) _videoCheckCache[file] = entry;
            }

            if (entry.Error.Length > 0)
            {
                skipped++;
                log($"  ⚠ Bỏ qua (lỗi đọc, không đụng vào): {Path.GetFileName(file)} — {entry.Error}");
            }
            else if (IsVideoStandard(file, entry))
            {
                already++;
            }
            else
            {
                log($"  ↻ Chuyển đổi: {Path.GetFileName(file)} ({entry.Width}x{entry.Height} {entry.Codec}) → chuẩn 1920x1080 H.264...");
                try
                {
                    string fixedPath = await NormalizeOneFileAsync(_ffmpegPath, ffprobe, file, token);
                    lock (_videoCheckCache) if (!string.Equals(fixedPath, file, StringComparison.OrdinalIgnoreCase)) _videoCheckCache.Remove(file);
                    fixedCount++;
                    log($"  ✓ Đã chuẩn hóa: {Path.GetFileName(fixedPath)}");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    skipped++;
                    log($"  ✗ Không chuyển đổi được (giữ nguyên file gốc): {Path.GetFileName(file)} — {FirstLine(ex.Message)}");
                }
            }
            UpdateSummary();
            if (scanned % 20 == 0) SaveVideoCheckCache();
        }
        SaveVideoCheckCache();
        log($"Hoàn tất: quét {scanned:N0} · đã chuẩn sẵn {already:N0} · đã sửa {fixedCount:N0} · lỗi/bỏ qua {skipped:N0}.");
    }

    /// <summary>Chuyển 1 file về chuẩn 1920x1080/H.264/.mp4; chỉ thay/xóa bản gốc sau khi xác nhận bản mới đọc tốt
    /// (convert vào file tạm trước, không đụng bản gốc cho tới lúc đã chắc ăn). Trả về đường dẫn cuối cùng.</summary>
    private async Task<string> NormalizeOneFileAsync(string ffmpeg, string ffprobe, string sourceFile, CancellationToken token)
    {
        string dir = Path.GetDirectoryName(sourceFile)!;
        string baseName = Path.GetFileNameWithoutExtension(sourceFile);
        string temp = Path.Combine(dir, baseName + ".chuanhoa-" + Guid.NewGuid().ToString("N")[..8] + ".mp4");
        try
        {
            await EncodeNormalizedClipAsync(ffmpeg, sourceFile, temp, token);
            var check = await ProbeVideoCheckAsync(ffprobe, temp, token);
            double dur = check.VideoDuration > 0 ? check.VideoDuration : check.FileDuration;
            if (check.Error.Length > 0 || dur < 0.5)
                throw new InvalidOperationException("Bản chuyển đổi không đọc được (" + (check.Error.Length > 0 ? check.Error : "thời lượng 0") + ").");

            string target = Path.ChangeExtension(sourceFile, ".mp4");
            bool samePath = string.Equals(Path.GetFullPath(target), Path.GetFullPath(sourceFile), StringComparison.OrdinalIgnoreCase);
            // Đổi đuôi (vd .mov -> .mp4) mà tên .mp4 đã có sẵn file khác: không ghi đè nhầm, thêm hậu tố.
            if (!samePath && File.Exists(target))
                target = Path.Combine(dir, baseName + "_chuanhoa.mp4");

            File.Move(temp, target, true);
            if (!samePath) TryDelete(sourceFile);
            var finalInfo = new FileInfo(target);
            var finalEntry = check with { FileLength = finalInfo.Length, LastWriteUtc = finalInfo.LastWriteTimeUtc };
            lock (_videoCheckCache) _videoCheckCache[target] = finalEntry;
            return target;
        }
        finally { TryDelete(temp); }
    }

    private async Task EncodeNormalizedClipAsync(string ffmpeg, string input, string output, CancellationToken token)
    {
        string common = $"-hide_banner -y -i {Q(input)} " +
            "-vf \"scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,setsar=1\" -r 30 -pix_fmt yuv420p -c:a aac -b:a 160k ";
        string nvencOptions = $"{NvencEncodeArgs} {Q(output)}";
        if (_nvencAvailable != false && _cudaPipelineAvailable != false)
        {
            try
            {
                string accelerated = $"-hide_banner -y -hwaccel cuda -hwaccel_output_format cuda -i {Q(input)} " +
                    "-vf \"scale_cuda=1920:1080:force_original_aspect_ratio=decrease:force_divisible_by=2:format=nv12,hwdownload,format=nv12,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,setsar=1\" -r 30 -pix_fmt yuv420p -c:a aac -b:a 160k ";
                await RunProcessAsync(ffmpeg, accelerated + nvencOptions,
                    token, "Chuẩn hóa video (CUDA + NVENC)");
                _nvencAvailable = true;
                _cudaPipelineAvailable = true;
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (InvalidOperationException)
            {
                _cudaPipelineAvailable = false;
                TryDelete(output);
            }
        }
        if (_nvencAvailable != false)
        {
            try
            {
                await RunProcessAsync(ffmpeg, common + nvencOptions,
                    token, "Chuẩn hóa video (NVENC)");
                _nvencAvailable = true;
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (InvalidOperationException)
            {
                _nvencAvailable = false;
                TryDelete(output);
            }
        }
        await RunProcessAsync(ffmpeg, common + $"-c:v libx264 -preset veryfast -crf 20 -profile:v high {BitrateCapArgs} {Q(output)}",
            token, "Chuẩn hóa video (CPU)");
    }
}
