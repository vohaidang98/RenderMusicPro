using RenderMusicPro.Services;
using Xunit;

namespace RenderMusicPro.Tests;

public sealed class Clock
{
    public DateTime Now;
    public Clock(DateTime start) => Now = start;
    public DateTime Get() => Now;
    public void Add(TimeSpan t) => Now += t;
}

public abstract class QueueTestBase : IDisposable
{
    protected readonly string Share = Path.Combine(Path.GetTempPath(), "rmp-test-" + Guid.NewGuid().ToString("N"));
    protected readonly Clock ClockA = new(new DateTime(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc));
    protected readonly Clock ClockB = new(new DateTime(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc));
    protected readonly LanRenderQueue A;   // máy gửi PCA
    protected readonly LanRenderQueue B;   // máy nhận PC_B
    protected readonly string Batch;

    protected QueueTestBase()
    {
        Directory.CreateDirectory(Share);
        A = new LanRenderQueue(Share, Share, Share, "PCA", ClockA.Get);
        B = new LanRenderQueue(Share, Share, Share, "PC_B", ClockB.Get);
        A.EnsureFolders();
        Batch = LanRenderQueue.NewBatchId("PCA", new DateTime(2026, 11, 7, 10, 0, 0));
    }

    protected string Enqueue(int index, string baseName = "1 - Playlist")
    {
        string id = LanRenderQueue.JobIdFor(Batch, index);
        A.Enqueue(new LanJob { JobId = id, BatchId = Batch, Sender = "PCA", Index = index, BaseName = baseName });
        return id;
    }

    protected static List<string> Snapshot(string root) =>
        Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(x => x).ToList() : new();

    public virtual void Dispose()
    {
        foreach (string s in new[] { "", "-local", "-recv", "-recv2", "-recv3", "-recv4", "-recv5", "-recv6", "-recv7" })
            try { Directory.Delete(Share + s, true); } catch { }
    }
}

public class ClaimTests : QueueTestBase
{
    [Fact]
    public void OnlyOneMachineClaimsSameTicket()
    {
        string id = Enqueue(1);
        A.WriteHeartbeat(true, null);
        string pending = Path.Combine(A.PendingDir, id + ".json");
        var other = new LanRenderQueue(Share, Share, Share, "PC_C", ClockB.Get);
        var j1 = B.TryClaim(pending);
        var j2 = other.TryClaim(pending);
        Assert.NotNull(j1);
        Assert.Null(j2);
        Assert.Equal(id, j1!.JobId);
        Assert.True(B.HoldsClaim(id));
        Assert.False(other.HoldsClaim(id));
    }

    [Fact]
    public void ClaimNextTakesEachTicketOnce()
    {
        Enqueue(1); Enqueue(2);
        A.WriteHeartbeat(true, null);
        Assert.NotNull(B.ClaimNext());
        Assert.NotNull(B.ClaimNext());
        Assert.Null(B.ClaimNext());
    }

    [Fact]
    public void BadBaseNameTicketGoesToFailedNotRendered()
    {
        string id = Enqueue(1, "..\\evil");
        A.WriteHeartbeat(true, null);
        Assert.Null(B.ClaimNext());
        Assert.True(File.Exists(Path.Combine(B.FailedDir, id + ".json")));
        Assert.True(File.Exists(Path.Combine(B.FailedDir, id + ".error.txt")));
    }
}

