using System.Diagnostics;
using System.Globalization;
using RenderMusicPro.Services;

namespace RandomVideoDangVH;

public sealed partial class MainForm
{
    private readonly Label _renderTimingLabel = new();
    private readonly Stopwatch _renderWatch = new();
    private readonly RenderTimeEstimator _renderEstimate = new();
    private System.Windows.Forms.Timer? _renderUiTimer;
    private string? _timingFinalOutput;

    private void StartRenderTiming()
    {
        _renderEstimate.Reset();
        _renderWatch.Restart();
        if (_renderUiTimer == null)
        {
            _renderUiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _renderUiTimer.Tick += (_, _) => UpdateRenderTiming();
        }
        _renderUiTimer.Start();
        UpdateRenderTiming();
    }
    private void StopRenderTiming()
    {
        _renderWatch.Stop();
        _renderUiTimer?.Stop();
        UpdateRenderTiming();
    }
    private void UpdateRenderTiming()
    {
        if (_renderTimingLabel.IsDisposed) return;
        string text = "Đã chạy: " + ClockText(_renderWatch.Elapsed);
        if (_renderEstimate.Finished) text += "   ·   Đã hoàn thành";
        else if (_renderWatch.IsRunning)
        {
            var estimate = _renderEstimate.Estimate(_renderWatch.Elapsed.TotalSeconds);
            if (estimate.Remaining is TimeSpan remaining)
                text += $"   ·   Còn ≈ {ClockText(remaining)}   ·   Xong khoảng {(DateTime.Now + remaining).ToString(remaining.TotalDays >= 1 ? "dd/MM HH:mm:ss" : "HH:mm:ss")}" +
                    (estimate.Initial ? " (ước tính ban đầu)" : "");
            else
            {
                text += _renderEstimate.Finalizing ? "   ·   Đang hoàn tất file..." : "   ·   Đang ước tính thời gian hoàn thành...";
                if (estimate.StageRemaining is TimeSpan stage)
                    text += "   ·   Bước hiện tại còn ≈ " + ClockText(stage);
            }
        }
        if (_renderTimingLabel.Text != text) _renderTimingLabel.Text = text;
    }
    private static string ClockText(TimeSpan value)
    {
        long seconds = (long)Math.Max(0, Math.Round(value.TotalSeconds, MidpointRounding.AwayFromZero));
        return $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
    }

    private bool BeginFfmpegTiming(string args)
    {
        if (!_renderWatch.IsRunning) return false;
        if (!TryReadTargetSeconds(args, out double target)) return false;
        bool final = _timingFinalOutput != null && args.EndsWith(Q(_timingFinalOutput), StringComparison.OrdinalIgnoreCase);
        _renderEstimate.BeginStage(_renderWatch.Elapsed.TotalSeconds, target, final);
        return true;
    }
    private static bool TryReadTargetSeconds(string args, out double target)
    {
        target = 0;
        bool quoted = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == '"' && (i == 0 || args[i - 1] != '\\')) { quoted = !quoted; continue; }
            if (quoted || args[i] != '-' || i + 2 >= args.Length || args[i + 1] != 't' ||
                !char.IsWhiteSpace(args[i + 2]) || (i > 0 && !char.IsWhiteSpace(args[i - 1]))) continue;
            int start = i + 3;
            while (start < args.Length && char.IsWhiteSpace(args[start])) start++;
            int end = start;
            while (end < args.Length && !char.IsWhiteSpace(args[end])) end++;
            return double.TryParse(args.AsSpan(start, end - start), NumberStyles.Float,
                CultureInfo.InvariantCulture, out target) && double.IsFinite(target) && target > 0;
        }
        return false;
    }
    private async Task<string> ReadFfmpegTimingAsync(StreamReader reader, Action? onActivity = null)
    {
        long lastTime = long.MinValue;
        while (await reader.ReadLineAsync().ConfigureAwait(false) is string line)
        {
            const string prefix = "out_time_us=";
            if (line.StartsWith(prefix, StringComparison.Ordinal) &&
                long.TryParse(line.AsSpan(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out long time))
            {
                _renderEstimate.ReportMicroseconds(time);
                // Chỉ tính là "có tiến trình" khi thời gian đầu ra thực sự tăng.
                if (time > lastTime) { lastTime = time; onActivity?.Invoke(); }
            }
        }
        return "";
    }
}
