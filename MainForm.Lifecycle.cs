using System.ComponentModel;

namespace RandomVideoDangVH;

public sealed partial class MainForm
{
    private readonly CancellationTokenSource _lifetimeCts = new();
    private bool _closeAfterRender;

    private void HandleFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_isRendering)
        {
            // Keep the message loop alive until FFmpeg has exited and all output
            // handles and temporary work directories have been cleaned up.
            e.Cancel = true;
            _closeAfterRender = true;
            StopRender();
            lblCurrent.Text = "Đang dừng render và dọn file tạm trước khi đóng...";
        }
        if (e.Cancel && !_closeAfterRender) return;
        _lifetimeCts.Cancel();
        if (!e.Cancel)
        {
            SavePersistentMetadataCache();
            SaveSettings();
        }
    }
}
