using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace RenderMusicPro.Services;

public sealed class UpdateDownloadDialog : Form
{
    private readonly IUpdateService _service;
    private readonly UpdateReleaseInfo _release;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _cancel = new();
    private bool _finished;

    public string? DownloadedPath { get; private set; }
    public Exception? DownloadError { get; private set; }

    public UpdateDownloadDialog(IUpdateService service, UpdateReleaseInfo release)
    {
        _service = service;
        _release = release;

        Text = "Render Music Pro - Đang tải cập nhật";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(470, 154);
        BackColor = Color.FromArgb(15, 20, 38);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9F);

        var title = new Label
        {
            Text = $"Đang tải bản v{release.TagName} từ {(release.Source == "GitHub" ? "GitHub" : "thư mục LAN")}…",
            AutoSize = false,
            Location = new Point(20, 18),
            Size = new Size(430, 24),
            ForeColor = Color.White
        };
        _status.Text = "Đang kết nối…";
        _status.AutoSize = false;
        _status.Location = new Point(20, 49);
        _status.Size = new Size(430, 22);
        _status.ForeColor = Color.FromArgb(165, 180, 205);

        _progress.Location = new Point(20, 78);
        _progress.Size = new Size(430, 18);
        _progress.Style = ProgressBarStyle.Marquee;
        _progress.MarqueeAnimationSpeed = 24;

        _cancel.Text = "Hủy";
        _cancel.Location = new Point(366, 112);
        _cancel.Size = new Size(84, 28);
        _cancel.FlatStyle = FlatStyle.Flat;
        _cancel.FlatAppearance.BorderColor = Color.FromArgb(57, 75, 112);
        _cancel.BackColor = Color.FromArgb(31, 42, 69);
        _cancel.ForeColor = Color.White;
        _cancel.Click += (_, _) => RequestCancellation();

        Controls.AddRange(new Control[] { title, _status, _progress, _cancel });
        Shown += async (_, _) => await DownloadAsync();
        FormClosing += (_, e) =>
        {
            if (!_finished)
            {
                e.Cancel = true;
                RequestCancellation();
            }
        };
    }

    private void RequestCancellation()
    {
        if (_finished || _cancellation.IsCancellationRequested)
            return;
        _cancel.Enabled = false;
        _cancel.Text = "Đang hủy";
        _status.Text = "Đang dừng tải và xóa file chưa hoàn chỉnh…";
        _cancellation.Cancel();
    }

    private async Task DownloadAsync()
    {
        try
        {
            var progress = new Progress<UpdateDownloadProgress>(UpdateProgress);
            DownloadedPath = await _service.DownloadUpdateAsync(
                _release, progress, _cancellation.Token);
            _finished = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            _finished = true;
            DialogResult = DialogResult.Cancel;
            Close();
        }
        catch (Exception ex)
        {
            DownloadError = ex;
            _finished = true;
            DialogResult = DialogResult.Abort;
            Close();
        }
    }

    private void UpdateProgress(UpdateDownloadProgress value)
    {
        if (IsDisposed)
            return;

        if (value.TotalBytes is > 0)
        {
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = (int)Math.Clamp(
                value.BytesReceived * 100d / value.TotalBytes.Value,
                0,
                100);
            _status.Text = $"{FormatBytes(value.BytesReceived)} / {FormatBytes(value.TotalBytes.Value)}";
        }
        else
        {
            _progress.Style = ProgressBarStyle.Marquee;
            _status.Text = $"Đã tải {FormatBytes(value.BytesReceived)}";
        }

        if (value.IsComplete)
            _status.Text = "Đang xác minh bản tải về…";
    }

    private static string FormatBytes(long value) =>
        (value / 1024d / 1024d).ToString("N1", CultureInfo.CurrentCulture) + " MB";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _cancellation.Dispose();
        base.Dispose(disposing);
    }
}

