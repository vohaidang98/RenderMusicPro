namespace RenderMusicPro.Services;

/// <summary>Một tài nguyên nhúng trong exe: đường dẫn tương đối (dùng '/'), dung lượng, cách mở.</summary>
public sealed record EmbeddedAsset(string RelativePath, long Length, Func<Stream> Open);

/// <summary>Một bộ hiệu ứng có sẵn = một thư mục con trong thư mục gốc các bộ.</summary>
public sealed record EffectPack(string Name, string Folder)
{
    public string DisplayName => EffectPackLibrary.DisplayName(Name);
}

/// <summary>Một khung camera = một file PNG trong suốt trong thư mục CameraFrames.</summary>
public sealed record CameraFrameFile(string Name, string Path)
{
    public string DisplayName => EffectPackLibrary.FrameDisplayName(Name);
}

/// <summary>
/// Logic thuần cho "bộ hiệu ứng có sẵn": liệt kê các bộ (mỗi thư mục con là một bộ, thả thêm thư mục là tự
/// hiện), đặt tên hiển thị tiếng Việt, và giải bộ mẫu nhúng trong exe ra đĩa. Không phụ thuộc MainForm.
/// </summary>
public static class EffectPackLibrary
{
    /// <summary>Tiền tố LogicalName của tài nguyên nhúng (xem RenderMusicPro.csproj).</summary>
    public const string ResourcePrefix = "BuiltInEffects/";

    /// <summary>Tên thư mục gốc chứa các bộ, cạnh exe là Assets\Presets_Effects (giống bản phát hành Full).</summary>
    public const string PresetsFolderName = "Presets_Effects";

