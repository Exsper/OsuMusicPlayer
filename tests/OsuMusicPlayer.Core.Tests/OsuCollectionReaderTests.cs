namespace OsuMusicPlayer.Core.Tests;

using OsuMusicPlayer.Core;
using OsuMusicPlayer.Core.IO;
using OsuMusicPlayer.Core.Models;
using Xunit;

/// <summary>collection.db 读写与 osu! 路径/配置定位。</summary>
public sealed class OsuCollectionReaderTests
{
    [Fact]
    public void 收藏夹应能完整往返()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("collection.db");

        List<OsuCollectionData> source =
        [
            new OsuCollectionData("我的最爱", ["aaa", "bbb", "ccc"]),
            new OsuCollectionData("日本語コレクション", []),
            new OsuCollectionData("Empty", []),
        ];

        OsuCollectionReader reader = new();
        reader.Write(source, path);

        IReadOnlyList<OsuCollectionData> loaded = reader.Read(path);

        Assert.Equal(3, loaded.Count);
        Assert.Equal("我的最爱", loaded[0].Name);
        Assert.Equal(["aaa", "bbb", "ccc"], loaded[0].BeatmapHashes);
        Assert.Equal("日本語コレクション", loaded[1].Name);
        Assert.Empty(loaded[2].BeatmapHashes);
    }

    [Fact]
    public void 非收藏夹文件应给出明确错误()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("not-a-collection.db");

        File.WriteAllText(path, "这显然不是二进制收藏夹文件");

        Assert.Throws<InvalidCollectionFileException>(() => new OsuCollectionReader().Read(path));
    }

    [Fact]
    public void 读取不存在的收藏夹应抛出FileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() => new OsuCollectionReader().Read(Path.Combine(Path.GetTempPath(), "no-such.db")));
    }

    [Fact]
    public void 示例数据的收藏夹应包含空收藏夹与缺失谱面()
    {
        using TempDirectory temp = new();
        SampleData.SampleOsuFolderResult sample = SampleData.SampleOsuFolderGenerator.Generate(
            new SampleData.SampleOsuFolderOptions { TargetDirectory = temp.Combine("osu"), AudioSeconds = 0.3 });

        IReadOnlyList<OsuCollectionData> collections = new OsuCollectionReader().Read(Path.Combine(sample.OsuDirectory, "collection.db"));

        Assert.Equal(5, collections.Count);
        Assert.Equal("我的最爱", collections[0].Name);
        Assert.Equal(7, collections[0].Count);
        Assert.Empty(collections.Single(static collection => collection.Name == "空收藏夹").BeatmapHashes);

        // 含缺失谱面：1 个本地不存在的哈希 + 1 个真实哈希
        Assert.Equal(2, collections.Single(static c => c.Name == "含缺失谱面").Count);
        Assert.Equal(2, collections.Single(static c => c.Name == "双音频谱面集").Count);
    }

    [Fact]
    public void osu路径定位应识别安装目录()
    {
        using TempDirectory temp = new();
        SampleData.SampleOsuFolderResult sample = SampleData.SampleOsuFolderGenerator.Generate(
            new SampleData.SampleOsuFolderOptions { TargetDirectory = temp.Combine("osu"), AudioSeconds = 0.3 });

        Assert.True(OsuPathLocator.IsStableInstall(sample.OsuDirectory));
        Assert.False(OsuPathLocator.IsStableInstall(temp.Path));
        Assert.False(OsuPathLocator.IsStableInstall(null));
    }

    [Fact]
    public void 配置文件读取应忽略无效内容()
    {
        using TempDirectory temp = new();

        Assert.Null(OsuConfigReader.TryGetBeatmapDirectory(temp.Path));
        Assert.Equal(Path.Combine(temp.Path, "Songs"), OsuConfigReader.ResolveSongsDirectory(temp.Path));

        File.WriteAllText(Path.Combine(temp.Path, "osu!tester.cfg"), "VolumeUniversal = 50" + Environment.NewLine + "BeatmapDirectory = Custom" + Environment.NewLine);

        Assert.Equal("Custom", OsuConfigReader.TryGetBeatmapDirectory(temp.Path));
    }
}
