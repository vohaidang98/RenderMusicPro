namespace RenderMusicPro.Services;

/// <summary>Khuôn chung cho nguồn cập nhật (LAN hoặc GitHub) để dùng chung UpdateDownloadDialog.</summary>
public interface IUpdateService
{
    string? LastError { get; }

    Task<UpdateReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken = default);

    Task<string> DownloadUpdateAsync(
        UpdateReleaseInfo release,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
