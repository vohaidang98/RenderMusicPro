using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RenderMusicPro.Services;

public sealed record PreparedEffect(string Path, bool IsImage, bool Reused);

public sealed class EffectCacheService
{
    public const string DefaultDirectory = @"D:\RenderMusicPro\EffectCache";
    private readonly string _directory;

    public EffectCacheService(string directory) => _directory = System.IO.Path.GetFullPath(directory);

    public async Task<PreparedEffect> GetOrCreateAsync(string source, bool image, string recipe,
        Func<string, CancellationToken, Task> prepare,
        Func<string, CancellationToken, Task> validate,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string sourcePath = System.IO.Path.GetFullPath(source);
        var original = new FileInfo(sourcePath);
        if (!original.Exists) throw new FileNotFoundException("Không tìm thấy hiệu ứng gốc.", sourcePath);
        long length = original.Length;
        long modified = original.LastWriteTimeUtc.Ticks;
        string identity = $"{sourcePath.ToUpperInvariant()}\n{length}\n{modified}\n{image}\n{recipe}";
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        Directory.CreateDirectory(_directory);
        string output = System.IO.Path.Combine(_directory, key + (image ? ".png" : ".mkv"));
        string manifestPath = System.IO.Path.Combine(_directory, key + ".json");
        if (IsReady(output, manifestPath, identity)) { Touch(manifestPath); return new PreparedEffect(output, image, true); }

        // An OS file lock also coordinates separate app instances; cancellation stops waiting.
        await using var gate = await AcquireLockAsync(System.IO.Path.Combine(_directory, key + ".lock"), token);
        if (IsReady(output, manifestPath, identity)) { Touch(manifestPath); return new PreparedEffect(output, image, true); }
        foreach (string abandoned in Directory.EnumerateFiles(_directory, key + ".partial-*")
            .Concat(Directory.EnumerateFiles(_directory, key + ".json.partial-*")))
        {
            try
            {
                // The per-key lock is held; never remove a partial still used by a writer.
                using (new FileStream(abandoned, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                File.Delete(abandoned);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        string partial = System.IO.Path.Combine(_directory,
            key + ".partial-" + Guid.NewGuid().ToString("N") + (image ? ".png" : ".mkv"));
        string partialManifest = manifestPath + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            await prepare(partial, token);
            token.ThrowIfCancellationRequested();
            await validate(partial, token);
            token.ThrowIfCancellationRequested();
            original.Refresh();
            if (!original.Exists || original.Length != length || original.LastWriteTimeUtc.Ticks != modified)
                throw new IOException("Hiệu ứng gốc đã thay đổi trong khi tách nền. Hãy chạy lại với file ổn định.");
            var file = new FileInfo(partial);
            if (!file.Exists || file.Length == 0) throw new IOException("Không tạo được cache hiệu ứng hợp lệ.");
            var manifest = new CacheManifest(identity, file.Length, file.LastWriteTimeUtc.Ticks);
            await File.WriteAllTextAsync(partialManifest, JsonSerializer.Serialize(manifest), token);
            token.ThrowIfCancellationRequested();
            File.Move(partial, output, true);
            File.Move(partialManifest, manifestPath, true);
            return new PreparedEffect(output, image, false);
        }
        finally
        {
            try { File.Delete(partial); } catch { }
            try { File.Delete(partialManifest); } catch { }
        }
    }

    // The manifest's write time is not part of validation, so it records "last used" for cache cleanup.
    private static void Touch(string manifestPath)
    {
        try { File.SetLastWriteTimeUtc(manifestPath, DateTime.UtcNow); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Tổng dung lượng các file trong thư mục cache (0 nếu chưa có).</summary>
    public long GetSize()
    {
        long total = 0;
        try
        {
            if (!Directory.Exists(_directory)) return 0;
            foreach (string file in Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return total;
    }

    /// <summary>
    /// Xóa các mục cache (file key.mkv/png/json/lock) không dùng quá maxAge.
    /// Chỉ đụng tới file nằm trực tiếp trong thư mục cache và có tên dạng khóa SHA-256.
    /// </summary>
    public (int Removed, long FreedBytes) CleanUnusedSince(TimeSpan maxAge, CancellationToken token = default)
    {
        int removed = 0;
        long freed = 0;
        if (!Directory.Exists(_directory)) return (0, 0);
        DateTime cutoff = DateTime.UtcNow - maxAge;
        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(_directory, "*", SearchOption.TopDirectoryOnly))
        {
            string name = System.IO.Path.GetFileName(file);
            int dot = name.IndexOf('.');
            if (dot != 64 || !name.AsSpan(0, 64).ToString().All(Uri.IsHexDigit)) continue;
            string key = name[..64];
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<string>();
            list.Add(file);
        }

        foreach (var group in groups)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (group.Value.Max(f => File.GetLastWriteTimeUtc(f)) >= cutoff) continue;
                string lockPath = System.IO.Path.Combine(_directory, group.Key + ".lock");
                using (var gate = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    foreach (string file in group.Value)
                    {
                        if (string.Equals(file, lockPath, StringComparison.OrdinalIgnoreCase)) continue;
                        long length = new FileInfo(file).Length;
                        File.Delete(file);
                        freed += length;
                    }
                }
                File.Delete(lockPath);
                removed++;
            }
            catch (IOException) { } // mục đang được dùng - giữ lại
            catch (UnauthorizedAccessException) { }
        }
        return (removed, freed);
    }

    private static bool IsReady(string output, string manifestPath, string identity)
    {
        try
        {
            var entry = JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(manifestPath));
            var file = new FileInfo(output);
            return entry != null && entry.Identity == identity && file.Exists && file.Length > 0 &&
                file.Length == entry.Length && file.LastWriteTimeUtc.Ticks == entry.LastWriteTicks;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (JsonException) { return false; }
    }

    private static async Task<FileStream> AcquireLockAsync(string path, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
            {
                await Task.Delay(150, token);
            }
        }
    }

    private sealed record CacheManifest(string Identity, long Length, long LastWriteTicks);
}
