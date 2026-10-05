namespace OsuMusicPlayer.Core.Tests;

using System.Text.Json;
using System.Text.Json.Nodes;
using OsuMusicPlayer.Core.IO;
using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Services;
using Xunit;

/// <summary>
/// 音乐库磁盘缓存：扫描一次后应能从缓存读回同样的曲目，
/// 只有 <c>osu!.db</c> 变化、缓存损坏或换目录时才需要重新扫描。
/// </summary>
[Collection("sample")]
public sealed class LibraryCacheTests(SampleLibraryFixture fixture)
{
    private const string DatabaseFileName = OsuPathLocator.DatabaseFileName;

    [Fact]
    public void 保存后的缓存应能完整读回()
    {
        using TempDirectory temp = new("osump-cache");
        string databasePath = Path.Combine(fixture.Sample.OsuDirectory, DatabaseFileName);

        Touch(databasePath);

        LibraryCache cache = new(temp.Combine("library.bin"));
        MusicLibrary library = new MusicLibraryBuilder().Build(fixture.Sample.OsuDirectory);

        Assert.True(cache.Save(fixture.Sample.OsuDirectory, library));

        LibraryCacheLoadResult result = cache.TryLoad(fixture.Sample.OsuDirectory);

        Assert.Equal(LibraryCacheFreshness.Current, result.Freshness);
        Assert.NotNull(result.Library);
        Assert.Equal(library.Tracks.Count, result.Library!.Tracks.Count);
        Assert.Equal(library.Statistics.BeatmapCount, result.Library.Statistics.BeatmapCount);
        Assert.Equal(library.SongsDirectory, result.Library.SongsDirectory);

        // 逐字段核对，确保缓存没有丢字段。
        for (int i = 0; i < library.Tracks.Count; i++)
        {
            MusicTrack expected = library.Tracks[i];
            MusicTrack actual = result.Library.Tracks[i];

            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Title, actual.Title);
            Assert.Equal(expected.TitleUnicode, actual.TitleUnicode);
            Assert.Equal(expected.Artist, actual.Artist);
            Assert.Equal(expected.ArtistUnicode, actual.ArtistUnicode);
            Assert.Equal(expected.Creator, actual.Creator);
            Assert.Equal(expected.Tags, actual.Tags);
            Assert.Equal(expected.Source, actual.Source);
            Assert.Equal(expected.Directory, actual.Directory);
            Assert.Equal(expected.Directories, actual.Directories);
            Assert.Equal(expected.AudioFileName, actual.AudioFileName);
            Assert.Equal(expected.AudioFilePath, actual.AudioFilePath);
            Assert.Equal(expected.OsuFilePath, actual.OsuFilePath);
            Assert.Equal(expected.AudioFileExists, actual.AudioFileExists);
            Assert.Equal(expected.AudioNameFromFallback, actual.AudioNameFromFallback);
            Assert.Equal(expected.MapSetId, actual.MapSetId);
            Assert.Equal(expected.PrimaryMapId, actual.PrimaryMapId);
            Assert.Equal(expected.State, actual.State);
            Assert.Equal(expected.PlayMode, actual.PlayMode);
            Assert.Equal(expected.StarsNomod, actual.StarsNomod);
            Assert.Equal(expected.MainBpm, actual.MainBpm);
            Assert.Equal(expected.PreviewTime, actual.PreviewTime);
            Assert.Equal(expected.TotalTime, actual.TotalTime);
            Assert.Equal(expected.BeatmapCount, actual.BeatmapCount);
            Assert.Equal(expected.Difficulties, actual.Difficulties);
            Assert.Equal(expected.BeatmapHashes, actual.BeatmapHashes);
            Assert.Equal(expected.BeatmapIds, actual.BeatmapIds);
        }

