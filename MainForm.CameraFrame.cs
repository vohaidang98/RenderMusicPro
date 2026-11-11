using RenderMusicPro.Services;

namespace RandomVideoDangVH;

// Khung camera (viewfinder/REC/LIVE...): PNG trong suốt phủ LÊN TRÊN CÙNG video đầu ra, sau nền + hiệu ứng.
// Dùng lại đúng tên trường settings đã có: EnableCameraFrame / CameraFramePath / CameraFramePosition.
// CameraFramePosition = mức cách mép (0 Sát mép, 1 Cách mép vừa, 2 Cách mép xa) — xem specs\003-camera-frame.
public sealed partial class MainForm
{
    private readonly CheckBox chkCameraFrame = new();
    private readonly StudioComboBox cmbCameraFrame = new();
    private readonly StudioComboBox cmbCameraFramePosition = new();
    private readonly RoundedButton btnCameraFrameFolder = new();
    private List<CameraFrameFile> _cameraFrames = new();
    private string _pendingCameraFramePath = "";
    private bool _refreshingCameraFrames;
    // Chỉ khác null trong một lượt render tự làm trên máy này (không áp cho phiếu LAN).
    private string? _runCameraFrame;
    private int _runCameraFramePosition;

    private static string BuiltInFramesDirectory => Path.Combine(SettingsDirectory, EffectPackLibrary.FramesFolderName);
    private static string DefaultFramesDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Assets", EffectPackLibrary.FramesFolderName);

    private CameraFrameFile? SelectedCameraFrame =>
        cmbCameraFrame.SelectedIndex >= 0 && cmbCameraFrame.SelectedIndex < _cameraFrames.Count
            ? _cameraFrames[cmbCameraFrame.SelectedIndex] : null;

    private void AddCameraFrameControls(FlowLayoutPanel flow)
    {
        chkCameraFrame.Text = "Khung camera";
        chkCameraFrame.Checked = false;
        StyleCheck(chkCameraFrame);
        chkCameraFrame.CheckedChanged += (_, _) => UpdateCameraFrameUi();
        flow.Controls.Add(chkCameraFrame);

        foreach (var (combo, width, name) in new[] { (cmbCameraFrame, 210, "Kiểu khung camera"),
                     (cmbCameraFramePosition, 140, "Vị trí khung camera: khoảng cách tới mép") })
        {
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.AccessibleName = name;
            StyleCombo(combo);
            combo.Width = width;
            combo.Margin = new Padding(4, 5, 6, 5);
            flow.Controls.Add(combo);
        }
        cmbCameraFrame.DropDownWidth = 260;
        cmbCameraFramePosition.Items.AddRange(EffectPackLibrary.FramePositionNames);
        cmbCameraFramePosition.SelectedIndex = 0;
        cmbCameraFrame.SelectedIndexChanged += (_, _) =>
        {
            if (!_refreshingCameraFrames) _pendingCameraFramePath = SelectedCameraFrame?.Path ?? _pendingCameraFramePath;
        };
        // Thả thêm PNG vào thư mục khung là hiện khi mở danh sách.
        cmbCameraFrame.DropDown += (_, _) => RefreshCameraFrames();
        flow.Controls.Add(StyleSmallButton(btnCameraFrameFolder, "Thư mục khung", 130, OpenCameraFrameFolder));
        btnCameraFrameFolder.Height = 30;
        btnCameraFrameFolder.Margin = new Padding(0, 2, 0, 0);
        UpdateCameraFrameUi();
    }

    private void UpdateCameraFrameUi()
    {
        cmbCameraFrame.Enabled = chkCameraFrame.Checked;
        cmbCameraFramePosition.Enabled = chkCameraFrame.Checked;
    }