public class ReclaimTests : QueueTestBase
{
    [Fact]
    public void StaleClaimReclaimedEvenIfHeartbeatKeepsChanging()
    {
        string id = Enqueue(1);
        A.WriteHeartbeat(true, null);
        Assert.NotNull(B.ClaimNext());

        Assert.Equal(0, A.ReclaimStale(Batch));          // lần đầu: ân hạn
        ClockA.Add(TimeSpan.FromMinutes(2));
        B.WriteHeartbeat(true, id);                        // nhịp tim B vẫn đổi
        Assert.Equal(0, A.ReclaimStale(Batch));          // < 3 phút: chưa thu hồi (đối chứng)
        ClockA.Add(TimeSpan.FromMinutes(2));
        B.WriteHeartbeat(true, id);
        Assert.Equal(1, A.ReclaimStale(Batch));          // > 3 phút không đổi: thu hồi
        Assert.True(File.Exists(Path.Combine(A.PendingDir, id + ".json")));
        Assert.False(B.HoldsClaim(id));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void ClaimBeingTouchedIsNotReclaimedDespiteSkewedClock(int dayOffset)
    {
        string id = Enqueue(1);
        A.WriteHeartbeat(true, null);
        ClockB.Add(TimeSpan.FromDays(dayOffset));          // đồng hồ máy B lệch ngày
        Assert.NotNull(B.ClaimNext());
        Assert.Equal(0, A.ReclaimStale(Batch));
        for (int i = 0; i < 6; i++)
        {
            ClockA.Add(TimeSpan.FromMinutes(1));
            ClockB.Add(TimeSpan.FromMinutes(1));
            Assert.True(B.TouchRunning(id));
            Assert.Equal(0, A.ReclaimStale(Batch));
        }
        Assert.True(B.HoldsClaim(id));
        // Đối chứng: ngừng chạm thì bị thu hồi
        ClockA.Add(TimeSpan.FromMinutes(4));
        Assert.Equal(1, A.ReclaimStale(Batch));
    }

    [Fact]
    public void ClaimWithFuturisticMtimeStillReclaimedWhenUntouched()
    {
        string id = Enqueue(1);
        A.WriteHeartbeat(true, null);
        Assert.NotNull(B.ClaimNext());
        File.SetLastWriteTimeUtc(B.RunningPath(id), ClockA.Now.AddDays(30));
        Assert.Equal(0, A.ReclaimStale(Batch));
        ClockA.Add(TimeSpan.FromMinutes(4));
        Assert.Equal(1, A.ReclaimStale(Batch));
    }

    [Fact]
    public void RecoverAfterRestartReturnsOwnRunningTickets()
    {
        string id = Enqueue(1);
        A.WriteHeartbeat(true, null);
        Assert.NotNull(B.ClaimNext());
        var restarted = new LanRenderQueue(Share, Share, Share, "PC_B", ClockB.Get);
        var (released, abandoned) = restarted.RecoverAfterRestart(true);
        Assert.Equal(1, released);
        Assert.Equal(0, abandoned);
        Assert.True(File.Exists(Path.Combine(A.PendingDir, id + ".json")));
        Assert.False(B.HoldsClaim(id));
    }

    [Fact]
    public void RecoverAfterRestartDoesNotTouchOtherMachinesTickets()
    {
        string id = Enqueue(1);
        A.WriteHeartbeat(true, null);
        Assert.NotNull(B.ClaimNext());
        var c = new LanRenderQueue(Share, Share, Share, "PC_C", ClockB.Get);
        Assert.Equal((0, 0), c.RecoverAfterRestart(true));
        Assert.True(B.HoldsClaim(id));
    }
}

public class PublishCollectTests : QueueTestBase
{
    private string MakeLocal(string dirName, string baseName)
    {
        string d = Path.Combine(Share + "-local", dirName);
        Directory.CreateDirectory(d);
        foreach (string ext in new[] { ".mp4", ".mp3", ".txt" })
            File.WriteAllText(Path.Combine(d, baseName + ext), "data" + ext);
        return d;
    }

    private static string[] LocalFiles(string d, string baseName) =>
        new[] { ".mp4", ".mp3", ".txt" }.Select(e => Path.Combine(d, baseName + e)).ToArray();

    [Fact]
    public void PublishGoesToPerMachineDirWithDoneLast()
    {
        string id = Enqueue(1);
        A.WriteHeartbeat(true, null);
        Assert.NotNull(B.ClaimNext());
        string d = MakeLocal("p1", "1 - Playlist");
        Assert.True(B.PublishResult(id, LocalFiles(d, "1 - Playlist")));
        string outDir = Path.Combine(Share, "out", id + "__PC_B");
        Assert.Equal(outDir, B.JobOutDir(id));
        Assert.True(File.Exists(Path.Combine(outDir, "done.json")));
        Assert.True(File.Exists(Path.Combine(outDir, "1 - Playlist.mp4")));
        Assert.Single(A.ListDone(Batch));
    }

    [Fact]
    public void PublishRefusedWhenClaimLost()
    {
        string id = Enqueue(1);
        A.WriteHeartbeat(true, null);
        Assert.NotNull(B.ClaimNext());
        Assert.Equal(0, A.ReclaimStale(Batch));
        ClockA.Add(TimeSpan.FromMinutes(4));
        Assert.Equal(1, A.ReclaimStale(Batch));
        Assert.False(B.HoldsClaim(id));
        string d = MakeLocal("p2", "1 - Playlist");
        Assert.False(B.PublishResult(id, LocalFiles(d, "1 - Playlist")));
        Assert.False(Directory.Exists(B.JobOutDir(id)) && Directory.GetFiles(B.JobOutDir(id)).Length > 0);
        Assert.Empty(A.ListDone(Batch));
    }

