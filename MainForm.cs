using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RenderMusicPro.Services;

namespace RandomVideoDangVH;

public sealed partial class MainForm : Form
{
    // =========================================================
    // INPUTS - MUSIC
    // =========================================================
    private readonly TextBox txtSuper = new();
    private readonly TextBox txtHay = new();
    private readonly TextBox txtKhoNhac = new();
    private readonly TextBox txtKho = new();

    // INPUT - NORMALIZED VIDEO
    private readonly TextBox txtVideoSource = new();
    private readonly TextBox txtEffectSource = new();
    private readonly CheckBox chkEffectGreen = new();
    private readonly StudioComboBox cmbEffectBackground = new();
    private bool _runEffectBlack;
    private int _runEffectBackgroundMode;
    private MusicPoolDeck? _effectDeck;
    private bool _runEffectGreen = true;
    private string _effectCacheDirectory = "";
    private (int Width, int Height)? _effectCanvasSize;
    private readonly TextBox txtDirectAudio = new();

    // OUTPUT
    private readonly TextBox txtMusicOutput = new();
    private readonly TextBox txtVideoOutput = new();

    // RANDOM MODE
    private readonly StudioComboBox cmbMode = new();
    private readonly StudioNumericUpDown numSuper = new();
    private readonly StudioNumericUpDown numHay = new();
    private readonly StudioNumericUpDown numKhoNhac = new();
    private readonly StudioNumericUpDown numKho = new();
    private readonly StudioNumericUpDown numPlaylists = new();
    private readonly StudioNumericUpDown numStartNumber = new();

    private readonly TextBox txtPlaylistName = new();

    // AUDIO CUT PATTERN
    private readonly StudioNumericUpDown numOnSeconds = new();
    private readonly StudioNumericUpDown numOffSeconds = new();

    // OPTIONS
    private readonly CheckBox chkOpenMusic = new();
    private readonly CheckBox chkOpenVideo = new();
    private readonly CheckBox chkReuseVideo = new();
    private readonly CheckBox chkFastStart = new();

    // UI
    private readonly StudioProgressBar progress = new();
    private readonly Label lblStatus = new();
    private readonly Label lblCurrent = new();
    private readonly Label lblFfmpeg = new();
    private readonly Label lblVideoCount = new();
    private readonly Label lblMusicCount = new();
    private readonly Label lblOutputCount = new();
    private readonly Label lblPercent = new();
    private readonly RichTextBox txtLog = new();
    private readonly RoundedButton btnStart = new();
    private readonly RoundedButton btnStop = new();
    private readonly RoundedButton btnCheckUpdate = new();
    private string? _availableUpdateVersion;
    private string? _availableUpdateUrl;
    private readonly LanUpdateService _updateService = new();
    // Dự phòng khi không đọc được thư mục LAN (máy không còn trong mạng nội bộ, thư mục chưa cấu hình...).
    private readonly GitHubUpdateService _githubUpdateService = new();

    private CancellationTokenSource? _cts;
    private string? _ffmpegPath;
    private const bool DarkThemeOnly = true;
    private bool _darkTheme = DarkThemeOnly;
    private int _videoCount;
    private int _musicCount;
    private bool _loadingSettings;
    private bool _licenseValid;
    private readonly Label lblLicenseAccount = new();
    private readonly Label lblLicenseStatus = new();
    private readonly TextBox txtLicenseKey = new();
    private readonly RoundedButton btnActivateLicense = new();
    private readonly LinkLabel lnkAdminZalo = new();
    // Cấu hình được lưu: đường dẫn + ON/OFF + các tùy chọn, KHÔNG lưu
    // CHẾ ĐỘ / SỐ BÀI-NHÓM / SỐ PLAYLIST / STT BẮT ĐẦU / TÊN PLAYLIST.
    // QUAN TRỌNG: SettingsDirectory phải được khởi tạo trước LicenseFile.
    private static readonly string SettingsDirectory =
        GetSettingsDirectory();

    private static readonly string SettingsFile =
        Path.Combine(SettingsDirectory, "settings.json");

    private static readonly string LicenseFile =
        Path.Combine(SettingsDirectory, "license.dat");

    private static string GetSettingsDirectory()
    {
        string[] roots =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Path.GetTempPath(),
            AppContext.BaseDirectory
        };

        foreach (string root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;

            try
            {
                string directory = Path.Combine(root, "RenderMusicPro");
                Directory.CreateDirectory(directory);
                return directory;
            }
            catch (Exception ex) when (ex is ArgumentException
                or IOException
                or UnauthorizedAccessException
                or NotSupportedException)
            {
                // Try the next writable location so a profile/path issue cannot
                // prevent the main window from starting.
            }
        }