    private void OpenCameraFrameFolder()
    {
        try
        {
            Directory.CreateDirectory(BuiltInFramesDirectory);
            OpenFolder(BuiltInFramesDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("Không mở được thư mục khung camera: " + ex.Message);
        }
    }

    private IEnumerable<string> CameraFrameRoots()
    {
        // Khung đã lưu nằm ở thư mục khác (người dùng tự làm) thì thư mục đó cũng được quét.
        string? saved = Path.GetDirectoryName(_pendingCameraFramePath);
        if (!string.IsNullOrWhiteSpace(saved)) yield return saved;
        yield return DefaultFramesDirectory;
        yield return BuiltInFramesDirectory;
    }

    private static int ExtractEmbeddedCameraFrames() =>
        EffectPackLibrary.Extract(BuiltInFramesDirectory, EmbeddedAssets(EffectPackLibrary.FrameResourcePrefix));

    private void RefreshCameraFrames()
    {
        if (_isRendering || IsDisposed) return;
        string keep = _pendingCameraFramePath;
        _cameraFrames = EffectPackLibrary.ListCameraFrames(CameraFrameRoots());
        _refreshingCameraFrames = true;
        try
        {
            cmbCameraFrame.BeginUpdate();
            cmbCameraFrame.Items.Clear();
            foreach (var frame in _cameraFrames) cmbCameraFrame.Items.Add(frame.DisplayName);
            int index = _cameraFrames.FindIndex(f => string.Equals(f.Path, keep, StringComparison.OrdinalIgnoreCase));
            if (index < 0 && keep.Length > 0)
                index = _cameraFrames.FindIndex(f => string.Equals(f.Name, Path.GetFileNameWithoutExtension(keep), StringComparison.OrdinalIgnoreCase));
            // Chưa chọn gì: mặc định kiểu "Máy quay (REC)" giống ảnh mẫu nếu có.
            if (index < 0 && keep.Length == 0)
                index = _cameraFrames.FindIndex(f => f.Name.Contains("Camcorder", StringComparison.OrdinalIgnoreCase));
            if (index < 0 && _cameraFrames.Count > 0) index = 0;
            cmbCameraFrame.SelectedIndex = index;
            if (keep.Length == 0 && index >= 0) _pendingCameraFramePath = _cameraFrames[index].Path;
        }
        finally
        {
            cmbCameraFrame.EndUpdate();
            _refreshingCameraFrames = false;
        }
    }

    private void ApplyCameraFrameSettings(AppSettings settings)
    {
        chkCameraFrame.Checked = settings.EnableCameraFrame;
        _pendingCameraFramePath = settings.CameraFramePath ?? "";
        cmbCameraFramePosition.SelectedIndex = Math.Clamp(settings.CameraFramePosition, 0,
            EffectPackLibrary.FramePositionNames.Length - 1);
        RefreshCameraFrames();
        UpdateCameraFrameUi();
    }

    private void CaptureCameraFrameSettings(AppSettings settings)
    {
        settings.EnableCameraFrame = chkCameraFrame.Checked;
        settings.CameraFramePath = SelectedCameraFrame?.Path ?? _pendingCameraFramePath;
        settings.CameraFramePosition = Math.Max(0, cmbCameraFramePosition.SelectedIndex);
    }

    /// <summary>Gọi lúc bắt đầu render: null khi tắt; bật mà không có file thì báo lỗi rõ ràng.</summary>
    private string? ResolveCameraFrameForRun()
    {
        if (!chkCameraFrame.Checked) return null;
        var frame = SelectedCameraFrame;
        if (frame == null || !File.Exists(frame.Path))
            throw new InvalidOperationException("Đã bật Khung camera nhưng không tìm thấy file khung. Hãy chọn kiểu khung hoặc tắt Khung camera.");
        return frame.Path;
    }

    private void LogCameraFrameForRun()
    {
        if (_runCameraFrame == null) return;
        string name = SelectedCameraFrame?.DisplayName ?? Path.GetFileNameWithoutExtension(_runCameraFrame);
        AddLog($"✓ Khung camera: {name} · {EffectPackLibrary.FramePositionNames[Math.Clamp(_runCameraFramePosition, 0, EffectPackLibrary.FramePositionNames.Length - 1)].ToLowerInvariant()}.", Cyan);
    }

    // =========================================================
    // FFMPEG: lớp khung là input cuối cùng (sau nhạc) nên chỉ số audio của các lệnh cũ không đổi.
    // =========================================================
    private string CameraFrameInputArgs() => _runCameraFrame == null ? "" : $" -framerate 30 -i {Q(_runCameraFrame)}";

    /// <summary>Đổi nhãn đầu ra [outv] của đồ thị thành lớp nền rồi phủ khung (giữ trong RAM như ảnh hiệu ứng).</summary>
    private string ApplyCameraFrame(string graph, int frameInput, (int Width, int Height) canvas)
    {
        if (_runCameraFrame == null) return graph;
        var (x, y, width, height) = EffectPackLibrary.CameraFrameLayout(canvas.Width, canvas.Height, _runCameraFramePosition);
        return graph.Replace("[outv]", "[preframe]", StringComparison.Ordinal) +
            $";[{frameInput}:v:0]scale={width}:{height},format=rgba[cameraframe];" +
            $"[preframe][cameraframe]overlay=x={x}:y={y}:shortest=0:eof_action=repeat:repeatlast=1,format=yuv420p[outv]";
    }

    /// <summary>Nền video không có hiệu ứng nhưng có khung: phải mã hóa lại (không stream copy được).</summary>
    private async Task RenderCameraFrameVideoAsync(string ffmpeg, string list, string music, bool audioEncoded,
        string output, string duration, (int Width, int Height) canvas, CancellationToken token)
    {
        string graph = ApplyCameraFrame("[0:v:0]setpts=PTS-STARTPTS,format=yuv420p[outv]", 2, canvas);
        string common = $"-hide_banner -y -f concat -safe 0 -i {Q(list)} -i {Q(music)}{CameraFrameInputArgs()} " +
            $"-filter_complex {Q(graph)} ";
        string end = $" -map \"[outv]\" -map 1:a:0 -t {duration} {AudioCodecArgs(audioEncoded)}{FastStartArgs}{Q(output)}";
        await EncodeOverlayWithFallbackAsync(ffmpeg, common, end, output, token,
            "Ghép khung camera và mã hóa NVENC",
            "  ✓ Đã ghép khung camera · mã hóa NVIDIA NVENC · giữ độ phân giải video nền.",
            "  ⚠ NVENC không sử dụng được cho khung camera; chuyển sang CPU.",
            "Ghép khung camera dự phòng CPU",
            "  ✓ Đã ghép khung camera bằng CPU · giữ độ phân giải video nền.");
    }
}