    private (string dir, LanDone done) Published(string baseName = "1 - Playlist")
    {
        string id = Enqueue(1, baseName);
        A.WriteHeartbeat(true, null);
        Assert.NotNull(B.ClaimNext());
        string d = MakeLocal("p3", baseName);
        Assert.True(B.PublishResult(id, LocalFiles(d, baseName)));
        var item = A.ListDone(Batch).Single();
        return (item.Dir, item.Done);
    }

    [Fact]
    public void CollectCopiesOnlyThreeExpectedNames()
    {
        var (dir, done) = Published();
        string vo = Path.Combine(Share + "-recv", "video"), mo = Path.Combine(Share + "-recv", "music");
        LanRenderQueue.CollectResult(dir, done, "1 - Playlist", vo, mo);
        Assert.Equal(new[] { "1 - Playlist.mp4" }, Snapshot(vo).Select(Path.GetFileName).ToArray());
        Assert.Equal(new[] { "1 - Playlist.mp3", "1 - Playlist.txt" }, Snapshot(mo).Select(Path.GetFileName).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void CollectIgnoresTraversalAndStrangeNamesInDoneJson()
    {
        var (dir, done) = Published();
        File.WriteAllText(Path.Combine(dir, "strange.exe"), "x");
        done.Files.Add(new LanDoneFile { Name = "..\\..\\x.exe", Size = 1 });
        done.Files.Add(new LanDoneFile { Name = "strange.exe", Size = 1 });
        string recv = Share + "-recv2";
        string vo = Path.Combine(recv, "a", "video"), mo = Path.Combine(recv, "a", "music");
        Directory.CreateDirectory(vo); Directory.CreateDirectory(mo);
        var before = Snapshot(recv);
        LanRenderQueue.CollectResult(dir, done, "1 - Playlist", vo, mo);
        var created = Snapshot(recv).Except(before).Select(p => Path.GetRelativePath(recv, p)).OrderBy(x => x).ToList();
        Assert.Equal(3, created.Count);
        Assert.All(created, p => Assert.True(p.StartsWith("a\\video\\") || p.StartsWith("a\\music\\"), p));
        Assert.DoesNotContain(Snapshot(recv), p => p.EndsWith("x.exe") || p.EndsWith("strange.exe"));
        Assert.False(File.Exists(Path.Combine(recv, "x.exe")));
        Assert.False(File.Exists(Path.Combine(recv, "a", "x.exe")));
        Assert.False(File.Exists(Path.Combine(vo, "strange.exe")));
        Assert.False(File.Exists(Path.Combine(mo, "strange.exe")));
    }

    [Fact]
    public void SnapshotHarnessDetectsStrayFiles_NotFalseGreen()
    {
        // Chứng minh cách kiểm "không tạo file ngoài" thật sự bắt được file lạ.
        string recv = Share + "-recv3";
        Directory.CreateDirectory(recv);
        var before = Snapshot(recv);
        File.WriteAllText(Path.Combine(recv, "x.exe"), "boom");
        var created = Snapshot(recv).Except(before).ToList();
        Assert.Single(created);
        Assert.EndsWith("x.exe", created[0]);
    }

    [Fact]
    public void CollectSizeMismatchThrows()
    {
        var (dir, done) = Published();
        done.Files.First(f => f.Name.EndsWith(".mp4")).Size += 5;
        string recv = Share + "-recv4";
        Assert.Throws<IOException>(() => LanRenderQueue.CollectResult(dir, done, "1 - Playlist",
            Path.Combine(recv, "v"), Path.Combine(recv, "m")));
        Assert.Empty(Snapshot(recv));
    }

    [Fact]
    public void CollectMissingExpectedNameThrows()
    {
        var (dir, done) = Published();
        done.Files.RemoveAll(f => f.Name.EndsWith(".txt"));
        string recv = Share + "-recv5";
        Assert.Throws<IOException>(() => LanRenderQueue.CollectResult(dir, done, "1 - Playlist",
            Path.Combine(recv, "v"), Path.Combine(recv, "m")));
    }

    [Fact]
    public void CollectUnsafeBaseNameThrows()
    {
        var (dir, done) = Published();
        string recv = Share + "-recv6";
        Assert.Throws<InvalidDataException>(() => LanRenderQueue.CollectResult(dir, done, "..\\x",
            Path.Combine(recv, "v"), Path.Combine(recv, "m")));
    }

    [Fact]
    public void CleanupFailureDoesNotLoseCollectedData()
    {
        var (dir, done) = Published();
        string recv = Share + "-recv7";
        string vo = Path.Combine(recv, "v"), mo = Path.Combine(recv, "m");
        LanRenderQueue.CollectResult(dir, done, "1 - Playlist", vo, mo);
        using (var locked = new FileStream(Path.Combine(dir, "1 - Playlist.mp4"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(LanRenderQueue.TryCleanupResult(dir));
            Assert.Equal(3, Snapshot(recv).Count);
            Assert.Equal("data.mp4", File.ReadAllText(Path.Combine(vo, "1 - Playlist.mp4")));
        }
        Assert.True(LanRenderQueue.TryCleanupResult(dir));   // thử lại sau khi mở khóa
        Assert.False(Directory.Exists(dir));
        Assert.Equal(3, Snapshot(recv).Count);
    }
}

public class CancelTests : QueueTestBase
{
    [Fact]
    public void CancelledBatchCannotBeClaimed()
    {
        string id = Enqueue(1);
        Enqueue(2);
        A.WriteHeartbeat(true, null);
        Assert.False(A.IsBatchCancelled(Batch));
        // ghi cờ hủy mà KHÔNG xóa pending để chắc chắn chính cờ hủy chặn giành
        File.WriteAllText(A.CancelledPath(Batch), "x");
        Assert.True(B.IsBatchCancelled(Batch));
        Assert.Null(B.ClaimNext());
        Assert.Null(B.TryClaim(Path.Combine(A.PendingDir, id + ".json")));
        Assert.False(B.HoldsClaim(id));
    }

    [Fact]
    public void CancelBatchDeletesPendingAndMarks()
    {
        Enqueue(1); Enqueue(2);
        A.CancelBatch(Batch);
        Assert.True(B.IsBatchCancelled(Batch));
        Assert.Empty(A.ListPending(Batch));
    }

    [Fact]
    public void DeadSenderBatchNotClaimed_AliveSenderIs()
    {
        Enqueue(1);
        A.WriteHeartbeat(true, null);
        Assert.True(B.IsSenderAlive("PCA"));          // quan sát đầu
        ClockB.Add(TimeSpan.FromMinutes(3));            // nhịp tim A không đổi >2 phút
        Assert.Null(B.ClaimNext());
        // đối chứng: A ghi lại nhịp tim thì giành được
        A.WriteHeartbeat(true, null);
        Assert.NotNull(B.ClaimNext());
    }

    [Fact]
    public void SenderWithoutHeartbeatFileNotClaimed()
    {
        Enqueue(1);
        Assert.Null(B.ClaimNext());
    }
}

public class PureRuleTests
{
    [Theory]
    [InlineData("1 - Playlist", true)]
    [InlineData("..\\x", false)]
    [InlineData("a/b", false)]
    [InlineData("C:x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("a..b", true)]
    [InlineData("1 - Nhạc Chill... Lofi", true)]
    [InlineData("..", false)]
    [InlineData(".", false)]
    public void SafeBaseName(string? name, bool expected) =>
        Assert.Equal(expected, LanRenderQueue.IsSafeBaseName(name));

    [Theory]
    [InlineData("D:\\")]
    [InlineData("C:\\")]
    [InlineData("C:\\Windows")]
    [InlineData("C:\\Windows\\System32")]
    [InlineData("C:\\Program Files")]
    [InlineData("C:\\Program Files (x86)")]
    public void BroadFoldersBlocked(string path)
    {
        Assert.True(LanRenderQueue.IsShareFolderTooBroad(path, out string reason));
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void UserProfileBlocked() =>
        Assert.True(LanRenderQueue.IsShareFolderTooBroad(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), out _));

    [Fact]
    public void NormalSubfolderAllowed()
    {
        Assert.False(LanRenderQueue.IsShareFolderTooBroad("D:\\Share\\TDN", out string r));
        Assert.Equal("", r);
    }

    [Fact]
    public void BelongsToBatchDoesNotConfusePCandPC_X()
    {
        string pc = LanRenderQueue.NewBatchId("PC", new DateTime(2026, 11, 7, 10, 0, 0));
        string pcx = LanRenderQueue.NewBatchId("PC_X", new DateTime(2026, 11, 7, 10, 0, 0));
        Assert.Equal("20261107-100000-PC", pc);
        Assert.True(LanRenderQueue.BelongsToBatch(LanRenderQueue.JobIdFor(pc, 1), pc));
        Assert.True(LanRenderQueue.BelongsToBatch(LanRenderQueue.JobIdFor(pc, 1) + "__M1", pc));
        Assert.False(LanRenderQueue.BelongsToBatch(LanRenderQueue.JobIdFor(pcx, 1), pc));
        Assert.False(LanRenderQueue.BelongsToBatch(LanRenderQueue.JobIdFor(pcx, 1) + "__M1", pc));
        Assert.True(LanRenderQueue.BelongsToBatch(LanRenderQueue.JobIdFor(pcx, 1), pcx));
        Assert.Equal("PC_X", LanRenderQueue.SenderOfBatch(pcx));
    }
}
