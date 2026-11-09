using System.Diagnostics;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace RenderMusicPro.Services;

public static class UpdateApplier
{
    private const string CommandName = "--apply-update";

    public static bool IsApplyCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.Ordinal);

    public static void Start(string downloadedFile, string version, string? expectedSha256)
    {
        string targetPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Không xác định được đường dẫn EXE đang chạy.");
        string helperDirectory = LanUpdateService.GetUpdateDirectory();
        Directory.CreateDirectory(helperDirectory);
        string helperPath = Path.Combine(helperDirectory,
            $"RenderMusicProUpdater-{Environment.ProcessId}-{Guid.NewGuid():N}.exe");
        File.Copy(targetPath, helperPath, overwrite: false);

        long startTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
        var startInfo = new ProcessStartInfo
        {
            FileName = helperPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add(CommandName);
        startInfo.ArgumentList.Add(targetPath);
        startInfo.ArgumentList.Add(Path.GetFullPath(downloadedFile));
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add(startTicks.ToString());
        startInfo.ArgumentList.Add(version);
        startInfo.ArgumentList.Add(expectedSha256 ?? "-");
        startInfo.ArgumentList.Add("RenderMusicPro");

        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException("Windows không khởi chạy được tiến trình cập nhật.");
    }

    public static void Run(string[] args)
    {
        try
        {
            if (args.Length != 8 || !IsApplyCommand(args))
                throw new ArgumentException("Tham số chạy trình cập nhật không hợp lệ.");

            string targetPath = Path.GetFullPath(args[1]);
            string downloadedFile = Path.GetFullPath(args[2]);
            if (!string.Equals(Path.GetFileName(targetPath), ServiceConfig.WindowsAssetName,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Đường dẫn ứng dụng đích không hợp lệ.");
            if (!IsInsideUpdateDirectory(downloadedFile))
                throw new InvalidOperationException("File cập nhật nằm ngoài thư mục tải tạm của app.");
            if (!int.TryParse(args[3], out int parentPid) ||
                !long.TryParse(args[4], out long parentStartTicks) ||
                !Version.TryParse(args[5], out Version? expectedVersion))
                throw new ArgumentException("Thông tin phiên bản hoặc tiến trình ứng dụng không hợp lệ.");

            string? expectedSha256 = args[6] == "-" ? null : args[6];
            // The final argument is a protocol marker to reject accidental direct invocation.
            if (args[7] != "RenderMusicPro")
                throw new ArgumentException("Yêu cầu cập nhật không hợp lệ.");

            ValidateDownloadedFile(downloadedFile, expectedVersion, expectedSha256);
            WaitForOriginalApp(parentPid, parentStartTicks);

            try
            {
                ReplaceAndRestart(targetPath, downloadedFile);
            }
            catch (UnauthorizedAccessException)
            {
                StartElevatedRetry(args);
                ScheduleHelperCleanup(Environment.ProcessPath);
                return;
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) == 5)
            {
                StartElevatedRetry(args);
                ScheduleHelperCleanup(Environment.ProcessPath);
                return;
            }

            ScheduleHelperCleanup(Environment.ProcessPath);
        }
        catch (Exception ex)
        {
            try
            {
                MessageBox.Show(
                    "Không thể hoàn tất cập nhật Render Music Pro.\n\n" + ex.Message,
                    "Render Music Pro - Cập nhật",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch { }
        }
    }

    private static bool IsInsideUpdateDirectory(string filePath)
    {
        string root = Path.GetFullPath(LanUpdateService.GetUpdateDirectory())
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(filePath);
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
               Path.GetFileName(fullPath).StartsWith("RenderMusicPro-", StringComparison.Ordinal) &&
               string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateDownloadedFile(
        string filePath,
        Version expectedVersion,
        string? expectedSha256)
    {
        var file = new FileInfo(filePath);
        if (!file.Exists || file.Length < 10L * 1024 * 1024 || file.Length > 600L * 1024 * 1024)
            throw new InvalidDataException("File cập nhật bị thiếu hoặc có kích thước bất thường.");

        var info = FileVersionInfo.GetVersionInfo(filePath);
        if (!Version.TryParse(info.FileVersion, out Version? actualVersion) ||
            actualVersion.Major != expectedVersion.Major ||
            actualVersion.Minor != expectedVersion.Minor ||
            Math.Max(0, actualVersion.Build) != Math.Max(0, expectedVersion.Build))
            throw new InvalidDataException("Phiên bản EXE tải về không khớp phiên bản release.");

        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            using var stream = File.OpenRead(filePath);
            string actualHash = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256 file cập nhật không khớp.");
        }
    }

    private static void WaitForOriginalApp(int processId, long expectedStartTicks)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            long actualStartTicks = process.StartTime.ToUniversalTime().Ticks;
            if (actualStartTicks != expectedStartTicks)
                return; // PID đã được Windows tái sử dụng.
            if (!process.WaitForExit(120_000))
                throw new TimeoutException("Ứng dụng chưa đóng sau 2 phút; không thay file để tránh mất dữ liệu.");
        }
        catch (ArgumentException)
        {
            // Ứng dụng đã thoát trước khi trình cập nhật bắt đầu.
        }
        catch (InvalidOperationException)
        {
            // Tiến trình đã thoát.
        }
    }

    private static void ReplaceAndRestart(string targetPath, string downloadedFile)
    {
        string directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("Không xác định được thư mục cài đặt.");
        string newPath = Path.Combine(directory, $"RenderMusicPro.update-{Guid.NewGuid():N}.tmp");
        string backupPath = Path.Combine(directory, $"RenderMusicPro.backup-{Guid.NewGuid():N}.tmp");

        try
        {
            File.Copy(downloadedFile, newPath, overwrite: false);
            File.Move(targetPath, backupPath);
            try
            {
                File.Move(newPath, targetPath);
            }
            catch
            {
                if (File.Exists(backupPath) && !File.Exists(targetPath))
                    File.Move(backupPath, targetPath);
                throw;
            }

            try { File.Delete(backupPath); } catch { }
            try { File.Delete(downloadedFile); } catch { }
            RestartApplication(targetPath, directory);
        }
        finally
        {
            try { File.Delete(newPath); } catch { }
        }
    }

    private static void RestartApplication(string targetPath, string workingDirectory)
    {
        bool isElevated = new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);
        if (isElevated)
        {
            // Let the user's normal Explorer shell start the app so an update does not
            // leave the everyday UI running with an elevated token.
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = QuoteArgument(targetPath),
                    UseShellExecute = true
                });
                return;
            }
            catch { }
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = targetPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        });
    }

    private static void StartElevatedRetry(string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath
                ?? throw new InvalidOperationException("Không xác định được trình cập nhật."),
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = string.Join(" ", args.Select(QuoteArgument))
        };

        try
        {
            Process.Start(startInfo);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            MessageBox.Show(
                "Bạn đã hủy quyền quản trị. Bản cập nhật chưa được cài; ứng dụng hiện tại vẫn được giữ nguyên.",
                "Render Music Pro - Cập nhật",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private static string QuoteArgument(string value) =>
        "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static void ScheduleHelperCleanup(string? helperPath)
    {
        if (string.IsNullOrWhiteSpace(helperPath) || !File.Exists(helperPath))
            return;
        try
        {
            string command = $"/d /c ping 127.0.0.1 -n 3 >nul & del /f /q \"{helperPath}\"";
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = command,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch { }
    }
}
