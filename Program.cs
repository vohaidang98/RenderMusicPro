using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace RandomVideoDangVH;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (RenderMusicPro.Services.UpdateApplier.IsApplyCommand(args))
        {
            RenderMusicPro.Services.UpdateApplier.Run(args);
            return;
        }

        ApplicationConfiguration.Initialize();

        // Bắt buộc nhập đúng mã kích hoạt mới dùng được app.
        if (!RenderMusicPro.Services.ActivationPrompt.EnsureActivated())
            return;

        Application.ThreadException += (_, e) =>
        {
            WriteCrashLog(e.Exception);
            MessageBox.Show(e.Exception.ToString(), "RenderMusicPro - LỖI",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                WriteCrashLog(ex);
        };

        bool startMinimized = args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
        try
        {
            Application.Run(new MainForm(startMinimized));
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
            MessageBox.Show(ex.ToString(), "RenderMusicPro - LỖI KHỞI ĐỘNG",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void WriteCrashLog(Exception ex)
    {
        try
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root)) root = AppContext.BaseDirectory;
            string dir = Path.Combine(root, "RenderMusicPro");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "RenderMusicPro_crash.log"),
                Environment.NewLine + "========================================" +
                Environment.NewLine + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                Environment.NewLine + ex + Environment.NewLine);
        }
        catch
        {
            // Không ghi được log crash (đĩa full / không có quyền) — không còn gì làm được
        }
    }
}
