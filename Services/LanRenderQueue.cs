using System.Text;
using System.Text.Json;

namespace RenderMusicPro.Services;

// v1.5.0: render nhiều máy qua LAN, chỉ dùng MỘT thư mục chia sẻ (không mở cổng mạng).
// <gốc share>\_RenderLAN\jobs\pending\<jobId>.json            phiếu việc đang chờ
// <gốc share>\_RenderLAN\jobs\running\<jobId>__<máy>.json     máy <máy> đã nhận (đổi tên = giành phiếu)
// <gốc share>\_RenderLAN\jobs\failed\<jobId>.json             lỗi 2 lần, máy gửi ghi nhận lỗi
// <gốc share>\_RenderLAN\jobs\cancelled\<batchId>             lô đã bị máy gửi hủy (máy nhận không giành / không đẩy kết quả)
// <gốc share>\_RenderLAN\machines\<máy>.json                  máy còn sống (ghi lại mỗi vài giây, có bộ đếm Seq)
// <gốc share>\out\<jobId>__<máy>\  mp4 + mp3 + txt, done.json ghi SAU CÙNG; máy gửi copy về rồi dọn.
// Mọi đường dẫn trong phiếu việc là tương đối so với gốc share, nên mỗi máy map ổ khác vẫn chạy.
// v1.5.1: phát hiện phiếu/máy chết dựa trên "lần cuối thấy giá trị thay đổi" theo đồng hồ của máy QUAN SÁT
// (LanChangeTracker), không so đồng hồ giữa các máy.

public sealed class LanJobTrack
{
    public string Category { get; set; } = "";
    public string Path { get; set; } = "";
}

public sealed class LanJobClip
{
    public string Path { get; set; } = "";
    public double Duration { get; set; }
}

public sealed class LanJob
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public string JobId { get; set; } = "";
    public string BatchId { get; set; } = "";
    public string Sender { get; set; } = "";
    public int Index { get; set; }
    public string BaseName { get; set; } = "";
    public string PlaylistName { get; set; } = "";
    public double OnSeconds { get; set; }
    public double OffSeconds { get; set; }
    public double PlannedDuration { get; set; }
    public List<LanJobTrack> Tracks { get; set; } = new();
    public List<LanJobClip> Clips { get; set; } = new();
    public string? Effect { get; set; }
    public bool EffectGreen { get; set; }
    public int EffectBackgroundMode { get; set; }
    public bool FastStart { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class LanDoneFile
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
}

public sealed class LanDone
{
    public string JobId { get; set; } = "";
    public string Machine { get; set; } = "";
    public List<LanDoneFile> Files { get; set; } = new();
    public DateTime FinishedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class LanMachineStatus
{
    public string Machine { get; set; } = "";
    public bool Accept { get; set; }
    public string? CurrentJob { get; set; }
    public string AppVersion { get; set; } = "";
    public DateTime Utc { get; set; } = DateTime.UtcNow;
    /// <summary>Bộ đếm tăng dần mỗi lần ghi nhịp tim: nội dung luôn đổi, không phụ thuộc đồng hồ.</summary>
    public long Seq { get; set; }
}

public sealed class LanSettings
{
    /// <summary>Cũ (trước 1.5.2, một thư mục share duy nhất chứa mọi thứ). Chỉ còn để đọc file cấu hình cũ;
    /// InitLan() tự chuyển sang 3 thư mục bên dưới khi nạp nếu 3 thư mục đó còn trống.</summary>
    public string ShareRoot { get; set; } = "";
    public string MusicRoot { get; set; } = "";
    public string VideoRoot { get; set; } = "";
    public string OutRoot { get; set; } = "";
    public bool Distribute { get; set; }
    public bool AcceptJobs { get; set; }
    /// <summary>Tự khởi động app cùng Windows (thu nhỏ) để máy NHẬN luôn sẵn sàng nhận render ngay khi bật máy.</summary>
    public bool AutoStartWithWindows { get; set; }
}

/// <summary>
/// Theo dõi "lần cuối thấy giá trị thay đổi" của từng khóa, đo bằng đồng hồ của máy đang quan sát.
/// Lần đầu thấy một khóa coi như vừa đổi (Observe trả về 0).
/// </summary>
public sealed class LanChangeTracker
{
    private readonly Func<DateTime> _utcNow;
    private readonly Dictionary<string, (string Stamp, DateTime Since)> _map = new(StringComparer.OrdinalIgnoreCase);

    public LanChangeTracker(Func<DateTime>? utcNow = null) => _utcNow = utcNow ?? (() => DateTime.UtcNow);

