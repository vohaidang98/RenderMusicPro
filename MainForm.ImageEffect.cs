using RenderMusicPro.Services;

namespace RandomVideoDangVH;

// v1.6.0: nền "Kho ảnh" (1 ảnh tĩnh / video, phủ hiệu ứng đã tách nền) + nguồn hiệu ứng "Bộ có sẵn"
// (mỗi thư mục con của Presets_Effects là một bộ; random trong bộ) + công tắc bật/tắt hiệu ứng.
// Mặc định (Kho video + Thư mục riêng + bật hiệu ứng) giữ nguyên hành vi cũ.
public sealed partial class MainForm
{
    private readonly StudioComboBox cmbBackgroundSource = new();
    private readonly TextBox txtImageSource = new();
    private readonly StudioComboBox cmbEffectSource = new();
    private readonly TextBox txtEffectPresetsRoot = new();
    private readonly CheckBox chkEffectEnabled = new();
    private Control? _videoPathInput, _imagePathInput, _effectPathInput, _presetsRootInput;
    private MusicPoolDeck? _imageDeck;
    private List<EffectPack> _effectPacks = new();
    private bool _effectPacksLoaded;
    private string _pendingEffectPackName = "";
    private bool _refreshingEffectPacks;

    private const int ImageCanvasWidth = 1920, ImageCanvasHeight = 1080;

    private bool ImageBackgroundSelected => cmbBackgroundSource.SelectedIndex == 1;

    private EffectPack? SelectedEffectPack =>
        cmbEffectSource.SelectedIndex > 0 && cmbEffectSource.SelectedIndex - 1 < _effectPacks.Count
            ? _effectPacks[cmbEffectSource.SelectedIndex - 1] : null;

    // Bộ mẫu nhúng trong exe được giải ra đây (thư mục cấu hình luôn ghi được, kể cả khi app nằm trong Program Files).
    private static string BuiltInPacksDirectory =>
        Path.Combine(SettingsDirectory, EffectPackLibrary.PresetsFolderName);

    private static string DefaultPresetsRoot =>
        Path.Combine(AppContext.BaseDirectory, "Assets", EffectPackLibrary.PresetsFolderName);

    /// <summary>Thư mục hiệu ứng dùng cho lượt render: "" khi tắt hiệu ứng / thư mục riêng để trống.</summary>
    private string EffectSourceFolder()
    {
        if (!chkEffectEnabled.Checked) return "";
        var pack = SelectedEffectPack;
        return pack != null ? pack.Folder : txtEffectSource.Text.Trim();
    }

