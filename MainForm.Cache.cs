using System.Text;
using System.Text.Json;
using RenderMusicPro.Services;

namespace RandomVideoDangVH;

public sealed partial class MainForm
{
    private string? _metadataCachePath;
    private readonly object _metadataSaveLock = new();
    private System.Threading.Timer? _metadataSaveTimer;
    private int _metadataRevision;
    private int _savedMetadataRevision;
    private int _metadataSaveScheduled;
    private int _metadataWriteWarning;

    private string CreateWorkDirectory()
    {
        string root = string.IsNullOrWhiteSpace(_effectCacheDirectory)
            ? ResolveCacheDirectory() : _effectCacheDirectory;
        string work = Path.Combine(root, "Work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        return work;
    }

    private string ResolveCacheDirectory()
    {
        string candidate = string.IsNullOrWhiteSpace(_effectCacheDirectory)
            ? EffectCacheService.DefaultDirectory : _effectCacheDirectory;
        while (true)
        {
            try
            {
                candidate = Path.GetFullPath(candidate);
                Directory.CreateDirectory(candidate);
                string check = Path.Combine(candidate, ".write-test-" + Guid.NewGuid().ToString("N"));
                using (new FileStream(check, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    1, FileOptions.DeleteOnClose)) { }
                if (!string.Equals(_effectCacheDirectory, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    _effectCacheDirectory = candidate;
                    SaveSettings();
                }
                return candidate;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                MessageBox.Show(this,
                    "Không có ổ D hoặc không ghi được vào thư mục cache:\n" + candidate +
                    "\n\nHãy chọn một thư mục trên ổ còn trống. App sẽ ghi nhớ vị trí bạn chọn.",
                    "Chọn nơi lưu cache", MessageBoxButtons.OK, MessageBoxIcon.Information);
                using var dialog = new FolderBrowserDialog
                {
                    Description = "Chọn thư mục lưu cache lâu dài (ưu tiên ổ D)",
                    UseDescriptionForTitle = true, ShowNewFolderButton = true
                };
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    throw new OperationCanceledException("Đã hủy chọn nơi lưu cache.");
                candidate = dialog.SelectedPath;
            }
        }
    }

    private void LoadPersistentMetadataCache(string root)
    {
        string path = Path.Combine(root, "Metadata", "media-v1.json");
        if (string.Equals(path, _metadataCachePath, StringComparison.OrdinalIgnoreCase)) return;
        SavePersistentMetadataCache();
        _metadataCachePath = path;
        try
        {
            if (File.Exists(path))
            {
                var entries = JsonSerializer.Deserialize<Dictionary<string, DurationCacheEntry>>(File.ReadAllText(path));
                if (entries != null)
                {
                    lock (_videoDurationCacheLock)
                    {
                        foreach (var entry in entries)
                        {
                            if (entry.Value != null && entry.Value.FileLength >= 0 &&
                                double.IsFinite(entry.Value.Duration) && entry.Value.Duration >= 0 &&
                                (entry.Value.Duration > 0 || (entry.Value.Width > 0 && entry.Value.Height > 0)))
                                _videoDurationCache[entry.Key] = entry.Value;
                        }
                    }
                    AddLog($"✓ Đã nạp cache thời lượng/thông tin media: {entries.Count:N0} file.", Cyan);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            AddLog("Cache thông tin media chưa đọc được; sẽ đo lại file cần dùng: " + ex.Message, Orange);
        }
        _metadataSaveTimer ??= new System.Threading.Timer(_ =>
        {
            Interlocked.Exchange(ref _metadataSaveScheduled, 0);
            SavePersistentMetadataCache();
        });
        MetadataCacheChanged();
    }

    private void MetadataCacheChanged()
    {
        Interlocked.Increment(ref _metadataRevision);
        if (_metadataSaveTimer != null && Interlocked.CompareExchange(ref _metadataSaveScheduled, 1, 0) == 0)
            _metadataSaveTimer.Change(TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
    }

    private void SavePersistentMetadataCache()
    {
        lock (_metadataSaveLock)
        {
            string? path = _metadataCachePath;
            int revision = Volatile.Read(ref _metadataRevision);
            if (path == null || revision == _savedMetadataRevision) return;
            string partial = path + ".partial-" + Guid.NewGuid().ToString("N");
            try
            {
                Dictionary<string, DurationCacheEntry> snapshot;
                lock (_videoDurationCacheLock)
                    snapshot = new Dictionary<string, DurationCacheEntry>(_videoDurationCache, StringComparer.OrdinalIgnoreCase);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(partial, JsonSerializer.Serialize(snapshot), new UTF8Encoding(false));
                File.Move(partial, path, true);
                _savedMetadataRevision = revision;
                Interlocked.Exchange(ref _metadataWriteWarning, 0);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine("Không ghi được cache media: " + ex.Message);
                if (Interlocked.Exchange(ref _metadataWriteWarning, 1) == 0 && !IsDisposed)
                {
                    try { AddLog("Chưa lưu được cache xuống đĩa; hiện vẫn dùng RAM: " + ex.Message, Orange); }
                    catch (InvalidOperationException) { }
                }
            }
            finally { try { File.Delete(partial); } catch { } }
        }
    }

    private void RememberVideoSize(string path, int width, int height)
    {
        if (IsTemporaryMediaFile(Path.GetFullPath(path))) return;
        var info = new FileInfo(path);
        if (!info.Exists) return;
        lock (_videoDurationCacheLock)
        {
            double duration = _videoDurationCache.TryGetValue(info.FullName, out var old) &&
                old.FileLength == info.Length && old.LastWriteUtc == info.LastWriteTimeUtc ? old.Duration : 0;
            _videoDurationCache[info.FullName] = new DurationCacheEntry(info.Length, info.LastWriteTimeUtc, duration, width, height);
        }
        MetadataCacheChanged();
    }
}
