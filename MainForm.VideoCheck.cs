using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RandomVideoDangVH;

// Đo thời lượng thật của từng clip khi nó được chọn để ghép (không đo cả kho).
// Kết quả đo lưu cache theo đường dẫn + dung lượng + ngày sửa; clip đã đo thì không đo lại.
public sealed partial class MainForm
{
    private sealed record VideoCheckEntry(long FileLength, DateTime LastWriteUtc, string Codec,
        int Width, int Height, string Fps, double VideoDuration, double FileDuration, string Error);

    private readonly Dictionary<string, VideoCheckEntry> _videoCheckCache = new(StringComparer.OrdinalIgnoreCase);
    private string? _videoCheckCachePath;

    private void LoadVideoCheckCache()
    {
        string? metaPath = _metadataCachePath;
        if (string.IsNullOrEmpty(metaPath)) return;
        string path = Path.Combine(Path.GetDirectoryName(metaPath)!, "videocheck-v1.json");
        if (string.Equals(path, _videoCheckCachePath, StringComparison.OrdinalIgnoreCase)) return;
        _videoCheckCachePath = path;
        _videoCheckCache.Clear();
        try
        {
            if (!File.Exists(path)) return;
            var entries = JsonSerializer.Deserialize<Dictionary<string, VideoCheckEntry>>(File.ReadAllText(path));
            if (entries == null) return;
            foreach (var e in entries)
                if (e.Value != null) _videoCheckCache[e.Key] = e.Value;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _videoCheckCache.Clear();
        }
    }

    private void SaveVideoCheckCache()
    {
        string? path = _videoCheckCachePath;
        if (path == null) return;
        string partial = path + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            Dictionary<string, VideoCheckEntry> snapshot;
            lock (_videoCheckCache) snapshot = new(_videoCheckCache, StringComparer.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(partial, JsonSerializer.Serialize(snapshot), new UTF8Encoding(false));
            File.Move(partial, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { try { File.Delete(partial); } catch { } }
    }

    private static double ParseSeconds(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String &&
            double.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d))
            return d;
        return 0;
    }

    private async Task<VideoCheckEntry> ProbeVideoCheckAsync(string ffprobe, string file, CancellationToken token)
    {
        var info = new FileInfo(file);
        long len = info.Exists ? info.Length : 0;
        DateTime mtime = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
        try
        {
            string json = await RunProcessCaptureAsync(ffprobe,
                "-v error -select_streams v:0 -show_entries stream=codec_name,width,height,r_frame_rate,duration" +
                $" -show_entries format=duration -of json {Q(file)}", token);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("streams", out var streams) || streams.GetArrayLength() == 0)
                return new VideoCheckEntry(len, mtime, "", 0, 0, "", 0, 0, "không có luồng hình");
            var s = streams[0];
            string codec = s.TryGetProperty("codec_name", out var c) ? c.GetString() ?? "" : "";
            int w = s.TryGetProperty("width", out var wv) && wv.TryGetInt32(out int wi) ? wi : 0;
            int h = s.TryGetProperty("height", out var hv) && hv.TryGetInt32(out int hi) ? hi : 0;
            string fps = s.TryGetProperty("r_frame_rate", out var f) ? f.GetString() ?? "" : "";
            double vdur = ParseSeconds(s, "duration");
            double fdur = root.TryGetProperty("format", out var fmt) ? ParseSeconds(fmt, "duration") : 0;
            return new VideoCheckEntry(len, mtime, codec, w, h, fps, vdur, fdur, "");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
        {
            return new VideoCheckEntry(len, mtime, "", 0, 0, "", 0, 0, "file lỗi/không đọc được");
        }
    }

}