    /// <summary>Ghi nhận giá trị hiện tại; trả về thời gian giá trị này đã không đổi. stamp = null (mất file): xóa khóa, trả về TimeSpan.MaxValue.</summary>
    public TimeSpan Observe(string key, string? stamp)
    {
        lock (_map)
        {
            if (stamp == null) { _map.Remove(key); return TimeSpan.MaxValue; }
            DateTime now = _utcNow();
            if (!_map.TryGetValue(key, out var entry) || entry.Stamp != stamp)
            {
                _map[key] = (stamp, now);
                return TimeSpan.Zero;
            }
            TimeSpan unchanged = now - entry.Since;
            return unchanged < TimeSpan.Zero ? TimeSpan.Zero : unchanged;
        }
    }

    /// <summary>Quên các khóa có tiền tố prefix mà không còn trong liveKeys.</summary>
    public void Prune(string prefix, IEnumerable<string> liveKeys)
    {
        var keep = new HashSet<string>(liveKeys, StringComparer.OrdinalIgnoreCase);
        lock (_map)
            foreach (string key in _map.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !keep.Contains(k)).ToList())
                _map.Remove(key);
    }
}

public sealed class LanRenderQueue
{
    public static readonly TimeSpan MachineAliveWindow = TimeSpan.FromSeconds(45);
    /// <summary>Phiếu running của máy khác không đổi quá khoảng này (theo đồng hồ máy quan sát) thì bị thu hồi.</summary>
    public static readonly TimeSpan StaleClaimWindow = TimeSpan.FromMinutes(3);
    /// <summary>Máy gửi không có nhịp tim thay đổi quá khoảng này thì máy nhận không giành phiếu của lô đó.</summary>
    public static readonly TimeSpan SenderAliveWindow = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan OldBatchAge = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const string Sep = "__";
    private const int BatchPrefixLength = 16; // "yyyyMMdd-HHmmss-"

    private readonly Func<DateTime> _utcNow;
    private long _heartbeatSeq;
    private DateTime _lastTouch = DateTime.MinValue;
    private readonly object _touchLock = new();

    /// <summary>Bộ theo dõi thay đổi (phiếu running, nhịp tim máy). Dùng chung xuyên suốt đời của hàng chờ này.</summary>
    public LanChangeTracker Tracker { get; }

    /// <summary>Thư mục "Nhạc vào" share LAN — mọi đường dẫn nhạc trong phiếu việc tương đối so với đây.</summary>
    public string MusicRoot { get; }
    /// <summary>Thư mục "Video vào" share LAN (gồm cả hiệu ứng) — mọi đường dẫn video/hiệu ứng tương đối so với đây.</summary>
    public string VideoRoot { get; }
    /// <summary>Thư mục "Ra" share LAN — nơi đặt phiếu việc (_RenderLAN) + trao đổi kết quả tạm (out).</summary>
    public string OutRoot { get; }
    public string Machine { get; }

    public LanRenderQueue(string musicRoot, string videoRoot, string outRoot, string? machineName = null, Func<DateTime>? utcNow = null)
    {
        MusicRoot = Path.GetFullPath(musicRoot.Trim());
        VideoRoot = Path.GetFullPath(videoRoot.Trim());
        OutRoot = Path.GetFullPath(outRoot.Trim());
        Machine = machineName == null ? CurrentMachineName() : SanitizeMachineName(machineName);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        Tracker = new LanChangeTracker(_utcNow);
    }

    public static string CurrentMachineName() => SanitizeMachineName(Environment.MachineName);

