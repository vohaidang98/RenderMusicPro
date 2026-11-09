using System.Drawing;
using System.Media;
using System.Text;
using System.Text.Json;
using RenderMusicPro.Services;

namespace RandomVideoDangVH;

// v1.4.0: retry/skip theo từng mục, kiểm tra trước khi chạy, preset, dọn cache, thông báo hoàn thành.
public sealed partial class MainForm
{
    // FFmpeg không có tiến trình quá thời gian này thì bị dừng (rồi được thử lại 1 lần).
    private static readonly TimeSpan FfmpegStallTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan WatchdogCheckInterval = TimeSpan.FromSeconds(15);
    private const int FfmpegLogTailLines = 20;
    private const long MinFreeBytes = 2L * 1024 * 1024 * 1024;
    private const long EstimatedBytesPerItem = 512L * 1024 * 1024;
    private const int CacheMaxAgeDays = 30;

    // Hết dung lượng đĩa thì dừng cả lô (thử lại/bỏ qua cũng vô ích).
    private sealed class OutOfDiskSpaceException : IOException
    {
        public OutOfDiskSpaceException(string message) : base(message) { }
    }

    private sealed record RenderFailure(string Name, string Reason);
    private readonly List<RenderFailure> _runFailures = new();
    private readonly HashSet<int> _runSkipIndices = new();
    private int _runSucceeded;
    private string? _encoderProbeFor;

    private readonly Label lblCacheSize = new();
    private readonly RoundedButton btnCleanCache = new();
    private readonly RoundedButton btnSavePreset = new();
    private readonly RoundedButton btnLoadPreset = new();

