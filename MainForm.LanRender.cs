using System.Drawing;
using System.Text;
using System.Text.Json;
using RenderMusicPro.Services;

namespace RandomVideoDangVH;

// v1.5.0: RENDER NHIỀU MÁY QUA LAN.
// Máy GỬI random nhạc + clip + hiệu ứng (giữ lịch sử ít dùng tại một chỗ), ghi phiếu việc vào thư mục
// share; tự render phần của mình và copy kết quả máy khác từ share\out về thư mục đầu ra đã chọn.
// Máy NHẬN (bật "Cho phép nhận bài render chung") chỉ thực thi phiếu: copy nhạc về máy, đọc clip qua LAN,
// render cục bộ rồi đẩy kết quả lên share\out\<jobId>\.
public sealed partial class MainForm
{
    private static readonly string LanSettingsFile = Path.Combine(SettingsDirectory, "lan-render.json");
    private LanSettings _lan = new();
    private readonly System.Windows.Forms.Timer _lanTimer = new() { Interval = 5000 };
    private readonly RoundedButton btnLan = new();
    private bool _lanTickBusy;
    private bool _lanWorkerBusy;
    private bool _lanBatchRunning;
    private bool _renderStarting;
    private bool _lanRecovered;
    private string? _lanCurrentJob;
    private string? _lanCurrentSender;
    private string? _lanCurrentBaseName;
    private CancellationTokenSource? _lanJobCts;
    private Task? _lanWorkerTask;
    private string? _lanStopReason;
    private int _lanLostTicks;
    private LanRenderQueue? _lanQueue;
    private string? _lanLastError;
    private bool? _runFastStart;

    private bool LanShareReady => _lan.MusicRoot.Length > 0 && _lan.VideoRoot.Length > 0 && _lan.OutRoot.Length > 0;
    private bool LanDistributeEnabled => LanShareReady && _lan.Distribute;

