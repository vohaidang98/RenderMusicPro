using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace RenderMusicPro.Services;

/// <summary>
/// Cập nhật qua GitHub Releases (repo công khai) — dùng DỰ PHÒNG khi không đọc được thư mục LAN.
/// Asset .exe phải đúng tên ServiceConfig.WindowsAssetName; nếu release có thêm asset
/// "&lt;tên exe&gt;.sha256" thì xác minh luôn SHA-256 (không bắt buộc, GitHub không tự cấp sẵn).
/// </summary>
public sealed class GitHubUpdateService : IUpdateService
{
    private const string Owner = "vohaidang98";
    private const string Repo = "RenderMusicPro";
    private static readonly Uri ApiLatest = new($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");

    private readonly HttpClient _http = CreateClient();

    public string? LastError { get; private set; }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RenderMusicPro-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public async Task<UpdateReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        LastError = null;
        try
        {
            using var response = await _http.GetAsync(ApiLatest, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LastError = $"GitHub trả lỗi HTTP {(int)response.StatusCode}.";
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            string? releaseUrl = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;

            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
            {
                LastError = "GitHub release có tag phiên bản không hợp lệ.";
                return null;
            }
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                LastError = "GitHub release chưa có file đính kèm.";
                return null;
            }

            string? downloadUrl = null;
            long assetSize = 0;
            string? sha256Url = null;
            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                string url = asset.TryGetProperty("browser_download_url", out var du) ? du.GetString() ?? "" : "";
                if (string.Equals(name, ServiceConfig.WindowsAssetName, StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = url;
                    assetSize = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out long sz) ? sz : 0;
                }
                else if (string.Equals(name, ServiceConfig.WindowsAssetName + ".sha256", StringComparison.OrdinalIgnoreCase))
                {
                    sha256Url = url;
                }
            }
            if (string.IsNullOrEmpty(downloadUrl))
            {
                LastError = "GitHub release phải có file " + ServiceConfig.WindowsAssetName + ".";
                return null;
            }

            string sha256 = "";
            if (sha256Url != null)
            {
                try
                {
                    string shaText = await _http.GetStringAsync(sha256Url, cancellationToken);
                    sha256 = shaText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } parts
                        ? parts[0] : "";
                }
                catch { /* Không có checksum thì vẫn cho qua; DownloadUpdateAsync bỏ so khớp SHA khi rỗng. */ }
            }

            return new UpdateReleaseInfo(
                "v" + version, version, notes, releaseUrl, downloadUrl, ServiceConfig.WindowsAssetName,
                assetSize, string.IsNullOrEmpty(sha256) ? null : sha256.ToLowerInvariant(), Source: "GitHub");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<string> DownloadUpdateAsync(
        UpdateReleaseInfo release,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (string.IsNullOrWhiteSpace(release.DownloadUrl))
            throw new InvalidOperationException("Thiếu đường dẫn tải file từ GitHub.");

        string updateDirectory = LanUpdateService.GetUpdateDirectory();
        Directory.CreateDirectory(updateDirectory);
        string finalPath = Path.Combine(updateDirectory, $"RenderMusicPro-v{release.Version}-{Guid.NewGuid():N}.exe");
        string partialPath = finalPath + ".download";
        const long maxUpdateBytes = 800L * 1024 * 1024;

        try
        {
            using var response = await _http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? release.AssetSize;
            if (total > maxUpdateBytes) throw new InvalidDataException("Bản cập nhật lớn hơn giới hạn 800 MB.");

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[128 * 1024];
                long received = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    hasher.AppendData(buffer, 0, count);
                    received += count;
                    progress?.Report(new UpdateDownloadProgress(received, total > 0 ? total : null));
                }
                await output.FlushAsync(cancellationToken);

                if (!string.IsNullOrEmpty(release.AssetSha256))
                {
                    string actualHash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                    if (!string.Equals(actualHash, release.AssetSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("File cập nhật bị lỗi (sai mã SHA-256), đã huỷ cập nhật.");
                }
                if (received < 10L * 1024 * 1024)
                    throw new InvalidDataException("File cập nhật quá nhỏ để là " + ServiceConfig.WindowsAssetName + " hợp lệ.");
            }

            VerifyExecutableVersion(partialPath, release.Version);
            File.Move(partialPath, finalPath);
            progress?.Report(new UpdateDownloadProgress(total, total > 0 ? total : null, IsComplete: true));
            return finalPath;
        }
        catch
        {
            try { File.Delete(partialPath); } catch { }
            try { File.Delete(finalPath); } catch { }
            throw;
        }
    }

    private static void VerifyExecutableVersion(string path, Version expectedVersion)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        if (!Version.TryParse(info.FileVersion, out var actual) ||
            actual.Major != expectedVersion.Major ||
            actual.Minor != expectedVersion.Minor ||
            Math.Max(0, actual.Build) != Math.Max(0, expectedVersion.Build))
            throw new InvalidDataException(
                $"Phiên bản trong EXE ({info.FileVersion ?? "không rõ"}) không khớp bản {expectedVersion}.");
    }
}
