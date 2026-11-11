using System.Text;
using RenderMusicPro.Services;
using Xunit;

namespace RenderMusicPro.Tests;

public sealed class EffectPackLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rmp-pack-tests-" + Guid.NewGuid().ToString("N"));

    public EffectPackLibraryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static EmbeddedAsset Asset(string path, string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        return new EmbeddedAsset(path, bytes.Length, () => new MemoryStream(bytes));
    }

    private static bool IsMp4(string path) => path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);

    private string MakePack(string root, string name, params string[] files)
    {
        string folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        foreach (string file in files) File.WriteAllText(Path.Combine(folder, file), "x");
        return folder;
    }

    [Fact]
    public void Extract_WritesFilesInSubfolders()
    {
        int written = EffectPackLibrary.Extract(_root, new[]
        {
            Asset("00_CoBan_Chung/snow_fall.mp4", "snow"),
            Asset("00_CoBan_Chung/disco_lights.mp4", "disco")
        });
        Assert.Equal(2, written);
        Assert.Equal("snow", File.ReadAllText(Path.Combine(_root, "00_CoBan_Chung", "snow_fall.mp4")));
        Assert.Empty(Directory.GetFiles(_root, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public void Extract_SkipsSameSize_RewritesChangedSize()
    {
        EffectPackLibrary.Extract(_root, new[] { Asset("p/a.mp4", "1234") });
        Assert.Equal(0, EffectPackLibrary.Extract(_root, new[] { Asset("p/a.mp4", "1234") }));
        Assert.Equal(1, EffectPackLibrary.Extract(_root, new[] { Asset("p/a.mp4", "123456") }));
        Assert.Equal("123456", File.ReadAllText(Path.Combine(_root, "p", "a.mp4")));
    }

    [Fact]
    public void Extract_NeverDeletesUserFiles()
    {
        string user = Path.Combine(MakePack(_root, "p"), "cua-toi.mp4");
        File.WriteAllText(user, "mine");
        EffectPackLibrary.Extract(_root, new[] { Asset("p/a.mp4", "1") });
        Assert.Equal("mine", File.ReadAllText(user));
    }

    [Theory]
    [InlineData("../evil.mp4")]
    [InlineData("p/../../evil.mp4")]
    [InlineData("C:/evil.mp4")]
    [InlineData("/evil.mp4")]
    [InlineData("")]
    [InlineData("p//a.mp4")]
    public void Extract_RejectsUnsafeNames(string path)
    {
        Assert.False(EffectPackLibrary.IsSafeRelativePath(path));
        Assert.Throws<InvalidDataException>(() => EffectPackLibrary.Extract(_root, new[] { Asset(path, "x") }));
    }

    [Fact]
    public void ListPacks_EachSubfolderWithEffectsIsAPack_EmptyOnesSkipped()
    {
        MakePack(_root, "07_PopBallad_Wedding", "glitter_heart.mp4");
        MakePack(_root, "01_CongGiao_HaoQuang", "godrays_gold.mp4");
        MakePack(_root, "11_Den_Disco"); // chưa có file -> chưa hiện
        MakePack(_root, "ghi-chu", "readme.txt");
        var packs = EffectPackLibrary.ListPacks(new[] { _root }, IsMp4);
        Assert.Equal(new[] { "01_CongGiao_HaoQuang", "07_PopBallad_Wedding" }, packs.Select(p => p.Name));
    }

    [Fact]
    public void ListPacks_NewFolderAppearsAutomatically()
    {
        MakePack(_root, "01_A", "a.mp4");
        Assert.Single(EffectPackLibrary.ListPacks(new[] { _root }, IsMp4));
        MakePack(_root, "11_Den_Disco", "disco.mp4");
        Assert.Equal(2, EffectPackLibrary.ListPacks(new[] { _root }, IsMp4).Count);
    }

    [Fact]
    public void ListPacks_FirstRootWinsOnDuplicateName_MissingRootsIgnored()
    {
        string first = Path.Combine(_root, "first"), second = Path.Combine(_root, "second");
        MakePack(first, "00_CoBan_Chung", "a.mp4");
        MakePack(second, "00_CoBan_Chung", "b.mp4");
        MakePack(second, "08_Winter_GiangSinh", "snow_fall.mp4");
        var packs = EffectPackLibrary.ListPacks(new[] { first, Path.Combine(_root, "khong-co"), "", second }, IsMp4);
        Assert.Equal(2, packs.Count);
        Assert.StartsWith(first, packs.Single(p => p.Name == "00_CoBan_Chung").Folder);
    }

    [Theory]
    [InlineData("07_PopBallad_Wedding", "07 · Pop Ballad – Đám cưới")]
    [InlineData("00_CoBan_Chung", "00 · Cơ bản – chung")]
    [InlineData("11_Den_Disco", "11 · Den Disco")]
    [InlineData("HieuUngCuaToi", "HieuUngCuaToi")]
    public void DisplayName_FriendlyVietnamese(string folder, string expected)
    {
        Assert.Equal(expected, EffectPackLibrary.DisplayName(folder));
    }
}
