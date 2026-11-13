using System.Diagnostics;
using System.Text;

namespace RenderMusicPro.Services;

/// <summary>
/// Tự khởi động RenderMusicPro cùng Windows (thu nhỏ, không giành focus) qua Task Scheduler — để máy NHẬN
/// luôn sẵn sàng nhận render ngay khi bật máy mà không cần mở app tay. Không cần quyền quản trị (chạy task
/// ở mức người dùng hiện tại, /RL LIMITED).
/// </summary>
public static class AutoStartService
{
    private const string TaskName = "RenderMusicPro-AutoStart";

    private static string VbsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RenderMusicPro", "start-hidden.vbs");

    /// <summary>Bật/tắt tự khởi động. Ném lỗi (thông điệp tiếng Việt) khi thất bại để form hiển thị cho người dùng.</summary>
    public static void SetEnabled(bool enabled)
    {
        if (enabled) Register();
        else Unregister();
    }

    public static bool IsRegistered()
    {
        try
        {
            var (code, _, _) = RunSchtasks("/Query", "/TN", TaskName);
            return code == 0;
        }
        catch { return false; }
    }

    private static void Register()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            throw new InvalidOperationException("Không xác định được đường dẫn RenderMusicPro.exe.");

        string vbsPath = VbsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(vbsPath)!);
        // Window style 2 = SW_SHOWMINNOACTIVE: mở thu nhỏ, không giành focus khỏi app khác lúc đăng nhập.
        string content = "CreateObject(\"WScript.Shell\").Run \"\"\"" + exePath + "\"\" --minimized\", 2, False";
        File.WriteAllText(vbsPath, content, new UTF8Encoding(false));

        // "wscript.exe \"<vbsPath>\"" là 1 giá trị DUY NHẤT cho /TR; truyền qua ArgumentList (không tự ghép
        // chuỗi dòng lệnh) để .NET tự escape đúng — tự ghép chuỗi từng làm hỏng đường dẫn khi hồ sơ người
        // dùng Windows có dấu cách (vd "C:\Users\John Smith\...").
        string action = "wscript.exe \"" + vbsPath + "\"";
        var (code, stdout, stderr) = RunSchtasks("/Create", "/TN", TaskName, "/TR", action, "/SC", "ONLOGON", "/RL", "LIMITED", "/IT", "/F");
        if (code != 0)
            throw new InvalidOperationException("schtasks tạo tác vụ thất bại (mã " + code + "): " + FirstNonEmpty(stderr, stdout));
    }

    private static void Unregister()
    {
        var (code, stdout, stderr) = RunSchtasks("/Delete", "/TN", TaskName, "/F");
        string detail = FirstNonEmpty(stderr, stdout);
        if (code != 0 && !detail.Contains("cannot find", StringComparison.OrdinalIgnoreCase) &&
            !detail.Contains("không tìm thấy", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("schtasks xóa tác vụ thất bại (mã " + code + "): " + detail);
    }

    private static string FirstNonEmpty(string a, string b) => string.IsNullOrWhiteSpace(a) ? b.Trim() : a.Trim();

    private static (int Code, string StdOut, string StdErr) RunSchtasks(params string[] args)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        foreach (string a in args) p.StartInfo.ArgumentList.Add(a);
        p.Start();
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(15000))
        {
            try { p.Kill(true); } catch { }
            throw new TimeoutException("schtasks.exe không phản hồi.");
        }
        return (p.ExitCode, stdout, stderr);
    }
}
