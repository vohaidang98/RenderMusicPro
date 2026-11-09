using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace RenderMusicPro.Services;

/// <summary>
/// Cập nhật từ thư mục mạng LAN: version.json + RenderMusicPro.exe (kiểm tra SHA-256).
/// version.json: {"version":"1.3.0","file":"RenderMusicPro.exe","sha256":"...","notes":"..."}
/// Chỉ cho tài khoản quản trị quyền GHI vào thư mục chia sẻ; máy khác chỉ cần quyền ĐỌC.
/// </summary>
public sealed class LanUpdateService : IUpdateService
{
    public string? LastError { get; private set; }

    private static string SettingsFile => Path.Combine(GetAppDataDirectory(), "update.json");

    private static string GetAppDataDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            throw new InvalidOperationException("Không xác định được thư mục AppData của người dùng.");
        return Path.Combine(localAppData, "RenderMusicPro");
    }

    public static string GetUpdateDirectory() => Path.Combine(GetAppDataDirectory(), "Updates");

    public static string GetFolder()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsFile));
            return doc.RootElement.TryGetProperty("folder", out var f) ? (f.GetString() ?? "").Trim() : "";
        }
        catch { return ""; }
    }

    public static void SetFolder(string folder)
    {
        string temp = "";
        try
        {
            Directory.CreateDirectory(GetAppDataDirectory());
            // Ghi ra file tạm rồi đổi tên để update.json không bao giờ bị ghi dở.
            temp = SettingsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new { folder }));
            File.Move(temp, SettingsFile, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.Write("Không lưu được thư mục cập nhật: " + ex.Message);
            try { if (temp.Length > 0) File.Delete(temp); } catch { }
        }
    }

    /// <summary>Trả về bản mới nếu có; null nếu lỗi (xem LastError) hoặc chưa chọn thư mục.</summary>
    public async Task<UpdateReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        LastError = null;
        string folder = GetFolder();
        if (string.IsNullOrWhiteSpace(folder))
        {
            LastError = "Chưa chọn thư mục cập nhật.";
            return null;
        }

        try
        {
            string manifestPath = Path.Combine(folder, "version.json");
            if (!File.Exists(manifestPath))
            {
                LastError = "Không tìm thấy version.json trong: " + folder;
                return null;
            }

            using var doc = JsonDocument.Parse(
                (await File.ReadAllTextAsync(manifestPath, cancellationToken)).TrimStart('﻿'));
            var root = doc.RootElement;
            string versionText = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
            string file = root.TryGetProperty("file", out var f) ? f.GetString() ?? "" : "";
            string sha = root.TryGetProperty("sha256", out var s) ? (s.GetString() ?? "").ToLowerInvariant() : "";
            string notes = root.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "";

            if (!Version.TryParse(versionText.TrimStart('v', 'V'), out var version))
            {
                LastError = "version.json có phiên bản không hợp lệ.";
                return null;
            }
            if (!string.Equals(file, ServiceConfig.WindowsAssetName, StringComparison.OrdinalIgnoreCase))
            {
                LastError = "version.json phải trỏ tới file " + ServiceConfig.WindowsAssetName + ".";
                return null;
            }
            if (sha.Length != 64 || !sha.All(Uri.IsHexDigit))
            {
                LastError = "version.json thiếu mã SHA-256 hợp lệ.";
                return null;
            }

            string path = Path.Combine(folder, file);
            if (!File.Exists(path))
            {
                LastError = "Chưa thấy file " + file + " trong thư mục cập nhật.";
                return null;
            }

            return new UpdateReleaseInfo(
                "v" + version, version, notes, null, path, file, new FileInfo(path).Length, sha);
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
        if (string.IsNullOrWhiteSpace(release.DownloadUrl) || !File.Exists(release.DownloadUrl))
            throw new FileNotFoundException("Không thấy file cập nhật trong thư mục LAN.");
        if (string.IsNullOrWhiteSpace(release.AssetSha256))
            throw new InvalidDataException("Thiếu mã SHA-256, không cập nhật để đảm bảo an toàn.");

        string updateDirectory = GetUpdateDirectory();
        Directory.CreateDirectory(updateDirectory);
        string finalPath = Path.Combine(updateDirectory, $"RenderMusicPro-v{release.Version}-{Guid.NewGuid():N}.exe");
        string partialPath = finalPath + ".download";
        const long maxUpdateBytes = 800L * 1024 * 1024;

        try
        {
            long total = new FileInfo(release.DownloadUrl).Length;
            if (total > maxUpdateBytes) throw new InvalidDataException("Bản cập nhật lớn hơn giới hạn 800 MB.");

            await using (var input = new FileStream(release.DownloadUrl, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
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
                    progress?.Report(new UpdateDownloadProgress(received, total));
                }
                await output.FlushAsync(cancellationToken);

                string actualHash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                if (!string.Equals(actualHash, release.AssetSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("File cập nhật bị lỗi (sai mã SHA-256), đã huỷ cập nhật.");
                if (received < 10L * 1024 * 1024)
                    throw new InvalidDataException("File cập nhật quá nhỏ để là RenderMusicPro.exe hợp lệ.");
            }

            VerifyExecutableVersion(partialPath, release.Version);
            File.Move(partialPath, finalPath);
            progress?.Report(new UpdateDownloadProgress(total, total, IsComplete: true));
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

public sealed record UpdateReleaseInfo(
    string TagName,
    Version Version,
    string Name,
    string? ReleaseUrl,
    string? DownloadUrl,
    string? AssetName,
    long AssetSize,
    string? AssetSha256,
    string Source = "LAN");

public sealed record UpdateDownloadProgress(long BytesReceived, long? TotalBytes, bool IsComplete = false);