    private static readonly Dictionary<string, string> KnownNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["00_CoBan_Chung"] = "Cơ bản – chung",
        ["01_CongGiao_HaoQuang"] = "Công giáo – Hào quang",
        ["02_PhatGiao_ThienDinh"] = "Phật giáo – Thiền định",
        ["03_Chill_Piano_Lofi"] = "Chill – Piano Lofi",
        ["04_Remix_Vinahouse_EDM"] = "Remix – Vinahouse EDM",
        ["05_Gufeng_CoTrang"] = "Gufeng – Cổ trang",
        ["06_Bolero_FilmGrain"] = "Bolero – Film grain",
        ["07_PopBallad_Wedding"] = "Pop Ballad – Đám cưới",
        ["08_Winter_GiangSinh"] = "Mùa đông – Giáng sinh",
        ["09_Epic_FireEmbers"] = "Epic – Tàn lửa",
        ["10_Kids_BongBong"] = "Thiếu nhi – Bong bóng",
    };

    /// <summary>"07_PopBallad_Wedding" → "07 · Pop Ballad – Đám cưới"; tên lạ: "11_Den_Disco" → "11 · Den Disco".</summary>
    public static string DisplayName(string folderName)
    {
        string name = folderName.Trim();
        string number = "";
        int underscore = name.IndexOf('_');
        if (underscore > 0 && name[..underscore].All(char.IsDigit))
        {
            number = name[..underscore];
            if (!KnownNames.ContainsKey(name)) name = name[(underscore + 1)..];
        }
        string label = KnownNames.TryGetValue(folderName.Trim(), out var known)
            ? known : name.Replace('_', ' ').Trim();
        if (label.Length == 0) label = folderName;
        return number.Length > 0 ? $"{number} · {label}" : label;
    }

    /// <summary>
    /// Liệt kê bộ hiệu ứng từ các thư mục gốc theo thứ tự ưu tiên; trùng tên thì giữ bộ ở gốc đứng trước.
    /// Gốc không tồn tại/không đọc được thì bỏ qua. Bộ không có file hỗ trợ nào thì không liệt kê.
    /// </summary>
    public static List<EffectPack> ListPacks(IEnumerable<string> roots, Func<string, bool> isSupportedFile)
    {
        var packs = new Dictionary<string, EffectPack>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            string[] folders;
            try
            {
                if (!Directory.Exists(root)) continue;
                folders = Directory.GetDirectories(root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                continue;
            }
            foreach (string folder in folders)
            {
                string name = Path.GetFileName(folder);
                if (name.Length == 0 || name.StartsWith('.') || packs.ContainsKey(name)) continue;
                if (!HasSupportedFile(folder, isSupportedFile)) continue;
                packs[name] = new EffectPack(name, Path.GetFullPath(folder));
            }
        }
        return packs.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool HasSupportedFile(string folder, Func<string, bool> isSupportedFile)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any(isSupportedFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // =========================================================
    // KHUNG CAMERA
    // =========================================================
    /// <summary>Tiền tố LogicalName của khung camera nhúng.</summary>
    public const string FrameResourcePrefix = "BuiltInFrames/";
    public const string FramesFolderName = "CameraFrames";

    private static readonly Dictionary<string, string> KnownFrames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Frame_01_Minimal_Rec"] = "Tối giản",
        ["Frame_02_Streamer_Neon"] = "Streamer neon (LIVE)",
        ["Frame_03_Camcorder_Viewfinder"] = "Máy quay (REC)",
        ["Frame_04_Luxury_Gold"] = "Sang trọng vàng",
    };

    /// <summary>"Frame_03_Camcorder_Viewfinder" → "03 · Máy quay (REC)"; "Frame_05_Abc" → "05 · Abc".</summary>
    public static string FrameDisplayName(string fileNameWithoutExtension)
    {
        string name = fileNameWithoutExtension.Trim();
        string core = name.StartsWith("Frame_", StringComparison.OrdinalIgnoreCase) ? name[6..] : name;
        if (!KnownFrames.TryGetValue(name, out var known)) return DisplayName(core);
        int underscore = core.IndexOf('_');
        return underscore > 0 && core[..underscore].All(char.IsDigit) ? $"{core[..underscore]} · {known}" : known;
    }

    /// <summary>Liệt kê file PNG khung camera từ các gốc theo thứ tự ưu tiên; trùng tên giữ file ở gốc trước.</summary>
    public static List<CameraFrameFile> ListCameraFrames(IEnumerable<string> roots)
    {
        var frames = new Dictionary<string, CameraFrameFile>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            string[] files;
            try
            {
                if (!Directory.Exists(root)) continue;
                files = Directory.GetFiles(root, "*.png");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                continue;
            }
            foreach (string file in files)
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(file);
                if (name.Length == 0 || frames.ContainsKey(name)) continue;
                frames[name] = new CameraFrameFile(name, System.IO.Path.GetFullPath(file));
            }
        }
        return frames.Values.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Tên hiển thị các mức "vị trí khung" (CameraFramePosition): khoảng cách khung tới mép video.</summary>
    public static readonly string[] FramePositionNames = { "Sát mép", "Cách mép vừa", "Cách mép xa" };
    private static readonly double[] FrameInsets = { 0, 0.03, 0.06 };

    /// <summary>
    /// Khung được co giữ tỉ lệ video và đặt giữa, cách mép theo mức <paramref name="position"/> (0 = sát mép).
    /// Giá trị lạ kẹp về khoảng hợp lệ. Kích thước/toạ độ luôn chẵn (yuv420p).
    /// </summary>
    public static (int X, int Y, int Width, int Height) CameraFrameLayout(int videoWidth, int videoHeight, int position)
    {
        if (videoWidth <= 0 || videoHeight <= 0) throw new ArgumentOutOfRangeException(nameof(videoWidth));
        double inset = FrameInsets[Math.Clamp(position, 0, FrameInsets.Length - 1)];
        int x = (int)Math.Round(videoWidth * inset), y = (int)Math.Round(videoHeight * inset);
        x -= x % 2; y -= y % 2;
        int width = videoWidth - 2 * x, height = videoHeight - 2 * y;
        width -= width % 2; height -= height % 2;
        return (x, y, width, height);
    }

    /// <summary>Đường dẫn tương đối an toàn: không rỗng, không tuyệt đối, không "..", không ký tự cấm.</summary>
    public static bool IsSafeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return false;
        if (relativePath.Contains(':') || relativePath.StartsWith('/') || relativePath.StartsWith('\\')) return false;
        string[] parts = relativePath.Split('/', '\\');
        char[] invalid = Path.GetInvalidFileNameChars();
        foreach (string part in parts)
        {
            if (part.Length == 0 || part == "." || part == "..") return false;
            if (part.IndexOfAny(invalid) >= 0) return false;
        }
        return true;
    }

    /// <summary>
    /// Giải tài nguyên nhúng vào <paramref name="targetRoot"/>. File đã có cùng dung lượng được giữ; thiếu hoặc
    /// khác thì ghi qua "&lt;file&gt;.part" rồi đổi tên. Không xóa file nào khác trong thư mục đích.
    /// Trả về số file đã ghi mới.
    /// </summary>
    public static int Extract(string targetRoot, IEnumerable<EmbeddedAsset> assets)
    {
        string root = Path.GetFullPath(targetRoot);
        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        int written = 0;
        foreach (var asset in assets)
        {
            if (!IsSafeRelativePath(asset.RelativePath))
                throw new InvalidDataException("Tên hiệu ứng nhúng không hợp lệ: " + asset.RelativePath);
            string target = Path.GetFullPath(Path.Combine(root, asset.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Tên hiệu ứng nhúng thoát khỏi thư mục đích: " + asset.RelativePath);
            var existing = new FileInfo(target);
            if (existing.Exists && existing.Length == asset.Length) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string part = target + ".part";
            try
            {
                using (var source = asset.Open())
                using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
                    source.CopyTo(output);
                File.Move(part, target, true);
                written++;
            }
            finally
            {
                try { if (File.Exists(part)) File.Delete(part); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        return written;
    }
}