    // =========================================================
    // UI
    // =========================================================
    private Control MakeSourceSelector(StudioComboBox combo, string accessibleName, int comboWidth,
        params Control[] pathInputs)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, BackColor = Color.Transparent };
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, comboWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.AccessibleName = accessibleName;
        StyleCombo(combo);
        row.Controls.Add(MakeInputField(combo, new Padding(0, 0, 8, 0)), 0, 0);
        var holder = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.Transparent };
        foreach (var input in pathInputs) holder.Controls.Add(input);
        row.Controls.Add(holder, 1, 0);
        return row;
    }

    private Control BuildBackgroundSourceRow(Control videoPathInput)
    {
        _videoPathInput = videoPathInput;
        _imagePathInput = MakePathInput(txtImageSource, "📁", BrowseImageSource);
        txtImageSource.PlaceholderText = "Thư mục kho ảnh nền";
        txtImageSource.AccessibleName = "Thư mục kho ảnh nền";
        _themedInputs.Add(txtImageSource);
        cmbBackgroundSource.Items.AddRange(new object[] { "Kho video", "Kho ảnh" });
        cmbBackgroundSource.SelectedIndex = 0;
        var row = MakeSourceSelector(cmbBackgroundSource, "Nguồn nền video", 130, _videoPathInput, _imagePathInput);
        cmbBackgroundSource.SelectedIndexChanged += (_, _) => UpdateBackgroundSourceUi();
        return row;
    }

    private Control BuildEffectSourceRow(Control effectPathInput)
    {
        _effectPathInput = effectPathInput;
        _presetsRootInput = MakePathInput(txtEffectPresetsRoot, "📁", BrowseEffectPresetsRoot);
        txtEffectPresetsRoot.PlaceholderText = "Thư mục chứa các bộ hiệu ứng (Presets_Effects)";
        txtEffectPresetsRoot.AccessibleName = "Thư mục chứa các bộ hiệu ứng có sẵn";
        txtEffectPresetsRoot.Text = DefaultPresetsRoot;
        txtEffectPresetsRoot.Leave += (_, _) => RefreshEffectPacks();
        _themedInputs.Add(txtEffectPresetsRoot);
        cmbEffectSource.Items.Add("Thư mục riêng");
        cmbEffectSource.SelectedIndex = 0;
        cmbEffectSource.DropDownWidth = 280;
        var row = MakeSourceSelector(cmbEffectSource, "Nguồn hiệu ứng: thư mục riêng hoặc bộ có sẵn", 190,
            _effectPathInput, _presetsRootInput);
        cmbEffectSource.SelectedIndexChanged += (_, _) =>
        {
            if (_refreshingEffectPacks) return;
            if (_effectPacksLoaded) _pendingEffectPackName = SelectedEffectPack?.Name ?? "";
            UpdateEffectSourceUi();
        };
        // Thả thêm thư mục bộ mới (ví dụ "11_Den_Disco") là hiện ngay khi mở danh sách.
        cmbEffectSource.DropDown += (_, _) => RefreshEffectPacks();

        chkEffectEnabled.Text = "Bật hiệu ứng";
        chkEffectEnabled.Checked = true;
        StyleCheck(chkEffectEnabled);
        chkEffectEnabled.CheckedChanged += (_, _) => UpdateEffectSourceUi();
        txtEffectSource.TextChanged += (_, _) => UpdateEffectSourceUi();
        return row;
    }

    private void UpdateBackgroundSourceUi()
    {
        bool image = ImageBackgroundSelected;
        if (_videoPathInput != null) _videoPathInput.Visible = !image;
        if (_imagePathInput != null) _imagePathInput.Visible = image;
        // Nền ảnh không dùng clip video nên tùy chọn lặp clip không có tác dụng.
        chkReuseVideo.Enabled = !image;
    }

    private void UpdateEffectSourceUi()
    {
        bool pack = cmbEffectSource.SelectedIndex > 0;
        bool enabled = chkEffectEnabled.Checked;
        if (_effectPathInput != null) _effectPathInput.Visible = !pack;
        if (_presetsRootInput != null) _presetsRootInput.Visible = pack;
        cmbEffectSource.Enabled = enabled;
        txtEffectSource.Enabled = enabled;
        txtEffectPresetsRoot.Enabled = enabled;
        chkEffectGreen.Enabled = enabled && (pack || !string.IsNullOrWhiteSpace(txtEffectSource.Text));
    }

    private void BrowseImageSource()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Chọn thư mục kho ảnh nền (mỗi video dùng 1 ảnh, ảnh giữ nguyên không tách nền)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (Directory.Exists(txtImageSource.Text.Trim())) dialog.SelectedPath = txtImageSource.Text.Trim();
        if (dialog.ShowDialog(this) == DialogResult.OK) txtImageSource.Text = dialog.SelectedPath;
    }

    private void BrowseEffectPresetsRoot()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Chọn thư mục chứa các bộ hiệu ứng (mỗi thư mục con là một bộ, ví dụ Presets_Effects)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (Directory.Exists(txtEffectPresetsRoot.Text.Trim())) dialog.SelectedPath = txtEffectPresetsRoot.Text.Trim();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        txtEffectPresetsRoot.Text = dialog.SelectedPath;
        RefreshEffectPacks();
    }

    // =========================================================
    // BỘ HIỆU ỨNG CÓ SẴN
    // =========================================================
    private IEnumerable<string> EffectPackRoots()
    {
        string chosen = txtEffectPresetsRoot.Text.Trim();
        if (chosen.Length > 0) yield return chosen;
        yield return DefaultPresetsRoot;
        yield return BuiltInPacksDirectory;
    }

    private static bool IsEffectFile(string path)
    {
        string extension = Path.GetExtension(path);
        return EffectImageExtensions.Contains(extension) ||
            VideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static List<EmbeddedAsset> EmbeddedAssets(string prefix)
    {
        var assembly = typeof(MainForm).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(name =>
            {
                using var probe = assembly.GetManifestResourceStream(name)!;
                return new EmbeddedAsset(name[prefix.Length..], probe.Length,
                    () => assembly.GetManifestResourceStream(name)!);
            }).ToList();
    }

    private static int ExtractEmbeddedEffectPacks() =>
        EffectPackLibrary.Extract(BuiltInPacksDirectory, EmbeddedAssets(EffectPackLibrary.ResourcePrefix));

    // Gọi khi cửa sổ hiện: giải bộ hiệu ứng cơ bản + khung camera nhúng trong exe rồi quét lại danh sách.
    private async Task InitializeEffectPacksAsync()
    {
        try
        {
            await Task.Run(ExtractEmbeddedEffectPacks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppLog.Write("Không giải được bộ hiệu ứng có sẵn: " + ex);
        }
        try
        {
            await Task.Run(ExtractEmbeddedCameraFrames);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppLog.Write("Không giải được khung camera có sẵn: " + ex);
        }
        if (IsDisposed) return;
        RefreshEffectPacks();
        RefreshCameraFrames();
    }

    private void RefreshEffectPacks()
    {
        if (_isRendering || IsDisposed) return;
        // _pendingEffectPackName = lựa chọn của người dùng (đổi trong SelectedIndexChanged / nạp settings).
        // Bộ đã lưu nhưng chưa thấy (ổ chưa cắm...) thì combo hiện "Thư mục riêng" nhưng vẫn nhớ tên để chọn lại sau.
        string keep = _pendingEffectPackName;
        _effectPacks = EffectPackLibrary.ListPacks(EffectPackRoots(), IsEffectFile);
        _refreshingEffectPacks = true;
        try
        {
            cmbEffectSource.BeginUpdate();
            cmbEffectSource.Items.Clear();
            cmbEffectSource.Items.Add("Thư mục riêng");
            foreach (var pack in _effectPacks) cmbEffectSource.Items.Add("Bộ " + pack.DisplayName);
            int index = _effectPacks.FindIndex(p => string.Equals(p.Name, keep, StringComparison.OrdinalIgnoreCase));
            cmbEffectSource.SelectedIndex = index >= 0 ? index + 1 : 0;
        }
        finally
        {
            cmbEffectSource.EndUpdate();
            _refreshingEffectPacks = false;
        }
        _effectPacksLoaded = true;
        UpdateEffectSourceUi();
    }

    private void ApplyImageEffectSettings(AppSettings settings)
    {
        cmbBackgroundSource.SelectedIndex = Math.Clamp(settings.BackgroundSourceMode, 0, 1);
        txtImageSource.Text = settings.ImageSourcePath ?? "";
        chkEffectEnabled.Checked = settings.EffectEnabled;
        string root = settings.EffectPresetsRoot ?? "";
        if (root.Length == 0)
        {
            // Đã từng chọn thư mục riêng nằm trong ...\Presets_Effects\<bộ> → lấy luôn thư mục cha làm gốc các bộ.
            string? parent = Path.GetDirectoryName((settings.EffectSourcePath ?? "").TrimEnd('\\', '/'));
            root = parent != null && string.Equals(Path.GetFileName(parent), EffectPackLibrary.PresetsFolderName,
                StringComparison.OrdinalIgnoreCase) ? parent : DefaultPresetsRoot;
        }
        txtEffectPresetsRoot.Text = root;
        _pendingEffectPackName = settings.EffectPackName ?? "";
        _effectPacksLoaded = false;
        RefreshEffectPacks();
        UpdateBackgroundSourceUi();
    }

    private void CaptureImageEffectSettings(AppSettings settings)
    {
        settings.BackgroundSourceMode = cmbBackgroundSource.SelectedIndex < 0 ? 0 : cmbBackgroundSource.SelectedIndex;
        settings.ImageSourcePath = txtImageSource.Text.Trim();
        settings.EffectEnabled = chkEffectEnabled.Checked;
        settings.EffectPresetsRoot = txtEffectPresetsRoot.Text.Trim();
        settings.EffectPackName = _pendingEffectPackName;
    }

    // =========================================================
    // RENDER
    // =========================================================
    private MusicPoolDeck CreateImageDeck()
    {
        string folder = txtImageSource.Text.Trim();
        if (!Directory.Exists(folder))
            throw new InvalidOperationException("Kho ảnh không tồn tại. Hãy chọn thư mục chứa ảnh nền.");
        var files = SafeEnumerateFiles(folder).Where(path => EffectImageExtensions.Contains(Path.GetExtension(path))).ToList();
        if (files.Count == 0)
            throw new InvalidOperationException("Kho ảnh không có ảnh được hỗ trợ (png, jpg, jpeg, bmp, webp, tif).");
        return new MusicPoolDeck(files);
    }

    private void LogEffectSourceForRun()
    {
        if (!chkEffectEnabled.Checked)
        {
            AddLog("ℹ Hiệu ứng đang tắt; video không ghép hiệu ứng.", Cyan);
            return;
        }
        var pack = SelectedEffectPack;
        if (pack != null && _effectDeck != null)
            AddLog($"✓ Bộ hiệu ứng: {pack.DisplayName} · {_effectDeck.Count:N0} hiệu ứng, random mỗi video.", Cyan);
    }

    private async Task RenderImageWithMusicAsync(string ffmpeg, string music, bool audioEncoded,
        string output, double targetDuration, CancellationToken token)
    {
        string image = _imageDeck!.Take(1, Random.Shared)[0];
        string tempOutput = CreateTemporaryOutputPath(output);
        _timingFinalOutput = tempOutput;
        try
        {
            string duration = targetDuration.ToString(System.Globalization.CultureInfo.InvariantCulture);
            AddLog($"  ✦ Ảnh nền: {Path.GetFileName(image)} · giữ tỉ lệ trong khung {ImageCanvasWidth}×{ImageCanvasHeight}.", Cyan);
            string? effect = null;
            if (_effectDeck != null)
            {
                effect = _effectDeck.Take(1, Random.Shared)[0];
                lblCurrent.Text = _runEffectGreen
                    ? (_runEffectBackgroundMode == 0 ? "Đang nhận diện nền và ghép hiệu ứng lên ảnh (ưu tiên GPU)..." :
                        (_runEffectBlack ? "Đang tách nền đen và ghép hiệu ứng lên ảnh (ưu tiên GPU)..." : "Đang tách nền xanh và ghép hiệu ứng lên ảnh (ưu tiên GPU)..."))
                    : "Đang ghép hiệu ứng lên ảnh (ưu tiên GPU)...";
                AddLog($"  ✦ Hiệu ứng: {Path.GetFileName(effect)} · lặp đến hết video · giữ kích thước gốc.", Cyan);
            }
            else
            {
                lblCurrent.Text = "Đang ghép ảnh nền với nhạc (ưu tiên GPU)...";
            }
            await RenderImageEffectVideoAsync(ffmpeg, image, music, audioEncoded, effect, tempOutput,
                duration, _runEffectGreen, token);
            File.Move(tempOutput, output, true);
        }
        finally
        {
            TryDelete(tempOutput);
            _timingFinalOutput = null;
        }
    }

    // Cùng đồ thị overlay với RenderEffectVideoAsync, chỉ thay nền concat video bằng 1 ảnh tĩnh:
    // ảnh được giải mã + scale MỘT lần rồi filter loop nhân khung trong RAM (không đọc lại ảnh mỗi khung).
    // Không có tầng "giải mã nền CUDA" vì nền không phải video; mã hóa NVENC → CPU dùng chung.
    private async Task RenderImageEffectVideoAsync(string ffmpeg, string image, string music, bool audioEncoded,
        string? effect, string output, string duration, bool greenScreen, CancellationToken token)
    {
        string background =
            $"[0:v:0]scale={ImageCanvasWidth}:{ImageCanvasHeight}:force_original_aspect_ratio=decrease:force_divisible_by=2," +
            $"pad={ImageCanvasWidth}:{ImageCanvasHeight}:(ow-iw)/2:(oh-ih)/2,setsar=1,format=yuv420p," +
            "loop=loop=-1:size=1:start=0,setpts=N/(30*TB)";
        string inputs, graph;
        int audioInput;
        if (effect == null)
        {
            inputs = $"-framerate 30 -i {Q(image)} -i {Q(music)}";
            graph = background + "[outv]";
            audioInput = 1;
        }
        else
        {
            bool effectImage = EffectImageExtensions.Contains(Path.GetExtension(effect));
            if (greenScreen)
            {
                effect = (await GetPreparedEffectAsync(ffmpeg, effect, token)).Path;
                AddLog("  ✓ Dùng hiệu ứng đã chuẩn bị; không tách nền lại khi lặp và ghép.", Cyan);
            }
            string loop = effectImage ? "-framerate 30" : "-stream_loop -1";
            inputs = $"-framerate 30 -i {Q(image)} {loop} -i {Q(effect)} -i {Q(music)}";
            graph = background + "[bg];" +
                "[1:v:0]setpts=PTS-STARTPTS,format=rgba[fx];" +
                "[bg][fx]overlay=x=(main_w-overlay_w)/2:y=(main_h-overlay_h)/2:shortest=0:eof_action=repeat:repeatlast=1,format=yuv420p[outv]";
            audioInput = 2;
        }
        // Khung camera (nếu bật) là input ngay sau nhạc, phủ trên cùng.
        graph = ApplyCameraFrame(graph, audioInput + 1, (ImageCanvasWidth, ImageCanvasHeight));
        inputs += CameraFrameInputArgs();
        string common = $"-hide_banner -y {inputs} -filter_complex {Q(graph)} ";
        string end = $" -map \"[outv]\" -map {audioInput}:a:0 -t {duration} {AudioCodecArgs(audioEncoded)}" +
            $"{FastStartArgs}{Q(output)}";
        if (effect == null)
            await EncodeOverlayWithFallbackAsync(ffmpeg, common, end, output, token,
                "Ghép ảnh nền và mã hóa NVENC",
                "  ✓ Đã ghép ảnh nền · mã hóa NVIDIA NVENC · khung 1920×1080.",
                "  ⚠ NVENC không sử dụng được cho ảnh nền này; chuyển sang CPU.",
                "Ghép ảnh nền dự phòng CPU",
                "  ✓ Đã ghép ảnh nền bằng CPU · khung 1920×1080.");
        else
            await EncodeOverlayWithFallbackAsync(ffmpeg, common, end, output, token,
                "Ghép hiệu ứng lên ảnh và mã hóa NVENC",
                "  ✓ Đã ghép hiệu ứng lên ảnh · mã hóa NVIDIA NVENC · khung 1920×1080.",
                "  ⚠ NVENC không sử dụng được cho hiệu ứng này; chuyển sang CPU.",
                "Ghép hiệu ứng lên ảnh dự phòng CPU",
                "  ✓ Đã ghép hiệu ứng lên ảnh bằng CPU · khung 1920×1080.");
    }
}
