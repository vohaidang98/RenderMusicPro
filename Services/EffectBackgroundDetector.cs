using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RenderMusicPro.Services;

public enum EffectBackground { Uncertain, Green, Black }

public static class EffectBackgroundDetector
{
    public static async Task<EffectBackground> DetectAsync(string ffmpeg, string source, bool image,
        string cacheRoot, Func<Task<double>> getDuration, CancellationToken token)
    {
        var info = new FileInfo(source);
        string identity = $"detect-v1|{info.FullName.ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        string directory = Path.Combine(cacheRoot, "Detection");
        string path = Path.Combine(directory, key + ".json");
        try
        {
            if (File.Exists(path))
            {
                int cached = JsonSerializer.Deserialize<int>(await File.ReadAllTextAsync(path, token));
                if (cached is >= 0 and <= 2) return (EffectBackground)cached;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        double duration = image ? 0 : await getDuration();
        int count = !image && duration > 0.15 ? 3 : 1;
        var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        void Arg(string value) => start.ArgumentList.Add(value);
        Arg("-hide_banner"); Arg("-loglevel"); Arg("error");
        double[] offsets = count == 3 ? new[] { 0d, duration * 0.5, duration * 0.85 } : new[] { 0d };
        foreach (double offset in offsets)
        {
            if (offset > 0) { Arg("-ss"); Arg(offset.ToString(CultureInfo.InvariantCulture)); }
            Arg("-i"); Arg(source);
        }
        string graph = string.Join(";", Enumerable.Range(0, count).Select(i =>
            $"[{i}:v:0]trim=end_frame=1,scale=64:64,format=rgba,setpts=PTS-STARTPTS[f{i}]"));
        graph += ";" + string.Concat(Enumerable.Range(0, count).Select(i => $"[f{i}]")) + $"concat=n={count}:v=1:a=0[out]";
        foreach (string value in new[] { "-filter_complex", graph, "-map", "[out]", "-frames:v", count.ToString(),
            "-fps_mode", "passthrough", "-an", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1" }) Arg(value);
        token.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = start };
        process.Start();
        using var cancel = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        var stderr = process.StandardError.ReadToEndAsync();
        using var output = new MemoryStream();
        var read = process.StandardOutput.BaseStream.CopyToAsync(output);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException) { try { await process.WaitForExitAsync(); } catch { } await read; await stderr; throw; }
        await read;
        string error = await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException("Không lấy được mẫu nhận diện nền hiệu ứng: " + error);
        EffectBackground result = Classify(output.ToArray());
        info.Refresh();
        string current = $"detect-v1|{info.FullName.ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        if (current != identity) throw new IOException("Hiệu ứng thay đổi khi đang nhận diện nền.");
        Directory.CreateDirectory(directory);
        string temporary = path + ".partial-" + Guid.NewGuid().ToString("N");
        try { await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize((int)result), token); File.Move(temporary, path, true); }
        finally { try { File.Delete(temporary); } catch { } }
        return result;
    }

    public static EffectBackground Classify(byte[] rgba)
    {
        double black = 0, green = 0, total = 0;
        for (int i = 0; i + 3 < rgba.Length; i += 4)
        {
            if (rgba[i + 3] < 24) continue;
            int pixel = i / 4 % 4096, x = pixel % 64, y = pixel / 64;
            double weight = x < 8 || x >= 56 || y < 8 || y >= 56 ? 3 : 1;
            int r = rgba[i], g = rgba[i + 1], b = rgba[i + 2];
            total += weight;
            int maximum = Math.Max(r, Math.Max(g, b)), minimum = Math.Min(r, Math.Min(g, b));
            if (maximum < 35 && maximum - minimum < 18) black += weight;
            if (g > 24 && g > r * 1.4 && g > b * 1.25 && g - r > 18) green += weight;
        }
        if (total == 0) return EffectBackground.Uncertain;
        if (green / total >= 0.3 && green > black * 1.2) return EffectBackground.Green;
        if (black / total >= 0.45 && black > green * 1.2) return EffectBackground.Black;
        return EffectBackground.Uncertain;
    }
}
