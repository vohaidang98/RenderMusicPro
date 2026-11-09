using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RenderMusicPro.Services;

/// <summary>
/// Kích hoạt bằng một mã duy nhất. Trong app chỉ lưu mã băm PBKDF2, không có mã gốc.
/// </summary>
internal static class Licensing
{
    private const string Salt = "RenderMusicPro|activation|v1";
    private const int Rounds = 200_000;
    private const string CodeHash = "344ecdc59bda1f96478e77afcd8d486be0cab36442908a4007d42a18a5d287ff";

    private static string StoreFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RenderMusicPro", "license.json");

    public static string HashCode(string code) => Convert.ToHexString(
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes((code ?? string.Empty).Trim()),
            Encoding.UTF8.GetBytes(Salt), Rounds, HashAlgorithmName.SHA256, 32)).ToLowerInvariant();

    private static bool Same(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    public static bool IsValidCode(string code) => Same(HashCode(code), CodeHash);

    public static bool IsActivated()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(StoreFile, Encoding.UTF8));
            string stored = doc.RootElement.TryGetProperty("hash", out var h) ? h.GetString() ?? "" : "";
            return stored.Length > 0 && Same(stored, CodeHash);
        }
        catch { return false; }
    }

    public static bool Activate(string code)
    {
        if (!IsValidCode(code)) return false;
        string temp = StoreFile + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StoreFile)!);
            // Ghi ra file tạm rồi đổi tên để file license.json không bao giờ bị ghi dở.
            File.WriteAllText(temp, JsonSerializer.Serialize(new { hash = CodeHash }), new UTF8Encoding(false));
            File.Move(temp, StoreFile, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); } catch { }
            AppLog.Write("Không ghi được license.json: " + ex.Message);
            throw new IOException("Không ghi được file kích hoạt: " + ex.Message, ex);
        }
        return true;
    }
}
