using System.Text;

namespace RenderMusicPro.Services;

/// <summary>
/// Nhật ký ra file: %LocalAppData%\RenderMusicPro\logs\yyyy-MM-dd.log.
/// Mọi lỗi khi ghi log đều bị nuốt để việc ghi log không bao giờ làm app dừng.
/// </summary>
internal static class AppLog
{
    private static readonly object Gate = new();
    private static readonly UTF8Encoding Utf8 = new(false);

    public static string LogDirectory
    {
        get
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrWhiteSpace(root) ? "" : Path.Combine(root, "RenderMusicPro", "logs");
        }
    }

    public static void Write(string text)
    {
        try
        {
            string directory = LogDirectory;
            if (directory.Length == 0) return;
            DateTime now = DateTime.Now;
            string line = $"[{now:HH:mm:ss}] {text}{Environment.NewLine}";
            lock (Gate)
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, now.ToString("yyyy-MM-dd") + ".log"), line, Utf8);
            }
        }
        catch
        {
            // Không ghi được log (đĩa đầy / không có quyền) - bỏ qua, không ảnh hưởng app.
        }
    }

    /// <summary>Xóa các file log cũ hơn <paramref name="days"/> ngày.</summary>
    public static void PruneOld(int days)
    {
        try
        {
            string directory = LogDirectory;
            if (directory.Length == 0 || !Directory.Exists(directory)) return;
            DateTime cutoff = DateTime.Now.AddDays(-days);
            lock (Gate)
            {
                foreach (string file in Directory.EnumerateFiles(directory, "*.log", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
                    }
                    catch
                    {
                        // File đang mở hoặc không có quyền - bỏ qua.
                    }
                }
            }
        }
        catch
        {
            // Không dọn được log cũ - bỏ qua.
        }
    }
}