        string fallbackRoot = Path.GetTempPath();
        if (string.IsNullOrWhiteSpace(fallbackRoot))
            fallbackRoot = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(fallbackRoot, "RenderMusicPro");
    }

    private readonly List<Control> _themedInputs = new();
    private readonly List<CardPanel> _cards = new();
    private readonly List<Control> _configurationCards = new();
    private bool _isRendering;
    // Label hiển thị số file thực tế của từng kho + NumericUpDown tương ứng.
    // Giới hạn SỐ BÀI được cập nhật động theo số audio thực tế trong từng thư mục.
    private readonly Dictionary<NumericUpDown, Label> _poolCountLabels = new();

    // Cache danh sách file theo thời gian sửa của thư mục. Mục tiêu là tránh quét
    // lại hàng nghìn/hàng chục nghìn file nhiều lần trong cùng một thao tác.
    private readonly Dictionary<string, FolderFileCache> _audioFolderCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FolderFileCache> _videoFolderCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DurationCacheEntry> _videoDurationCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _videoDurationCacheLock = new();
    private Dictionary<string, double>? _runDurationSnapshot;
    private bool? _nvencAvailable;
    private bool? _cudaPipelineAvailable;
    // p6 + AQ dùng nhiều năng lực GPU hơn p4 (chất lượng cao hơn ở cùng tốc độ thực tế do NVENC rảnh tay);
    // nếu GPU cũ không hỗ trợ, ffmpeg lỗi ngay và pipeline tự rơi về NVENC cơ bản/CPU như cũ.
    // maxrate/bufsize = chuẩn YouTube 1080p (8 Mbps khung hình thường) — CHUẨN HÓA VIDEO và render chính
    // dùng CHUNG mức này để video đã chuẩn hóa không cần nén lại khác đi khi vào bước render thật.
    private const string BitrateCapArgs = "-maxrate 8M -bufsize 16M";
    private const string NvencEncodeArgs =
        "-c:v h264_nvenc -preset p6 -rc vbr -cq 19 -b:v 0 -profile:v high -spatial-aq 1 -temporal-aq 1 -rc-lookahead 20 " + BitrateCapArgs;
    private MusicPoolDeck? _videoRandomDeck;
    private List<string>? _videoDeckSource;
    private readonly HashSet<string> _musicPlaylistSignatures = new(StringComparer.OrdinalIgnoreCase);

    private sealed record DurationCacheEntry(long FileLength, DateTime LastWriteUtc, double Duration,
        int Width = 0, int Height = 0);

    private sealed class FolderFileCache
    {
        public string Folder { get; init; } = string.Empty;
        public DateTime LastWriteUtc { get; init; }
        public List<string> Files { get; init; } = new();
    }

    private sealed class MusicPoolDeck
    {
        private readonly List<string> _source;
        private readonly Dictionary<string, long> _uses = new(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _previous = new(StringComparer.OrdinalIgnoreCase);
        private List<string> _previousOrder = new();

        public MusicPoolDeck(IEnumerable<string> files)
        {
            _source = files.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (string path in _source) _uses[path] = 0;
        }

        public int Count => _source.Count;

        public List<string> Take(int count, Random rng, ISet<string>? excluded = null)
        {
            if (count <= 0) return new List<string>();

            List<string> selected;
            if (count == 1)
            {
                // Reservoir sampling among equally fair candidates, O(N), without sorting.
                // Video sequences call this once per clip, so skip building an eligible list.
                long bestUses = long.MaxValue;
                int bestRecent = int.MaxValue;
                int tied = 0;
                string? chosen = null;
                foreach (string path in _source)
                {
                    if (excluded != null && excluded.Contains(path)) continue;
                    long uses = _uses[path];
                    int recent = _previous.Contains(path) ? 1 : 0;
                    if (uses < bestUses || (uses == bestUses && recent < bestRecent))
                    {
                        bestUses = uses;
                        bestRecent = recent;
                        tied = 0;
                    }
                    if (uses == bestUses && recent == bestRecent && rng.Next(++tied) == 0)
                        chosen = path;
                }
                if (chosen == null)
                    throw new InvalidOperationException("Không đủ file khác nhau trong nhóm. Kiểm tra các thư mục trùng nhau hoặc giảm số bài.");
                selected = new List<string> { chosen };
            }
            else
            {
                var eligible = _source.Where(path => excluded == null || !excluded.Contains(path)).ToList();
                if (count > eligible.Count)
                    throw new InvalidOperationException("Không đủ file khác nhau trong nhóm. Kiểm tra các thư mục trùng nhau hoặc giảm số bài.");
                // First balance usage, then avoid the preceding playlist where possible.
                selected = ShuffleCopy(eligible, rng).OrderBy(path => _uses[path])
                    .ThenBy(path => _previous.Contains(path) ? 1 : 0).Take(count).ToList();
                selected = ShuffleCopy(selected, rng);
                if (selected.SequenceEqual(_previousOrder, StringComparer.OrdinalIgnoreCase))
                    (selected[0], selected[1]) = (selected[1], selected[0]);
            }
            foreach (string path in selected) _uses[path]++;
            _previous = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
            _previousOrder = selected.ToList();
            return selected;
        }
    }
    private static readonly string[] VideoExtensions =
    {
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v", ".wmv", ".ts"
    };

    private static readonly string[] AudioExtensions =
    {
        ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma"
    };

    // =========================================================
    // THEME COLORS
    // =========================================================
    private Color Bg => _darkTheme ? Color.FromArgb(12, 15, 20) : Color.FromArgb(244, 247, 251);
    private Color Bg2 => _darkTheme ? Color.FromArgb(12, 15, 20) : Color.FromArgb(232, 237, 246);
    private Color Card => _darkTheme ? Color.FromArgb(22, 27, 35) : Color.White;
    private Color Card2 => _darkTheme ? Color.FromArgb(27, 36, 62) : Color.FromArgb(246, 248, 252);
    private Color Input => _darkTheme ? Color.FromArgb(15, 19, 26) : Color.FromArgb(239, 243, 249);
    private Color LogBg => _darkTheme ? Color.FromArgb(15, 19, 26) : Color.FromArgb(247, 249, 252);
    private Color TextMain => _darkTheme ? Color.FromArgb(248, 250, 252) : Color.FromArgb(25, 31, 42);
    private Color TextMuted => _darkTheme ? Color.FromArgb(165, 177, 194) : Color.FromArgb(91, 103, 122);
    private static readonly Color Cyan = Color.FromArgb(89, 220, 163);
    private static readonly Color Blue = Color.FromArgb(59, 130, 246);
    private static readonly Color Purple = Color.FromArgb(139, 92, 246);
    private static readonly Color Pink = Color.FromArgb(236, 72, 153);
    private static readonly Color Green = Color.FromArgb(34, 197, 94);
    private static readonly Color Red = Color.FromArgb(239, 68, 68);
    private static readonly Color Orange = Color.FromArgb(251, 146, 60);

    private sealed class AppSettings
    {
        public string SuperMusicPath { get; set; } = "";
        public string HayMusicPath { get; set; } = "";
        public string KhoNhacMusicPath { get; set; } = "";
        public string KhoMusicPath { get; set; } = "";
        public string VideoSourcePath { get; set; } = "";
        public string EffectSourcePath { get; set; } = "";
        public bool EffectGreenScreen { get; set; } = true;
        public int EffectBackgroundMode { get; set; }
        public string EffectCacheDirectory { get; set; } = "";
        public string DirectAudioPath { get; set; } = "";
        public string MusicOutputPath { get; set; } = "";
        public string VideoOutputPath { get; set; } = "";
        public decimal OnSeconds { get; set; } = 2;
        public decimal OffSeconds { get; set; } = 8;
        public bool OpenMusic { get; set; } = true;
        public bool OpenVideo { get; set; } = true;
        public bool ReuseVideo { get; set; } = false;
        public bool FastStart { get; set; } = false;
        // v1.6.0: nền ảnh + bộ hiệu ứng có sẵn + công tắc hiệu ứng (thiếu trường = hành vi cũ).
        public int BackgroundSourceMode { get; set; }
        public string ImageSourcePath { get; set; } = "";
        public bool EffectEnabled { get; set; } = true;
        public string EffectPackName { get; set; } = "";
        public string EffectPresetsRoot { get; set; } = "";
    }

    private void LoadSettings()
    {
        if (!File.Exists(SettingsFile)) return;

        try
        {
            _loadingSettings = true;
            string json = File.ReadAllText(SettingsFile, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            if (settings == null) return;
            ApplySettings(settings);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Không thể đọc settings: " + ex.Message);
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        {
            SetIfNotBlank(txtSuper, settings.SuperMusicPath);
            SetIfNotBlank(txtHay, settings.HayMusicPath);
            SetIfNotBlank(txtKhoNhac, settings.KhoNhacMusicPath);
            SetIfNotBlank(txtKho, settings.KhoMusicPath);
            SetIfNotBlank(txtVideoSource, settings.VideoSourcePath);
            txtEffectSource.Text = settings.EffectSourcePath ?? "";
            chkEffectGreen.Checked = settings.EffectGreenScreen;
            cmbEffectBackground.SelectedIndex = Math.Clamp(settings.EffectBackgroundMode, 0, 2);
            _effectCacheDirectory = settings.EffectCacheDirectory ?? "";
            SetIfNotBlank(txtDirectAudio, settings.DirectAudioPath);
            SetIfNotBlank(txtMusicOutput, settings.MusicOutputPath);
            SetIfNotBlank(txtVideoOutput, settings.VideoOutputPath);

            SetNumericIfValid(numOnSeconds, settings.OnSeconds);
            SetNumericIfValid(numOffSeconds, settings.OffSeconds);

            chkOpenMusic.Checked = settings.OpenMusic;
            chkOpenVideo.Checked = settings.OpenVideo;
            chkReuseVideo.Checked = settings.ReuseVideo;
            chkFastStart.Checked = settings.FastStart;
            ApplyImageEffectSettings(settings);
        }
    }

    private AppSettings CaptureSettings()
    {
        {
            var captured = new AppSettings
            {
                SuperMusicPath = txtSuper.Text.Trim(),
                HayMusicPath = txtHay.Text.Trim(),
                KhoNhacMusicPath = txtKhoNhac.Text.Trim(),
                KhoMusicPath = txtKho.Text.Trim(),
                VideoSourcePath = txtVideoSource.Text.Trim(),
                EffectSourcePath = txtEffectSource.Text.Trim(),
                EffectGreenScreen = chkEffectGreen.Checked,
                EffectBackgroundMode = cmbEffectBackground.SelectedIndex,
                EffectCacheDirectory = _effectCacheDirectory,
                DirectAudioPath = txtDirectAudio.Text.Trim(),
                MusicOutputPath = txtMusicOutput.Text.Trim(),
                VideoOutputPath = txtVideoOutput.Text.Trim(),
                OnSeconds = numOnSeconds.Value,
                OffSeconds = numOffSeconds.Value,
                OpenMusic = chkOpenMusic.Checked,
                OpenVideo = chkOpenVideo.Checked,
                ReuseVideo = chkReuseVideo.Checked,
                FastStart = chkFastStart.Checked
            };
            CaptureImageEffectSettings(captured);
            return captured;
        }
    }

    private void SaveSettings()
    {
        if (_loadingSettings) return;

        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var settings = CaptureSettings();

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, options), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Không thể lưu settings: " + ex.Message);
        }
    }

    private static void SetIfNotBlank(TextBox box, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            box.Text = value;
    }

    private static void SetNumericIfValid(NumericUpDown control, decimal value)
    {
        if (value < control.Minimum) value = control.Minimum;
        if (value > control.Maximum) value = control.Maximum;
        control.Value = value;
    }

    public MainForm(bool startMinimized = false)
    {
        AppLog.PruneOld(30);
        AppLog.Write($"===== Render Music Pro v{AppVersion.Current} khởi động =====");
        Text = $"Render Music Pro · v{AppVersion.Current} · TDN Media";
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? System.Drawing.SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1080, 740);
        var workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(1440, workArea.Width), Math.Min(1040, workArea.Height));
        FormBorderStyle = FormBorderStyle.Sizable;
        DoubleBuffered = true;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5F);
        BackColor = Bg;
        ForeColor = TextMain;

        BuildUi();

        // Khôi phục các thiết lập cần nhớ khi mở app.
        // Không khôi phục CHẾ ĐỘ / SỐ BÀI-NHÓM / SỐ PLAYLIST / STT / TÊN PLAYLIST.
        LoadSettings();
        InitLan();
        UpdateLanButton();
        _darkTheme = DarkThemeOnly;
        ApplyTheme();
        FormClosing += HandleFormClosing;
        Disposed += (_, _) => { _metadataSaveTimer?.Dispose(); SavePersistentMetadataCache(); };
        Disposed += (_, _) => { _renderUiTimer?.Dispose(); _lifetimeCts.Cancel(); };

        _ffmpegPath = FindFfmpeg();
        UpdateFfmpegStatus();
        RefreshCounts();
        Shown += async (_, _) =>
        {
            BeginInvoke(() => { if (ActiveControl is TextBoxBase startupText) { startupText.SelectionLength = 0; startupText.SelectionStart = 0; } });
            _ = RefreshCacheSizeAsync();
            _ = InitializeEffectPacksAsync();
            await CheckLicenseAsync();
            if (!_lifetimeCts.IsCancellationRequested) await CheckForUpdatesAsync(isAutomatic: true);
        };
        // Tự khởi động cùng Windows (--minimized): mở thu nhỏ, không giành focus, để máy NHẬN sẵn sàng ngay
        // khi bật máy mà không che màn hình người dùng.
        if (startMinimized) WindowState = FormWindowState.Minimized;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var brush = new LinearGradientBrush(ClientRectangle, Bg, Bg2, 135F);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    // =========================================================
    // UI
    // =========================================================
    private void BuildUi()
    {
        SuspendLayout();
        var shell = new TableLayoutPanel
        {
            Name = "studioShell", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Padding = new Padding(24, 14, 24, 10), BackColor = Bg, Margin = Padding.Empty
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 148));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        Controls.Add(shell);
        BuildHeader(shell);
        var viewport = new Panel { Name = "configurationViewport", Dock = DockStyle.Fill,
            AutoScroll = true, BackColor = Bg, Margin = Padding.Empty, TabIndex = 1 };
        shell.Controls.Add(viewport, 0, 1);
        var root = new TableLayoutPanel
        {
            Name = "workspaceGrid", ColumnCount = 2, RowCount = 2,
            Padding = Padding.Empty, BackColor = Bg, Margin = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 310));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 336));
        viewport.Controls.Add(root);
        BuildMusicSettings(root);
        BuildCutAndVideoSettings(root);
        BuildAction(shell);
        BuildLog(shell);
        void FitWorkspace()
        {
            int minimumHeight = (int)Math.Ceiling(root.RowStyles.Cast<RowStyle>().Sum(row => row.Height));
            int width = Math.Max(0, viewport.ClientSize.Width -
                (minimumHeight > viewport.ClientSize.Height ? SystemInformation.VerticalScrollBarWidth : 0));
            var size = new Size(width, Math.Max(minimumHeight, viewport.ClientSize.Height));
            if (root.Size != size) root.Size = size;
        }
        viewport.ClientSizeChanged += (_, _) => FitWorkspace();
        viewport.Layout += (_, _) => FitWorkspace();
        root.DpiChangedAfterParent += (_, _) => FitWorkspace();
        FitWorkspace();
        ResumeLayout(true);
    }

    private void BuildHeader(TableLayoutPanel root)
    {
        var header = new Panel { Dock = DockStyle.Fill, BackColor = Bg, Margin = Padding.Empty };
        root.Controls.Add(header, 0, 0);
        root.SetColumnSpan(header, 1);
        var title = new Label
        {
            Text = "Render Music Pro", Font = new Font("Segoe UI Semibold", 24F),
            ForeColor = TextMain, AutoSize = true, Location = new Point(66, 0), Tag = "accent"
        };
        header.Controls.Add(new StudioBrandMark { Location = new Point(0, 5), Size = new Size(52, 52) });
        header.Controls.Add(title);
        var subtitle = new Label
        {
            Text = $"STUDIO  /  v{AppVersion.Current}   ·   Không gian sáng tạo âm nhạc",
            AutoSize = true, Location = new Point(69, 47), Tag = "muted",
            Font = new Font("Segoe UI", 9.25F), ForeColor = TextMuted
        };
        header.Controls.Add(subtitle);
        lblFfmpeg.AutoSize = true;
        lblFfmpeg.Font = new Font("Segoe UI", 8.5F);
        // Availability is shown in the render status/log; the header stays compact.
        lblFfmpeg.Visible = false;
        BuildLicensePanel(header);
        header.Resize += (_, _) => PositionLicensePanel(header);
    }


    private void BuildLicensePanel(Control header)
    {
        var panel = new CardPanel
        {
            Name = "licensePanel",
            FillColor = Card,
            BorderColor = Color.FromArgb(43, 52, 65),
            Radius = 10,
            Size = new Size(505, 64),
            BackColor = Card,
            Padding = new Padding(12, 8, 12, 8)
        };
        header.Controls.Add(panel);

        lblLicenseAccount.Text = "Tài khoản: CHƯA KÍCH HOẠT";
        lblLicenseAccount.AutoSize = true;
        lblLicenseAccount.Font = new Font("Segoe UI Semibold", 8.4F, FontStyle.Bold);
        lblLicenseAccount.ForeColor = TextMain;
        lblLicenseAccount.BackColor = Color.Transparent;
        panel.Controls.Add(lblLicenseAccount);

        lblLicenseStatus.Text = "Trạng thái: CHƯA KÍCH HOẠT";
        lblLicenseStatus.AutoSize = true;
        lblLicenseStatus.Font = new Font("Segoe UI", 8.1F);
        lblLicenseStatus.ForeColor = Orange;
        lblLicenseStatus.BackColor = Color.Transparent;
        panel.Controls.Add(lblLicenseStatus);

        btnCheckUpdate.Text = "↻  KIỂM TRA CẬP NHẬT";
        btnCheckUpdate.Size = new Size(180, 30);
        btnCheckUpdate.Font = new Font("Segoe UI Semibold", 8.2F, FontStyle.Bold);
        btnCheckUpdate.Click += async (_, _) => await CheckForUpdatesAsync(isAutomatic: false);
        panel.Controls.Add(btnCheckUpdate);

        txtLicenseKey.BorderStyle = BorderStyle.FixedSingle;
        txtLicenseKey.Font = new Font("Segoe UI", 8.3F);
        txtLicenseKey.Width = 155;
        txtLicenseKey.Height = 24;
        txtLicenseKey.PlaceholderText = "Nhập license key";
        txtLicenseKey.UseSystemPasswordChar = true;
        txtLicenseKey.BackColor = Input;
        txtLicenseKey.ForeColor = TextMain;
        panel.Controls.Add(txtLicenseKey);

        btnActivateLicense.Text = "KÍCH HOẠT";
        btnActivateLicense.Size = new Size(84, 26);
        btnActivateLicense.Click += async (_, _) => await ActivateLicenseAsync();
        panel.Controls.Add(btnActivateLicense);

        lnkAdminZalo.Text = "Liên hệ Admin qua Zalo";
        lnkAdminZalo.AutoSize = true;
        lnkAdminZalo.LinkColor = Cyan;
        lnkAdminZalo.ActiveLinkColor = Purple;
        lnkAdminZalo.Font = new Font("Segoe UI", 7.2F, FontStyle.Underline);
        lnkAdminZalo.Cursor = Cursors.Hand;
        lnkAdminZalo.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(ServiceConfig.AdminZaloUrl) { UseShellExecute = true }); }
            catch (Exception openError)
            {
                MessageBox.Show("Không mở được liên kết Zalo: " + openError.Message,
                    "RenderMusicPro", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
        panel.Controls.Add(lnkAdminZalo);

        PositionLicensePanel(header);
        SetLicenseState(false, "Chưa kiểm tra license.", null, false);
    }

    private void PositionLicensePanel(Control header)
    {
        var panel = header.Controls["licensePanel"];
        if (panel == null) return;
        int P(int value) => (int)Math.Round(value * header.DeviceDpi / 96F);
        panel.SetBounds(Math.Max(0, header.ClientSize.Width - P(505)), 0, P(505), P(64));
        lblLicenseAccount.Location = new Point(P(14), P(11));
        lblLicenseStatus.Location = new Point(P(14), P(36));
        btnCheckUpdate.SetBounds(P(310), P(15), P(180), P(34));
        txtLicenseKey.Location = new Point(P(245), P(8));
        btnActivateLicense.Location = new Point(P(406), P(7));
        lnkAdminZalo.Location = new Point(P(245), P(38));
    }

    private async Task CheckForUpdatesAsync(bool isAutomatic = false)
    {
        if (!btnCheckUpdate.Enabled || _lifetimeCts.IsCancellationRequested)
            return;

        btnCheckUpdate.Enabled = false;
        btnCheckUpdate.Text = "ĐANG KIỂM TRA";

        try
        {
            AddLog($"Kiểm tra cập nhật (LAN, dự phòng GitHub)... Phiên bản hiện tại: v{AppVersion.Current}", Cyan);

            if (string.IsNullOrWhiteSpace(LanUpdateService.GetFolder()))
            {
                if (isAutomatic)
                {
                    btnCheckUpdate.Text = "↻  KIỂM TRA CẬP NHẬT";
                    return;
                }
                using var picker = new FolderBrowserDialog { Description = "Chọn thư mục cập nhật (trên mạng LAN)" };
                if (picker.ShowDialog(this) != DialogResult.OK)
                {
                    btnCheckUpdate.Text = "↻  KIỂM TRA CẬP NHẬT";
                    return;
                }
                LanUpdateService.SetFolder(picker.SelectedPath);
            }

            IUpdateService activeService = _updateService;
            var release = await _updateService.GetLatestReleaseAsync(_lifetimeCts.Token);

            if (_lifetimeCts.IsCancellationRequested) return;

            if (release == null)
            {
                string lanDetail = string.IsNullOrWhiteSpace(_updateService.LastError)
                    ? "Không có chi tiết lỗi từ máy chủ."
                    : _updateService.LastError!;
                AddLog("Không đọc được cập nhật từ LAN (" + lanDetail + "); thử qua GitHub...", Orange);

                release = await _githubUpdateService.GetLatestReleaseAsync(_lifetimeCts.Token);
                if (_lifetimeCts.IsCancellationRequested) return;
                activeService = _githubUpdateService;

                if (release == null)
                {
                    string detail = string.IsNullOrWhiteSpace(_githubUpdateService.LastError)
                        ? lanDetail
                        : $"LAN: {lanDetail}\nGitHub: {_githubUpdateService.LastError}";
                    AddLog("Không đọc được thông tin cập nhật (cả LAN lẫn GitHub). " + detail, Orange);

                    // Kiểm tra tự động thì không làm phiền người dùng nếu cả 2 nguồn tạm thời không truy cập được.
                    if (!isAutomatic)
                    {
                        DialogResult chooseAgain = MessageBox.Show(
                            this,
                            "Ứng dụng chưa đọc được thông tin cập nhật (đã thử cả LAN lẫn GitHub).\n\n" +
                            "Chi tiết: " + detail + "\n\n" +
                            "Chọn lại thư mục cập nhật LAN?",
                            "Render Music Pro - Cập nhật",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning);

                        if (chooseAgain == DialogResult.Yes)
                            LanUpdateService.SetFolder("");
                    }

                    btnCheckUpdate.Text = "↻  KIỂM TRA CẬP NHẬT";
                    return;
                }

                AddLog($"Đọc được thông tin cập nhật qua GitHub (v{release.Version}).", Cyan);
            }

            Version current = Version.Parse(AppVersion.Current);

            if (release.Version <= current)
            {
                _availableUpdateVersion = null;
                _availableUpdateUrl = null;
                btnCheckUpdate.Text = "↻  KIỂM TRA CẬP NHẬT";

                AddLog($"Đang dùng phiên bản mới nhất: v{AppVersion.Current}.", Green);

                // Chỉ hiện thông báo "mới nhất" khi người dùng tự bấm nút.
                if (!isAutomatic)
                {
                    MessageBox.Show(
                        this,
                        $"Bạn đang sử dụng phiên bản mới nhất.\n\n" +
                        $"Phiên bản hiện tại: v{AppVersion.Current}",
                        "Render Music Pro - Cập nhật",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return;
            }

            _availableUpdateVersion = release.TagName;
            _availableUpdateUrl = release.ReleaseUrl;

            // Có bản mới: thay hẳn chữ "KIỂM TRA CẬP NHẬT".
            btnCheckUpdate.Text = $"⬆  {release.TagName}";

            AddLog($"Có phiên bản mới: {release.TagName}.", Green);

            string updateNotes = (release.Name ?? "").Trim();
            if (updateNotes.Length > 800) updateNotes = updateNotes[..800] + "…";
            string message =
                $"Đã có phiên bản mới của Render Music Pro.\n\n" +
                $"Phiên bản hiện tại: v{AppVersion.Current}\n" +
                $"Phiên bản mới: {release.TagName}\n\n" +
                (updateNotes.Length > 0 ? "Nội dung cập nhật:\n" + updateNotes + "\n\n" : "") +
                "Tải và cài đặt ngay? Ứng dụng sẽ đóng rồi tự mở lại sau khi cập nhật.";

            // Tự động kiểm tra khi mở app: nếu có bản mới thì thông báo ngay.
            // Kiểm tra thủ công cũng thông báo như bình thường.
            if (MessageBox.Show(
                    this,
                    message,
                    "Render Music Pro - Có bản cập nhật",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information) == DialogResult.Yes &&
                !string.IsNullOrWhiteSpace(release.DownloadUrl))
            {
                if (_isRendering)
                {
                    MessageBox.Show(
                        this,
                        "Hãy dừng render hiện tại trước khi cập nhật. File đầu ra đang được sử dụng.",
                        "Render Music Pro - Cập nhật",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                using var downloadDialog = new UpdateDownloadDialog(activeService, release);
                DialogResult downloadResult = downloadDialog.ShowDialog(this);
                if (downloadResult == DialogResult.OK &&
                    !string.IsNullOrWhiteSpace(downloadDialog.DownloadedPath))
                {
                    try
                    {
                        string? sha256 = release.AssetSha256;
                        UpdateApplier.Start(
                            downloadDialog.DownloadedPath,
                            release.Version.ToString(),
                            sha256);
                        AddLog($"Đã tải và xác minh v{release.TagName}; đang đóng app để cài cập nhật.", Green);
                        Close();
                    }
                    catch (Exception ex)
                    {
                        AddLog("Không khởi chạy được trình cập nhật: " + ex.Message, Orange);
                        MessageBox.Show(
                            this,
                            "Không khởi chạy được bước cài cập nhật. Ứng dụng hiện tại vẫn được giữ nguyên.\n\n" + ex.Message,
                            "Render Music Pro - Cập nhật",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }
                else if (downloadResult == DialogResult.Abort)
                {
                    string detail = downloadDialog.DownloadError?.Message ?? "Không rõ lỗi tải file.";
                    AddLog("Tải bản cập nhật thất bại: " + detail, Orange);
                    MessageBox.Show(
                        this,
                        "Không tải được bản cập nhật. Ứng dụng hiện tại vẫn được giữ nguyên.\n\n" + detail,
                        "Render Music Pro - Cập nhật",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AddLog("Kiểm tra cập nhật lỗi: " + ex.Message, Orange);

            if (!isAutomatic)
            {
                MessageBox.Show(
                    this,
                    "Không thể kiểm tra bản cập nhật.\n\n" + ex.Message,
                    "Render Music Pro - Cập nhật",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            btnCheckUpdate.Text = "↻  KIỂM TRA CẬP NHẬT";
        }
        finally
        {
            if (!IsDisposed) btnCheckUpdate.Enabled = true;
        }
    }

    private Task CheckLicenseAsync()
    {
        if (Licensing.IsActivated())
        {
            SetLicenseState(true, "ĐÃ KÍCH HOẠT", null, false);
            AddLog("✓ Đã kích hoạt trên máy này.", Green);
        }
        else
        {
            SetLicenseState(false, "Chưa kích hoạt", null, true);
        }
        return Task.CompletedTask;
    }

    private Task ActivateLicenseAsync()
    {
        string key = txtLicenseKey.Text.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            ShowError("Vui lòng nhập mã kích hoạt.");
            return Task.CompletedTask;
        }

        try
        {
            if (Licensing.Activate(key))
            {
                SetLicenseState(true, "ĐÃ KÍCH HOẠT", null, false);
                AddLog("✓ Kích hoạt thành công.", Green);
            }
            else
            {
                SetLicenseState(false, "Chưa kích hoạt", null, true);
                txtLicenseKey.Clear();
                ShowError("Mã không đúng. Hãy thử lại.");
            }
        }
        catch (Exception ex)
        {
            SetLicenseState(false, "Kích hoạt thất bại", null, true);
            ShowError(ex.Message);
        }
        return Task.CompletedTask;
    }

    private void SetLicenseState(bool valid, string status, string? account, bool showActivation)
    {
        _licenseValid = valid;
        lblLicenseAccount.Text = valid
            ? (string.Equals(account, "ADMIN", StringComparison.Ordinal) ? "Tài khoản: ADMIN" : "Tài khoản: ĐÃ KÍCH HOẠT")
            : "Tài khoản: CHƯA KÍCH HOẠT";
        lblLicenseStatus.Text = "Trạng thái: " + status;
        lblLicenseStatus.ForeColor = valid ? Green : Orange;
        txtLicenseKey.Visible = showActivation;
        if (valid) txtLicenseKey.Clear();
        btnActivateLicense.Visible = showActivation;
        lnkAdminZalo.Visible = showActivation;

        // Khi đã kích hoạt: chỉ hiển thị nút kiểm tra cập nhật ở cột phải.
        btnCheckUpdate.Visible = !showActivation;

        // Khi chưa kích hoạt: cột phải dành cho license key.
        btnStart.Enabled = valid && (_cts == null || _lanWorkerBusy);
    }

    private static string FormatLicenseDate(string? value)
    {
        if (DateTime.TryParse(value, out var dt)) return dt.ToLocalTime().ToString("dd/MM/yyyy");
        return value ?? "";
    }

    private void BuildMusicSettings(TableLayoutPanel root)
    {
        var libraryCard = MakeCard();
        libraryCard.Margin = new Padding(0, 0, 8, 16);
        _cards.Add(libraryCard);
        _configurationCards.Add(libraryCard);
        root.Controls.Add(libraryCard, 0, 0);
        var library = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6,
            Padding = new Padding(22, 14, 22, 16), BackColor = Color.Transparent
        };
        library.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        library.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        library.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        for (int i = 1; i < 6; i++) library.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        libraryCard.Controls.Add(library);
        AddSectionTitle(library, 0, "01   Thư viện nhạc", 2);
        AddPathField(library, 1, "Siêu hay", txtSuper, "", () => BrowseMusic(txtSuper));
        AddPathField(library, 2, "Hay", txtHay, "", () => BrowseMusic(txtHay));
        AddPathField(library, 3, "Kho nhạc", txtKhoNhac, "", () => BrowseMusic(txtKhoNhac));
        AddPathField(library, 4, "Kho", txtKho, "", () => BrowseMusic(txtKho));
        AddPathField(library, 5, "Audio có sẵn", txtDirectAudio, "", BrowseDirectAudio);
        txtSuper.Name = "Siêu hay"; txtHay.Name = "Hay";
        txtKhoNhac.Name = "Kho nhạc"; txtKho.Name = "Kho";
        txtSuper.Text = Path.Combine(AppContext.BaseDirectory, "SIEU-HAY");
        txtHay.Text = Path.Combine(AppContext.BaseDirectory, "HAY");
        txtKhoNhac.Text = Path.Combine(AppContext.BaseDirectory, "Kho-Nhac");
        txtKho.Text = Path.Combine(AppContext.BaseDirectory, "KHO");
        _themedInputs.AddRange(new[] { txtSuper, txtHay, txtKhoNhac, txtKho, txtDirectAudio, txtPlaylistName });
        foreach (var input in new[] { txtSuper, txtHay, txtKhoNhac, txtKho, txtDirectAudio })
            input.Leave += (_, _) => RefreshCounts();

        var playlistCard = MakeCard();
        playlistCard.Margin = new Padding(8, 0, 0, 16);
        _cards.Add(playlistCard);
        _configurationCards.Add(playlistCard);
        root.Controls.Add(playlistCard, 1, 0);
        var settings = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6,
            Padding = new Padding(22, 14, 22, 16), BackColor = Color.Transparent
        };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] {52, 48, 23, 66, 70})
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        playlistCard.Controls.Add(settings);
        AddSectionTitle(settings, 0, "02   Thiết lập playlist", 1);
        var mode = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Color.Transparent };
        mode.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mode.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        mode.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddLabel(mode, 0, 0, "Chế độ");
        cmbMode.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbMode.AccessibleName = "Chế độ tạo playlist";
        cmbMode.Items.AddRange(new object[] { "Random 2 tổ hợp", "Random 3 tổ hợp", "Random 4 tổ hợp", "Audio có sẵn (không Random)" });
        cmbMode.SelectedIndex = 0;
        StyleCombo(cmbMode);
        mode.Controls.Add(MakeInputField(cmbMode, Padding.Empty), 1, 0);
        settings.Controls.Add(mode, 0, 1);
        cmbMode.SelectedIndexChanged += (_, _) => UpdateModeState();
        AddLabel(settings, 2, 0, "Số bài / nhóm");
        var counts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty, BackColor = Color.Transparent };
        for (int i = 0; i < 4; i++) counts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        counts.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        counts.Controls.Add(MakeCountBox("Siêu hay", numSuper), 0, 0);
        counts.Controls.Add(MakeCountBox("Hay", numHay), 1, 0);
        counts.Controls.Add(MakeCountBox("Kho nhạc", numKhoNhac), 2, 0);
        counts.Controls.Add(MakeCountBox("Kho", numKho), 3, 0);
        settings.Controls.Add(counts, 0, 3);

        var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, BackColor = Color.Transparent };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        details.Controls.Add(MakeLabeledField("Số playlist", MakeNumeric(numPlaylists, 1, 100000, 10)), 0, 0);
        details.Controls.Add(MakeLabeledField("STT bắt đầu", MakeNumeric(numStartNumber, 1, 1000000, 1)), 1, 0);
        txtPlaylistName.Text = "Playlist";
        details.Controls.Add(MakeLabeledField("Tên playlist", MakeTextField(txtPlaylistName)), 2, 0);
        settings.Controls.Add(details, 0, 4);
        UpdateModeState();
    }

    private Control MakeLabeledField(string caption, Control input)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0, 2, 8, 2), BackColor = Color.Transparent };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddLabel(grid, 0, 0, caption);
        input.Margin = Padding.Empty;
        grid.Controls.Add(input, 0, 1);
        return grid;
    }
    private void BuildCutAndVideoSettings(TableLayoutPanel root)
    {
        var videoCard = MakeCard();
        videoCard.Margin = new Padding(0, 0, 8, 16);
        _configurationCards.Add(videoCard);
        _cards.Add(videoCard);
        root.Controls.Add(videoCard, 0, 1);

        var videoGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 6,
            Padding = new Padding(22, 14, 22, 16),
            BackColor = Color.Transparent
        };
        videoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        videoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        videoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        videoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        videoGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        videoGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        videoGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        videoGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        videoGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        videoGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        videoCard.Controls.Add(videoGrid);

        AddSectionTitle(videoGrid, 0, "03   Video & hiệu ứng", 4);
        AddLabel(videoGrid, 1, 0, "Nguồn nền");
        var videoPathPanel = MakePathInput(
            txtVideoSource,
            "📁",
            BrowseVideoSource);
        txtVideoSource.Text = Path.Combine(AppContext.BaseDirectory, "VideoChuanHoa");
        _themedInputs.Add(txtVideoSource);
        var backgroundRow = BuildBackgroundSourceRow(videoPathPanel);
        videoGrid.Controls.Add(backgroundRow, 1, 1);
        videoGrid.SetColumnSpan(backgroundRow, 3);

        AddLabel(videoGrid, 2, 0, "Hiệu ứng");
        var effectsPath = MakePathInput(txtEffectSource, "📁", BrowseEffectSource);
        txtEffectSource.PlaceholderText = "Thư mục hiệu ứng (không bắt buộc)";
        txtEffectSource.AccessibleName = "Thư mục hiệu ứng, không bắt buộc";
        _themedInputs.Add(txtEffectSource);
        var effectRow = BuildEffectSourceRow(effectsPath);
        videoGrid.Controls.Add(effectRow, 1, 2);
        videoGrid.SetColumnSpan(effectRow, 3);
        AddLabel(videoGrid, 3, 0, "Có nhạc (s)");
        videoGrid.Controls.Add(MakeNumeric(numOnSeconds, 1, 3600, 2), 1, 3);
        AddLabel(videoGrid, 3, 2, "Im lặng (s)");
        videoGrid.Controls.Add(MakeNumeric(numOffSeconds, 1, 3600, 8), 3, 3);

        chkReuseVideo.Text = "Tự lặp video khi không đủ clip";
        chkReuseVideo.Checked = true;
        StyleCheck(chkReuseVideo);
        videoGrid.Controls.Add(chkReuseVideo, 0, 4);
        videoGrid.SetColumnSpan(chkReuseVideo, 4);

        chkEffectGreen.Text = "Tách nền";
        chkEffectGreen.Checked = true;
        StyleCheck(chkEffectGreen);
        // Bật/tắt "Tách nền" theo nguồn hiệu ứng + công tắc hiệu ứng (UpdateEffectSourceUi, MainForm.ImageEffect.cs).
        UpdateEffectSourceUi();
        UpdateBackgroundSourceUi();
        cmbEffectBackground.Items.AddRange(new object[] { "Tự nhận diện", "Nền xanh", "Nền đen" });
        cmbEffectBackground.SelectedIndex = 0;
        cmbEffectBackground.DropDownStyle = ComboBoxStyle.DropDownList;
        StyleCombo(cmbEffectBackground);
        cmbEffectBackground.Dock = DockStyle.None;
        cmbEffectBackground.Width = 128;
        cmbEffectBackground.Margin = new Padding(4, 5, 6, 5);
        cmbEffectBackground.Enabled = chkEffectGreen.Checked && chkEffectGreen.Enabled;
        chkEffectGreen.CheckedChanged += (_, _) => cmbEffectBackground.Enabled = chkEffectGreen.Checked && chkEffectGreen.Enabled;
        chkEffectGreen.EnabledChanged += (_, _) => cmbEffectBackground.Enabled = chkEffectGreen.Checked && chkEffectGreen.Enabled;
        var effectOptions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            BackColor = Color.Transparent, Margin = Padding.Empty };
        effectOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        effectOptions.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        effectOptions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var backgroundOptions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false,
            BackColor = Color.Transparent, Margin = Padding.Empty };
        backgroundOptions.Controls.Add(chkEffectEnabled);
        backgroundOptions.Controls.Add(chkEffectGreen);
        backgroundOptions.Controls.Add(cmbEffectBackground);
        var cacheOptions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false,
            BackColor = Color.Transparent, Margin = Padding.Empty };
        AddCacheControls(cacheOptions);
        effectOptions.Controls.Add(backgroundOptions, 0, 0);
        effectOptions.Controls.Add(cacheOptions, 0, 1);
        videoGrid.Controls.Add(effectOptions, 0, 5);
        videoGrid.SetColumnSpan(effectOptions, 4);

        var outputCard = MakeCard();
        outputCard.Margin = new Padding(8, 0, 0, 16);
        _configurationCards.Add(outputCard);
        _cards.Add(outputCard);
        root.Controls.Add(outputCard, 1, 1);

        var outputGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(22, 14, 22, 16),
            BackColor = Color.Transparent
        };
        outputGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        outputGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outputGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        outputGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        outputGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        outputGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        outputGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outputCard.Controls.Add(outputGrid);

        AddSectionTitle(outputGrid, 0, "04   Thư mục đầu ra", 2);
        AddLabel(outputGrid, 1, 0, "Nhạc");
        txtMusicOutput.Text = Path.Combine(AppContext.BaseDirectory, "Music");
        outputGrid.Controls.Add(MakePathInput(txtMusicOutput, "📁", BrowseMusicOutput), 1, 1);
        _themedInputs.Add(txtMusicOutput);

        AddLabel(outputGrid, 2, 0, "Video");
        txtVideoOutput.Text = Path.Combine(AppContext.BaseDirectory, "Video");
        outputGrid.Controls.Add(MakePathInput(txtVideoOutput, "📁", BrowseVideoOutput), 1, 2);
        _themedInputs.Add(txtVideoOutput);

        chkOpenMusic.Text = "Mở nhạc khi xong";
        chkOpenVideo.Text = "Mở video khi xong";
        chkOpenMusic.Checked = true;
        chkOpenVideo.Checked = true;
        // faststart rewrites the whole MP4 once more after encoding; YouTube does not need it.
        chkFastStart.Text = "Tối ưu web (chậm hơn)";
        chkFastStart.Checked = false;
        StyleCheck(chkOpenMusic);
        StyleCheck(chkOpenVideo);
        StyleCheck(chkFastStart);

        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0, 8, 0, 0)
        };
        options.Controls.Add(chkOpenMusic);
        options.Controls.Add(chkOpenVideo);
        options.SetFlowBreak(chkOpenVideo, true);
        options.Controls.Add(chkFastStart);
        outputGrid.Controls.Add(options, 0, 3);
        outputGrid.SetColumnSpan(options, 2);
        AddPresetControls(outputGrid, 4);
    }
    private void BuildAction(TableLayoutPanel root)
    {
        var card = MakeCard(); _cards.Add(card);
        root.Controls.Add(card, 0, 2); root.SetColumnSpan(card, 1);
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(18, 10, 18, 10), BackColor = Color.Transparent };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 318));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(grid);
        lblStatus.Text = "●  Sẵn sàng render"; lblStatus.Font = new Font("Segoe UI Semibold", 11F);
        lblStatus.ForeColor = Green; lblStatus.Dock = DockStyle.Fill; lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        grid.Controls.Add(lblStatus, 0, 0);
        lblCurrent.Text = "Chọn nguồn nhạc và video, sau đó bắt đầu render.";
        ApplyMutedLabel(lblCurrent); lblCurrent.AutoSize = false;
        lblCurrent.Dock = DockStyle.Fill; lblCurrent.AutoEllipsis = true; lblCurrent.TextAlign = ContentAlignment.MiddleLeft;
        grid.Controls.Add(lblCurrent, 0, 1);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(10, 7, 0, 7), BackColor = Color.Transparent };
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        btnStart.Text = "Bắt đầu render"; btnStart.Symbol = StudioSymbol.Play; btnStart.Dock = DockStyle.Fill; btnStart.Margin = new Padding(0, 0, 10, 0);
        btnStart.Font = new Font("Segoe UI Semibold", 11F);
        btnStart.Click += async (_, _) => await StartRenderAsync();
        btnStop.Text = "Dừng"; btnStop.Symbol = StudioSymbol.Stop; btnStop.Dock = DockStyle.Fill; btnStop.Margin = Padding.Empty;
        btnStop.Font = new Font("Segoe UI Semibold", 9.5F); btnStop.Enabled = false;
        btnStop.Click += (_, _) => StopRender();
        buttons.Controls.Add(btnStart, 0, 0); buttons.Controls.Add(btnStop, 1, 0);
        grid.Controls.Add(buttons, 1, 0); grid.SetRowSpan(buttons, 2);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, BackColor = Color.Transparent };
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        var stats = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Left, WrapContents = false, Margin = new Padding(0, 0, 16, 0), BackColor = Color.Transparent };
        foreach (var label in new[] { lblMusicCount, lblVideoCount, lblOutputCount }) { StyleStatLabel(label); stats.Controls.Add(label); }
        footer.Controls.Add(stats, 0, 0);
        progress.Minimum = 0; progress.Maximum = 100;
        progress.Dock = DockStyle.Fill; progress.Margin = new Padding(0, 9, 0, 9);
        footer.Controls.Add(progress, 1, 0);
        lblPercent.Text = "0%"; lblPercent.Font = new Font("Segoe UI Semibold", 9F); lblPercent.ForeColor = Cyan;
        lblPercent.Dock = DockStyle.Fill; lblPercent.TextAlign = ContentAlignment.MiddleRight;
        footer.Controls.Add(lblPercent, 2, 0);
        ApplyMutedLabel(_renderTimingLabel);
        _renderTimingLabel.Text = "Đã chạy: 00:00:00   ·   Dự kiến hoàn thành: —";
        _renderTimingLabel.Dock = DockStyle.Fill;
        _renderTimingLabel.TextAlign = ContentAlignment.MiddleLeft;
        _renderTimingLabel.AutoEllipsis = true;
        grid.Controls.Add(_renderTimingLabel, 0, 2); grid.SetColumnSpan(_renderTimingLabel, 2);
        grid.Controls.Add(footer, 0, 3); grid.SetColumnSpan(footer, 2);
    }

    private void StyleStatLabel(Label label)
    {
        label.AutoSize = true;
        label.Padding = new Padding(8, 3, 8, 3);
        label.Margin = new Padding(0, 0, 8, 0);
        label.Font = new Font("Segoe UI", 8.5F);
        label.ForeColor = TextMuted;
        label.BackColor = Input;
    }

    private void BuildLog(TableLayoutPanel root)
    {
        var card = MakeCard(); _cards.Add(card); card.Margin = Padding.Empty;
        root.Controls.Add(card, 0, 3); root.SetColumnSpan(card, 1);
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(16, 8, 16, 12), BackColor = Color.Transparent };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(grid);
        var title = new Label { Text = "Nhật ký xử lý", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 10F), ForeColor = TextMain };
        grid.Controls.Add(title, 0, 0);
        txtLog.Dock = DockStyle.Fill; txtLog.ReadOnly = true; txtLog.BorderStyle = BorderStyle.None;
        txtLog.DetectUrls = false; txtLog.Font = new Font("Cascadia Mono", 9F); txtLog.Margin = new Padding(0, 4, 0, 0);
        grid.Controls.Add(txtLog, 0, 1);
        AddLog("Render Music Pro đã sẵn sàng.", Cyan);
        AddLog("Chọn nguồn nhạc, kho video và thư mục đầu ra để bắt đầu.", TextMuted);
    }

    // =========================================================
    // THEME / STYLE
    // =========================================================
    private CardPanel MakeCard()
    {
        return new CardPanel
        {
            Dock = DockStyle.Fill,
            Radius = 16,
            Margin = new Padding(0, 0, 0, 12)
        };
    }

    private void ApplyTheme()
    {
        BackColor = Bg;
        ForeColor = TextMain;

        foreach (var card in _cards)
        {
            card.FillColor = Card;
            card.BorderColor = _darkTheme ? Color.FromArgb(43, 52, 65) : Color.FromArgb(216, 222, 233);
            card.Invalidate();
        }

        foreach (var input in _themedInputs)
        {
            if (input is TextBox tb)
            {
                tb.BackColor = Input;
                tb.ForeColor = TextMain;
            }
            else if (input is NumericUpDown nu)
            {
                nu.BackColor = Input;
                nu.ForeColor = TextMain;
            }
        }

        txtLog.BackColor = LogBg;
        txtLog.ForeColor = TextMuted;
        cmbMode.BackColor = Input;
        cmbMode.ForeColor = TextMain;
        progress.BackColor = Input;

        foreach (var c in GetAllControls(this))
        {
            if (c is Label label && c != lblStatus && c != lblFfmpeg && c != lblPercent)
            {
                // Keep explicit accent/status colors intact.
                if (label.Tag as string == "muted") label.ForeColor = TextMuted;
                else if (label.Tag as string == "label") label.ForeColor = TextMuted;
                else if (label.Tag as string != "accent" && label != lblLicenseStatus) label.ForeColor = TextMain;
            }
        }

        btnCheckUpdate.Color1 = Color.FromArgb(33, 42, 54);
        btnCheckUpdate.Color2 = btnCheckUpdate.Color1;
        btnCheckUpdate.HoverColor1 = Color.FromArgb(47, 61, 76);
        btnCheckUpdate.HoverColor2 = btnCheckUpdate.HoverColor1;
        btnCheckUpdate.ForeColor = TextMain;
        StyleActionButtons();
        StyleCheck(chkOpenMusic);
        StyleCheck(chkOpenVideo);
        StyleCheck(chkReuseVideo);
        StyleCheck(chkFastStart);
        Invalidate(true);
    }

    private IEnumerable<Control> GetAllControls(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            yield return c;
            foreach (Control child in GetAllControls(c)) yield return child;
        }
    }

    private void StyleActionButtons()
    {
        btnStart.Color1 = Color.FromArgb(89, 220, 163); btnStart.Color2 = Color.FromArgb(63, 199, 143);
        btnStart.HoverColor1 = Color.FromArgb(119, 236, 184); btnStart.HoverColor2 = Color.FromArgb(89, 220, 163);
        btnStart.ForeColor = Color.FromArgb(10, 34, 26);
        btnStop.Color1 = Color.FromArgb(73, 38, 48); btnStop.Color2 = btnStop.Color1;
        btnStop.HoverColor1 = Color.FromArgb(123, 46, 60); btnStop.HoverColor2 = btnStop.HoverColor1;
        btnStop.ForeColor = Color.FromArgb(255, 164, 170);
    }

    private void ApplyLabelStyle(Label label)
    {
        label.Tag = "label";
        label.Font = new Font("Segoe UI", 9.25F);
        label.Margin = Padding.Empty;
        label.ForeColor = TextMuted;
        label.TextAlign = ContentAlignment.MiddleLeft;
    }

    private void ApplyMutedLabel(Label label)
    {
        label.Tag = "muted";
        label.Font = new Font("Segoe UI", 9F);
        label.ForeColor = TextMuted;
    }

    private void StyleTextBox(TextBox tb)
    {
        tb.AutoSize = true; tb.BorderStyle = BorderStyle.None;
        tb.Font = new Font("Segoe UI", 9.5F); tb.Dock = DockStyle.None;
        tb.Margin = Padding.Empty; tb.BackColor = Input; tb.ForeColor = TextMain;
    }

    private void StyleCombo(ComboBox combo)
    {
        combo.FlatStyle = FlatStyle.Flat; combo.Dock = DockStyle.None;
        combo.Margin = Padding.Empty; combo.Font = new Font("Segoe UI", 10F);
    }

    private void StyleCheck(CheckBox check)
    {
        check.AutoSize = true;
        check.Cursor = Cursors.Hand;
        check.ForeColor = TextMuted;
        check.BackColor = Color.Transparent;
        check.Font = new Font("Segoe UI", 9F);
        check.Margin = new Padding(0, 3, 12, 3);
    }

    private void AddSectionTitle(TableLayoutPanel grid, int row, string text, int span)
    {
        string number = text[..2];
        string title = text[2..].Trim();
        string hint = number switch
        {
            "01" => "Chọn các kho nhạc để phối playlist.",
            "02" => "Tùy chỉnh tổ hợp và số lượng đầu ra.",
            "03" => "Nguồn hình ảnh, nhịp cắt và lớp hiệu ứng.",
            _ => "Lưu thành phẩm và quản lý cấu hình."
        };
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
            Margin = Padding.Empty, BackColor = Color.Transparent };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var badge = new StudioStepBadge { Text = number, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 10) };
        heading.Controls.Add(badge, 0, 0); heading.SetRowSpan(badge, 2);
        heading.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 12F), ForeColor = TextMain,
            Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
        heading.Controls.Add(new Label { Text = hint, Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9F), ForeColor = TextMuted, Tag = "muted",
            Margin = Padding.Empty, TextAlign = ContentAlignment.TopLeft, AutoEllipsis = true }, 1, 1);
        grid.Controls.Add(heading, 0, row);
        grid.SetColumnSpan(heading, span);
    }

    private void AddLabel(TableLayoutPanel grid, int row, int col, string text)
    {
        var label = new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        ApplyLabelStyle(label);
        grid.Controls.Add(label, col, row);
    }

    private void AddPathField(TableLayoutPanel grid, int row, string label, TextBox textBox, string buttonText, Action browse)
    {
        textBox.AccessibleName = "Đường dẫn " + label;
        AddLabel(grid, row, 0, label);
        var panel = MakePathInput(textBox, buttonText, browse);
        grid.Controls.Add(panel, 1, row);
        grid.SetColumnSpan(panel, 1);
    }

    private Control MakePathInput(TextBox tb, string buttonText, Action browse)
    {
        StyleTextBox(tb);
        tb.PlaceholderText = "Chọn hoặc dán đường dẫn…";
        var button = new RoundedButton
        {
            FolderIcon = true, AccessibleName = "Chọn thư mục hoặc tệp",
            Color1 = Color.FromArgb(29, 43, 62), Color2 = Color.FromArgb(29, 43, 62),
            HoverColor1 = Color.FromArgb(42, 62, 83), HoverColor2 = Color.FromArgb(42, 62, 83),
            ForeColor = Cyan, BackColor = Input, TabStop = true
        };
        button.Click += (_, _) => browse();
        button.Enabled = tb.Enabled;
        tb.EnabledChanged += (_, _) => button.Enabled = tb.Enabled;
        return new StudioInputHost(tb, button) { Dock = DockStyle.Fill, Margin = Padding.Empty };
    }

    private Control MakeNumeric(NumericUpDown numeric, int min, int max, int value)
    {
        numeric.AutoSize = false;
        numeric.Minimum = min;
        numeric.Maximum = max;
        if (numeric.Parent is StudioInputHost host) host.RefreshState();
        numeric.Value = Math.Clamp(value, min, max);
        numeric.DecimalPlaces = 0;
        numeric.ThousandsSeparator = true;
        numeric.BorderStyle = BorderStyle.None;
        numeric.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
        numeric.BackColor = Input;
        numeric.ForeColor = TextMain;
        numeric.TextAlign = HorizontalAlignment.Center;
        numeric.Margin = Padding.Empty;

        var field = MakeInputField(numeric, new Padding(0, 0, 8, 0));
        _themedInputs.Add(numeric);
        return field;
    }

    private Control MakeTextField(TextBox textBox)
    {
        textBox.AutoSize = false;
        textBox.BorderStyle = BorderStyle.None;
        textBox.Font = new Font("Segoe UI", 9.5F);
        textBox.Dock = DockStyle.None;
        textBox.Margin = Padding.Empty;
        textBox.BackColor = Input;
        textBox.ForeColor = TextMain;

        return MakeInputField(textBox, Padding.Empty);
    }

    private Control MakeInputField(Control input, Padding margin)
    {
        return new StudioInputHost(input) { Dock = DockStyle.Fill, Margin = margin };
    }

    private Control MakeCountBox(string caption, NumericUpDown numeric)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent, Margin = new Padding(0, 0, 8, 2) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var label = new Label { Text = caption + " · 0", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 8.75F), ForeColor = TextMuted, Tag = "muted", Margin = Padding.Empty, AutoEllipsis = true };
        panel.Controls.Add(label, 0, 0); _poolCountLabels[numeric] = label;
        var input = MakeCompactNumeric(numeric, 0);
        input.AccessibleName = "Số bài nhóm " + caption;
        numeric.EnabledChanged += (_, _) => label.ForeColor = numeric.Enabled ? TextMuted : Color.FromArgb(101, 117, 139);
        panel.Controls.Add(new StudioInputHost(input) { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 1);
        return panel;
    }

    private Control MakeCompactNumeric(NumericUpDown numeric, int value)
    {
        numeric.Minimum = 0;
        // Maximum thực tế sẽ được RefreshMusicPoolLimits() thay đổi theo số audio
        // trong thư mục tương ứng. 100000 chỉ là giá trị khởi tạo an toàn.
        numeric.Maximum = 100000;
        numeric.Value = value;
        numeric.Width = 82;
        numeric.Height = 26;
        numeric.DecimalPlaces = 0;
        numeric.BorderStyle = BorderStyle.None;
        numeric.BackColor = Input;
        numeric.ForeColor = TextMain;
        numeric.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        numeric.TextAlign = HorizontalAlignment.Center;
        numeric.Margin = Padding.Empty;
        _themedInputs.Add(numeric);
        return numeric;
    }

    private void UpdateModeState()
    {
        if (cmbMode.SelectedIndex < 0) return;

        bool directAudioMode = cmbMode.SelectedIndex == 3;

        ApplyInputEnabledState();

        if (cmbMode.SelectedIndex == 0) numSuper.Value = 0;
        if (cmbMode.SelectedIndex < 2) numKho.Value = 0;

        if (directAudioMode)
        {
            lblCurrent.Text = "Chế độ Audio có sẵn: audio được copy nguyên bản vào Music, bản CUT chỉ dùng để ghép Video.";
        }
        else
        {
            lblCurrent.Text = "Kiểm tra thư mục nhạc và kho video rồi bấm BẮT ĐẦU RENDER.";
        }

        if (!_loadingSettings)
            RefreshCounts();
    }

    private void BrowseMusic(TextBox target)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Chọn thư mục nhạc: " + target.Name,
            ShowNewFolderButton = false,
            UseDescriptionForTitle = true
        };
        if (Directory.Exists(target.Text)) dialog.SelectedPath = target.Text;
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            target.Text = dialog.SelectedPath;
            RefreshCounts();
            AddLog($"✓ Đã chọn kho {target.Name}: {dialog.SelectedPath}", Cyan);
        }
    }

    private void BrowseDirectAudio()
    {
        // Cho phép chọn cả FILE hoặc cả THƯ MỤC audio.
        // Chế độ thư mục sẽ chạy tuần tự hết toàn bộ audio rồi mới dừng.
        var menu = new ContextMenuStrip();

        var folderItem = new ToolStripMenuItem("📁  Chọn thư mục Audio");
        folderItem.Click += (_, _) =>
        {
            using var folder = new FolderBrowserDialog
            {
                Description = "Chọn thư mục Audio có sẵn để chạy tuần tự đến hết thư mục",
                ShowNewFolderButton = false,
                UseDescriptionForTitle = true
            };

            if (Directory.Exists(txtDirectAudio.Text.Trim()))
                folder.SelectedPath = txtDirectAudio.Text.Trim();

            if (folder.ShowDialog(this) == DialogResult.OK)
            {
                txtDirectAudio.Text = folder.SelectedPath;
                RefreshCounts();
                AddLog($"✓ Đã chọn thư mục Audio: {folder.SelectedPath}", Cyan);
            }
        };

        var fileItem = new ToolStripMenuItem("🎵  Chọn 1 file Audio");
        fileItem.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Chọn audio có sẵn",
                Filter = "Audio|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.wma|Tất cả|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (File.Exists(txtDirectAudio.Text.Trim()))
                dialog.FileName = txtDirectAudio.Text.Trim();

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                txtDirectAudio.Text = dialog.FileName;
                RefreshCounts();
                AddLog($"✓ Đã chọn Audio: {Path.GetFileName(dialog.FileName)}", Cyan);
            }
        };

        menu.Items.Add(folderItem);
        menu.Items.Add(fileItem);

        // Không dùng "using" ở đây: ContextMenuStrip.Show() chạy bất đồng bộ,
        // nếu dispose ngay khi BrowseDirectAudio() kết thúc thì menu vừa hiện sẽ bị hủy.
        // Closed fires while the Windows menu message filter is still unwinding.
        // Defer disposal until the current input message has finished processing.
        menu.Closed += (_, _) =>
        {
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(new Action(menu.Dispose));
            else
                menu.Dispose();
        };
        menu.Show(Cursor.Position);
    }

    private void BrowseVideoSource()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Chọn hoặc dán kho video đã chuẩn hóa",
            ShowNewFolderButton = false,
            UseDescriptionForTitle = true
        };
        // Có cấu hình Render LAN (thư mục Video vào): mặc định mở ngay trong đó để chọn đúng 1 kho con,
        // cho khớp yêu cầu "kho video phải nằm trong thư mục Video vào" khi bật chia bài LAN.
        if (Directory.Exists(txtVideoSource.Text)) dialog.SelectedPath = txtVideoSource.Text;
        else if (Directory.Exists(_lan.VideoRoot)) dialog.SelectedPath = _lan.VideoRoot;
        if (dialog.ShowDialog() == DialogResult.OK) txtVideoSource.Text = dialog.SelectedPath;
    }

    private void BrowseMusicOutput()
    {
        using var dialog = new FolderBrowserDialog { Description = "Chọn thư mục Music", ShowNewFolderButton = true, UseDescriptionForTitle = true };
        if (Directory.Exists(txtMusicOutput.Text)) dialog.SelectedPath = txtMusicOutput.Text;
        if (dialog.ShowDialog() == DialogResult.OK) txtMusicOutput.Text = dialog.SelectedPath;
    }

    private void BrowseVideoOutput()
    {
        using var dialog = new FolderBrowserDialog { Description = "Chọn thư mục Video", ShowNewFolderButton = true, UseDescriptionForTitle = true };
        if (Directory.Exists(txtVideoOutput.Text)) dialog.SelectedPath = txtVideoOutput.Text;
        if (dialog.ShowDialog() == DialogResult.OK) txtVideoOutput.Text = dialog.SelectedPath;
    }

    private void RefreshCounts()
    {
        // Quét từng kho riêng biệt. Không cộng gộp rồi dùng một giới hạn chung,
        // vì mỗi ô SỐ BÀI phải bị giới hạn đúng bằng số audio của chính kho đó.
        var counts = ScanMusicPoolCounts();
        ApplyPoolMaximum(numSuper, counts["SIEU-HAY"], "SH");
        ApplyPoolMaximum(numHay, counts["HAY"], "HAY");
        ApplyPoolMaximum(numKhoNhac, counts["Kho-Nhac"], "KHO-NHAC");
        ApplyPoolMaximum(numKho, counts["KHO"], "KHO");

        _musicCount = cmbMode.SelectedIndex == 3
            ? CountDirectAudioFiles()
            : GetActivePoolAudioCount(counts);

        _videoCount = CountVideoFiles(txtVideoSource.Text.Trim());
        lblMusicCount.Text = $"Nhạc nguồn: {_musicCount:N0}";
        lblVideoCount.Text = $"Video chuẩn hóa: {_videoCount:N0}";
        lblOutputCount.Text = $"Output: {(int)numPlaylists.Value:N0}";
    }

    private Dictionary<string, int> ScanMusicPoolCounts()
    {
        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["SIEU-HAY"] = GetAudioFiles(txtSuper.Text.Trim()).Count,
            ["HAY"] = GetAudioFiles(txtHay.Text.Trim()).Count,
            ["Kho-Nhac"] = GetAudioFiles(txtKhoNhac.Text.Trim()).Count,
            ["KHO"] = GetAudioFiles(txtKho.Text.Trim()).Count
        };
    }

    private int CountAudioFilesInFolder(string folder) => GetAudioFiles(folder).Count;

    private int CountDirectAudioFiles()
    {
        string source = txtDirectAudio.Text.Trim();
        if (File.Exists(source)) return IsAudioFile(source) ? 1 : 0;
        if (Directory.Exists(source)) return CountAudioFilesInFolder(source);
        return 0;
    }

    private int GetActivePoolAudioCount(Dictionary<string, int> counts)
    {
        int total = 0;
        if (cmbMode.SelectedIndex >= 1 && GetCount(numSuper) > 0) total += counts["SIEU-HAY"];
        if (GetCount(numHay) > 0) total += counts["HAY"];
        if (GetCount(numKhoNhac) > 0) total += counts["Kho-Nhac"];
        if (cmbMode.SelectedIndex >= 2 && GetCount(numKho) > 0) total += counts["KHO"];
        return total;
    }

    private void ApplyPoolMaximum(NumericUpDown numeric, int actualCount, string caption)
    {
        int max = Math.Max(0, actualCount);

        // Giảm Value trước khi giảm Maximum để không bao giờ rơi vào trạng thái
        // NumericUpDown có Value > Maximum.
        if (numeric.Value > max)
            numeric.Value = max;

        numeric.Maximum = max;
        if (numeric.Parent?.Parent is StudioInputHost host) host.RefreshState();

        if (_poolCountLabels.TryGetValue(numeric, out var label))
            label.Text = $"{(caption == "SH" ? "Siêu hay" : caption == "HAY" ? "Hay" : caption == "KHO-NHAC" ? "Kho nhạc" : "Kho")} · {actualCount:N0}";
    }

    private int CountVideoFiles(string folder)
    {
        return GetVideoFiles(folder).Count;
    }

    // =========================================================
    // MAIN PIPELINE
    // =========================================================
    // v1.5.1: cờ _renderStarting bật NGAY đầu (trước preflight/hộp thoại) để máy nhận LAN không giành phiếu xen vào;
    // đang render giúp máy khác thì hỏi, rồi hủy + chờ phiếu LAN dọn xong trước khi chạy lượt của mình.
    private async Task StartRenderAsync()
    {
        if (_renderStarting) return;
        _renderStarting = true;
        try
        {
            if (!await PreemptLanWorkerAsync()) return;
            await StartRenderCoreAsync();
        }
        finally { _renderStarting = false; }
    }

    private async Task StartRenderCoreAsync()
    {
        if (!_licenseValid)
        {
            ShowError("Phần mềm chưa được kích hoạt license.");
            return;
        }
        if (_cts != null) return;

        _ffmpegPath = FindFfmpeg();
        UpdateFfmpegStatus();
        if (_ffmpegPath == null)
        {
            ShowError("Không tìm thấy ffmpeg.exe. Hãy đặt ffmpeg.exe cạnh file .exe của Render Music Pro hoặc thêm FFmpeg vào PATH.");
            return;
        }

        if (!ValidateSettings(out string validationError))
        {
            ShowError(validationError);
            return;
        }

        btnStart.Enabled = false;
        bool preflightOk;
        try { preflightOk = await RunPreflightChecksAsync(); }
        finally { btnStart.Enabled = _licenseValid && _cts == null; }
        if (!preflightOk) return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _runFailures.Clear();
        _runSucceeded = 0;
        string? summaryBox = null;

        try
        {
            SetRunning(true);
            string cacheRoot = ResolveCacheDirectory();
            _runDurationSnapshot = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            _videoRandomDeck = null;
            _videoDeckSource = null;
            _musicPlaylistSignatures.Clear();
            _effectDeck = CreateEffectDeck();
            _imageDeck = ImageBackgroundSelected ? CreateImageDeck() : null;
            _runEffectGreen = chkEffectGreen.Checked;
            _runEffectBackgroundMode = cmbEffectBackground.SelectedIndex;
            _runEffectBlack = _runEffectBackgroundMode == 2;
            Directory.CreateDirectory(txtMusicOutput.Text.Trim());
            Directory.CreateDirectory(txtVideoOutput.Text.Trim());
            txtLog.Clear();
            LoadPersistentMetadataCache(cacheRoot);
            AddLog("✓ Cache trên đĩa: " + cacheRoot, Cyan);
            progress.Value = 0;
            lblPercent.Text = "0%";
            lblStatus.Text = cmbMode.SelectedIndex == 3 ? "●  ĐANG XỬ LÝ AUDIO" : "●  ĐANG RANDOM NHẠC";
            lblStatus.ForeColor = Cyan;

            int playlistCount = (int)numPlaylists.Value;
            int startNumber = (int)numStartNumber.Value;
            string playlistName = SanitizeFileName(txtPlaylistName.Text.Trim());
            if (string.IsNullOrWhiteSpace(playlistName)) playlistName = "Playlist";

            var rng = new Random(Environment.TickCount ^ Guid.NewGuid().GetHashCode());
            var videoFiles = new List<string>();
            if (_imageDeck != null)
            {
                // Nền ảnh: không cần kho video.
                AddLog($"✓ Kho ảnh: {_imageDeck.Count:N0} ảnh · mỗi video dùng 1 ảnh làm nền.", Green);
            }
            else
            {
                videoFiles = await Task.Run(() => GetVideoFiles(txtVideoSource.Text.Trim()), token);
                if (videoFiles.Count == 0) throw new InvalidOperationException("Kho video chuẩn hóa không có video hợp lệ.");
                AddLog($"✓ Kho video: {videoFiles.Count:N0} video.", Green);
                _videoCount = videoFiles.Count;
            }
            LogEffectSourceForRun();
            AddLog($"✓ Chế độ: {ModeName()}.", Cyan);
            AddLog($"✓ Pattern nhạc dùng cho Video: ON {numOnSeconds.Value:N0}s / OFF {numOffSeconds.Value:N0}s.", Cyan);

            if (cmbMode.SelectedIndex == 3)
            {
                if (LanDistributeEnabled)
                    AddLog("ℹ Chế độ audio có sẵn không chia bài LAN; render hết trên máy này.", Cyan);
                await RenderDirectAudioModeAsync(videoFiles, token, rng);
            }
            else
            {
                var categoryFiles = await LoadMusicPoolsAsync(token);
                // Quét lại ngay trước khi render: nếu người dùng thêm/xóa audio sau khi
                // chọn thư mục, giới hạn và kiểm tra vẫn dựa trên số file thực tế mới nhất.
                RefreshCounts();
                var activePools = GetActivePools(categoryFiles);
                ValidatePools(activePools);
                var musicDecks = activePools.ToDictionary(
                    pair => pair.Key,
                    pair => new MusicPoolDeck(pair.Value),
                    StringComparer.OrdinalIgnoreCase);

                AddLog($"✓ Random {ModeName()} | {playlistCount:N0} playlist.", Cyan);
                AddLog("✓ Giữ thứ tự nhóm; ưu tiên bài ít dùng, hạn chế lặp với playlist trước.", Cyan);

                if (LanDistributeEnabled && _imageDeck != null)
                    AddLog("ℹ Chế độ nền ảnh chưa chia bài LAN; render hết trên máy này.", Cyan);
                if (LanDistributeEnabled && _imageDeck == null)
                {
                    await RenderLanBatchAsync(musicDecks, videoFiles, playlistCount, startNumber, playlistName, rng, token);
                }
                else
                {
                string musicOutput = txtMusicOutput.Text.Trim();
                string videoOutput = txtVideoOutput.Text.Trim();
                double onSeconds = (double)numOnSeconds.Value, offSeconds = (double)numOffSeconds.Value;
                string BaseNameAt(int index) => $"{startNumber + index} - {playlistName}";
                var runIndices = Enumerable.Range(0, playlistCount).Where(n => !_runSkipIndices.Contains(n)).ToList();
                _renderEstimate.SetTotal(runIndices.Count);
                if (playlistCount > runIndices.Count)
                    AddLog($"ℹ Bỏ qua {playlistCount - runIndices.Count:N0} mục đã có file đầu ra.", Cyan);

                // Pipeline: audio for the next playlists is prepared on spare CPU cores while the
                // current video is muxed. Music and video are still drawn in playlist order.
                int lookahead = Math.Clamp(Environment.ProcessorCount / 4, 1, 3);
                using var pipelineCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var pipelineToken = pipelineCts.Token;
                var pending = new Queue<Task<PreparedPlaylist>>();
                int nextToPrepare = 0;
                void FillPipeline()
                {
                    while (nextToPrepare < runIndices.Count && pending.Count < lookahead)
                    {
                        int index = runIndices[nextToPrepare++];
                        pending.Enqueue(PreparePlaylistAudioAsync(index, playlistCount, BaseNameAt(index),
                            playlistName, musicDecks, rng, onSeconds, offSeconds, pipelineToken));
                    }
                }

                try
                {
                    FillPipeline();
                    for (int k = 0; k < runIndices.Count; k++)
                    {
                        int i = runIndices[k];
                        token.ThrowIfCancellationRequested();
                        _renderEstimate.BeginItem(_renderWatch.Elapsed.TotalSeconds);
                        string baseName = BaseNameAt(i);
                        string musicPath = Path.Combine(musicOutput, baseName + ".mp3");
                        string trackPath = Path.Combine(musicOutput, baseName + ".txt");
                        string videoPath = Path.Combine(videoOutput, baseName + ".mp4");

                        lblCurrent.Text = $"Playlist {i + 1:N0}/{playlistCount:N0} • random nhạc...";
                        Task<PreparedPlaylist> preparedTask = pending.Dequeue();
                        FillPipeline();
                        bool itemOk = await RunItemWithRetryAsync(baseName, async attempt =>
                        {
                            PreparedPlaylist? current = null;
                            try
                            {
                            // Lần thử lại: chuẩn bị lại nhạc từ đầu (lần đầu dùng bản đã chuẩn bị sẵn).
                            current = attempt == 1 ? await preparedTask
                                : await PreparePlaylistAudioAsync(i, playlistCount, baseName,
                                    playlistName, musicDecks, rng, onSeconds, offSeconds, pipelineToken);
                            AddLog($"▶ [{i + 1:N0}/{playlistCount:N0}] {baseName} • ghép video", TextMain);
                            AddLog($"  ✓ Thời lượng nhạc ON/OFF: {FormatDuration(current.Duration)}", Green);

                            if (_imageDeck != null)
                            {
                                lblCurrent.Text = $"Playlist {i + 1:N0}/{playlistCount:N0} • ghép ảnh nền + hiệu ứng...";
                                await RenderImageWithMusicAsync(_ffmpegPath!, current.VideoAudio, true,
                                    videoPath, current.Duration, pipelineToken);
                            }
                            else
                            {
                            lblCurrent.Text = $"Playlist {i + 1:N0}/{playlistCount:N0} • random video đủ {FormatDuration(current.Duration)}...";
                            var chosenVideos = await BuildVideoSequenceAsync(videoFiles, current.Duration, rng, pipelineToken);
                            AddLog($"  ✓ Đã chọn {chosenVideos.Count:N0} video nguồn.", Green);

                            lblCurrent.Text = $"Playlist {i + 1:N0}/{playlistCount:N0} • ghép video (ưu tiên stream copy)...";
                            await RenderVideoWithMusicAsync(_ffmpegPath!, chosenVideos, current.VideoAudio, true,
                                videoPath, current.Duration, pipelineToken);
                            }
                            await Task.Run(() =>
                            {
                                File.Move(current.MusicFile, musicPath, true);
                                File.Move(current.TrackFile, trackPath, true);
                            }, CancellationToken.None);
                            AddLog($"  ✓ Music: {Path.GetFileName(musicPath)}", Green);
                            AddLog($"  ✓ Video: {Path.GetFileName(videoPath)}", Green);
                            _renderEstimate.CompleteItem(_renderWatch.Elapsed.TotalSeconds);
                            }
                            finally
                            {
                                if (current != null) TryDeleteDirectory(current.WorkDirectory);
                            }
                        }, token);

                        if (itemOk) _runSucceeded++;
                        else _renderEstimate.SetTotal(runIndices.Count - _runFailures.Count);
                        int pct = (int)Math.Round((k + 1) * 100.0 / runIndices.Count);
                        progress.Value = Math.Clamp(pct, 0, 100);
                        lblPercent.Text = pct + "%";
                        lblOutputCount.Text = $"Đã tạo: {_runSucceeded:N0}/{runIndices.Count:N0}" +
                            (_runFailures.Count > 0 ? $" · lỗi {_runFailures.Count:N0}" : "");
                    }
                }
                finally
                {
                    // Stop playlists still being prepared and remove their work folders.
                    if (pending.Count > 0) pipelineCts.Cancel();
                    while (pending.Count > 0)
                    {
                        try { TryDeleteDirectory((await pending.Dequeue()).WorkDirectory); }
                        catch { }
                    }
                }
                }
            }

            bool hasFailures = _runFailures.Count > 0;
            lblStatus.Text = hasFailures ? "●  HOÀN THÀNH (CÓ LỖI)" : "●  HOÀN THÀNH";
            _renderEstimate.Finish();
            lblStatus.ForeColor = hasFailures ? Orange : Green;
            lblCurrent.Text = hasFailures
                ? $"Hoàn thành: {_runSucceeded:N0} thành công, {_runFailures.Count:N0} lỗi."
                : cmbMode.SelectedIndex == 3
                    ? "Đã xử lý toàn bộ audio có sẵn và tạo video tương ứng."
                    : $"Đã tạo {_runSucceeded:N0} video thành công.";
            progress.Value = 100;
            lblPercent.Text = "100%";
            AddLog("════════════════════════════════════════════════════════", TextMuted);
            AddLog(hasFailures ? "⚠ HOÀN THÀNH — CÓ MỤC LỖI." : "✓ HOÀN THÀNH TOÀN BỘ.", hasFailures ? Orange : Green);
            string summary = BuildRunSummary(_renderWatch.Elapsed);
            foreach (string line in summary.Split('\n')) AddLog(line, hasFailures ? Orange : Green);
            if (hasFailures) summaryBox = summary;

            NotifyBatchFinished();
            if (chkOpenMusic.Checked) OpenFolder(txtMusicOutput.Text.Trim());
            if (chkOpenVideo.Checked) OpenFolder(txtVideoOutput.Text.Trim());
        }
        catch (OperationCanceledException)
        {
            lblStatus.Text = "●  ĐÃ DỪNG";
            lblStatus.ForeColor = Orange;
            lblCurrent.Text = "Đã dừng theo yêu cầu.";
            AddLog("■ Đã dừng.", Orange);
        }
        catch (Exception ex)
        {
            lblStatus.Text = "●  CÓ LỖI";
            lblStatus.ForeColor = Red;
            lblCurrent.Text = "Quá trình dừng do lỗi.";
            AddLog("✗ " + ex.Message, Red);
            AppLog.Write("LỖI RENDER:\n" + ex);
            NotifyBatchFinished();
            ShowError(ex.Message);
        }
        finally
        {
            SavePersistentMetadataCache();
            _runDurationSnapshot = null;
            _effectDeck = null;
            _imageDeck = null;
            _cts?.Dispose();
            _cts = null;
            SetRunning(false);
            if (_closeAfterRender && !IsDisposed) Close();
        }

        if (summaryBox != null && !IsDisposed)
            MessageBox.Show(this, summaryBox, "Render Music Pro - Kết quả",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        if (!IsDisposed) _ = RefreshCacheSizeAsync();
    }

    private sealed record PreparedPlaylist(string WorkDirectory, string VideoAudio, string MusicFile,
        string TrackFile, double Duration);

    // All audio work for one playlist: random, concat, track list, then MP3 and the ON/OFF
    // AAC for the video in parallel. Several playlists are prepared at once because the
    // AAC encoder is single-threaded; the video mux then only copies streams.
    // Outputs stay in the work folder until the playlist's video is finished.
    private async Task<PreparedPlaylist> PreparePlaylistAudioAsync(int index, int playlistCount, string baseName,
        string playlistName, Dictionary<string, MusicPoolDeck> musicDecks, Random rng, double on, double off,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var selected = SelectMusic(musicDecks, rng);
        string workDirectory = CreateWorkDirectory();
        string masterAudio = Path.Combine(workDirectory, "playlist-master.wav");
        string musicFile = Path.Combine(workDirectory, baseName + ".mp3");
        string trackFile = Path.Combine(workDirectory, baseName + ".txt");
        string videoAudio = Path.Combine(workDirectory, "video-audio.m4a");
        try
        {
            AddLog($"♪ [{index + 1:N0}/{playlistCount:N0}] {baseName} • chuẩn bị nhạc", TextMain);
            await ConcatAudioAsync(_ffmpegPath!, selected.Select(x => x.Path).ToList(), masterAudio, token);
            await WriteTrackListAsync(trackFile, selected, playlistName, token);
            // The ON/OFF filter does not change length, so the master duration is the video duration.
            double duration = await GetDurationAsync(_ffmpegPath!, masterAudio, token);
            if (duration <= 0.1) throw new InvalidOperationException("Không đọc được thời lượng nhạc đã ghép: " + baseName);
            await Task.WhenAll(
                ExportMusicAsync(_ffmpegPath!, masterAudio, musicFile, token),
                EncodeVideoAudioAsync(_ffmpegPath!, masterAudio, videoAudio, on, off, token));
            TryDelete(masterAudio);
            AddLog($"  ♪ [{index + 1:N0}/{playlistCount:N0}] Nhạc sẵn sàng: {FormatDuration(duration)}", Cyan);
            return new PreparedPlaylist(workDirectory, videoAudio, musicFile, trackFile, duration);
        }
        catch
        {
            TryDeleteDirectory(workDirectory);
            throw;
        }
    }

    private async Task RenderDirectAudioModeAsync(List<string> videoFiles, CancellationToken token, Random rng)
    {
        string source = txtDirectAudio.Text.Trim();
        List<string> audioFiles;

        if (File.Exists(source))
        {
            audioFiles = new List<string> { source };
        }
        else
        {
            audioFiles = await Task.Run(() => ListDirectAudioFiles(source), token);
        }

        if (audioFiles.Count == 0)
            throw new InvalidOperationException("Không tìm thấy audio có sẵn để xử lý.");


        AddLog($"✓ Audio có sẵn: {audioFiles.Count:N0} file.", Green);
        if (Directory.Exists(source))
            AddLog("✓ Chế độ thư mục: chạy tuần tự từ file đầu đến file cuối, hết thư mục thì DỪNG — không random Audio.", Cyan);
        else
            AddLog("✓ Chế độ 1 file: xử lý đúng Audio đã chọn — không random Audio.", Cyan);
        AddLog("✓ Audio gốc sẽ được copy thẳng vào Music; bản CUT chỉ dùng cho Video.", Cyan);

        int total = audioFiles.Count;
        int runTotal = total - _runSkipIndices.Count(n => n < total);
        _renderEstimate.SetTotal(runTotal);
        lblOutputCount.Text = $"Số audio: {total:N0}";
        if (runTotal < total)
            AddLog($"ℹ Bỏ qua {total - runTotal:N0} mục đã có file đầu ra.", Cyan);
        int processed = 0;
        for (int i = 0; i < total; i++)
        {
            token.ThrowIfCancellationRequested();
            if (_runSkipIndices.Contains(i)) continue;
            _renderEstimate.BeginItem(_renderWatch.Elapsed.TotalSeconds);

            string sourceAudio = audioFiles[i];
            string originalBaseName = Path.GetFileNameWithoutExtension(sourceAudio);
            string safeBaseName = SanitizeFileName(originalBaseName);
            if (string.IsNullOrWhiteSpace(safeBaseName))
                safeBaseName = $"Audio_{i + 1:0000}";

            string musicPath = Path.Combine(txtMusicOutput.Text.Trim(), safeBaseName + Path.GetExtension(sourceAudio));
            string videoPath = Path.Combine(txtVideoOutput.Text.Trim(), safeBaseName + ".mp4");

            bool itemOk = await RunItemWithRetryAsync(Path.GetFileName(sourceAudio), async _ =>
            {
            string cutDir = CreateWorkDirectory();
            string cutPath = Path.Combine(cutDir, safeBaseName + " - CUT.wav");
            Directory.CreateDirectory(cutDir);
            Task copyMusic = Task.CompletedTask;

            try
            {
                lblCurrent.Text = $"Audio {i + 1:N0}/{total:N0} • copy audio vào Music...";
                AddLog($"▶ [{i + 1:N0}/{total:N0}] {Path.GetFileName(sourceAudio)}", TextMain);
                double sourceDuration = await GetDurationAsync(_ffmpegPath!, sourceAudio, token);
                if (sourceDuration <= 0)
                    throw new InvalidOperationException("Không đọc được thời lượng nhạc: " + Path.GetFileName(sourceAudio));
                AddLog($"  ✓ Thời lượng nguồn: {FormatDuration(sourceDuration)} (tái sử dụng nếu đã đo).", Cyan);

                // Giữ nguyên audio gốc và đưa thẳng vào Music. Nếu cùng tên, ghi đè bản cũ.
                // Copy chạy nền song song với ON/OFF + ghép video, không chặn giao diện.
                if (!string.Equals(Path.GetFullPath(sourceAudio), Path.GetFullPath(musicPath), StringComparison.OrdinalIgnoreCase))
                {
                    copyMusic = Task.Run(() =>
                    {
                        string tempMusicPath = CreateTemporaryOutputPath(musicPath);
                        try
                        {
                            File.Copy(sourceAudio, tempMusicPath, true);
                            File.Move(tempMusicPath, musicPath, true);
                        }
                        finally
                        {
                            TryDelete(tempMusicPath);
                        }
                    }, token);
                }

                lblCurrent.Text = $"Audio {i + 1:N0}/{total:N0} • xử lý ON/OFF để ghép Video...";
                await CutAudioAsync(_ffmpegPath!, sourceAudio, cutPath, (double)numOnSeconds.Value, (double)numOffSeconds.Value, token);

                double duration = await GetDurationAsync(_ffmpegPath!, cutPath, token);
                if (duration <= 0.1)
                    throw new InvalidOperationException("Không đọc được thời lượng audio đã xử lý ON/OFF: " + Path.GetFileName(sourceAudio));

                AddLog($"  ✓ Thời lượng Video: {FormatDuration(duration)}", Green);

                if (_imageDeck != null)
                {
                    lblCurrent.Text = $"Audio {i + 1:N0}/{total:N0} • ghép ảnh nền + hiệu ứng...";
                    await RenderImageWithMusicAsync(_ffmpegPath!, cutPath, false, videoPath, duration, token);
                }
                else
                {
                lblCurrent.Text = $"Audio {i + 1:N0}/{total:N0} • random video đủ {FormatDuration(duration)}...";
                var chosenVideos = await BuildVideoSequenceAsync(videoFiles, duration, rng, token);
                AddLog($"  ✓ Đã chọn {chosenVideos.Count:N0} video chuẩn hóa.", Green);

                await RenderVideoWithMusicAsync(_ffmpegPath!, chosenVideos, cutPath, false, videoPath, duration, token);
                }
                await copyMusic;
                AddLog($"  ✓ Music: {Path.GetFileName(musicPath)}", Green);
                AddLog($"  ✓ Video: {Path.GetFileName(videoPath)}", Green);
                _renderEstimate.CompleteItem(_renderWatch.Elapsed.TotalSeconds);
            }
            finally
            {
                try { await copyMusic; } catch { }
                TryDelete(cutPath);
                TryDeleteDirectory(cutDir);
            }
            }, token);

            processed++;
            if (itemOk) _runSucceeded++;
            else _renderEstimate.SetTotal(runTotal - _runFailures.Count);
            int pct = (int)Math.Round(processed * 100.0 / Math.Max(1, runTotal));
            progress.Value = Math.Clamp(pct, 0, 100);
            lblPercent.Text = pct + "%";
            lblOutputCount.Text = $"Đã tạo: {_runSucceeded:N0}/{runTotal:N0}" +
                (_runFailures.Count > 0 ? $" · lỗi {_runFailures.Count:N0}" : "");
        }

        lblCurrent.Text = $"Đã xử lý {total:N0} audio có sẵn.";
    }

    private bool ValidateSettings(out string error)
    {
        error = "";
        if (ImageBackgroundSelected)
        {
            if (!Directory.Exists(txtImageSource.Text.Trim()))
            {
                error = "Kho ảnh không tồn tại. Hãy chọn thư mục chứa ảnh nền (paste đường dẫn hoặc bấm Browse).";
                return false;
            }
        }
        else if (!Directory.Exists(txtVideoSource.Text.Trim()))
        {
            error = "Kho video chuẩn hóa không tồn tại. Bạn có thể paste đường dẫn hoặc bấm Browse.";
            return false;
        }
        if ((double)numOnSeconds.Value <= 0 || (double)numOffSeconds.Value <= 0)
        {
            error = "Thời gian ON và OFF phải lớn hơn 0.";
            return false;
        }

        if (cmbMode.SelectedIndex == 3)
        {
            string source = txtDirectAudio.Text.Trim();
            bool isFile = File.Exists(source);
            bool isFolder = Directory.Exists(source);
            if (!isFile && !isFolder)
            {
                error = "Hãy chọn file audio có sẵn hoặc một thư mục chứa audio.";
                return false;
            }

            if (isFile && !IsAudioFile(source))
            {
                error = "File đã chọn không phải định dạng audio được hỗ trợ.";
                return false;
            }

            if (isFolder && GetAudioFiles(source).Count == 0)
            {
                error = "Thư mục Audio có sẵn không có file audio hợp lệ.";
                return false;
            }

            return true;
        }

        if (string.IsNullOrWhiteSpace(txtPlaylistName.Text))
        {
            error = "Hãy nhập tên playlist.";
            return false;
        }
        return true;
    }

    private string ModeName() => cmbMode.SelectedIndex switch
    {
        0 => "Random 2 tổ hợp (HAY + KHO-NHAC)",
        1 => "Random 3 tổ hợp (SIEU-HAY + HAY + KHO-NHAC)",
        2 => "Random 4 tổ hợp (SIEU-HAY + HAY + KHO-NHAC + KHO)",
        _ => "Audio có sẵn (không Random)"
    };

    private Dictionary<string, List<string>> GetActivePools(Dictionary<string, List<string>> pools)
    {
        var active = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (cmbMode.SelectedIndex >= 1 && GetCount(numSuper) > 0) active["SIEU-HAY"] = pools["SIEU-HAY"];
        if (GetCount(numHay) > 0) active["HAY"] = pools["HAY"];
        if (GetCount(numKhoNhac) > 0) active["Kho-Nhac"] = pools["Kho-Nhac"];
        if (cmbMode.SelectedIndex >= 2 && GetCount(numKho) > 0) active["KHO"] = pools["KHO"];
        return active;
    }

    private void ValidatePools(Dictionary<string, List<string>> active)
    {
        if (active.Count == 0) throw new InvalidOperationException("Bạn chưa nhập số bài cho tổ hợp random.");
        foreach (var pair in active)
        {
            int requested = pair.Key switch
            {
                "SIEU-HAY" => GetCount(numSuper),
                "HAY" => GetCount(numHay),
                "Kho-Nhac" => GetCount(numKhoNhac),
                "KHO" => GetCount(numKho),
                _ => 0
            };
            if (requested > pair.Value.Count)
                throw new InvalidOperationException($"Thư mục {pair.Key} chỉ có {pair.Value.Count:N0} bài nhưng bạn yêu cầu {requested:N0} bài/playlist.");
        }
    }

    private async Task<Dictionary<string, List<string>>> LoadMusicPoolsAsync(CancellationToken token)
    {
        // Các kho đã được quét/cached trong RefreshCounts().  Ở đây chỉ lấy lại
        // snapshot từ cache thay vì EnumerateFiles lần thứ hai. Nếu thư mục đổi
        // trong lúc chờ render, GetAudioFiles() sẽ tự phát hiện LastWriteTime thay đổi.
        var dirs = new Dictionary<string, string>
        {
            ["SIEU-HAY"] = txtSuper.Text.Trim(),
            ["HAY"] = txtHay.Text.Trim(),
            ["Kho-Nhac"] = txtKhoNhac.Text.Trim(),
            ["KHO"] = txtKho.Text.Trim()
        };

        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in dirs)
        {
            token.ThrowIfCancellationRequested();
            string folder = item.Value;
            result[item.Key] = await Task.Run(() => GetAudioFiles(folder), token);
        }
        return result;
    }

    private List<MusicItem> SelectMusic(Dictionary<string, MusicPoolDeck> pools, Random rng)
    {
        var result = new List<MusicItem>();
        AddRandomFromPool(result, pools, "SIEU-HAY", GetCount(numSuper), rng);
        AddRandomFromPool(result, pools, "HAY", GetCount(numHay), rng);
        AddRandomFromPool(result, pools, "Kho-Nhac", GetCount(numKhoNhac), rng);
        AddRandomFromPool(result, pools, "KHO", GetCount(numKho), rng);
        for (int attempt = 0; attempt < 32; attempt++)
        {
            if (_musicPlaylistSignatures.Add(string.Join("\0", result.Select(item => item.Path))))
                return result;
            // Change order within each group only. Song counts and usage fairness stay intact.
            result = result.GroupBy(item => item.Category)
                .SelectMany(group => ShuffleCopy(group.ToList(), rng)).ToList();
        }
        return result;
    }

    private static void AddRandomFromPool(
        List<MusicItem> result,
        Dictionary<string, MusicPoolDeck> pools,
        string name,
        int count,
        Random rng)
    {
        if (count <= 0) return;

        foreach (string file in pools[name].Take(count, rng,
            new HashSet<string>(result.Select(item => item.Path), StringComparer.OrdinalIgnoreCase)))
            result.Add(new MusicItem(name, file));
    }
    private static List<T> ShuffleCopy<T>(IReadOnlyList<T> source, Random rng)
    {
        var result = source.ToList();
        for (int i = result.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }
        return result;
    }

    private async Task<List<string>> BuildVideoSequenceAsync(List<string> videos, double targetDuration, Random rng, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (videos.Count == 0 || !double.IsFinite(targetDuration) || targetDuration <= 0)
            throw new InvalidOperationException("Kho video trống hoặc thời lượng đích không hợp lệ.");
        if (Math.Ceiling(targetDuration / 8d) > int.MaxValue)
            throw new InvalidOperationException("Thời lượng quá lớn để tạo danh sách video.");
        if (_videoRandomDeck == null || !ReferenceEquals(_videoDeckSource, videos))
        {
            _videoRandomDeck = new MusicPoolDeck(videos);
            _videoDeckSource = videos;
        }
        string ffprobe = _ffmpegPath == null ? "" : Path.Combine(Path.GetDirectoryName(_ffmpegPath)!, "ffprobe.exe");
        if (!File.Exists(ffprobe)) return BuildVideoSequenceBy8Seconds(targetDuration, rng, token);

        LoadVideoCheckCache();
        var result = new List<string>((int)Math.Ceiling(targetDuration / 8d) + 8);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bad = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skipped = new List<string>();
        string? reference = null;
        double total = 0;
        int loop = 0, measured = 0, cached = 0;
        const double margin = 1.0;

        while (total < targetDuration + margin)
        {
            token.ThrowIfCancellationRequested();
            if (bad.Count >= _videoRandomDeck.Count)
                throw new InvalidOperationException("Kho video không có clip hợp lệ để ghép (clip lỗi hoặc không đọc được).");

            // Chọn một lượt clip đủ cho phần còn thiếu (ước 8 giây/clip), đo song song những clip chưa có cache.
            int batchSize = (int)Math.Clamp(Math.Ceiling((targetDuration + margin - total) / 8d), 1, 96);
            var batch = new List<string>(batchSize);
            while (batch.Count < batchSize)
            {
                if (seen.Count >= _videoRandomDeck.Count)
                {
                    if (!chkReuseVideo.Checked)
                        throw new InvalidOperationException("Kho video không đủ clip hợp lệ cho thời lượng nhạc. Hãy bật tùy chọn lặp video khi kho thiếu.");
                    seen.Clear();
                    AddLog($"  ↻ Đã dùng hết kho; xáo lại video, ưu tiên file ít dùng — vòng {++loop}.", Cyan);
                }
                string file = _videoRandomDeck.Take(1, rng, seen)[0];
                seen.Add(file);
                if (!bad.Contains(file)) batch.Add(file);
                else if (seen.Count >= _videoRandomDeck.Count && bad.Count >= _videoRandomDeck.Count) break;
            }

            var entries = new Dictionary<string, VideoCheckEntry>(StringComparer.OrdinalIgnoreCase);
            var todo = new List<string>();
            foreach (string file in batch.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var info = new FileInfo(file);
                VideoCheckEntry? hit;
                lock (_videoCheckCache) _videoCheckCache.TryGetValue(file, out hit);
                if (info.Exists && hit != null && hit.FileLength == info.Length && hit.LastWriteUtc == info.LastWriteTimeUtc)
                { entries[file] = hit; cached++; }
                else todo.Add(file);
            }
            if (todo.Count > 0)
            {
                using var gate = new SemaphoreSlim(6);
                var tasks = todo.Select(async file =>
                {
                    await gate.WaitAsync(token);
                    try { return (file, entry: await ProbeVideoCheckAsync(ffprobe, file, token)); }
                    finally { gate.Release(); }
                }).ToList();
                foreach (var r in await Task.WhenAll(tasks))
                {
                    entries[r.file] = r.entry;
                    lock (_videoCheckCache) _videoCheckCache[r.file] = r.entry;
                    measured++;
                }
                SaveVideoCheckCache();
            }

            foreach (string file in batch)
            {
                if (total >= targetDuration + margin) break;
                var e = entries[file];
                string why = "";
                if (e.Error.Length > 0) why = e.Error;
                else
                {
                    string format = $"{e.Codec} {e.Width}x{e.Height} {e.Fps}";
                    reference ??= format;
                    if (format != reference) why = $"khác chuẩn ({format})";
                    else if (e.VideoDuration > 0 && e.FileDuration - e.VideoDuration > 0.15)
                        why = $"hình ngắn hơn file ({e.VideoDuration:0.##}s/{e.FileDuration:0.##}s)";
                }
                double d = e.VideoDuration > 0 ? e.VideoDuration : e.FileDuration;
                if (why.Length == 0 && d < 0.5) why = "quá ngắn";
                if (why.Length > 0)
                {
                    if (bad.Add(file)) skipped.Add($"{Path.GetFileName(file)} ({why})");
                    continue;
                }
                result.Add(file);
                total += d;
            }
        }

        AddLog($"  ✓ Ghép {result.Count:N0} clip, tổng {TimeSpan.FromSeconds(total):hh\\:mm\\:ss} cho nhạc {TimeSpan.FromSeconds(targetDuration):hh\\:mm\\:ss}" +
            $" (đo thật thời lượng từng clip: {measured:N0} clip mới đo, {cached:N0} dùng cache).", Cyan);
        if (skipped.Count > 0)
            AddLog($"  ⚠ Bỏ {skipped.Count:N0} clip lệch/lỗi: {string.Join("; ", skipped.Take(5))}" +
                (skipped.Count > 5 ? $"; … và {skipped.Count - 5:N0} clip khác" : ""), Orange);
        return result;
    }

    // Dự phòng khi không có ffprobe: giả định mỗi clip 8 giây như trước.
    private List<string> BuildVideoSequenceBy8Seconds(double targetDuration, Random rng, CancellationToken token)
    {
        int count = (int)Math.Ceiling(targetDuration / 8d);
        if (!chkReuseVideo.Checked && count > _videoRandomDeck!.Count)
            throw new InvalidOperationException("Kho video không đủ số clip theo mốc 8 giây. Hãy bật tùy chọn lặp video khi kho thiếu.");
        var result = new List<string>(count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int loop = 0;
        while (result.Count < count)
        {
            token.ThrowIfCancellationRequested();
            if (seen.Count == _videoRandomDeck!.Count)
            {
                seen.Clear();
                AddLog($"  ↻ Đã dùng hết kho; xáo lại video, ưu tiên file ít dùng — vòng {++loop}.", Cyan);
            }
            string file = _videoRandomDeck.Take(1, rng, seen)[0];
            seen.Add(file);
            result.Add(file);
        }
        AddLog($"  ⚠ Không có ffprobe; chọn {count:N0} clip theo mốc 8 giây (không đo thời lượng).", Orange);
        return result;
    }
    private async Task RenderVideoWithMusicAsync(string ffmpeg, List<string> videos, string music, bool audioEncoded,
        string output, double targetDuration, CancellationToken token)
    {
        string tempDir = CreateWorkDirectory();
        Directory.CreateDirectory(tempDir);
        string listFile = Path.Combine(tempDir, "videos.txt");
        string tempOutput = CreateTemporaryOutputPath(output);
        _timingFinalOutput = tempOutput;

        try
        {
            WriteConcatList(listFile, videos);
            string duration = targetDuration.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (_effectDeck != null)
            {
                string effect = _effectDeck.Take(1, Random.Shared)[0];
                lblCurrent.Text = _runEffectGreen
                    ? (_runEffectBackgroundMode == 0 ? "Đang nhận diện nền và ghép hiệu ứng (ưu tiên GPU)..." :
                        (_runEffectBlack ? "Đang tách nền đen và ghép hiệu ứng (ưu tiên GPU)..." : "Đang tách nền xanh và ghép hiệu ứng (ưu tiên GPU)..."))
                    : "Đang ghép hiệu ứng (ưu tiên GPU)...";
                AddLog($"  ✦ Hiệu ứng: {Path.GetFileName(effect)} · lặp đến hết video · giữ kích thước gốc.", Cyan);
                _effectCanvasSize = await GetEffectVideoSizeAsync(ffmpeg, videos[0], false, token);
                await RenderEffectVideoAsync(ffmpeg, listFile, music, audioEncoded, effect, tempOutput,
                    duration, _runEffectGreen, token);
                File.Move(tempOutput, output, true);
                return;
            }

            try
            {
                await RunFfmpegAsync(
                    ffmpeg,
                    $"-hide_banner -y -f concat -safe 0 -i {Q(listFile)} -i {Q(music)} -map 0:v:0 -map 1:a:0 -t {duration} -c:v copy {AudioCodecArgs(audioEncoded)}{FastStartArgs}{Q(tempOutput)}",
                    token,
                    "Ghép video chuẩn hóa + nhạc (copy video)");
                AddLog("  ✓ Hình video được nối bằng stream copy; không mã hóa lại.", Green);
            }
            catch (InvalidOperationException ex)
            {
                token.ThrowIfCancellationRequested();
                string reason = ex.Message;
                int detailStart = reason.IndexOf("\n\n", StringComparison.Ordinal);
                if (detailStart >= 0) reason = reason[(detailStart + 2)..];
                reason = reason.Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (reason.Length > 500) reason = reason[^500..];

                lblCurrent.Text = "Đang mã hóa dự phòng (ưu tiên NVIDIA GPU)...";
                AddLog($"  ⚠ Stream copy thất bại; chuyển sang mã hóa dự phòng. Chi tiết: {reason}", Orange);

                string fallbackVideo = Path.Combine(tempDir, "video-fallback.mp4");
                await EncodeFallbackVideoAsync(ffmpeg, listFile, fallbackVideo, duration, token);

                await RunFfmpegAsync(
                    ffmpeg,
                    $"-hide_banner -y -i {Q(fallbackVideo)} -i {Q(music)} -map 0:v:0 -map 1:a:0 -t {duration} -c:v copy {AudioCodecArgs(audioEncoded)}{FastStartArgs}{Q(tempOutput)}",
                    token,
                    "Ghép video + nhạc (dự phòng)");
                AddLog("  ✓ Hoàn tất bằng mã hóa dự phòng.", Green);
            }

            File.Move(tempOutput, output, true);
        }
        finally
        {
            TryDelete(tempOutput);
            TryDelete(listFile);
            _effectCanvasSize = null;
            _timingFinalOutput = null;
            TryDeleteDirectory(tempDir);
        }
    }
    private async Task EncodeFallbackVideoAsync(string ffmpeg, string listFile, string output,
        string duration, CancellationToken token)
    {
        string common = $"-hide_banner -y -f concat -safe 0 -i {Q(listFile)} -an -t {duration} " +
            "-vf \"scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,setsar=1\" -r 30 -pix_fmt yuv420p ";
        string nvencOptions = $"{NvencEncodeArgs} {Q(output)}";
        if (_nvencAvailable != false && _cudaPipelineAvailable != false)
        {
            try
            {
                string accelerated = $"-hide_banner -y -hwaccel cuda -hwaccel_output_format cuda -f concat -safe 0 -i {Q(listFile)} -an -t {duration} " +
                    "-vf \"scale_cuda=1920:1080:force_original_aspect_ratio=decrease:force_divisible_by=2:format=nv12,hwdownload,format=nv12,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,setsar=1\" -r 30 -pix_fmt yuv420p ";
                await RunFfmpegAsync(ffmpeg, accelerated + nvencOptions,
                    token, "Giải mã CUDA, scale CUDA và mã hóa NVIDIA NVENC");
                _nvencAvailable = true;
                _cudaPipelineAvailable = true;
                AddLog("  ✓ GPU: giải mã CUDA + scale CUDA + mã hóa NVENC.", Green);
                return;
            }
            catch (InvalidOperationException)
            {
                token.ThrowIfCancellationRequested();
                _cudaPipelineAvailable = false;
                TryDelete(output);
                AddLog("  ⚠ CUDA không phù hợp với nguồn này; thử mã hóa NVENC với giải mã CPU.", Orange);
            }
        }
        if (_nvencAvailable != false)
        {
            try
            {
                await RunFfmpegAsync(ffmpeg, common + nvencOptions,
                    token, "Mã hóa video bằng NVIDIA NVENC");
                _nvencAvailable = true;
                AddLog("  ✓ Đã mã hóa hình bằng NVIDIA NVENC (GPU).", Green);
                return;
            }
            catch (InvalidOperationException)
            {
                token.ThrowIfCancellationRequested();
                _nvencAvailable = false;
                TryDelete(output);
                AddLog("  ⚠ NVENC không sử dụng được; tiếp tục bằng CPU.", Orange);
            }
        }
        await RunFfmpegAsync(ffmpeg, common +
            $"-c:v libx264 -preset veryfast -crf 20 -profile:v high {BitrateCapArgs} {Q(output)}",
            token, "Mã hóa video dự phòng CPU");
    }

    // FFMPEG
    // =========================================================
    private static string? FindFfmpeg()
    {
        string local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(local)) return local;
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = "ffmpeg",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            p.Start();
            // Read asynchronously so the 3-second limit applies even if where.exe hangs.
            var read = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(3000))
            {
                try { p.Kill(); } catch { }
                return null;
            }
            string output = read.GetAwaiter().GetResult();
            var first = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return !string.IsNullOrWhiteSpace(first) && File.Exists(first.Trim()) ? first.Trim() : null;
        }
        catch (Exception)
        {
            return null; // where.exe không có / lỗi PATH — app vẫn chạy bằng ffmpeg kèm theo
        }
    }

    private void UpdateFfmpegStatus()
    {
        if (_ffmpegPath == null)
        {
            lblFfmpeg.Text = "●  FFmpeg chưa sẵn sàng";
            lblFfmpeg.ForeColor = Orange;
        }
        else
        {
            lblFfmpeg.Text = "●  FFmpeg sẵn sàng";
            lblFfmpeg.ForeColor = Green;
        }
    }

    private async Task WriteTrackListAsync(
        string outputPath,
        List<MusicItem> tracks,
        string playlistName,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var lines = new List<string>();

        double startSeconds = 0;
        for (int i = 0; i < tracks.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            MusicItem track = tracks[i];
            string fileName = Path.GetFileNameWithoutExtension(track.Path);
            // Reuse durations already measured while concatenating this playlist.
            // Keep fractional seconds in the sum; round only the displayed timestamp.
            long wholeSeconds = (long)Math.Floor(startSeconds);
            string timestamp = $"{wholeSeconds / 3600:00}:{wholeSeconds / 60 % 60:00}:{wholeSeconds % 60:00}";
            lines.Add($"{timestamp}: {fileName}");
            double duration = await GetDurationAsync(_ffmpegPath!, track.Path, token);
            if (!double.IsFinite(duration) || duration <= 0)
                throw new InvalidOperationException("Không đọc được thời lượng để ghi mốc bài hát: " + fileName);
            startSeconds += duration;
        }

        string tempOutput = CreateTemporaryOutputPath(outputPath);
        try
        {
            await File.WriteAllLinesAsync(
                tempOutput,
                lines,
                new UTF8Encoding(false),
                token);
            File.Move(tempOutput, outputPath, true);
        }
        finally
        {
            TryDelete(tempOutput);
        }
    }

    private async Task ConcatAudioAsync(string ffmpeg, List<string> files, string output, CancellationToken token)
    {
        string temp = CreateWorkDirectory();
        Directory.CreateDirectory(temp);
        string list = Path.Combine(temp, "audio.txt");
        string tempOutput = CreateTemporaryOutputPath(output);
        try
        {
            if (files.Count == 0) throw new InvalidOperationException("Playlist không có bài hát.");
            var parts = new List<string>();
            double totalDuration = 0;
            foreach (string[] durationBatch in files.Chunk(4))
            {
                var durations = await Task.WhenAll(durationBatch.Select(file => GetDurationAsync(ffmpeg, file, token)));
                for (int i = 0; i < durationBatch.Length; i++)
                {
                    if (durations[i] <= 0)
                        throw new InvalidOperationException("Không đọc được nhạc nguồn: " + Path.GetFileName(durationBatch[i]));
                    totalDuration += durations[i];
                }
            }
            AddLog($"  ✓ Thời lượng nhạc nguồn: {FormatDuration(totalDuration)} (dùng cache cho bài đã đo).", Cyan);
            // Decode the original songs to PCM. Each batch also handles mixed codecs/rates.
            foreach (string[] batch in files.Chunk(16))
            {
                string part = Path.Combine(temp, $"part-{parts.Count:0000}.wav");
                string inputs = string.Join(" ", batch.Select(file => "-i " + Q(file)));
                var filters = batch.Select((_, i) =>
                    $"[{i}:a:0]aresample=48000,aformat=sample_fmts=s32:channel_layouts=stereo,asetpts=PTS-STARTPTS[a{i}]");
                string graph = string.Join(";", filters) + ";" +
                    string.Concat(Enumerable.Range(0, batch.Length).Select(i => $"[a{i}]")) +
                    $"concat=n={batch.Length}:v=0:a=1[out]";
                await RunFfmpegAsync(ffmpeg,
                    $"-hide_banner -y {inputs} -filter_complex {Q(graph)} -map \"[out]\" -vn -c:a pcm_s24le -ar 48000 -ac 2 -rf64 auto {Q(part)}",
                    token, "Ghép nhạc gốc qua PCM lossless");
                parts.Add(part);
            }
            bool wavOutput = string.Equals(Path.GetExtension(output), ".wav", StringComparison.OrdinalIgnoreCase);
            if (wavOutput && parts.Count == 1)
            {
                // A single part is already the finished PCM; copying it again only doubles disk I/O.
                File.Move(parts[0], output, true);
                return;
            }
            WriteConcatList(list, parts);
            string codec = wavOutput ? "-c:a copy -rf64 auto" : "-c:a libmp3lame -q:a 2";
            await RunFfmpegAsync(ffmpeg, $"-hide_banner -y -f concat -safe 0 -i {Q(list)} -vn {codec} {Q(tempOutput)}", token, "Hoàn tất playlist nhạc");
            File.Move(tempOutput, output, true);
        }
        finally
        {
            TryDelete(tempOutput);
            TryDelete(list);
            TryDeleteDirectory(temp);
        }
    }

    private async Task ExportMusicAsync(string ffmpeg, string input, string output, CancellationToken token)
    {
        string temporary = CreateTemporaryOutputPath(output);
        try
        {
            await RunFfmpegAsync(ffmpeg,
                $"-hide_banner -y -i {Q(input)} -vn -c:a libmp3lame -q:a 2 -ar 48000 -ac 2 {Q(temporary)}",
                token, "Xuất file Music MP3");
            File.Move(temporary, output, true);
        }
        finally { TryDelete(temporary); }
    }

    private async Task CutAudioAsync(string ffmpeg, string input, string output, double on, double off, CancellationToken token)
    {
        await RunFfmpegAsync(ffmpeg, $"-hide_banner -y -i {Q(input)} -vn -af {Q(OnOffFilter(on, off))} -ar 48000 -ac 2 -c:a pcm_s24le -rf64 auto {Q(output)}", token, "Xử lý ON/OFF qua PCM lossless");
    }

    // Same ON/OFF filter whether it writes a CUT.wav or runs inside the video mux;
    // the volume filter keeps the length, so the mux output is sample-identical.
    private static string OnOffFilter(double on, double off)
    {
        string cycleText = (on + off).ToString(System.Globalization.CultureInfo.InvariantCulture);
        string onText = on.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"volume=enable='gte(mod(t\\,{cycleText})\\,{onText})':volume=0";
    }

    // AAC for the video, ON/OFF applied, encoded ahead so the mux only copies streams.
    // Decodes to the same samples as encoding during the mux.
    private async Task EncodeVideoAudioAsync(string ffmpeg, string input, string output, double on, double off, CancellationToken token)
    {
        await RunFfmpegAsync(ffmpeg, $"-hide_banner -y -i {Q(input)} -vn -af {Q(OnOffFilter(on, off))} -c:a aac -b:a 384k -ar 48000 -ac 2 {Q(output)}", token, "Mã hóa AAC ON/OFF cho video");
    }

    private static string AudioCodecArgs(bool audioEncoded) =>
        audioEncoded ? "-c:a copy " : "-c:a aac -b:a 384k -ar 48000 -ac 2 ";

    // Bài LAN dùng tùy chọn faststart của máy gửi (ghi trong phiếu việc).
    private string FastStartArgs => (_runFastStart ?? chkFastStart.Checked) ? "-movflags +faststart " : "";

    private async Task<double> GetDurationAsync(string ffmpeg, string file, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        string fullPath;
        FileInfo fileInfo;
        try
        {
            fullPath = Path.GetFullPath(file);
            lock (_videoDurationCacheLock)
            {
                if (_runDurationSnapshot != null && _runDurationSnapshot.TryGetValue(fullPath, out double known))
                    return known;
            }
            fileInfo = new FileInfo(fullPath);
            fileInfo.Refresh();
            if (!fileInfo.Exists) return 0;
        }
        catch
        {
            return 0;
        }

        long fileLength = fileInfo.Length;
        DateTime lastWriteUtc = fileInfo.LastWriteTimeUtc;
        lock (_videoDurationCacheLock)
        {
            if (_videoDurationCache.TryGetValue(fullPath, out var cached) &&
                cached.FileLength == fileLength && cached.LastWriteUtc == lastWriteUtc && cached.Duration > 0)
            {
                _runDurationSnapshot?.TryAdd(fullPath, cached.Duration);
                return cached.Duration;
            }
        }

        double duration = 0;
        int probeWidth = 0, probeHeight = 0;
        bool parsed = string.Equals(Path.GetExtension(fullPath), ".wav", StringComparison.OrdinalIgnoreCase) &&
            WaveDurationReader.TryRead(fullPath, out duration);
        string ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        if (!parsed && File.Exists(ffprobe))
        {
            string output = await RunProcessCaptureAsync(ffprobe,
                $"-v error -show_entries format=duration:stream=width,height -of json {Q(file)}", token);
            try
            {
                using var metadata = JsonDocument.Parse(output);
                if (metadata.RootElement.TryGetProperty("format", out var format) &&
                    format.TryGetProperty("duration", out var value))
                    parsed = double.TryParse(value.GetString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out duration) && double.IsFinite(duration) && duration > 0;
                if (metadata.RootElement.TryGetProperty("streams", out var streams))
                {
                    foreach (var stream in streams.EnumerateArray())
                    {
                        if (stream.TryGetProperty("width", out var width) && stream.TryGetProperty("height", out var height))
                        { probeWidth = width.GetInt32(); probeHeight = height.GetInt32(); break; }
                    }
                }
            }
            catch (JsonException) { parsed = false; }
        }

        if (!parsed)
        {
            // Read the header only. No output is requested, so FFmpeg does not decode
            // hours of audio/video just to determine duration when ffprobe is missing.
            string captured = await RunProcessCaptureAsync(ffmpeg, $"-hide_banner -i {Q(file)}", token, captureError: true);
            const string marker = "Duration:";
            int idx = captured.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                string part = captured[(idx + marker.Length)..].Trim();
                int comma = part.IndexOf(',');
                if (comma > 0) part = part[..comma].Trim();
                if (TimeSpan.TryParse(part, System.Globalization.CultureInfo.InvariantCulture, out var time) && time.TotalSeconds > 0)
                {
                    duration = time.TotalSeconds;
                    parsed = true;
                }
            }
        }

        lock (_videoDurationCacheLock)
        {
            // Remember both successful and failed probes for this immutable run.
            // Temporary WAVs need only their header and must not grow the source cache.
            if (_runDurationSnapshot != null && !IsTemporaryMediaFile(fullPath))
                _runDurationSnapshot[fullPath] = parsed ? duration : 0;
            if (parsed && duration > 0 && !IsTemporaryMediaFile(fullPath))
            {
                var previous = _videoDurationCache.GetValueOrDefault(fullPath);
                bool sameFile = previous?.FileLength == fileLength && previous.LastWriteUtc == lastWriteUtc;
                _videoDurationCache[fullPath] = new DurationCacheEntry(fileLength, lastWriteUtc, duration,
                    probeWidth > 0 ? probeWidth : (sameFile ? previous!.Width : 0),
                    probeHeight > 0 ? probeHeight : (sameFile ? previous!.Height : 0));
                MetadataCacheChanged();
            }
        }

        return parsed ? duration : 0;
    }

    private bool IsTemporaryMediaFile(string path)
    {
        string legacy = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RenderMusicPro")) + Path.DirectorySeparatorChar;
        if (path.StartsWith(legacy, StringComparison.OrdinalIgnoreCase)) return true;
        string root = string.IsNullOrWhiteSpace(_effectCacheDirectory)
            ? EffectCacheService.DefaultDirectory : _effectCacheDirectory;
        string work = Path.GetFullPath(Path.Combine(root, "Work")) + Path.DirectorySeparatorChar;
        return path.StartsWith(work, StringComparison.OrdinalIgnoreCase);
    }

    private async Task RunFfmpegAsync(string exe, string args, CancellationToken token, string step)
    {
        bool timing = BeginFfmpegTiming(args);
        try
        {
            await RunProcessAsync(exe, timing ? "-progress pipe:1 -stats_period 1 -nostats " + args : args, token, step);
        }
        finally { if (timing) _renderEstimate.EndStage(); }
    }

    private async Task RunProcessAsync(string exe, string args, CancellationToken token, string step)
    {
        token.ThrowIfCancellationRequested();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };

        process.Start();
        long lastActivity = Environment.TickCount64;
        void Touch() => Interlocked.Exchange(ref lastActivity, Environment.TickCount64);
        Task<string> stderr = ReadTailWithActivityAsync(process.StandardError, Touch);
        Task<string> stdout = args.StartsWith("-progress pipe:1 ", StringComparison.Ordinal)
            ? ReadFfmpegTimingAsync(process.StandardOutput, Touch) : ReadTailWithActivityAsync(process.StandardOutput, Touch);

        bool stalled = false;
        try
        {
            Task exited = process.WaitForExitAsync(CancellationToken.None);
            while (!exited.IsCompleted)
            {
                await Task.WhenAny(exited, Task.Delay(WatchdogCheckInterval, token));
                token.ThrowIfCancellationRequested();
                if (!exited.IsCompleted &&
                    Environment.TickCount64 - Interlocked.Read(ref lastActivity) > FfmpegStallTimeout.TotalMilliseconds)
                {
                    stalled = true;
                    break;
                }
            }
            if (stalled)
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
                try { await process.WaitForExitAsync(CancellationToken.None); } catch { }
                try { await Task.WhenAll(stderr, stdout); } catch { }
            }
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            try { await process.WaitForExitAsync(CancellationToken.None); } catch { }
            try { await Task.WhenAll(stderr, stdout); } catch { }
            throw;
        }

        string err = await stderr;
        string output = await stdout;
        if (stalled)
        {
            LogFfmpegFailure(step, exe, args, "treo (không có tiến trình)", err);
            throw new TimeoutException($"FFmpeg không có tiến trình trong {FfmpegStallTimeout.TotalMinutes:0} phút ở bước {step}; đã dừng tiến trình.");
        }
        if (process.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(err) ? output : err;
            LogFfmpegFailure(step, exe, args, "mã thoát " + process.ExitCode, detail);
            if (detail.Contains("No space left on device", StringComparison.OrdinalIgnoreCase) ||
                detail.Contains("Disk quota exceeded", StringComparison.OrdinalIgnoreCase))
                throw new OutOfDiskSpaceException("Ổ đĩa đang ghi video hoặc cache đã hết dung lượng. Hãy dọn chỗ trống hoặc đổi thư mục đầu ra rồi chạy lại. App đã dừng, không mã hóa lại bằng CPU.");
            detail = detail.Length > 2500 ? detail[^2500..] : detail;
            throw new InvalidOperationException($"FFmpeg lỗi ở bước {step}.\n\n{detail.Trim()}");
        }
    }

    private static async Task<string> RunProcessCaptureAsync(string exe, string args, CancellationToken token, bool captureError = false)
    {
        token.ThrowIfCancellationRequested();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8
            }
        };
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            try { await process.WaitForExitAsync(CancellationToken.None); } catch { }
            try { await Task.WhenAll(output, error); } catch { }
            throw;
        }

        await Task.WhenAll(output, error);
        return captureError ? await error : await output;
    }

    private static void WriteConcatList(string path, IEnumerable<string> files)
    {
        using var sw = new StreamWriter(path, false, new UTF8Encoding(false));
        foreach (string file in files)
        {
            string normalized = file.Replace("\\", "/").Replace("'", "'\\''");
            sw.WriteLine($"file '{normalized}'");
        }
    }

    // Quote một giá trị an toàn cho dòng lệnh Windows: bọc trong ngoặc kép và
    // nhân đôi dấu " (cách ffmpeg/Windows CRT parse). Dùng cho đường dẫn + tham số ffmpeg.
    private static string Q(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private static string CreateTemporaryOutputPath(string output)
    {
        string directory = Path.GetDirectoryName(output) ?? AppContext.BaseDirectory;
        string name = Path.GetFileNameWithoutExtension(output);
        string extension = Path.GetExtension(output);
        return Path.Combine(directory, $"{name}.{Guid.NewGuid():N}.partial{extension}");
    }

    // =========================================================
    // LOG / STATE
    // =========================================================
    private void SetRunning(bool running)
    {
        if (running) StartRenderTiming(); else StopRenderTiming();
        _isRendering = running;
        foreach (var card in _configurationCards) card.Enabled = !running;
        ApplyInputEnabledState();
        btnStart.Enabled = !running && _licenseValid;
        btnStop.Enabled = running;
    }

    private void ApplyInputEnabledState()
    {
        bool ready = !_isRendering;
        bool direct = cmbMode.SelectedIndex == 3;
        cmbMode.Enabled = ready;
        numSuper.Enabled = ready && !direct && cmbMode.SelectedIndex >= 1;
        numHay.Enabled = ready && !direct; numKhoNhac.Enabled = ready && !direct;
        numKho.Enabled = ready && !direct && cmbMode.SelectedIndex >= 2;
        numPlaylists.Enabled = ready && !direct; numStartNumber.Enabled = ready && !direct;
        txtPlaylistName.Enabled = ready && !direct; txtDirectAudio.Enabled = ready && direct;
        numOnSeconds.Enabled = ready; numOffSeconds.Enabled = ready;
    }

    private void StopRender()
    {
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException)
        {
            // Token source đã bị dispose — phiên render đã kết thúc, không cần huỷ nữa
        }
    }

    private void AddLog(string text, Color color)
    {
        AppLog.Write(text);
        AddLogToUi(text, color);
    }

    private void AddLogToUi(string text, Color color)
    {
        if (txtLog.IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => AddLogToUi(text, color)));
            return;
        }
        txtLog.SelectionStart = txtLog.TextLength;
        txtLog.SelectionLength = 0;
        txtLog.SelectionColor = color;
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        txtLog.SelectionColor = TextMuted;
        txtLog.ScrollToCaret();
    }

    private void ShowError(string message)
    {
        MessageBox.Show(this, message, "Render Music Pro - Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    // =========================================================
    // FILE HELPERS
    // =========================================================
    private Dictionary<string, List<string>> GetMusicPoolsForCount()
    {
        return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["SIEU-HAY"] = GetAudioFiles(txtSuper.Text.Trim()),
            ["HAY"] = GetAudioFiles(txtHay.Text.Trim()),
            ["Kho-Nhac"] = GetAudioFiles(txtKhoNhac.Text.Trim()),
            ["KHO"] = GetAudioFiles(txtKho.Text.Trim())
        };
    }

    private List<string> GetAudioFiles(string folder)
    {
        return GetCachedFiles(folder, IsAudioFile, _audioFolderCache);
    }

    private List<string> GetVideoFiles(string folder)
    {
        return GetCachedFiles(folder, IsVideoFile, _videoFolderCache);
    }

    private List<string> GetCachedFiles(
        string folder,
        Func<string, bool> predicate,
        Dictionary<string, FolderFileCache> cache)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return new List<string>();

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(folder);
            if (fullPath.Length > (Path.GetPathRoot(fullPath)?.Length ?? 0))
                fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(fullPath)) fullPath = Path.GetPathRoot(folder) ?? folder;
        }
        catch
        {
            return new List<string>();
        }

        DateTime lastWriteUtc;
        try { lastWriteUtc = Directory.GetLastWriteTimeUtc(fullPath); }
        catch { lastWriteUtc = DateTime.MinValue; }

        if (cache.TryGetValue(fullPath, out var cached) && cached.LastWriteUtc == lastWriteUtc)
            return new List<string>(cached.Files);

        var files = SafeEnumerateFiles(fullPath)
            .Where(predicate)
            .ToList();

        cache[fullPath] = new FolderFileCache
        {
            Folder = fullPath,
            LastWriteUtc = lastWriteUtc,
            Files = files
        };

        return new List<string>(files);
    }

    private IEnumerable<string> GetActiveMusicDirectories()
    {
        if (Directory.Exists(txtSuper.Text)) yield return txtSuper.Text;
        if (Directory.Exists(txtHay.Text)) yield return txtHay.Text;
        if (Directory.Exists(txtKhoNhac.Text)) yield return txtKhoNhac.Text;
        if (Directory.Exists(txtKho.Text)) yield return txtKho.Text;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string folder)
    {
        try
        {
            // EnumerateFiles is lazy; materialize inside the try so access and I/O
            // errors raised during enumeration are handled by the catch clauses.
            return Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly).ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
        catch (DirectoryNotFoundException)
        {
            return Array.Empty<string>();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (System.Security.SecurityException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsVideoFile(string path) => VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    private static bool IsAudioFile(string path) => AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    private static int GetCount(NumericUpDown n) => (int)n.Value;

    private static string SanitizeFileName(string value)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c.ToString(), "_");
        return value.Trim().TrimEnd('.');
    }

    private static string FormatDuration(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return ts.TotalHours >= 1 ? ts.ToString(@"hh\:mm\:ss") : ts.ToString(@"mm\:ss");
    }

    private static void OpenFolder(string folder)
    {
        if (!Directory.Exists(folder)) return;
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = Q(folder), UseShellExecute = true });
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { } // File đang bị ffmpeg giữ — dọn lần sau
        catch (UnauthorizedAccessException) { } // Không có quyền — bỏ qua, không làm giật đoạn render
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record MusicItem(string Category, string Path);
}