    // =========================================================
    // RETRY / CONTINUE ON FAILURE
    // =========================================================
    // Chạy một mục tối đa 2 lần. Lỗi lần 2 được ghi nhận rồi bỏ qua (trả về false).
    // Người dùng bấm Dừng (token bị hủy) thì ném OperationCanceledException để dừng cả lô.
    private async Task<bool> RunItemWithRetryAsync(string name, Func<int, Task> attempt, CancellationToken token)
    {
        Exception? last = null;
        for (int n = 1; n <= 2; n++)
        {
            if (n > 1) token.ThrowIfCancellationRequested();
            try
            {
                await attempt(n);
                return true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (OutOfDiskSpaceException) { throw; }
            catch (Exception ex)
            {
                last = ex;
                AppLog.Write($"LỖI mục \"{name}\" (lần {n}/2):\n{ex}");
                AddLog($"  ✗ Lỗi ở {name}: {FirstLine(ex.Message)}", Red);
                if (n == 1) AddLog($"  ↻ Thử lại 1 lần: {name}", Orange);
            }
        }
        _runFailures.Add(new RenderFailure(name, FirstLine(last?.Message ?? "Lỗi không rõ")));
        AddLog($"  ⚠ Bỏ qua {name}, tiếp tục mục tiếp theo.", Orange);
        return false;
    }

    private static string FirstLine(string text)
    {
        string line = (text ?? "").Split('\n')[0].Trim();
        return line.Length > 200 ? line[..200] + "…" : line;
    }

    private string BuildRunSummary(TimeSpan elapsed)
    {
        var sb = new StringBuilder();
        sb.Append($"Tổng kết: {_runSucceeded:N0} thành công, {_runFailures.Count:N0} lỗi · tổng thời gian {ClockText(elapsed)}");
        if (_runFailures.Count > 0)
        {
            sb.Append("\nDanh sách mục lỗi (đã thử lại 1 lần):");
            foreach (var failure in _runFailures)
                sb.Append($"\n  ✗ {failure.Name}: {failure.Reason}");
            sb.Append("\nChi tiết xem trong file log: " + AppLog.LogDirectory);
        }
        return sb.ToString();
    }

    private void NotifyBatchFinished()
    {
        try
        {
            SystemSounds.Asterisk.Play();
            if (!IsDisposed && IsHandleCreated) TaskbarFlash.FlashIfInactive(Handle);
        }
        catch
        {
            // Không phát được âm thanh / nháy taskbar - bỏ qua.
        }
    }

    // =========================================================
    // FFMPEG OUTPUT / LOG
    // =========================================================
    private static async Task<string> ReadTailWithActivityAsync(StreamReader reader, Action onActivity)
    {
        const int maxChars = 64 * 1024;
        var sb = new StringBuilder();
        while (await reader.ReadLineAsync().ConfigureAwait(false) is string line)
        {
            onActivity();
            sb.Append(line).Append('\n');
            if (sb.Length > maxChars) sb.Remove(0, sb.Length - maxChars);
        }
        return sb.ToString();
    }

    private static void LogFfmpegFailure(string step, string exe, string args, string reason, string stderr)
    {
        var lines = (stderr ?? "").Replace("\r", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(FfmpegLogTailLines);
        AppLog.Write($"FFmpeg lỗi [{step}] ({reason})\n  Lệnh: \"{exe}\" {args}\n  {FfmpegLogTailLines} dòng cuối của FFmpeg:\n    " +
            string.Join("\n    ", lines));
    }

    private static List<string> ListDirectAudioFiles(string source) =>
        SafeEnumerateFiles(source).Where(IsAudioFile).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

    // =========================================================
    // PRE-FLIGHT
    // =========================================================
    private async Task<bool> RunPreflightChecksAsync()
    {
        _runSkipIndices.Clear();
        if (_ffmpegPath == null || !File.Exists(_ffmpegPath))
        {
            ShowError("Không tìm thấy ffmpeg.exe. Hãy đặt ffmpeg.exe cạnh file .exe của Render Music Pro hoặc thêm FFmpeg vào PATH.");
            return false;
        }

        await ProbeEncodersAsync(_ffmpegPath);

        bool direct = cmbMode.SelectedIndex == 3;
        string musicOut = txtMusicOutput.Text.Trim(), videoOut = txtVideoOutput.Text.Trim();
        int items = direct ? CountDirectAudioFiles() : (int)numPlaylists.Value;
        long estimate = items * EstimatedBytesPerItem;

        var warnings = new List<string>();
        var checkedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void CheckDrive(string path, string label, long needed)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                string? root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal)) return;
                if (!checkedRoots.Add(root + "|" + label)) return;
                var drive = new DriveInfo(root);
                if (!drive.IsReady) return;
                long free = drive.AvailableFreeSpace;
                if (free < MinFreeBytes)
                    warnings.Add($"Ổ {root} ({label}) chỉ còn {FormatBytes(free)} trống (dưới 2 GB).");
                else if (free < needed)
                    warnings.Add($"Ổ {root} ({label}) còn {FormatBytes(free)} trống, có thể không đủ " +
                        $"(ước tính thô khoảng {FormatBytes(needed)} cho {items:N0} mục).");
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Không đọc được thông tin ổ đĩa - bỏ qua kiểm tra này.
            }
        }
        CheckDrive(videoOut, "thư mục Video", estimate);
        // Ổ chứa Music chỉ cần kiểm tra riêng nếu khác ổ Video.
        string? videoRoot = SafeRoot(videoOut), musicRoot = SafeRoot(musicOut);
        if (!string.Equals(videoRoot, musicRoot, StringComparison.OrdinalIgnoreCase))
            CheckDrive(musicOut, "thư mục Music", estimate / 8);
        string cacheDir = string.IsNullOrWhiteSpace(_effectCacheDirectory)
            ? EffectCacheService.DefaultDirectory : _effectCacheDirectory;
        string? cacheRoot = SafeRoot(cacheDir);
        if (!string.Equals(cacheRoot, videoRoot, StringComparison.OrdinalIgnoreCase))
            CheckDrive(cacheDir, "cache/file tạm", 0);

        if (warnings.Count > 0 && MessageBox.Show(this,
                string.Join("\n", warnings) + "\n\nNếu hết dung lượng giữa chừng, lô render sẽ dừng. Vẫn tiếp tục?",
                "Render Music Pro - Cảnh báo dung lượng", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return false;

        return ConfirmOverwrite(direct, musicOut, videoOut);
    }

    private static string? SafeRoot(string path)
    {
        try { return string.IsNullOrWhiteSpace(path) ? null : Path.GetPathRoot(Path.GetFullPath(path)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return null; }
    }

    // Hỏi trước khi ghi đè file đầu ra đã có: Có = ghi đè, Không = bỏ qua mục đã có, Hủy = không chạy.
    private bool ConfirmOverwrite(bool direct, string musicOut, string videoOut)
    {
        var existing = new List<int>();
        string? example = null;
        try
        {
            if (direct)
            {
                string source = txtDirectAudio.Text.Trim();
                var files = File.Exists(source) ? new List<string> { source } : ListDirectAudioFiles(source);
                for (int i = 0; i < files.Count; i++)
                {
                    string safe = SanitizeFileName(Path.GetFileNameWithoutExtension(files[i]));
                    if (string.IsNullOrWhiteSpace(safe)) safe = $"Audio_{i + 1:0000}";
                    string video = Path.Combine(videoOut, safe + ".mp4");
                    string music = Path.Combine(musicOut, safe + Path.GetExtension(files[i]));
                    bool musicClash = File.Exists(music) &&
                        !string.Equals(Path.GetFullPath(files[i]), Path.GetFullPath(music), StringComparison.OrdinalIgnoreCase);
                    if (File.Exists(video) || musicClash)
                    {
                        existing.Add(i);
                        example ??= safe;
                    }
                }
            }
            else
            {
                string name = SanitizeFileName(txtPlaylistName.Text.Trim());
                if (string.IsNullOrWhiteSpace(name)) name = "Playlist";
                int start = (int)numStartNumber.Value, count = (int)numPlaylists.Value;
                for (int i = 0; i < count; i++)
                {
                    string baseName = $"{start + i} - {name}";
                    if (File.Exists(Path.Combine(videoOut, baseName + ".mp4")) ||
                        File.Exists(Path.Combine(musicOut, baseName + ".mp3")) ||
                        File.Exists(Path.Combine(musicOut, baseName + ".txt")))
                    {
                        existing.Add(i);
                        example ??= baseName;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return true; // Không kiểm tra được thì không chặn việc render.
        }

        if (existing.Count == 0) return true;
        var answer = MessageBox.Show(this,
            $"Đã có {existing.Count:N0} mục với file đầu ra trùng tên (ví dụ: {example}).\n\n" +
            "Có: ghi đè các file đã có\nKhông: bỏ qua các mục đã có, chỉ tạo mục mới\nHủy: không chạy",
            "Render Music Pro - File đã tồn tại", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        if (answer == DialogResult.Cancel) return false;
        if (answer == DialogResult.No)
            foreach (int index in existing) _runSkipIndices.Add(index);
        return true;
    }

    // Kiểm tra bộ mã hóa NVENC một lần (kết quả được nhớ theo đường dẫn ffmpeg).
    private async Task ProbeEncodersAsync(string ffmpeg)
    {
        if (string.Equals(_encoderProbeFor, ffmpeg, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            string listing = await RunProcessCaptureAsync(ffmpeg, "-hide_banner -encoders", cts.Token);
            _encoderProbeFor = ffmpeg;
            if (listing.Contains("h264_nvenc", StringComparison.OrdinalIgnoreCase))
                AddLog("✓ FFmpeg có bộ mã hóa NVIDIA NVENC (GPU).", Cyan);
            else
            {
                _nvencAvailable = false;
                AddLog("⚠ FFmpeg này không có NVENC: sẽ mã hóa bằng CPU (chậm hơn). Dùng bản FFmpeg có hỗ trợ NVIDIA nếu muốn dùng GPU.", Orange);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.Write("Không kiểm tra được danh sách encoder: " + ex.Message);
        }
    }

    private static string FormatBytes(long bytes)
    {
        double gb = bytes / (1024d * 1024 * 1024);
        return gb >= 1 ? $"{gb:0.0} GB" : $"{bytes / (1024d * 1024):0} MB";
    }

    // =========================================================
    // PRESETS
    // =========================================================
    private static string PresetDirectory => Path.Combine(SettingsDirectory, "presets");

    private RoundedButton StyleSmallButton(RoundedButton button, string text, int width, Action onClick)
    {
        button.Text = text;
        button.Size = new Size(width, 36);
        button.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular);
        button.Color1 = Color.FromArgb(33, 42, 54);
        button.Color2 = Color.FromArgb(33, 42, 54);
        button.HoverColor1 = Color.FromArgb(47, 61, 76);
        button.HoverColor2 = Color.FromArgb(47, 61, 76);
        button.ForeColor = TextMain;
        button.BackColor = Card;
        button.Margin = new Padding(0, 0, 8, 0);
        button.Click += (_, _) => onClick();
        return button;
    }

    private void AddPresetControls(TableLayoutPanel grid, int row)
    {
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
            BackColor = Color.Transparent, Margin = Padding.Empty, Padding = new Padding(0, 4, 0, 0) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var buttons = new[] {
            StyleSmallButton(btnSavePreset, "Lưu preset", 130, SavePreset),
            StyleSmallButton(btnLoadPreset, "Nạp preset", 130, LoadPreset),
            StyleSmallButton(btnLan, "Render LAN", 200, ShowLanDialog),
            StyleSmallButton(btnNormalize, "Chuẩn hóa video", 190, ShowNormalizeDialog)
        };
        var symbols = new[] { StudioSymbol.Save, StudioSymbol.Folder, StudioSymbol.Network, StudioSymbol.Video };
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].Symbol = symbols[i]; buttons[i].Dock = DockStyle.Fill;
            buttons[i].Margin = new Padding(i % 2 == 0 ? 0 : 4, 3, i % 2 == 0 ? 4 : 0, 3);
            actions.Controls.Add(buttons[i], i % 2, i / 2);
        }
        grid.Controls.Add(actions, 0, row);
        grid.SetColumnSpan(actions, 2);
    }

    private void SavePreset()
    {
        try
        {
            Directory.CreateDirectory(PresetDirectory);
            using var dialog = new SaveFileDialog
            {
                Title = "Lưu preset cài đặt",
                InitialDirectory = PresetDirectory,
                Filter = "Preset Render Music Pro (*.json)|*.json",
                DefaultExt = "json",
                FileName = "preset",
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            string json = JsonSerializer.Serialize(CaptureSettings(), new JsonSerializerOptions { WriteIndented = true });
            string temp = dialog.FileName + ".tmp";
            try
            {
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                File.Move(temp, dialog.FileName, true);
            }
            finally { TryDelete(temp); }
            AddLog("✓ Đã lưu preset: " + dialog.FileName, Green);
        }
        catch (Exception ex)
        {
            AddLog("Không lưu được preset: " + ex.Message, Orange);
            ShowError("Không lưu được preset.\n\n" + ex.Message);
        }
    }

    private void LoadPreset()
    {
        try
        {
            Directory.CreateDirectory(PresetDirectory);
            using var dialog = new OpenFileDialog
            {
                Title = "Nạp preset cài đặt",
                InitialDirectory = PresetDirectory,
                Filter = "Preset Render Music Pro (*.json)|*.json|Tất cả|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(dialog.FileName, Encoding.UTF8));
            if (settings == null) throw new InvalidDataException("File preset trống hoặc không hợp lệ.");
            try
            {
                _loadingSettings = true;
                ApplySettings(settings);
            }
            finally { _loadingSettings = false; }
            SaveSettings();
            RefreshCounts();
            _ = RefreshCacheSizeAsync();
            AddLog("✓ Đã nạp preset: " + Path.GetFileName(dialog.FileName), Green);
        }
        catch (Exception ex)
        {
            AddLog("Không nạp được preset: " + ex.Message, Orange);
            ShowError("Không nạp được preset.\n\n" + ex.Message);
        }
    }

    // =========================================================
    // CACHE MANAGEMENT
    // =========================================================
    private void AddCacheControls(FlowLayoutPanel flow)
    {
        ApplyMutedLabel(lblCacheSize);
        lblCacheSize.AutoSize = true;
        lblCacheSize.Text = "Cache hiệu ứng: đang tính...";
        lblCacheSize.Margin = new Padding(0, 7, 12, 0);
        flow.Controls.Add(lblCacheSize);
        flow.Controls.Add(StyleSmallButton(btnCleanCache, "Dọn cache", 112, () => _ = CleanCacheAsync()));
        btnCleanCache.Margin = new Padding(0, 0, 0, 0);
        btnCleanCache.Height = 30;
        btnCleanCache.Symbol = StudioSymbol.Clean;
    }

    private string PreparedEffectsDirectory()
    {
        string root = string.IsNullOrWhiteSpace(_effectCacheDirectory)
            ? EffectCacheService.DefaultDirectory : _effectCacheDirectory;
        return Path.Combine(Path.GetFullPath(root), "PreparedEffects");
    }

    private async Task RefreshCacheSizeAsync()
    {
        try
        {
            string directory = PreparedEffectsDirectory();
            long size = await Task.Run(() => new EffectCacheService(directory).GetSize());
            if (!IsDisposed) lblCacheSize.Text = "Cache hiệu ứng: " + FormatBytes(size);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            if (!IsDisposed) lblCacheSize.Text = "Cache hiệu ứng: không đọc được";
        }
    }

    private async Task CleanCacheAsync()
    {
        if (_isRendering)
        {
            ShowError("Hãy dừng render trước khi dọn cache.");
            return;
        }
        try
        {
            string directory = PreparedEffectsDirectory();
            if (MessageBox.Show(this,
                    $"Xóa các mục cache hiệu ứng không dùng trong {CacheMaxAgeDays} ngày qua?\n\n" +
                    "Thư mục: " + directory + "\n\nCache bị xóa sẽ được tạo lại tự động ở lần render sau (mất thêm thời gian tách nền).",
                    "Render Music Pro - Dọn cache", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            btnCleanCache.Enabled = false;
            var (removed, freed) = await Task.Run(() =>
                new EffectCacheService(directory).CleanUnusedSince(TimeSpan.FromDays(CacheMaxAgeDays)));
            AddLog($"✓ Dọn cache: đã xóa {removed:N0} mục, giải phóng {FormatBytes(freed)}.", Green);
            MessageBox.Show(this,
                removed == 0 ? "Không có mục cache nào cũ hơn " + CacheMaxAgeDays + " ngày."
                    : $"Đã xóa {removed:N0} mục cache, giải phóng {FormatBytes(freed)}.",
                "Render Music Pro - Dọn cache", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AddLog("Dọn cache lỗi: " + ex.Message, Orange);
            ShowError("Không dọn được cache.\n\n" + ex.Message);
        }
        finally
        {
            if (!IsDisposed) { btnCleanCache.Enabled = true; await RefreshCacheSizeAsync(); }
        }
    }
}