    private static string SanitizeMachineName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
        return name.Replace(Sep, "-");
    }

    private string Base => Path.Combine(OutRoot, "_RenderLAN");
    public string PendingDir => Path.Combine(Base, "jobs", "pending");
    public string RunningDir => Path.Combine(Base, "jobs", "running");
    public string FailedDir => Path.Combine(Base, "jobs", "failed");
    public string CancelledDir => Path.Combine(Base, "jobs", "cancelled");
    public string MachinesDir => Path.Combine(Base, "machines");
    public string OutDir => Path.Combine(OutRoot, "out");

    public void EnsureFolders()
    {
        foreach (string dir in new[] { PendingDir, RunningDir, FailedDir, CancelledDir, MachinesDir, OutDir })
            Directory.CreateDirectory(dir);
    }

    // =========================================================
    // QUY TẮC THUẦN (không đụng share)
    // =========================================================

    /// <summary>Tên gốc an toàn: không rỗng, không ký tự cấm/đường dẫn/"..", không đổi khi làm sạch như SanitizeFileName.</summary>
    public static bool IsSafeBaseName(string? baseName)
    {
        if (string.IsNullOrEmpty(baseName) || baseName.Length > 200) return false;
        // Không cấm ".." ở giữa tên (vd "Chill... Lofi"): đã cấm \ / : nên không thể thoát thư mục;
        // tên chỉ gồm dấu chấm ("." hoặc "..") bị loại ở bước TrimEnd('.') bên dưới.
        if (baseName.IndexOfAny(new[] { '\\', '/', ':' }) >= 0) return false;
        if (baseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        string sanitized = baseName.Trim().TrimEnd('.');
        return sanitized.Length > 0 && sanitized == baseName;
    }

    /// <summary>Ba tên file kết quả hợp lệ (video, nhạc, danh sách bài) do máy gửi tự tính từ baseName của nó.</summary>
    public static (string Video, string Music, string Tracks) ExpectedResultNames(string baseName) =>
        (baseName + ".mp4", baseName + ".mp3", baseName + ".txt");

    public static string NewBatchId() => NewBatchId(CurrentMachineName(), DateTime.Now);
    public static string NewBatchId(string machine, DateTime localNow) =>
        localNow.ToString("yyyyMMdd-HHmmss") + "-" + SanitizeMachineName(machine);
    public static string JobIdFor(string batchId, int index) => $"{batchId}_{index:000000}";

    /// <summary>Lô của một jobId: bỏ hậu tố "_000000". Không đúng dạng thì trả về nguyên chuỗi.</summary>
    public static string BatchIdOf(string jobId) =>
        jobId.Length > 7 && jobId[^7] == '_' && jobId[^6..].All(char.IsAsciiDigit) ? jobId[..^7] : jobId;

    /// <summary>Tên máy gửi lấy từ batchId (yyyyMMdd-HHmmss-MÁY); null nếu không đúng dạng.</summary>
    public static string? SenderOfBatch(string batchId) =>
        batchId.Length > BatchPrefixLength && batchId[8] == '-' && batchId[15] == '-' ? batchId[BatchPrefixLength..] : null;

    /// <summary>Tên file/thư mục (jobId, jobId__máy, ...) có thuộc đúng lô batchId không (khớp chính xác, không khớp tiền tố tên máy khác).</summary>
    public static bool BelongsToBatch(string name, string batchId)
    {
        string prefix = batchId + "_";
        if (!name.StartsWith(prefix, StringComparison.Ordinal)) return false;
        string rest = name[prefix.Length..];
        if (rest.Length < 6 || !rest[..6].All(char.IsAsciiDigit)) return false;
        return rest.Length == 6 || rest.AsSpan(6).StartsWith(Sep, StringComparison.Ordinal);
    }

    /// <summary>Tách "jobId__máy" thành (jobId, máy); không có hậu tố máy thì máy = "".</summary>
    public static (string JobId, string Machine) SplitRunningName(string name)
    {
        int sep = name.LastIndexOf(Sep, StringComparison.Ordinal);
        return sep <= 0 ? (name, "") : (name[..sep], name[(sep + Sep.Length)..]);
    }

    /// <summary>Thư mục có quá rộng để tự động chia sẻ (gốc ổ, thư mục hệ thống, hồ sơ người dùng, Desktop/Documents/Downloads gốc)?</summary>
    public static bool IsShareFolderTooBroad(string folder, out string reason)
    {
        reason = "";
        string full;
        try { full = Path.GetFullPath(folder.Trim()); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            reason = "Đường dẫn không hợp lệ."; return true;
        }
        string trimmed = full.TrimEnd('\\', '/');
        string? root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root) || string.Equals(root.TrimEnd('\\', '/'), trimmed, StringComparison.OrdinalIgnoreCase) || trimmed.Length <= 2)
        {
            reason = "Không chia sẻ gốc ổ đĩa (" + root + "). Hãy tạo một thư mục riêng, ví dụ " + root + "RenderLAN."; return true;
        }
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var exact = new List<(string Path, string Name)>
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ProgramData"),
            (userProfile, "thư mục hồ sơ người dùng"),
            (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Desktop"),
            (Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Documents"),
            (string.IsNullOrEmpty(userProfile) ? "" : Path.Combine(userProfile, "Downloads"), "Downloads"),
            (string.IsNullOrEmpty(userProfile) ? "" : (Path.GetDirectoryName(userProfile.TrimEnd('\\', '/')) ?? ""), "thư mục Users"),
        };
        var tree = new List<(string Path, string Name)>
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.Windows), "thư mục Windows"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Program Files"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Program Files (x86)"),
        };
        foreach (var (p, name) in exact)
        {
            if (string.IsNullOrEmpty(p)) continue;
            if (string.Equals(p.TrimEnd('\\', '/'), trimmed, StringComparison.OrdinalIgnoreCase))
            { reason = "Không chia sẻ " + name + ". Hãy tạo một thư mục riêng cho Render LAN."; return true; }
        }
        foreach (var (p, name) in tree)
        {
            if (string.IsNullOrEmpty(p)) continue;
            string t = p.TrimEnd('\\', '/');
            if (string.Equals(t, trimmed, StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(t + "\\", StringComparison.OrdinalIgnoreCase))
            { reason = "Không chia sẻ " + name + " hoặc thư mục bên trong. Hãy tạo một thư mục riêng cho Render LAN."; return true; }
        }
        return false;
    }

    /// <summary>Kiểm tra phiếu việc trước khi render: phiên bản, BaseName, đường dẫn nằm trong share. Trả về lý do lỗi, hoặc null nếu hợp lệ.</summary>
    public string? ValidateJob(LanJob job)
    {
        if (job.Version != LanJob.CurrentVersion) return $"Phiếu việc phiên bản {job.Version} không được hỗ trợ.";
        if (!IsSafeBaseName(job.BaseName)) return "Tên bài trong phiếu việc không hợp lệ: " + job.BaseName;
        try
        {
            foreach (var t in job.Tracks) ResolveMusic(t.Path);
            foreach (var c in job.Clips) ResolveVideo(c.Path);
            if (!string.IsNullOrEmpty(job.Effect)) ResolveVideo(job.Effect);
        }
        catch (InvalidDataException ex) { return ex.Message; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "Đường dẫn trong phiếu việc không hợp lệ.";
        }
        return null;
    }

    // ---------- đường dẫn tương đối (mỗi thư mục nhạc/video có gốc riêng) ----------
    public bool TryMakeRelativeMusic(string fullPath, out string relative) => TryMakeRelativeUnder(MusicRoot, fullPath, out relative);
    public bool TryMakeRelativeVideo(string fullPath, out string relative) => TryMakeRelativeUnder(VideoRoot, fullPath, out relative);

    private static bool TryMakeRelativeUnder(string root, string fullPath, out string relative)
    {
        relative = Path.GetRelativePath(root, Path.GetFullPath(fullPath));
        return !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    public string ResolveMusic(string relative) => ResolveUnder(MusicRoot, "Nhạc vào", relative);
    public string ResolveVideo(string relative) => ResolveUnder(VideoRoot, "Video vào", relative);

    private static string ResolveUnder(string root, string label, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(root, relative));
        if (!TryMakeRelativeUnder(root, full, out _))
            throw new InvalidDataException($"Phiếu việc có đường dẫn nằm ngoài thư mục {label}: " + relative);
        return full;
    }

    // ---------- ghi/đọc JSON an toàn ----------
    private static void WriteJsonAtomic<T>(string path, T value)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));
            File.Move(temp, path, true);
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
    }

    private static T? ReadJson<T>(string path) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8)); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private static IEnumerable<string> SafeFiles(string dir, string pattern)
    {
        try { return Directory.GetFiles(dir, pattern); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    private static string[] SafeDirectories(string dir)
    {
        try { return Directory.GetDirectories(dir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    // ---------- máy ----------
    public void WriteHeartbeat(bool accept, string? currentJob)
    {
        Directory.CreateDirectory(MachinesDir);
        WriteJsonAtomic(Path.Combine(MachinesDir, Machine + ".json"), new LanMachineStatus
        {
            Machine = Machine, Accept = accept, CurrentJob = currentJob,
            AppVersion = RenderMusicPro.Services.AppVersion.Current, Utc = _utcNow(),
            Seq = Interlocked.Increment(ref _heartbeatSeq)
        });
    }

    /// <summary>Danh sách máy còn sống để HIỂN THỊ / đếm máy phụ (so mtime với đồng hồ máy này, chỉ mang tính gần đúng).
    /// Quyết định thu hồi/giành phiếu KHÔNG dùng hàm này mà dùng Tracker.</summary>
    public List<LanMachineStatus> GetAliveMachines()
    {
        var result = new List<LanMachineStatus>();
        foreach (string file in SafeFiles(MachinesDir, "*.json"))
        {
            if (_utcNow() - SafeWriteTime(file) > MachineAliveWindow) continue;
            var status = ReadJson<LanMachineStatus>(file);
            if (status != null) result.Add(status);
        }
        return result;
    }

    private static DateTime SafeWriteTime(string file)
    {
        try { return File.GetLastWriteTimeUtc(file); } catch { return DateTime.MinValue; }
    }

    /// <summary>Giá trị "dấu vân tay" nhịp tim của máy (đổi mỗi lần máy đó ghi); null nếu chưa có file.</summary>
    private string? HeartbeatStamp(string machine)
    {
        string file = Path.Combine(MachinesDir, machine + ".json");
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists) return null;
            var status = ReadJson<LanMachineStatus>(file);
            return $"{info.LastWriteTimeUtc.Ticks}:{info.Length}:{status?.Seq ?? -1}:{status?.Utc.Ticks ?? 0}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Máy gửi còn nhịp tim thay đổi trong SenderAliveWindow gần đây (đo bằng đồng hồ máy này)? Máy này luôn là còn sống.</summary>
    public bool IsSenderAlive(string sender)
    {
        if (string.Equals(sender, Machine, StringComparison.OrdinalIgnoreCase)) return true;
        return Tracker.Observe("m:" + sender, HeartbeatStamp(sender)) <= SenderAliveWindow;
    }

    // ---------- phiếu việc ----------
    public void Enqueue(LanJob job) => WriteJsonAtomic(Path.Combine(PendingDir, job.JobId + ".json"), job);

    public int CountPending(string batchId) => ListPending(batchId).Count;

    /// <summary>Giành phiếu: đổi tên pending -> running__máy. Chỉ một máy đổi tên thành công.
    /// Trả null nếu thua tranh giành, phiếu hỏng/không hợp lệ (đã chuyển sang failed) hoặc lô đã bị hủy.</summary>
    public LanJob? TryClaim(string pendingFile)
    {
        string jobId = Path.GetFileNameWithoutExtension(pendingFile);
        string batchId = BatchIdOf(jobId);
        if (IsBatchCancelled(batchId)) return null;
        string running = Path.Combine(RunningDir, jobId + Sep + Machine + ".json");
        try { File.Move(pendingFile, running, false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        TouchRunning(jobId);
        var job = ReadJson<LanJob>(running);
        if (job == null) { MoveRunningTo(jobId, FailedDir, "Phiếu việc hỏng, không đọc được."); return null; }
        // Không tin JobId/BatchId trong nội dung: lấy theo tên file đã giành.
        job.JobId = jobId;
        job.BatchId = batchId;
        string? invalid = ValidateJob(job);
        if (invalid != null) { MoveRunningTo(jobId, FailedDir, invalid); return null; }
        // Lô bị hủy ngay giữa lúc giành: bỏ phiếu.
        if (IsBatchCancelled(batchId)) { try { File.Delete(running); } catch { } return null; }
        return job;
    }

    /// <summary>Máy nhận: giành phiếu pending đầu tiên hợp lệ. Bỏ qua lô đã hủy và lô mà máy gửi không còn nhịp tim thay đổi.</summary>
    public LanJob? ClaimNext()
    {
        foreach (string file in ListPending())
        {
            string batchId = BatchIdOf(Path.GetFileNameWithoutExtension(file));
            if (IsBatchCancelled(batchId)) continue;
            string? sender = SenderOfBatch(batchId);
            if (sender != null && !IsSenderAlive(sender)) continue;
            var claimed = TryClaim(file);
            if (claimed != null) return claimed;
        }
        return null;
    }

    /// <summary>Phiếu đang chờ (cũ nhất trước). batchId != null: chỉ của đúng lô đó.</summary>
    public List<string> ListPending(string? batchId = null) =>
        SafeFiles(PendingDir, "*.json")
            .Where(f => batchId == null || BelongsToBatch(Path.GetFileNameWithoutExtension(f), batchId))
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToList();

    public string RunningPath(string jobId) => Path.Combine(RunningDir, jobId + Sep + Machine + ".json");

    /// <summary>Máy này còn giữ phiếu (file running__máy còn tồn tại)?</summary>
    public bool HoldsClaim(string jobId) => File.Exists(RunningPath(jobId));

    /// <summary>Chạm phiếu để "dấu vân tay" luôn đổi (mtime tăng dần tuyệt đối, kể cả khi đồng hồ chậm lại).
    /// Trả false chỉ khi chắc chắn phiếu đã mất (bị thu hồi/xóa); lỗi IO tạm thời trả true.</summary>
    public bool TouchRunning(string jobId)
    {
        DateTime stamp;
        lock (_touchLock)
        {
            stamp = _utcNow();
            if (stamp <= _lastTouch) stamp = _lastTouch.AddMilliseconds(10);
            _lastTouch = stamp;
        }
        try { File.SetLastWriteTimeUtc(RunningPath(jobId), stamp); return true; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return false; }
        catch { return true; }
    }

    public bool ReleaseToPending(string jobId)
    {
        try
        {
            if (IsBatchCancelled(BatchIdOf(jobId))) { File.Delete(RunningPath(jobId)); return false; }
            File.Move(RunningPath(jobId), Path.Combine(PendingDir, jobId + ".json"), true);
            return true;
        }
        catch { return false; }
    }

    public void CompleteRunning(string jobId)
    {
        try { File.Delete(RunningPath(jobId)); } catch { }
    }

    public void MarkFailed(string jobId, string reason) => MoveRunningTo(jobId, FailedDir, reason);

    // Ghi .error.txt TRƯỚC rồi mới chuyển .json, để TakeFailed không đọc được .json mà thiếu lý do.
    private void MoveRunningTo(string jobId, string dir, string reason)
    {
        string errFile = Path.Combine(dir, jobId + ".error.txt");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(errFile, $"{Machine}: {reason}", new UTF8Encoding(false));
            File.Move(RunningPath(jobId), Path.Combine(dir, jobId + ".json"), true);
        }
        catch
        {
            try { File.Delete(errFile); } catch { }
        }
    }

    // ---------- thu hồi / khởi động lại ----------

    /// <summary>
    /// Máy gửi: trả về hàng chờ các phiếu running của máy khác mà dấu vân tay (mtime/kích thước) KHÔNG đổi quá
    /// StaleClaimWindow theo đồng hồ máy này; không phụ thuộc nhịp tim hay đồng hồ máy khác.
    /// Phiếu mà máy đó đã đẩy kết quả (thư mục out\jobId__máy có done.json) thì không thu hồi. Lỗi IO: ghi log, vòng sau thử lại.
    /// </summary>
    public int ReclaimStale(string batchId, Action<string>? log = null)
    {
        int count = 0;
        var seen = new List<string>();
        foreach (string file in SafeFiles(RunningDir, "*.json"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!BelongsToBatch(name, batchId)) continue;
            var (jobId, machine) = SplitRunningName(name);
            if (machine.Length == 0 || string.Equals(machine, Machine, StringComparison.OrdinalIgnoreCase)) continue;
            string key = "r:" + name;
            seen.Add(key);
            string? stamp;
            try { var info = new FileInfo(file); stamp = info.Exists ? $"{info.LastWriteTimeUtc.Ticks}:{info.Length}" : null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log?.Invoke("Không đọc được phiếu " + name + ": " + ex.Message); continue; }
            if (Tracker.Observe(key, stamp) <= StaleClaimWindow) continue;
            if (File.Exists(Path.Combine(OutDir, ResultDirName(jobId, machine), "done.json"))) continue;
            try
            {
                File.Move(file, Path.Combine(PendingDir, jobId + ".json"), false);
                Tracker.Observe(key, null);
                count++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log?.Invoke("Chưa thu hồi được phiếu " + name + ", sẽ thử lại: " + ex.Message);
            }
        }
        Tracker.Prune("r:", seen);
        return count;
    }

    /// <summary>
    /// Khi app khởi động / bật nhận bài: trả về hàng chờ mọi phiếu running\*__&lt;máy này&gt;.json.
    /// includeOwnBatches = true (khởi động): phiếu thuộc lô do chính máy này gửi (lô cũ đã chết) và phiếu pending
    /// của các lô đó bị hủy (ghi file cancelled, xóa pending) thay vì trả về hàng chờ.
    /// </summary>
    public (int Released, int Abandoned) RecoverAfterRestart(bool includeOwnBatches, Action<string>? log = null)
    {
        int released = 0, abandoned = 0;
        foreach (string file in SafeFiles(RunningDir, "*" + Sep + Machine + ".json"))
        {
            var (jobId, machine) = SplitRunningName(Path.GetFileNameWithoutExtension(file));
            if (!string.Equals(machine, Machine, StringComparison.OrdinalIgnoreCase)) continue;
            string batchId = BatchIdOf(jobId);
            bool ownBatch = string.Equals(SenderOfBatch(batchId), Machine, StringComparison.OrdinalIgnoreCase);
            try
            {
                if (ownBatch)
                {
                    if (!includeOwnBatches) continue;
                    CancelBatch(batchId);
                    File.Delete(file);
                    abandoned++;
                }
                else if (IsBatchCancelled(batchId)) { File.Delete(file); }
                else { File.Move(file, Path.Combine(PendingDir, jobId + ".json"), true); released++; }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log?.Invoke("Chưa trả được phiếu " + Path.GetFileName(file) + " về hàng chờ, sẽ thử lại: " + ex.Message);
            }
        }
        if (includeOwnBatches)
        {
            var batches = ListPending().Select(f => BatchIdOf(Path.GetFileNameWithoutExtension(f)))
                .Where(b => string.Equals(SenderOfBatch(b), Machine, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal).ToList();
            foreach (string batch in batches)
            {
                try { CancelBatch(batch); abandoned++; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    log?.Invoke("Chưa hủy được lô cũ " + batch + ": " + ex.Message);
                }
            }
        }
        return (released, abandoned);
    }

    public int CountRunningOthers(string batchId) =>
        SafeFiles(RunningDir, "*.json").Count(f =>
        {
            var (jobId, machine) = SplitRunningName(Path.GetFileNameWithoutExtension(f));
            return BelongsToBatch(Path.GetFileNameWithoutExtension(f), batchId) &&
                !string.Equals(machine, Machine, StringComparison.OrdinalIgnoreCase);
        });

    /// <summary>Phiếu lỗi của lô: (jobId, lý do). Xóa khỏi thư mục failed sau khi đọc.</summary>
    public List<(string JobId, string Reason)> TakeFailed(string batchId)
    {
        var list = new List<(string, string)>();
        foreach (string file in SafeFiles(FailedDir, "*.json"))
        {
            string jobId = Path.GetFileNameWithoutExtension(file);
            if (!BelongsToBatch(jobId, batchId)) continue;
            string errFile = Path.Combine(FailedDir, jobId + ".error.txt");
            string reason = "";
            try { reason = File.Exists(errFile) ? File.ReadAllText(errFile) : "Lỗi không rõ"; } catch { }
            try { File.Delete(file); File.Delete(errFile); } catch { }
            list.Add((jobId, reason));
        }
        return list;
    }

    // ---------- hủy lô ----------
    public string CancelledPath(string batchId) => Path.Combine(CancelledDir, batchId);

    public bool IsBatchCancelled(string batchId)
    {
        try { return File.Exists(CancelledPath(batchId)); } catch { return false; }
    }

    /// <summary>Máy gửi Dừng: ghi file cancelled TRƯỚC rồi mới xóa pending của lô.</summary>
    public void CancelBatch(string batchId)
    {
        Directory.CreateDirectory(CancelledDir);
        File.WriteAllText(CancelledPath(batchId), _utcNow().ToString("O"), new UTF8Encoding(false));
        DeletePending(batchId);
    }

    public void DeletePending(string batchId)
    {
        foreach (string file in ListPending(batchId))
            try { File.Delete(file); } catch { }
    }

    /// <summary>
    /// Máy gửi, khi bắt đầu lô mới: xóa file cancelled quá OldBatchAge và thư mục kết quả out\&lt;lô cũ của chính máy này&gt;
    /// đã bị hủy hoặc quá OldBatchAge (tính theo mtime so với đồng hồ máy này). Trả về số mục đã dọn.
    /// </summary>
    public int CleanupOwnOldBatches(string currentBatchId)
    {
        int count = 0;
        DateTime now = _utcNow();
        foreach (string file in SafeFiles(CancelledDir, "*"))
        {
            string batch = Path.GetFileName(file);
            if (!string.Equals(SenderOfBatch(batch), Machine, StringComparison.OrdinalIgnoreCase)) continue;
            if (now - SafeWriteTime(file) <= OldBatchAge) continue;
            try { File.Delete(file); count++; } catch { }
        }
        foreach (string dir in SafeDirectories(OutDir))
        {
            string name = Path.GetFileName(dir);
            string batch = BatchIdOf(SplitRunningName(name).JobId);
            if (batch == currentBatchId) continue;
            if (!string.Equals(SenderOfBatch(batch), Machine, StringComparison.OrdinalIgnoreCase)) continue;
            bool old = now - SafeWriteTime(dir) > OldBatchAge;
            if (!old && !IsBatchCancelled(batch)) continue;
            try { Directory.Delete(dir, true); count++; } catch { }
        }
        return count;
    }

    // ---------- kết quả ----------
    public static string ResultDirName(string jobId, string machine) => jobId + Sep + machine;
    public string JobOutDir(string jobId) => Path.Combine(OutDir, ResultDirName(jobId, Machine));

    /// <summary>
    /// Máy nhận: copy file đã render vào out\jobId__máy\ rồi ghi done.json sau cùng.
    /// Trả false (không đẩy / dọn phần đã đẩy) nếu máy này không còn giữ phiếu hoặc lô đã bị hủy.
    /// </summary>
    public bool PublishResult(string jobId, IEnumerable<string> localFiles)
    {
        if (!HoldsClaim(jobId) || IsBatchCancelled(BatchIdOf(jobId))) return false;
        string dir = JobOutDir(jobId);
        Directory.CreateDirectory(dir);
        try
        {
            var done = new LanDone { JobId = jobId, Machine = Machine, FinishedUtc = _utcNow() };
            foreach (string file in localFiles)
            {
                if (!HoldsClaim(jobId)) { TryDeleteDir(dir); return false; }
                string name = Path.GetFileName(file);
                string target = Path.Combine(dir, name);
                string temp = target + ".part";
                File.Copy(file, temp, true);
                File.Move(temp, target, true);
                long size = new FileInfo(file).Length;
                if (new FileInfo(target).Length != size)
                    throw new IOException("Copy lên thư mục share bị thiếu dữ liệu: " + name);
                done.Files.Add(new LanDoneFile { Name = name, Size = size });
            }
            if (!HoldsClaim(jobId) || IsBatchCancelled(BatchIdOf(jobId))) { TryDeleteDir(dir); return false; }
            WriteJsonAtomic(Path.Combine(dir, "done.json"), done);
            return true;
        }
        catch
        {
            TryDeleteDir(dir);
            throw;
        }
    }

    private static bool TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); return true; } catch { return false; }
    }

    /// <summary>Kết quả đã xong của lô (thư mục out\jobId__máy có done.json). JobId lấy từ tên thư mục.</summary>
    public List<(string JobId, string Dir, LanDone Done)> ListDone(string batchId)
    {
        var list = new List<(string, string, LanDone)>();
        foreach (string dir in SafeDirectories(OutDir).OrderBy(d => d, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(dir);
            if (!BelongsToBatch(name, batchId)) continue;
            string marker = Path.Combine(dir, "done.json");
            if (!File.Exists(marker)) continue;
            var done = ReadJson<LanDone>(marker);
            if (done != null) list.Add((SplitRunningName(name).JobId, dir, done));
        }
        return list;
    }

    /// <summary>JobId của mọi thư mục kết quả của lô còn trên share (kể cả chưa có done.json hoặc chưa dọn).</summary>
    public List<string> ListResultJobIds(string batchId) =>
        SafeDirectories(OutDir).Select(Path.GetFileName).Where(n => n != null && BelongsToBatch(n, batchId))
            .Select(n => SplitRunningName(n!).JobId).ToList();

    /// <summary>
    /// Máy gửi: chỉ chép đúng 3 tên do máy gửi tự tính từ baseName ({baseName}.mp4 vào videoOutput,
    /// .mp3 và .txt vào musicOutput); mọi tên khác trong done.json bị bỏ qua. Kích thước trong done.json chỉ để so khớp.
    /// Không xóa gì ở share (gọi TryCleanupResult sau khi đã ghi nhận bài đã nhận).
    /// </summary>
    public static void CollectResult(string dir, LanDone done, string baseName, string videoOutput, string musicOutput)
    {
        if (!IsSafeBaseName(baseName)) throw new InvalidDataException("Tên bài không hợp lệ: " + baseName);
        var (video, music, tracks) = ExpectedResultNames(baseName);
        foreach (var (name, outputDir) in new[] { (video, videoOutput), (music, musicOutput), (tracks, musicOutput) })
        {
            var entry = done.Files.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (entry == null) throw new IOException("Kết quả trên share thiếu file: " + name);
            var info = new FileInfo(Path.Combine(dir, name));
            if (!info.Exists || info.Length != entry.Size)
                throw new IOException("File kết quả trên share chưa đủ: " + name);
        }
        foreach (var (name, outputDir) in new[] { (video, videoOutput), (music, musicOutput), (tracks, musicOutput) })
        {
            var entry = done.Files.First(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            string source = Path.Combine(dir, name);
            string target = Path.Combine(outputDir, name);
            Directory.CreateDirectory(outputDir);
            string temp = target + ".part";
            File.Copy(source, temp, true);
            if (new FileInfo(temp).Length != entry.Size)
            {
                try { File.Delete(temp); } catch { }
                throw new IOException("Copy về máy bị thiếu dữ liệu: " + name);
            }
            File.Move(temp, target, true);
        }
    }

    /// <summary>Dọn thư mục kết quả trên share kiểu cố gắng hết sức: xóa done.json trước, rồi cả thư mục. Trả false nếu còn sót (thử lại sau).</summary>
    public static bool TryCleanupResult(string dir)
    {
        try
        {
            string marker = Path.Combine(dir, "done.json");
            if (File.Exists(marker)) File.Delete(marker);
        }
        catch { return false; }
        return TryDeleteDir(dir);
    }
}