    // =========================================================
    // CÀI ĐẶT + NHỊP TIM
    // =========================================================
    private void InitLan()
    {
        try
        {
            if (File.Exists(LanSettingsFile))
                _lan = JsonSerializer.Deserialize<LanSettings>(File.ReadAllText(LanSettingsFile, Encoding.UTF8)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            AppLog.Write("Không đọc được cài đặt Render LAN: " + ex.Message);
        }
        _lan.MusicRoot = (_lan.MusicRoot ?? "").Trim();
        _lan.VideoRoot = (_lan.VideoRoot ?? "").Trim();
        _lan.OutRoot = (_lan.OutRoot ?? "").Trim();
        // Nâng cấp từ bản cũ (1 thư mục share duy nhất): chuyển sang cả 3 thư mục, chạy được ngay không cần cấu hình lại.
        if (_lan.MusicRoot.Length == 0 && _lan.VideoRoot.Length == 0 && _lan.OutRoot.Length == 0 && (_lan.ShareRoot ?? "").Trim().Length > 0)
            _lan.MusicRoot = _lan.VideoRoot = _lan.OutRoot = (_lan.ShareRoot ?? "").Trim();
        _lanTimer.Tick += async (_, _) => await LanTickAsync();
        _lanTimer.Start();
        Disposed += (_, _) => _lanTimer.Dispose();
        if (_lan.AcceptJobs && LanShareReady)
            AddLog("🌐 Đang bật nhận bài render chung · Ra: " + _lan.OutRoot, Cyan);
        if (LanShareReady) _ = RecoverLanAfterRestartAsync();
    }

    /// <summary>Hàng chờ gắn với 3 thư mục share hiện tại; giữ nguyên giữa các tick để bộ theo dõi "lần cuối thấy đổi" hoạt động.</summary>
    private LanRenderQueue GetLanQueue()
    {
        var queue = _lanQueue;
        string music = Path.GetFullPath(_lan.MusicRoot.Trim());
        string video = Path.GetFullPath(_lan.VideoRoot.Trim());
        string outRoot = Path.GetFullPath(_lan.OutRoot.Trim());
        if (queue == null || !string.Equals(queue.MusicRoot, music, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(queue.VideoRoot, video, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(queue.OutRoot, outRoot, StringComparison.OrdinalIgnoreCase))
            _lanQueue = queue = new LanRenderQueue(_lan.MusicRoot, _lan.VideoRoot, _lan.OutRoot);
        return queue;
    }

    /// <summary>Khởi động / đổi share: trả về hàng chờ phiếu running của chính máy này; lô cũ do máy này gửi bị hủy.</summary>
    private async Task RecoverLanAfterRestartAsync()
    {
        try
        {
            var queue = GetLanQueue();
            var (released, abandoned) = await Task.Run(() =>
            {
                queue.EnsureFolders();
                return queue.RecoverAfterRestart(true, AppLog.Write);
            });
            _lanRecovered = true;
            if (released > 0) AddLog($"🌐 Đã trả {released:N0} phiếu LAN dang dở của máy này về hàng chờ.", Cyan);
            if (abandoned > 0) AddLog($"🌐 Đã hủy {abandoned:N0} lô/phiếu LAN cũ do máy này gửi trước khi tắt.", Cyan);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _lanRecovered = false;
            AppLog.Write("Chưa dọn được phiếu LAN dang dở (sẽ thử lại): " + ex.Message);
        }
    }

    private void SaveLanSettings()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            string temp = LanSettingsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_lan, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
            File.Move(temp, LanSettingsFile, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("Không lưu được cài đặt Render LAN: " + ex.Message);
        }
    }

    private void ReportLanError(string message)
    {
        if (message == _lanLastError) return;
        _lanLastError = message;
        AddLog("⚠ Render LAN: " + message, Orange);
    }

    // Máy nhận chỉ giành phiếu khi: bật nhận, rảnh, không đang bấm Bắt đầu / render, không đang làm phiếu LAN.
    private bool LanCanClaim() =>
        _lan.AcceptJobs && _licenseValid && !_isRendering && !_renderStarting && _cts == null && !_lanWorkerBusy && !IsDisposed;

    private void CancelLanJob(string reason)
    {
        _lanStopReason ??= reason;
        try { _lanJobCts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    // Tick chỉ làm việc ngắn (nhịp tim, chạm phiếu, giành phiếu); render phiếu chạy tách khỏi tick.
    private async Task LanTickAsync()
    {
        if (_lanTickBusy || !LanShareReady || IsDisposed) return;
        // Máy GỬI tự quảng bá tên máy qua UDP broadcast để máy NHẬN dò tìm được, không phụ thuộc AcceptJobs.
        if (_lan.Distribute) _ = LanDiscoveryService.AnnounceOnceAsync();
        if (!_lan.AcceptJobs && !_lanBatchRunning && !_lanWorkerBusy) return;
        _lanTickBusy = true;
        try
        {
            var queue = GetLanQueue();
            bool accept = _lan.AcceptJobs && !_isRendering && _licenseValid;
            string? current = _lanCurrentJob;
            bool owned = true, batchCancelled = false;
            await Task.Run(() =>
            {
                queue.WriteHeartbeat(accept || _lanWorkerBusy, current);
                if (current != null)
                {
                    owned = queue.TouchRunning(current);
                    batchCancelled = queue.IsBatchCancelled(LanRenderQueue.BatchIdOf(current));
                }
            });
            _lanLastError = null;

            if (current != null && current == _lanCurrentJob)
            {
                if (batchCancelled) CancelLanJob("cancelled");
                else if (!owned) { if (++_lanLostTicks >= 3) CancelLanJob("lost"); }
                else _lanLostTicks = 0;
            }

            if (!_lanRecovered && !_lanBatchRunning && LanCanClaim())
                await RecoverLanAfterRestartAsync();

            if (!LanCanClaim()) return;
            _ffmpegPath ??= FindFfmpeg();
            if (_ffmpegPath == null) { ReportLanError("Máy này không có ffmpeg.exe nên không nhận bài."); return; }

            // Đặt _lanWorkerBusy NGAY (cùng luồng UI, trước await) để không tick/Bắt đầu nào chen vào giành thêm.
            _lanWorkerBusy = true;
            LanJob? job = null;
            try
            {
                job = await Task.Run(() =>
                {
                    queue.EnsureFolders();
                    return queue.ClaimNext();
                });
            }
            finally { if (job == null) _lanWorkerBusy = false; }
            if (job != null) _lanWorkerTask = RunRemoteJobSafeAsync(queue, job);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ReportLanError("Không truy cập được thư mục share LAN (nhạc: " + _lan.MusicRoot + " · video: " + _lan.VideoRoot +
                " · ra: " + _lan.OutRoot + "): " + FirstLine(ex.Message));
        }
        finally { _lanTickBusy = false; }
    }

    /// <summary>
    /// Bấm Bắt đầu khi đang render giúp máy khác: hỏi; Có thì hủy phiếu LAN và CHỜ nó dọn xong (trả phiếu, xóa thư mục tạm,
    /// khôi phục biến hiệu ứng, giải phóng _cts) rồi mới cho phép lượt render của mình. Trả false nếu người dùng chọn Không.
    /// </summary>
    private async Task<bool> PreemptLanWorkerAsync()
    {
        // Máy nhận đang giành phiếu (chưa có tác vụ): chờ ngắn để biết có việc thật hay không.
        for (int i = 0; i < 200 && _lanWorkerBusy && _lanWorkerTask == null; i++) await Task.Delay(100);
        if (!_lanWorkerBusy) return true;
        var task = _lanWorkerTask;
        string who = _lanCurrentSender ?? "khác", name = _lanCurrentBaseName ?? "";
        var answer = MessageBox.Show(this,
            $"Máy đang render giúp máy {who}: {name}. Dừng bài đang giúp để render việc của bạn?\n(Bài sẽ được trả về hàng chờ cho máy khác làm.)",
            "Render LAN - Ưu tiên việc của mình", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return false;
        if (!_lanWorkerBusy) return true; // phiếu đã xong trong lúc hộp thoại mở
        CancelLanJob("preempt");
        task = _lanWorkerTask ?? task;
        if (task != null)
        {
            try { await task; } catch { }
        }
        for (int i = 0; i < 100 && _lanWorkerBusy; i++) await Task.Delay(100);
        if (_lanWorkerBusy || _cts != null)
        {
            ShowError("Chưa dừng xong bài đang render giúp. Hãy thử bấm Bắt đầu lại sau vài giây.");
            return false;
        }
        return true;
    }

    /// <summary>Hộp thoại chọn 1 trong danh sách (dùng khi dò tìm UDP thấy nhiều máy chủ kho cùng lúc).</summary>
    private string? PromptForChoice(IWin32Window owner, string title, string label, List<string> options)
    {
        using var form = new Form
        {
            Text = title, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ClientSize = new Size(360, 260),
            BackColor = Card, ForeColor = TextMain, Font = Font
        };
        var lbl = new Label { Text = label, Location = new Point(12, 12), Size = new Size(336, 36), ForeColor = TextMain };
        var list = new ListBox { Location = new Point(12, 52), Size = new Size(336, 150), BackColor = Input, ForeColor = TextMain };
        list.Items.AddRange(options.Cast<object>().ToArray());
        list.SelectedIndex = 0;
        var ok = new Button { Text = "Chọn", DialogResult = DialogResult.OK, Location = new Point(168, 212), Size = new Size(90, 30), FlatStyle = FlatStyle.Flat, ForeColor = TextMain };
        var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, Location = new Point(264, 212), Size = new Size(84, 30), FlatStyle = FlatStyle.Flat, ForeColor = TextMain };
        form.Controls.AddRange(new Control[] { lbl, list, ok, cancel });
        form.AcceptButton = ok; form.CancelButton = cancel;
        return form.ShowDialog(owner) == DialogResult.OK ? list.SelectedItem?.ToString() : null;
    }

    /// <summary>Hộp thoại nhập 1 dòng chữ đơn giản (không có Microsoft.VisualBasic.Interaction.InputBox trong WinForms thuần).</summary>
    private string? PromptForText(IWin32Window owner, string title, string label, string defaultValue)
    {
        using var form = new Form
        {
            Text = title, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ClientSize = new Size(420, 150),
            BackColor = Card, ForeColor = TextMain, Font = Font
        };
        var lbl = new Label { Text = label, Location = new Point(12, 12), Size = new Size(396, 40), ForeColor = TextMain };
        var txt = new TextBox { Text = defaultValue, Location = new Point(12, 56), Size = new Size(396, 24), BackColor = Input, ForeColor = TextMain, BorderStyle = BorderStyle.FixedSingle };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(228, 96), Size = new Size(90, 30), FlatStyle = FlatStyle.Flat, ForeColor = TextMain };
        var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, Location = new Point(324, 96), Size = new Size(84, 30), FlatStyle = FlatStyle.Flat, ForeColor = TextMain };
        form.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
        form.AcceptButton = ok; form.CancelButton = cancel;
        txt.SelectAll();
        return form.ShowDialog(owner) == DialogResult.OK ? txt.Text : null;
    }

    // =========================================================
    // HỘP THOẠI CÀI ĐẶT
    // =========================================================
    private void ShowLanDialog()
    {
        using var form = new Form
        {
            Text = "Render nhiều máy qua LAN",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false,
            ClientSize = new Size(640, 562),
            BackColor = Card, ForeColor = TextMain, Font = Font
        };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(16), BackColor = Color.Transparent };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        form.Controls.Add(grid);
        int row = 0;

        Label Muted(string text) => new()
        {
            Text = text, AutoSize = true, ForeColor = TextMuted, MaximumSize = new Size(600, 0), Margin = new Padding(0, 2, 0, 8)
        };

        TextBox AddFolderRow(string label, string current, string dialogDescription)
        {
            var title = new Label { Text = label, AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            grid.Controls.Add(title, 0, row); grid.SetColumnSpan(title, 2); row++;
            var txt = new TextBox { Text = current, Dock = DockStyle.Fill, BackColor = Input, ForeColor = TextMain, BorderStyle = BorderStyle.FixedSingle };
            var browse = new Button { Text = "Chọn...", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, ForeColor = TextMain };
            browse.Click += async (_, _) =>
            {
                using var dialog = new FolderBrowserDialog { Description = dialogDescription, UseDescriptionForTitle = true };
                if (Directory.Exists(txt.Text.Trim())) dialog.SelectedPath = txt.Text.Trim();
                if (dialog.ShowDialog(form) != DialogResult.OK) return;
                txt.Text = dialog.SelectedPath;
                browse.Enabled = false;
                // Dùng `this` (MainForm) làm owner, không dùng `form`: nếu người dùng bấm Lưu/Hủy đóng hộp thoại
                // RENDER LAN trước khi việc bật chia sẻ (có thể phải chờ UAC) xong, `form` đã bị Dispose và mọi
                // MessageBox.Show(form, ...) sau đó sẽ ném "Cannot access a disposed object".
                try { await EnsureFolderSharedAsync(this, dialog.SelectedPath); }
                finally { if (!browse.IsDisposed) browse.Enabled = true; }
            };
            grid.Controls.Add(txt, 0, row);
            grid.Controls.Add(browse, 1, row);
            row++;
            return txt;
        }

        var txtMusic = AddFolderRow("Thư mục Nhạc vào (kho nhạc share LAN)", _lan.MusicRoot, "Chọn thư mục Nhạc vào share LAN");
        var txtVideo = AddFolderRow("Thư mục Video vào (kho video + hiệu ứng share LAN)", _lan.VideoRoot, "Chọn thư mục Video vào share LAN");
        var txtOut = AddFolderRow("Thư mục Ra (trung gian trao đổi phiếu việc + kết quả)", _lan.OutRoot, "Chọn thư mục Ra share LAN");

        var hint = Muted("Máy gửi: chọn đúng 3 thư mục share (có thể là ổ trên máy này). Máy nhận: chọn cùng 3 thư mục đó qua mạng, ví dụ \\\\TEN-MAY\\share-nhac. " +
            "Kho nhạc của máy gửi phải nằm trong thư mục Nhạc vào; kho video + hiệu ứng phải nằm trong thư mục Video vào. Thư mục Ra chỉ dùng để trao đổi phiếu việc, không cần chứa gì sẵn.");
        grid.Controls.Add(hint, 0, row); grid.SetColumnSpan(hint, 2); row++;

        // Khai báo trước (chưa thêm vào grid) để các nút thiết lập nhanh bên dưới dùng được.
        var chkDistribute = new CheckBox { Text = "Máy GỬI: chia bài cho các máy khác khi bấm Bắt đầu render", Checked = _lan.Distribute, AutoSize = true, ForeColor = TextMain };
        var chkAccept = new CheckBox { Text = "Máy NHẬN: cho phép nhận bài render chung (khi máy đang rảnh)", Checked = _lan.AcceptJobs, AutoSize = true, ForeColor = TextMain };
        var chkAutoStart = new CheckBox { Text = "🪟 Tự khởi động cùng Windows (thu nhỏ) — bật máy lên là tự sẵn sàng nhận render, không cần mở tay", Checked = _lan.AutoStartWithWindows, AutoSize = true, ForeColor = TextMain };

        var btnFixedSetup = new Button
        {
            Text = "📁 Tạo/Dùng kho cố định trên ổ D (chỉ bấm ở MÁY GỬI, chủ kho)",
            Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, ForeColor = TextMain, Height = 28
        };
        btnFixedSetup.Click += async (_, _) =>
        {
            btnFixedSetup.Enabled = false;
            try
            {
                const string baseRoot = @"D:\Cache_Render_Music_Pro";
                string musicIn = Path.Combine(baseRoot, "Music_In");
                string videoIn = Path.Combine(baseRoot, "Video_In");
                string outDir = Path.Combine(baseRoot, "Out");
                foreach (string sub in new[] { "SIEU-HAY", "HAY", "Kho-Nhac", "KHO" })
                    Directory.CreateDirectory(Path.Combine(musicIn, sub));
                Directory.CreateDirectory(videoIn);
                Directory.CreateDirectory(outDir);

                txtMusic.Text = musicIn;
                txtVideo.Text = videoIn;
                txtOut.Text = outDir;
                txtSuper.Text = Path.Combine(musicIn, "SIEU-HAY");
                txtHay.Text = Path.Combine(musicIn, "HAY");
                txtKhoNhac.Text = Path.Combine(musicIn, "Kho-Nhac");
                txtKho.Text = Path.Combine(musicIn, "KHO");
                RefreshCounts();

                // Chia sẻ cả 3 thư mục trong MỘT lệnh PowerShell nâng quyền duy nhất — chỉ 1 lần Windows
                // hỏi UAC cho cả 3, thay vì hỏi riêng từng thư mục (người dùng không phải ngồi cấp quyền
                // nhiều lần liên tiếp).
                await EnableSharesBatchAsync(new[]
                {
                    (Name: "Music_In", Path: musicIn),
                    (Name: "Video_In", Path: videoIn),
                    (Name: "Out", Path: outDir)
                });
                AddLog("📁 Đã tạo + chia sẻ kho cố định: " + musicIn + " · " + videoIn + " · " + outDir, Green);

                try { System.Diagnostics.Process.Start("explorer.exe", musicIn); } catch { }
                try { System.Diagnostics.Process.Start("explorer.exe", videoIn); } catch { }

                // Bật luôn "Máy GỬI: chia bài" — nút này là thiết lập 1 lần cho XONG cả phần máy gửi,
                // không bắt người dùng tự nhớ tick thêm rồi bấm Lưu riêng.
                chkDistribute.Checked = true;

                MessageBox.Show(this,
                    "Đã tạo + chia sẻ xong 4 thư mục nhạc trong:\n" + musicIn + "\n(SIEU-HAY, HAY, Kho-Nhac, KHO)\n\n" +
                    "Thư mục video trong:\n" + videoIn + "\n\n" +
                    "Và thư mục Ra (trung gian) trong:\n" + outDir + "\n\n" +
                    "Đã tự bật \"Máy GỬI: chia bài\" — bấm OK ở đây là hộp thoại RENDER LAN tự Lưu và đóng lại luôn, xong cả phần cấu hình.\n\n" +
                    "Việc còn lại: COPY nhạc vào đúng 4 thư mục trên; tạo thư mục con (từng kho video) bên trong " +
                    videoIn + " rồi vào \"Nguồn video\" chọn đúng thư mục con đó.\n\n" +
                    "CHỈ bấm nút này ở MÁY CHỦ KHO (máy giữ nhạc/video thật). Máy khác muốn giúp render thì KHÔNG bấm nút này — " +
                    "dùng nút \"Dò tìm máy chủ kho\" hoặc \"Kết nối tới máy chủ kho\" ở bên dưới.",
                    "Render LAN - Kho cố định", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // Tự Lưu + đóng luôn (tương đương bấm "Lưu"): tạo kho xong là xem như đã bật dùng LAN hoàn chỉnh.
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
            catch (Exception ex)
            {
                AppLog.Write("Tạo kho cố định lỗi: " + ex);
                ShowError("Không tạo/chia sẻ được kho cố định trên ổ D.\n\n" + FirstLine(ex.Message));
            }
            finally { if (!btnFixedSetup.IsDisposed) btnFixedSetup.Enabled = true; }
        };
        grid.Controls.Add(btnFixedSetup, 0, row); grid.SetColumnSpan(btnFixedSetup, 2); row++;

        var btnDiscover = new Button
        {
            Text = "🔍 Dò tìm máy chủ kho trên mạng (tự động, không cần gõ tên máy)",
            Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, ForeColor = TextMain, Height = 28
        };
        btnDiscover.Click += async (_, _) =>
        {
            btnDiscover.Enabled = false;
            string originalText = btnDiscover.Text;
            btnDiscover.Text = "🔍 Đang dò tìm (3 giây)...";
            try
            {
                var hosts = await LanDiscoveryService.DiscoverAsync(TimeSpan.FromSeconds(3));
                string? chosen = hosts.Count switch
                {
                    0 => null,
                    1 => hosts[0],
                    _ => PromptForChoice(form, "Chọn máy chủ kho", "Tìm thấy nhiều máy đang chia bài, chọn 1 máy:", hosts)
                };
                if (chosen == null)
                {
                    if (hosts.Count == 0)
                        MessageBox.Show(form,
                            "Không tìm thấy máy nào đang bật \"chia bài\" trên mạng.\n\nKiểm tra: máy chủ kho có đang mở Render Music Pro, " +
                            "đã tick \"Máy GỬI: chia bài\" rồi bấm Lưu chưa; 2 máy có cùng mạng LAN (đặt Riêng tư) không; " +
                            "tường lửa có đang chặn không. Bạn có thể dùng nút \"Kết nối tới máy chủ kho\" bên dưới, tự gõ tên máy.",
                            "Render LAN - Dò tìm", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                string music = $@"\\{chosen}\Music_In", video = $@"\\{chosen}\Video_In", outp = $@"\\{chosen}\Out";
                txtMusic.Text = music; txtVideo.Text = video; txtOut.Text = outp;
                chkAccept.Checked = true;
                chkAutoStart.Checked = true;
                var (okMusic, okVideo, okOut) = await Task.Run(() =>
                    (Directory.Exists(music), Directory.Exists(video), Directory.Exists(outp)));
                if (okMusic && okVideo && okOut)
                {
                    AddLog("🔍 Đã tìm thấy + kết nối máy " + chosen + " — tự Lưu + bật nhận render, tự khởi động cùng Windows.", Green);
                    // Tự động hóa hoàn toàn phần máy NHẬN: tìm thấy + đọc được là xong luôn, không cần bấm Lưu tay.
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                }
                else
                    AddLog($"🔍 Tìm thấy máy {chosen} nhưng CHƯA đọc được đủ 3 thư mục — kiểm tra lại chia sẻ bên máy chủ kho rồi dò lại.", Orange);
            }
            finally { if (!btnDiscover.IsDisposed) { btnDiscover.Enabled = true; btnDiscover.Text = originalText; } }
        };
        grid.Controls.Add(btnDiscover, 0, row); grid.SetColumnSpan(btnDiscover, 2); row++;

        var btnConnectReceiver = new Button
        {
            Text = "🔌 Kết nối tới máy chủ kho (gõ tay tên máy, dùng khi dò tìm không thấy)",
            Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, ForeColor = TextMain, Height = 28
        };
        btnConnectReceiver.Click += async (_, _) =>
        {
            string? host = PromptForText(form, "Kết nối máy NHẬN",
                "Nhập tên máy hoặc IP của máy đang giữ kho (máy đã bấm \"Tạo/Dùng kho cố định trên ổ D\"):",
                Environment.MachineName);
            if (string.IsNullOrWhiteSpace(host)) return;
            host = host.Trim().Trim('\\');
            string music = $@"\\{host}\Music_In", video = $@"\\{host}\Video_In", outp = $@"\\{host}\Out";
            txtMusic.Text = music; txtVideo.Text = video; txtOut.Text = outp;
            chkAccept.Checked = true;
            chkAutoStart.Checked = true;

            btnConnectReceiver.Enabled = false;
            try
            {
                var (okMusic, okVideo, okOut) = await Task.Run(() =>
                    (Directory.Exists(music), Directory.Exists(video), Directory.Exists(outp)));
                if (okMusic && okVideo && okOut)
                {
                    AddLog("🔌 Đã kết nối máy " + host + " — tự Lưu + bật nhận render, tự khởi động cùng Windows.", Green);
                    // Tự động hóa hoàn toàn phần máy NHẬN: đọc được cả 3 thư mục là xong luôn, không cần bấm Lưu tay.
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                }
                else
                    AddLog($"🔌 Đã điền đường dẫn tới máy {host}, NHƯNG chưa đọc được: " +
                        string.Join(", ", new[] { (okMusic, "Music_In"), (okVideo, "Video_In"), (okOut, "Out") }
                            .Where(t => !t.Item1).Select(t => t.Item2)) +
                        " — kiểm tra lại tên máy/chia sẻ bên máy chủ kho.", Orange);
            }
            finally { if (!btnConnectReceiver.IsDisposed) btnConnectReceiver.Enabled = true; }
        };
        grid.Controls.Add(btnConnectReceiver, 0, row); grid.SetColumnSpan(btnConnectReceiver, 2); row++;

        grid.Controls.Add(chkDistribute, 0, row); grid.SetColumnSpan(chkDistribute, 2); row++;
        grid.Controls.Add(chkAccept, 0, row); grid.SetColumnSpan(chkAccept, 2); row++;
        grid.Controls.Add(chkAutoStart, 0, row); grid.SetColumnSpan(chkAutoStart, 2); row++;

        var lblMachines = Muted("Máy đang hoạt động: bấm \"Kiểm tra\" để xem.");
        var check = new Button { Text = "Kiểm tra", Dock = DockStyle.Top, FlatStyle = FlatStyle.Flat, ForeColor = TextMain, Height = 28 };
        check.Click += async (_, _) =>
        {
            string music = txtMusic.Text.Trim(), video = txtVideo.Text.Trim(), outRoot = txtOut.Text.Trim();
            if (music.Length == 0 || video.Length == 0 || outRoot.Length == 0) { lblMachines.Text = "Chưa chọn đủ 3 thư mục."; return; }
            check.Enabled = false;
            lblMachines.Text = "Đang kiểm tra...";
            try
            {
                var (alive, pending) = await Task.Run(() =>
                {
                    var q = new LanRenderQueue(music, video, outRoot);
                    q.EnsureFolders();
                    return (q.GetAliveMachines(), q.ListPending().Count);
                });
                string list = alive.Count == 0 ? "chưa có máy nào" : string.Join(", ", alive.Select(m =>
                    m.Machine + (m.CurrentJob != null ? " (đang render)" : m.Accept ? " (rảnh, nhận bài)" : " (không nhận bài)")));
                lblMachines.Text = $"Ghi được vào share ✓ · Máy đang hoạt động: {list} · Phiếu chờ: {pending:N0}";
            }
            catch (Exception ex)
            {
                lblMachines.Text = "Không truy cập được thư mục share: " + FirstLine(ex.Message);
            }
            finally { check.Enabled = true; }
        };
        grid.Controls.Add(lblMachines, 0, row);
        grid.Controls.Add(check, 1, row);
        row++;

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, BackColor = Color.Transparent };
        var ok = new Button { Text = "Lưu", Width = 96, Height = 30, DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, ForeColor = TextMain };
        var cancel = new Button { Text = "Hủy", Width = 96, Height = 30, DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, ForeColor = TextMain };
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
        grid.Controls.Add(buttons, 0, row); grid.SetColumnSpan(buttons, 2);
        form.AcceptButton = ok; form.CancelButton = cancel;

        if (form.ShowDialog(this) != DialogResult.OK) return;
        string newMusic = txtMusic.Text.Trim(), newVideo = txtVideo.Text.Trim(), newOut = txtOut.Text.Trim();
        if ((chkDistribute.Checked || chkAccept.Checked) && (newMusic.Length == 0 || newVideo.Length == 0 || newOut.Length == 0))
        {
            ShowError("Hãy chọn đủ 3 thư mục share LAN (Nhạc vào, Video vào, Ra) trước khi bật chia bài / nhận bài.");
            return;
        }
        bool acceptChanged = chkAccept.Checked != _lan.AcceptJobs;
        bool autoStartChanged = chkAutoStart.Checked != _lan.AutoStartWithWindows;
        bool rootChanged = !string.Equals(newMusic, _lan.MusicRoot, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(newVideo, _lan.VideoRoot, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(newOut, _lan.OutRoot, StringComparison.OrdinalIgnoreCase);
        _lan = new LanSettings
        {
            MusicRoot = newMusic, VideoRoot = newVideo, OutRoot = newOut,
            Distribute = chkDistribute.Checked, AcceptJobs = chkAccept.Checked, AutoStartWithWindows = chkAutoStart.Checked
        };
        SaveLanSettings();
        if (autoStartChanged)
        {
            try
            {
                AutoStartService.SetEnabled(chkAutoStart.Checked);
                AddLog(chkAutoStart.Checked ? "🪟 Đã bật tự khởi động cùng Windows (thu nhỏ)." : "🪟 Đã tắt tự khởi động cùng Windows.", Cyan);
            }
            catch (Exception ex)
            {
                AppLog.Write("Tự khởi động cùng Windows lỗi: " + ex);
                ShowError("Không đăng ký được tự khởi động cùng Windows.\n\n" + FirstLine(ex.Message));
            }
        }
        if (rootChanged) _lanRecovered = false;
        if (LanShareReady && _lan.AcceptJobs && (acceptChanged || rootChanged) && !_lanWorkerBusy && !_lanBatchRunning)
            _ = RecoverLanAfterRestartAsync(); // bật nhận bài: trả về hàng chờ phiếu running__<máy này> còn sót
        UpdateLanButton();
        if (acceptChanged)
            AddLog(_lan.AcceptJobs ? "🌐 Đã bật nhận bài render chung · Ra: " + newOut : "🌐 Đã tắt nhận bài render chung.", Cyan);
        if (_lan.Distribute)
        {
            AddLog("🌐 Lần render tới sẽ chia bài cho các máy nhận · Nhạc: " + newMusic + " · Video: " + newVideo + " · Ra: " + newOut, Cyan);
            // Gõ tay đường dẫn (không qua nút Chọn) thì vẫn nhắc bật chia sẻ nếu thư mục chưa share.
            _ = EnsureFolderSharedAsync(this, newMusic, announceExisting: false);
            _ = EnsureFolderSharedAsync(this, newVideo, announceExisting: false);
            _ = EnsureFolderSharedAsync(this, newOut, announceExisting: false);
        }
    }

    // =========================================================
    // BẬT CHIA SẺ LAN CHO THƯ MỤC (máy gửi)
    // =========================================================
    private sealed record SmbShareInfo(string Name, string Path);

    private static async Task<List<SmbShareInfo>> GetSmbSharesAsync()
    {
        string output = await RunProcessCaptureAsync("powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-SmbShare | Where-Object { $_.Path } | ForEach-Object { $_.Name + '|' + $_.Path }\"",
            CancellationToken.None);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|', 2)).Where(p => p.Length == 2 && !p[0].EndsWith('$'))
            .Select(p => new SmbShareInfo(p[0], p[1])).ToList();
    }

    private static string NormalizeDir(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

    /// <summary>Đường dẫn mạng (\\MÁY\share\...) nếu thư mục (hoặc thư mục cha) đã được chia sẻ.</summary>
    private static string? FindUncPath(List<SmbShareInfo> shares, string folder)
    {
        string target = NormalizeDir(folder);
        foreach (var share in shares.OrderByDescending(s => s.Path.Length))
        {
            string root;
            try { root = NormalizeDir(share.Path); } catch { continue; }
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            string rest = target[root.Length..].TrimEnd(Path.DirectorySeparatorChar);
            return $@"\\{Environment.MachineName}\{share.Name}" + (rest.Length > 0 ? @"\" + rest : "");
        }
        return null;
    }

    private async Task EnsureFolderSharedAsync(IWin32Window owner, string folder, bool announceExisting = true, bool silent = false)
    {
        if (folder.StartsWith(@"\\", StringComparison.Ordinal) || !Directory.Exists(folder)) return; // đường dẫn mạng = máy nhận
        try
        {
            var shares = await GetSmbSharesAsync();
            string? unc = FindUncPath(shares, folder);
            if (unc != null && !announceExisting) return;
            if (unc == null)
            {
                if (LanRenderQueue.IsShareFolderTooBroad(folder, out string broadReason))
                {
                    ShowError("Thư mục này quá rộng để tự bật chia sẻ:\n" + folder + "\n\n" + broadReason);
                    return;
                }
                if (!silent && MessageBox.Show(owner,
                        "Thư mục này CHƯA được chia sẻ LAN:\n" + folder +
                        "\n\nBật chia sẻ ngay? Windows sẽ hỏi quyền quản trị. App sẽ:\n" +
                        "• Chia sẻ thư mục cho mọi người trong mạng (đọc + ghi)\n" +
                        "• Cấp quyền ghi cho thư mục\n" +
                        "• Bật quy tắc tường lửa \"Chia sẻ tệp và máy in\"",
                        "Render LAN - Bật chia sẻ", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                // silent=true (vd nút "Tạo/Dùng kho cố định"): người dùng đã đồng ý rõ ràng qua chính nút đó,
                // Windows vẫn tự hỏi quyền quản trị (UAC) cho từng thư mục — không bỏ qua được bước này.
                string name = await EnableShareAsync(folder, shares);
                shares = await GetSmbSharesAsync();
                unc = FindUncPath(shares, folder);
                if (unc == null)
                {
                    ShowError("Chưa bật được chia sẻ cho thư mục (" + name + "). Bạn có thể bật tay: chuột phải thư mục > Properties > Sharing > Advanced Sharing.");
                    return;
                }
                AddLog("🌐 Đã bật chia sẻ LAN: " + folder + " → " + unc, Green);
            }
            try { Clipboard.SetText(unc); } catch { }
            if (!silent)
                MessageBox.Show(owner,
                    "Thư mục đã được chia sẻ LAN.\n\nĐường dẫn cho MÁY NHẬN (đã copy sẵn, chỉ cần dán):\n" + unc +
                    "\n\nTrên máy nhận: bấm RENDER LAN, dán đường dẫn này vào ô thư mục share, tick \"cho phép nhận bài\".\n" +
                    "Nếu máy nhận không vào được: kiểm tra 2 máy cùng mạng, mạng đặt là Private (Riêng tư).",
                    "Render LAN - Đường dẫn chia sẻ", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.Write("Bật chia sẻ LAN lỗi: " + ex);
            ShowError("Không kiểm tra/bật được chia sẻ LAN.\n\n" + FirstLine(ex.Message) +
                "\n\nBạn có thể bật tay: chuột phải thư mục > Properties > Sharing > Advanced Sharing.");
        }
    }

    /// <summary>Chia sẻ nhiều thư mục trong MỘT lần nâng quyền PowerShell duy nhất (1 lần hỏi UAC cho cả
    /// lô) — dùng cho nút "Tạo/Dùng kho cố định" vì đã biết trước tên + đường dẫn cả 3 thư mục, không
    /// cần hỏi xác nhận từng cái như luồng "Chọn..." thủ công (EnsureFolderSharedAsync/EnableShareAsync).
    /// Bỏ qua thư mục nào đã được chia sẻ sẵn; không làm gì nếu cả lô đã chia sẻ đủ (không bật PowerShell).</summary>
    private static async Task EnableSharesBatchAsync(IEnumerable<(string Name, string Path)> toShare)
    {
        var shares = await GetSmbSharesAsync();
        var missing = toShare.Where(f => FindUncPath(shares, f.Path) == null).ToList();
        if (missing.Count == 0) return;
        foreach (var f in missing)
            if (LanRenderQueue.IsShareFolderTooBroad(f.Path, out string reason))
                throw new InvalidOperationException(reason + " (" + f.Path + ")");

        static string B64(string s) => "'" + Convert.ToBase64String(Encoding.UTF8.GetBytes(s)) + "'";
        const string DecodeName = "[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String({0}))";
        var statements = new List<string>
        {
            "$ErrorActionPreference='Stop'",
            "$everyone=([System.Security.Principal.SecurityIdentifier]'S-1-1-0').Translate([System.Security.Principal.NTAccount]).Value"
        };
        foreach (var f in missing)
        {
            statements.Add("$shareName=" + string.Format(DecodeName, B64(f.Name)));
            statements.Add("$sharePath=" + string.Format(DecodeName, B64(Path.GetFullPath(f.Path))));
            statements.Add("New-SmbShare -Name $shareName -Path $sharePath -ChangeAccess $everyone -ErrorAction SilentlyContinue | Out-Null");
            statements.Add("icacls $sharePath /grant '*S-1-1-0:(OI)(CI)M' | Out-Null");
        }
        statements.Add("try { Get-NetFirewallRule -Group '@FirewallAPI.dll,-28502' | Enable-NetFirewallRule } catch {}");
        string script = string.Join("; ", statements);
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + encoded)
        { UseShellExecute = true, Verb = "runas", WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden };
        try
        {
            using var process = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("Không chạy được PowerShell.");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new InvalidOperationException("Lệnh bật chia sẻ báo lỗi (mã " + process.ExitCode + ").");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("Bạn đã không cấp quyền quản trị nên chưa bật được chia sẻ.");
        }
    }

    private static async Task<string> EnableShareAsync(string folder, List<SmbShareInfo> shares)
    {
        string baseName = new string(Path.GetFileName(NormalizeDir(folder).TrimEnd(Path.DirectorySeparatorChar))
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray()).Trim('-');
        if (baseName.Length == 0) baseName = "RenderLAN";
        if (baseName.Length > 60) baseName = baseName[..60];
        string name = baseName;
        for (int n = 2; shares.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); n++) name = baseName + n;

        if (LanRenderQueue.IsShareFolderTooBroad(folder, out string broadReason))
            throw new InvalidOperationException(broadReason);
        // Truyền tên + đường dẫn qua base64 UTF-8 (giải mã trong script): không ghép chuỗi có dấu nháy vào lệnh chạy quyền admin.
        static string B64(string s) => "'" + Convert.ToBase64String(Encoding.UTF8.GetBytes(s)) + "'";
        const string DecodeName = "[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String({0}))";
        // Everyone theo SID (S-1-1-0) để chạy đúng trên Windows tiếng Việt/tiếng Anh;
        // nhóm tường lửa theo mã tài nguyên, không phụ thuộc ngôn ngữ.
        string script = string.Join("; ",
            "$ErrorActionPreference='Stop'",
            "$everyone=([System.Security.Principal.SecurityIdentifier]'S-1-1-0').Translate([System.Security.Principal.NTAccount]).Value",
            "$shareName=" + string.Format(DecodeName, B64(name)),
            "$sharePath=" + string.Format(DecodeName, B64(Path.GetFullPath(folder))),
            "New-SmbShare -Name $shareName -Path $sharePath -ChangeAccess $everyone | Out-Null",
            "icacls $sharePath /grant '*S-1-1-0:(OI)(CI)M' | Out-Null",
            "try { Get-NetFirewallRule -Group '@FirewallAPI.dll,-28502' | Enable-NetFirewallRule } catch {}");
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + encoded)
        { UseShellExecute = true, Verb = "runas", WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden };
        try
        {
            using var process = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("Không chạy được PowerShell.");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new InvalidOperationException("Lệnh bật chia sẻ báo lỗi (mã " + process.ExitCode + ").");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("Bạn đã không cấp quyền quản trị nên chưa bật được chia sẻ.");
        }
        return name;
    }

    private void UpdateLanButton()
    {
        string state = _lan.Distribute && _lan.AcceptJobs ? " (GỬI+NHẬN)" : _lan.Distribute ? " (GỬI)" : _lan.AcceptJobs ? " (NHẬN)" : "";
        btnLan.Text = "Render LAN" + state;
    }

    // =========================================================
    // MÁY GỬI
    // =========================================================
    private void ValidateLanPaths(LanRenderQueue queue, IEnumerable<string> musicFolders, IEnumerable<string> videoFolders)
    {
        foreach (string folder in musicFolders.Where(f => !string.IsNullOrWhiteSpace(f)))
            if (!queue.TryMakeRelativeMusic(folder, out _))
                throw new InvalidOperationException(
                    $"Đang bật chia bài LAN nhưng thư mục nhạc \"{folder}\" không nằm trong thư mục Nhạc vào \"{queue.MusicRoot}\". " +
                    "Hãy để kho nhạc bên trong thư mục Nhạc vào, hoặc tắt chia bài trong RENDER LAN.");
        foreach (string folder in videoFolders.Where(f => !string.IsNullOrWhiteSpace(f)))
            if (!queue.TryMakeRelativeVideo(folder, out _))
                throw new InvalidOperationException(
                    $"Đang bật chia bài LAN nhưng thư mục \"{folder}\" không nằm trong thư mục Video vào \"{queue.VideoRoot}\". " +
                    "Hãy để kho video và hiệu ứng bên trong thư mục Video vào, hoặc tắt chia bài trong RENDER LAN.");
    }

    private string RelMusic(LanRenderQueue queue, string fullPath)
    {
        if (!queue.TryMakeRelativeMusic(fullPath, out string rel))
            throw new InvalidOperationException("File nhạc nằm ngoài thư mục Nhạc vào: " + fullPath);
        return rel;
    }

    private string RelVideo(LanRenderQueue queue, string fullPath)
    {
        if (!queue.TryMakeRelativeVideo(fullPath, out string rel))
            throw new InvalidOperationException("File video/hiệu ứng nằm ngoài thư mục Video vào: " + fullPath);
        return rel;
    }

    private async Task<LanJob> PlanLanJobAsync(LanRenderQueue queue, string batchId, int index, string baseName,
        string playlistName, Dictionary<string, MusicPoolDeck> musicDecks, List<string> videoFiles,
        MusicPoolDeck? effectDeck, Random rng, CancellationToken token)
    {
        var selected = SelectMusic(musicDecks, rng);
        double total = 0;
        foreach (var chunk in selected.Chunk(4))
        {
            var durations = await Task.WhenAll(chunk.Select(m => GetDurationAsync(_ffmpegPath!, m.Path, token)));
            for (int i = 0; i < chunk.Length; i++)
            {
                if (durations[i] <= 0) throw new InvalidOperationException("Không đọc được nhạc nguồn: " + Path.GetFileName(chunk[i].Path));
                total += durations[i];
            }
        }
        // +2 giây dự phòng: thời lượng ghép thật có thể lệch vài mili giây so với tổng từng bài.
        var clips = await BuildVideoSequenceAsync(videoFiles, total + 2, rng, token);
        var job = new LanJob
        {
            JobId = LanRenderQueue.JobIdFor(batchId, index),
            BatchId = batchId,
            Sender = LanRenderQueue.CurrentMachineName(),
            Index = index,
            BaseName = baseName,
            PlaylistName = playlistName,
            OnSeconds = (double)numOnSeconds.Value,
            OffSeconds = (double)numOffSeconds.Value,
            PlannedDuration = total,
            EffectGreen = _runEffectGreen,
            EffectBackgroundMode = _runEffectBackgroundMode,
            FastStart = chkFastStart.Checked
        };
        foreach (var m in selected) job.Tracks.Add(new LanJobTrack { Category = m.Category, Path = RelMusic(queue, m.Path) });
        foreach (string clip in clips)
        {
            VideoCheckEntry? e;
            lock (_videoCheckCache) _videoCheckCache.TryGetValue(clip, out e);
            double d = e == null ? 8 : e.VideoDuration > 0 ? e.VideoDuration : e.FileDuration;
            job.Clips.Add(new LanJobClip { Path = RelVideo(queue, clip), Duration = d });
        }
        if (effectDeck != null) job.Effect = RelVideo(queue, effectDeck.Take(1, rng)[0]);
        await Task.Run(() => queue.Enqueue(job), token);
        return job;
    }

    private async Task RenderLanBatchAsync(Dictionary<string, MusicPoolDeck> musicDecks, List<string> videoFiles,
        int playlistCount, int startNumber, string playlistName, Random rng, CancellationToken token)
    {
        var queue = GetLanQueue();
        await Task.Run(queue.EnsureFolders, token);
        if (!_lanRecovered) await RecoverLanAfterRestartAsync();
        var poolBoxes = new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase)
        { ["SIEU-HAY"] = txtSuper, ["HAY"] = txtHay, ["Kho-Nhac"] = txtKhoNhac, ["KHO"] = txtKho };
        ValidateLanPaths(queue,
            musicDecks.Keys.Where(poolBoxes.ContainsKey).Select(k => poolBoxes[k].Text.Trim()),
            new[] { txtVideoSource.Text.Trim(), _effectDeck != null ? EffectSourceFolder() : "" });

        string musicOutput = txtMusicOutput.Text.Trim();
        string videoOutput = txtVideoOutput.Text.Trim();
        string batchId = LanRenderQueue.NewBatchId();
        await Task.Run(() => queue.CleanupOwnOldBatches(batchId), token);
        var effectDeck = _effectDeck;
        var runIndices = Enumerable.Range(0, playlistCount).Where(n => !_runSkipIndices.Contains(n)).ToList();
        var outstanding = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // jobId -> baseName
        int finished = 0, nextPlan = 0, lostChecks = 0, remoteDone = 0;
        var cleanupWarned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int lookahead = Math.Clamp(Environment.ProcessorCount / 4, 1, 3);
        string BaseNameAt(int index) => $"{startNumber + index} - {playlistName}";

        _renderEstimate.SetTotal(runIndices.Count);
        if (playlistCount > runIndices.Count)
            AddLog($"ℹ Bỏ qua {playlistCount - runIndices.Count:N0} mục đã có file đầu ra.", Cyan);
        AddLog($"🌐 Chia bài LAN: lô {batchId} · {runIndices.Count:N0} playlist · share ra {queue.OutRoot}", Cyan);

        using var pipelineCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var local = new Queue<(LanJob Job, Task<PreparedPlaylist> Prep)>();
        _lanBatchRunning = true;

        void UpdateProgress()
        {
            int pct = runIndices.Count == 0 ? 100 : (int)Math.Round(finished * 100.0 / runIndices.Count);
            progress.Value = Math.Clamp(pct, 0, 100);
            lblPercent.Text = pct + "%";
            lblOutputCount.Text = $"Đã tạo: {_runSucceeded:N0}/{runIndices.Count:N0}" +
                (remoteDone > 0 ? $" · máy khác {remoteDone:N0}" : "") +
                (_runFailures.Count > 0 ? $" · lỗi {_runFailures.Count:N0}" : "");
        }

        try
        {
            while (finished < runIndices.Count)
            {
                token.ThrowIfCancellationRequested();

                // 1) Nhận kết quả + lỗi từ máy khác.
                var done = await Task.Run(() => queue.ListDone(batchId), token);
                foreach (var (jobId, dir, info) in done)
                {
                    if (!outstanding.TryGetValue(jobId, out string? name))
                    {
                        // Đã nhận rồi (hoặc kết quả trùng của máy khác): chỉ dọn, thử lại mỗi vòng cho tới khi sạch.
                        if (!await Task.Run(() => LanRenderQueue.TryCleanupResult(dir), token) && cleanupWarned.Add(dir))
                            AddLog($"  ⚠ Chưa dọn được thư mục kết quả trên share ({Path.GetFileName(dir)}), sẽ thử lại.", Orange);
                        continue;
                    }
                    try
                    {
                        await Task.Run(() => LanRenderQueue.CollectResult(dir, info, name, videoOutput, musicOutput), token);
                        // Copy + kiểm tra dung lượng xong = ĐÃ NHẬN; dọn share là việc riêng, lỗi dọn không ảnh hưởng đếm.
                        outstanding.Remove(jobId);
                        finished++; remoteDone++; _runSucceeded++;
                        _renderEstimate.CompleteItem(_renderWatch.Elapsed.TotalSeconds);
                        AddLog($"  ✓ Nhận từ máy {info.Machine}: {name} (đã chuyển về thư mục đầu ra)", Green);
                        if (!await Task.Run(() => LanRenderQueue.TryCleanupResult(dir), token) && cleanupWarned.Add(dir))
                            AddLog($"  ⚠ Chưa dọn được thư mục kết quả trên share ({Path.GetFileName(dir)}), sẽ thử lại.", Orange);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (InvalidDataException ex)
                    {
                        outstanding.Remove(jobId);
                        finished++;
                        _runFailures.Add(new RenderFailure(name, FirstLine(ex.Message)));
                        AddLog($"  ✗ Kết quả {name} không hợp lệ: {FirstLine(ex.Message)}", Red);
                    }
                    catch (Exception ex)
                    {
                        AddLog($"  ⚠ Chưa chuyển được kết quả {name}: {FirstLine(ex.Message)} — sẽ thử lại.", Orange);
                    }
                }
                foreach (var (jobId, reason) in await Task.Run(() => queue.TakeFailed(batchId), token))
                {
                    if (!outstanding.Remove(jobId, out string? name)) continue;
                    finished++;
                    _runFailures.Add(new RenderFailure(name, FirstLine(reason)));
                    AddLog($"  ✗ Máy khác render lỗi {name}: {FirstLine(reason)}", Red);
                }
                int reclaimed = await Task.Run(() => queue.ReclaimStale(batchId, AppLog.Write), token);
                if (reclaimed > 0) AddLog($"  ↻ {reclaimed:N0} phiếu của máy mất kết nối đã được trả về hàng chờ.", Orange);
                UpdateProgress();

                // 2) Lên phiếu việc: mỗi máy nhận có sẵn 2 phiếu (máy này đang render thì không lên phiếu
                //    được, nên để dư 1 phiếu cho máy nhận làm xong bài hiện tại là lấy được bài tiếp).
                int helpers = (await Task.Run(queue.GetAliveMachines, token))
                    .Count(m => m.Accept && !string.Equals(m.Machine, queue.Machine, StringComparison.OrdinalIgnoreCase));
                int want = helpers * 2 + lookahead;
                while (nextPlan < runIndices.Count && await Task.Run(() => queue.CountPending(batchId), token) < want)
                {
                    int index = runIndices[nextPlan++];
                    string baseName = BaseNameAt(index);
                    lblCurrent.Text = $"Đang lên phiếu việc {nextPlan:N0}/{runIndices.Count:N0}: {baseName}";
                    try
                    {
                        var job = await PlanLanJobAsync(queue, batchId, index, baseName, playlistName, musicDecks,
                            videoFiles, effectDeck, rng, token);
                        outstanding[job.JobId] = baseName;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (OutOfDiskSpaceException) { throw; }
                    catch (Exception ex)
                    {
                        finished++;
                        _runFailures.Add(new RenderFailure(baseName, FirstLine(ex.Message)));
                        AddLog($"  ✗ Không lên được phiếu việc {baseName}: {FirstLine(ex.Message)}", Red);
                    }
                }

                // 3) Máy này tự giành phiếu để render (chuẩn bị nhạc trước vài bài như chế độ thường).
                while (local.Count < lookahead)
                {
                    var job = await Task.Run(() =>
                    {
                        foreach (string file in queue.ListPending(batchId))
                        {
                            var claimed = queue.TryClaim(file);
                            if (claimed != null) return claimed;
                        }
                        return (LanJob?)null;
                    }, token);
                    if (job == null) break;
                    local.Enqueue((job, PrepareJobAudioAsync(queue, job, false, pipelineCts.Token)));
                }

                if (local.Count > 0)
                {
                    lostChecks = 0;
                    var (job, prep) = local.Dequeue();
                    string name = outstanding.GetValueOrDefault(job.JobId, job.BaseName);
                    try
                    {
                    _renderEstimate.BeginItem(_renderWatch.Elapsed.TotalSeconds);
                    AddLog($"▶ [máy này] {name} • ghép video", TextMain);
                    bool ok = await RunItemWithRetryAsync(name, async attempt =>
                    {
                        PreparedPlaylist? current = null;
                        try
                        {
                            current = attempt == 1 ? await prep : await PrepareJobAudioAsync(queue, job, false, pipelineCts.Token);
                            await RenderJobVideoAsync(queue, job, current,
                                Path.Combine(videoOutput, job.BaseName + ".mp4"),
                                Path.Combine(musicOutput, job.BaseName + ".mp3"),
                                Path.Combine(musicOutput, job.BaseName + ".txt"), pipelineCts.Token);
                            _renderEstimate.CompleteItem(_renderWatch.Elapsed.TotalSeconds);
                        }
                        finally { if (current != null) TryDeleteDirectory(current.WorkDirectory); }
                    }, token);
                    await Task.Run(() => queue.CompleteRunning(job.JobId), CancellationToken.None);
                    outstanding.Remove(job.JobId);
                    finished++;
                    if (ok) _runSucceeded++;
                    else _renderEstimate.SetTotal(runIndices.Count - _runFailures.Count);
                    UpdateProgress();
                    }
                    catch (OperationCanceledException)
                    {
                        // Bấm Dừng giữa chừng: không để phiếu của chính mình nằm lại ở running\.
                        queue.ReleaseToPending(job.JobId);
                        throw;
                    }
                    continue;
                }

                // 4) Không còn gì để tự làm: chờ máy khác.
                if (nextPlan >= runIndices.Count && outstanding.Count > 0)
                {
                    int pending = await Task.Run(() => queue.CountPending(batchId), token);
                    int running = await Task.Run(() => queue.CountRunningOthers(batchId), token);
                    int resultDirs = await Task.Run(() => queue.ListResultJobIds(batchId).Count(outstanding.ContainsKey), token);
                    if (pending == 0 && running == 0 && resultDirs == 0)
                    {
                        // Phiếu không còn ở đâu cả (bị xóa tay?) — sau vài lần kiểm tra thì ghi nhận lỗi.
                        if (++lostChecks >= 5)
                        {
                            foreach (var pair in outstanding)
                                _runFailures.Add(new RenderFailure(pair.Value, "Mất phiếu việc trên thư mục share."));
                            AddLog($"  ✗ {outstanding.Count:N0} phiếu việc không còn trên share, ghi nhận lỗi.", Red);
                            finished += outstanding.Count;
                            outstanding.Clear();
                            break;
                        }
                    }
                    else lostChecks = 0;
                    lblCurrent.Text = $"🌐 Đang chờ máy khác: {outstanding.Count:N0} bài (đang render {running:N0}, chờ {pending:N0})...";
                }
                if (finished < runIndices.Count) await Task.Delay(3000, token);
            }
        }
        finally
        {
            _lanBatchRunning = false;
            if (local.Count > 0) pipelineCts.Cancel();
            while (local.Count > 0)
            {
                var (job, prep) = local.Dequeue();
                try { TryDeleteDirectory((await prep).WorkDirectory); } catch { }
                try { await Task.Run(() => queue.ReleaseToPending(job.JobId)); } catch { }
            }
            if (finished < runIndices.Count)
            {
                // Ghi file cancelled TRƯỚC rồi mới xóa pending: máy nhận không giành, không đẩy kết quả của lô này nữa.
                try { await Task.Run(() => queue.CancelBatch(batchId)); }
                catch (Exception ex) { AppLog.Write("Chưa ghi được dấu hủy lô " + batchId + ": " + ex.Message); }
                int remote = 0;
                try { remote = await Task.Run(() => queue.CountRunningOthers(batchId)); } catch { }
                if (remote > 0)
                    AddLog($"ℹ {remote:N0} bài đang render ở máy khác sẽ được báo hủy; kết quả dở (nếu có) trong {queue.OutDir} sẽ được dọn ở lần chia bài sau.", Orange);
            }
        }
    }

    // =========================================================
    // THỰC THI PHIẾU (dùng chung cho máy gửi và máy nhận)
    // =========================================================
    private async Task<PreparedPlaylist> PrepareJobAudioAsync(LanRenderQueue queue, LanJob job, bool copyLocal, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!LanRenderQueue.IsSafeBaseName(job.BaseName))
            throw new InvalidDataException("Tên bài trong phiếu việc không hợp lệ: " + job.BaseName);
        string workDirectory = CreateWorkDirectory();
        string masterAudio = Path.Combine(workDirectory, "playlist-master.wav");
        string musicFile = Path.Combine(workDirectory, job.BaseName + ".mp3");
        string trackFile = Path.Combine(workDirectory, job.BaseName + ".txt");
        string videoAudio = Path.Combine(workDirectory, "video-audio.m4a");
        try
        {
            AddLog($"♪ {job.BaseName} • chuẩn bị nhạc ({job.Tracks.Count:N0} bài)", TextMain);
            var tracks = new List<MusicItem>();
            for (int i = 0; i < job.Tracks.Count; i++)
            {
                string source = queue.ResolveMusic(job.Tracks[i].Path);
                if (!File.Exists(source)) throw new FileNotFoundException("Không thấy bài nhạc trên share: " + job.Tracks[i].Path);
                string path = source;
                if (copyLocal)
                {
                    // Copy nhạc về ổ máy này cho nhanh (giữ nguyên tên file để danh sách bài đúng tên).
                    path = Path.Combine(workDirectory, "src", i.ToString("000"), Path.GetFileName(source));
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await Task.Run(() => File.Copy(source, path, true), token);
                }
                tracks.Add(new MusicItem(job.Tracks[i].Category, path));
            }
            await ConcatAudioAsync(_ffmpegPath!, tracks.Select(x => x.Path).ToList(), masterAudio, token);
            await WriteTrackListAsync(trackFile, tracks, job.PlaylistName, token);
            double duration = await GetDurationAsync(_ffmpegPath!, masterAudio, token);
            if (duration <= 0.1) throw new InvalidOperationException("Không đọc được thời lượng nhạc đã ghép: " + job.BaseName);
            await Task.WhenAll(
                ExportMusicAsync(_ffmpegPath!, masterAudio, musicFile, token),
                EncodeVideoAudioAsync(_ffmpegPath!, masterAudio, videoAudio, job.OnSeconds, job.OffSeconds, token));
            TryDelete(masterAudio);
            if (copyLocal) TryDeleteDirectory(Path.Combine(workDirectory, "src"));
            AddLog($"  ♪ {job.BaseName}: nhạc sẵn sàng {FormatDuration(duration)}", Cyan);
            return new PreparedPlaylist(workDirectory, videoAudio, musicFile, trackFile, duration);
        }
        catch
        {
            TryDeleteDirectory(workDirectory);
            throw;
        }
    }

    private async Task RenderJobVideoAsync(LanRenderQueue queue, LanJob job, PreparedPlaylist prepared,
        string videoPath, string musicPath, string trackPath, CancellationToken token)
    {
        var clips = job.Clips.Select(c => queue.ResolveVideo(c.Path)).ToList();
        if (clips.Count == 0) throw new InvalidOperationException("Phiếu việc không có clip video.");
        // Nếu nhạc ghép thật dài hơn dự tính, lặp lại clip từ đầu danh sách cho đủ (không để hình ngắn hơn tiếng).
        double total = job.Clips.Sum(c => c.Duration);
        for (int i = 0; total < prepared.Duration + 0.5 && i < job.Clips.Count * 4; i++)
        {
            clips.Add(clips[i % job.Clips.Count]);
            total += Math.Max(job.Clips[i % job.Clips.Count].Duration, 0.5);
        }
        AddLog($"  ✓ {clips.Count:N0} clip theo phiếu việc · nhạc {FormatDuration(prepared.Duration)}", Green);

        var savedDeck = _effectDeck;
        bool savedGreen = _runEffectGreen, savedBlack = _runEffectBlack;
        int savedMode = _runEffectBackgroundMode;
        try
        {
            _effectDeck = job.Effect == null ? null : new MusicPoolDeck(new[] { queue.ResolveVideo(job.Effect) });
            _runEffectGreen = job.EffectGreen;
            _runEffectBackgroundMode = job.EffectBackgroundMode;
            _runEffectBlack = job.EffectBackgroundMode == 2;
            _runFastStart = job.FastStart;
            lblCurrent.Text = $"{job.BaseName} • ghép video...";
            await RenderVideoWithMusicAsync(_ffmpegPath!, clips, prepared.VideoAudio, true, videoPath, prepared.Duration, token);
        }
        finally
        {
            _effectDeck = savedDeck;
            _runEffectGreen = savedGreen; _runEffectBlack = savedBlack; _runEffectBackgroundMode = savedMode;
            _runFastStart = null;
        }
        await Task.Run(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(musicPath)!);
            File.Move(prepared.MusicFile, musicPath, true);
            File.Move(prepared.TrackFile, trackPath, true);
        }, CancellationToken.None);
        AddLog($"  ✓ Video: {Path.GetFileName(videoPath)} · Music: {Path.GetFileName(musicPath)}", Green);
    }

    // =========================================================
    // MÁY NHẬN
    // =========================================================
    private async Task RunRemoteJobSafeAsync(LanRenderQueue queue, LanJob job)
    {
        try { await RunRemoteJobAsync(queue, job); }
        catch (Exception ex)
        {
            AppLog.Write($"LỖI không lường trước khi render giúp \"{job.BaseName}\":\n{ex}");
            AddLog($"  ✗ Lỗi khi render giúp {job.BaseName}: {FirstLine(ex.Message)}", Red);
            try { queue.ReleaseToPending(job.JobId); } catch { }
        }
        finally
        {
            _lanCurrentJob = null;
            _lanWorkerBusy = false;
            _lanWorkerTask = null;
        }
    }

    private async Task RunRemoteJobAsync(LanRenderQueue queue, LanJob job)
    {
        var myCts = new CancellationTokenSource();
        _lanWorkerBusy = true;
        _lanCurrentJob = job.JobId;
        _lanCurrentSender = job.Sender;
        _lanCurrentBaseName = job.BaseName;
        _lanStopReason = null;
        _lanLostTicks = 0;
        _lanJobCts = myCts;
        _cts = myCts;
        var token = myCts.Token;
        bool released = false, finalized = false;
        try
        {
            SetRunning(true);
            btnStart.Enabled = _licenseValid; // cho phép "ưu tiên việc của mình": bấm Bắt đầu sẽ hỏi rồi dừng bài đang giúp
            _renderEstimate.SetTotal(1);
            _runDurationSnapshot = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            lblStatus.Text = "●  ĐANG RENDER GIÚP MÁY " + job.Sender;
            lblStatus.ForeColor = Cyan;
            progress.Value = 0; lblPercent.Text = "0%";
            AddLog($"🌐 Nhận bài từ máy {job.Sender}: {job.BaseName}", Cyan);
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                string? workRoot = null;
                PreparedPlaylist? prepared = null;
                try
                {
                    workRoot = CreateWorkDirectory();
                    string outDir = Path.Combine(workRoot, "out");
                    Directory.CreateDirectory(outDir);
                    _renderEstimate.BeginItem(_renderWatch.Elapsed.TotalSeconds);
                    prepared = await PrepareJobAudioAsync(queue, job, true, token);
                    string video = Path.Combine(outDir, job.BaseName + ".mp4");
                    string music = Path.Combine(outDir, job.BaseName + ".mp3");
                    string track = Path.Combine(outDir, job.BaseName + ".txt");
                    await RenderJobVideoAsync(queue, job, prepared, video, music, track, token);
                    lblCurrent.Text = $"{job.BaseName} • đang đẩy kết quả lên share...";
                    bool published = await Task.Run(() => queue.PublishResult(job.JobId, new[] { video, music, track }), token);
                    finalized = true;
                    if (!published)
                    {
                        AddLog($"  ↯ Phiếu {job.BaseName} đã bị thu hồi hoặc lô đã hủy, bỏ kết quả (không đẩy lên share).", Orange);
                        lblStatus.Text = "●  PHIẾU ĐÃ BỊ THU HỒI"; lblStatus.ForeColor = Orange;
                        break;
                    }
                    await Task.Run(() => queue.CompleteRunning(job.JobId), CancellationToken.None);
                    _renderEstimate.CompleteItem(_renderWatch.Elapsed.TotalSeconds);
                    AddLog($"  ✓ Đã gửi kết quả {job.BaseName} lên share cho máy {job.Sender}.", Green);
                    lblStatus.Text = "●  ĐÃ RENDER GIÚP XONG"; lblStatus.ForeColor = Green;
                    progress.Value = 100; lblPercent.Text = "100%";
                    break;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    await Task.Run(() => queue.ReleaseToPending(job.JobId), CancellationToken.None);
                    released = true; finalized = true;
                    string why = _lanStopReason switch
                    {
                        "preempt" => "để render việc của bạn",
                        "lost" => "(phiếu đã bị thu hồi)",
                        "cancelled" => "(máy gửi đã hủy lô)",
                        _ => "theo yêu cầu Dừng"
                    };
                    AddLog($"■ Đã dừng {why}; trả bài {job.BaseName} về hàng chờ cho máy khác.", Orange);
                    lblStatus.Text = "●  ĐÃ DỪNG"; lblStatus.ForeColor = Orange;
                    break;
                }
                catch (Exception ex)
                {
                    AppLog.Write($"LỖI bài LAN \"{job.BaseName}\" (lần {attempt}/2):\n{ex}");
                    AddLog($"  ✗ Lỗi bài {job.BaseName}: {FirstLine(ex.Message)}", Red);
                    if (attempt == 2)
                    {
                        await Task.Run(() => queue.MarkFailed(job.JobId, FirstLine(ex.Message)), CancellationToken.None);
                        finalized = true;
                        lblStatus.Text = "●  CÓ LỖI"; lblStatus.ForeColor = Red;
                    }
                    else AddLog($"  ↻ Thử lại 1 lần: {job.BaseName}", Orange);
                }
                finally
                {
                    if (prepared != null) TryDeleteDirectory(prepared.WorkDirectory);
                    if (workRoot != null) TryDeleteDirectory(workRoot);
                }
            }
            if (!finalized)
                await Task.Run(() => queue.ReleaseToPending(job.JobId), CancellationToken.None);
        }
        finally
        {
            _renderEstimate.Finish();
            SavePersistentMetadataCache();
            _runDurationSnapshot = null;
            // Chỉ giải phóng CTS do chính phiếu này tạo.
            if (ReferenceEquals(_cts, myCts)) _cts = null;
            if (ReferenceEquals(_lanJobCts, myCts)) _lanJobCts = null;
            myCts.Dispose();
            _lanCurrentJob = null;
            _lanCurrentSender = null;
            _lanCurrentBaseName = null;
            _lanWorkerBusy = false;
            SetRunning(false);
            lblCurrent.Text = released || !_lan.AcceptJobs ? lblCurrent.Text : "🌐 Đang chờ bài render chung tiếp theo...";
            if (_closeAfterRender && !IsDisposed) Close();
        }
    }
}