        // 反向索引（收藏夹导入依赖 MD5）也要能用。
        MusicTrack probe = library.Tracks.First(static track => track.BeatmapHashes.Count > 0);
        Assert.NotNull(result.Library.FindByBeatmapHash(probe.BeatmapHashes[0]));
        Assert.NotNull(result.Library.FindById(probe.Id));
    }

    [Fact]
    public void 缓存应保留音频时长()
    {
        using TempDirectory temp = new("osump-cache");
        string databasePath = Path.Combine(fixture.Sample.OsuDirectory, DatabaseFileName);

        Touch(databasePath);

        LibraryCache cache = new(temp.Combine("library.bin"));
        MusicLibrary library = new MusicLibraryBuilder().Build(fixture.Sample.OsuDirectory);
        MusicTrack track = library.Tracks[0];
        track.Duration = TimeSpan.FromSeconds(123.456);

        Assert.True(cache.Save(fixture.Sample.OsuDirectory, library));

        LibraryCacheLoadResult result = cache.TryLoad(fixture.Sample.OsuDirectory);
        MusicTrack reloaded = result.Library!.FindById(track.Id)!;

        Assert.NotNull(reloaded.Duration);
        Assert.Equal(123.456, reloaded.Duration!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void osu数据库变化时缓存应标记为过期但仍可读取()
    {
        using TempDirectory temp = new("osump-cache");
        string databasePath = Path.Combine(fixture.Sample.OsuDirectory, DatabaseFileName);

        Touch(databasePath);

        LibraryCache cache = new(temp.Combine("library.bin"));
        MusicLibrary library = new MusicLibraryBuilder().Build(fixture.Sample.OsuDirectory);

        Assert.True(cache.Save(fixture.Sample.OsuDirectory, library));

        // 模拟 osu! 更新了谱面库（长度与时间都变了）。
        using (FileStream stream = new(databasePath, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            stream.Position = stream.Length;
            stream.WriteByte(0);
        }

        LibraryCacheLoadResult result = cache.TryLoad(fixture.Sample.OsuDirectory);

        try
        {
            Assert.Equal(LibraryCacheFreshness.Outdated, result.Freshness);
            Assert.NotNull(result.Library);
            Assert.Equal(library.Tracks.Count, result.Library!.Tracks.Count);
        }
        finally
        {
            // 把示例数据库还原，避免影响共享夹具里的其它测试。
            RestoreSampleDatabase(databasePath);
        }
    }

    [Fact]
    public void 换了一个osu目录时缓存应不可用()
    {
        using TempDirectory temp = new("osump-cache");
        LibraryCache cache = new(temp.Combine("library.bin"));

        Assert.True(cache.Save(fixture.Sample.OsuDirectory, new MusicLibraryBuilder().Build(fixture.Sample.OsuDirectory)));

        LibraryCacheLoadResult result = cache.TryLoad(Path.Combine(temp.Path, "另一个osu目录"));

        Assert.Equal(LibraryCacheFreshness.None, result.Freshness);
        Assert.Null(result.Library);
    }

    [Fact]
    public void 没有缓存时应返回未缓存()
    {
        using TempDirectory temp = new("osump-cache");
        LibraryCache cache = new(temp.Combine("library.bin"));

        LibraryCacheLoadResult result = cache.TryLoad(fixture.Sample.OsuDirectory);

        Assert.Equal(LibraryCacheFreshness.None, result.Freshness);
        Assert.Null(result.Library);
        Assert.False(result.HasLibrary);
    }

    [Fact]
    public void 缓存数据损坏时应回退为重新扫描()
    {
        using TempDirectory temp = new("osump-cache");
        string cachePath = temp.Combine("library.bin");
        string databasePath = Path.Combine(fixture.Sample.OsuDirectory, DatabaseFileName);

        Touch(databasePath);

        LibraryCache cache = new(cachePath);

        Assert.True(cache.Save(fixture.Sample.OsuDirectory, new MusicLibraryBuilder().Build(fixture.Sample.OsuDirectory)));

        // 截断曲目数据文件，模拟写到一半就退出。
        string payloadPath = Path.ChangeExtension(cachePath, ".bin");
        using (FileStream stream = new(payloadPath, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(16);
        }

        LibraryCacheLoadResult result = cache.TryLoad(fixture.Sample.OsuDirectory);

        Assert.Equal(LibraryCacheFreshness.None, result.Freshness);
        Assert.Null(result.Library);
    }

    [Fact]
    public void 缓存格式版本变化时应失效()
    {
        using TempDirectory temp = new("osump-cache");
        string cachePath = temp.Combine("library.bin");
        string databasePath = Path.Combine(fixture.Sample.OsuDirectory, DatabaseFileName);

        Touch(databasePath);

        LibraryCache cache = new(cachePath);

        Assert.True(cache.Save(fixture.Sample.OsuDirectory, new MusicLibraryBuilder().Build(fixture.Sample.OsuDirectory)));

        string headerPath = Path.ChangeExtension(cachePath, ".json");
        JsonNode header = JsonNode.Parse(File.ReadAllText(headerPath))!;
        header["Version"] = LibraryCache.CurrentVersion + 1;
        File.WriteAllText(headerPath, header.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        LibraryCacheLoadResult result = cache.TryLoad(fixture.Sample.OsuDirectory);

        Assert.Equal(LibraryCacheFreshness.None, result.Freshness);
        Assert.Null(result.Library);
    }

    [Fact]
    public void 删除缓存后应回到未缓存状态()
    {
        using TempDirectory temp = new("osump-cache");
        string cachePath = temp.Combine("library.bin");
        string databasePath = Path.Combine(fixture.Sample.OsuDirectory, DatabaseFileName);

        Touch(databasePath);

        LibraryCache cache = new(cachePath);

        Assert.True(cache.Save(fixture.Sample.OsuDirectory, new MusicLibraryBuilder().Build(fixture.Sample.OsuDirectory)));
        cache.Clear();

        Assert.False(File.Exists(cachePath));
        Assert.False(File.Exists(Path.ChangeExtension(cachePath, ".json")));
        Assert.Equal(LibraryCacheFreshness.None, cache.TryLoad(fixture.Sample.OsuDirectory).Freshness);
    }

    [Theory]
    [InlineData(@"F:\osu!", @"F:\osu!\")]
    [InlineData(@"F:\osu!", @"f:\OSU!")]
    public void 目录比较应忽略大小写与结尾分隔符(string left, string right)
        => Assert.True(LibraryCache.SameDirectory(left, right));

    [Fact]
    public void 目录比较应区分不同目录()
        => Assert.False(LibraryCache.SameDirectory(@"F:\osu!", @"F:\osu!lazer"));

    private static void Touch(string path)
    {
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-1));
    }

    private static void RestoreSampleDatabase(string databasePath)
    {
        using FileStream stream = new(databasePath, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.SetLength(stream.Length - 1);
        Touch(databasePath);
    }
}
