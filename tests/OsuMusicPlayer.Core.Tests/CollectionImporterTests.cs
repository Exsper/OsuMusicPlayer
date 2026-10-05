namespace OsuMusicPlayer.Core.Tests;

using OsuMusicPlayer.Core.IO;
using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Services;
using Xunit;

/// <summary>把 osu! 收藏夹导入为播放列表。</summary>
[Collection("sample")]
public sealed class CollectionImporterTests(SampleLibraryFixture fixture)
{
    private static IReadOnlyList<OsuCollectionData> ReadCollections(SampleLibraryFixture fixture)
        => new OsuCollectionReader().Read(Path.Combine(fixture.Sample.OsuDirectory, "collection.db"));

    private static (PlaylistStore Store, CollectionImporter Importer) CreateImporter(string? sourceFile = null)
    {
        PlaylistStore store = new();
        store.Load();

        return (store, new CollectionImporter(store));
    }

    [Fact]
    public void 示例收藏夹应能正确导入为播放列表()
    {
        (PlaylistStore store, CollectionImporter importer) = CreateImporter();

        CollectionImportResult result = importer.Import(ReadCollections(fixture), fixture.Library);

        Assert.Equal(5, result.CollectionsRead);
        Assert.Equal(4, result.PlaylistsCreated);
        Assert.Equal(1, result.SkippedEmpty);
        Assert.Equal(0, result.PlaylistsUpdated);
        Assert.Equal(7, result.TracksAdded);

        // 我的最爱：1001(4 难度) + 1002(3 难度) -> 2 首曲目
        Playlist favorites = store.FindByName("我的最爱")!;
        Assert.Equal(2, favorites.TrackIds.Count);
        Assert.True(favorites.IsImported);
        Assert.Contains(fixture.TrackOfMapSet(1001).Id, favorites.TrackIds);
        Assert.Contains(fixture.TrackOfMapSet(1002).Id, favorites.TrackIds);

        // 练习曲：1003 + 1004 -> 2 首
        Assert.Equal(2, store.FindByName("练习曲")!.TrackIds.Count);

        // 含缺失谱面：只有 1005 能匹配上，另有 1 张谱面本地没有
        CollectionImportDetail missing = result.Details.Single(static detail => detail.CollectionName == "含缺失谱面");
        Assert.Equal(1, missing.MissingBeatmaps);
        Assert.Equal(1, missing.TrackCount);
        Assert.Equal(1, missing.MatchedBeatmaps);

        // 双音频谱面集：同一谱面集的两首不同音频都要导入
        Assert.Equal(2, store.FindByName("双音频谱面集")!.TrackIds.Count);

        // 空收藏夹被跳过，不创建播放列表
        Assert.Null(store.FindByName("空收藏夹"));
        Assert.Equal(PlaylistKind.Collection, store.FindByName("我的最爱")!.Kind);
    }

    [Fact]
    public void 重复导入应合并而不是重复创建()
    {
        (PlaylistStore store, CollectionImporter importer) = CreateImporter();
        IReadOnlyList<OsuCollectionData> collections = ReadCollections(fixture);

        importer.Import(collections, fixture.Library);
        int countAfterFirst = store.Playlists.Count;

        CollectionImportResult second = importer.Import(collections, fixture.Library);

        Assert.Equal(countAfterFirst, store.Playlists.Count);
        Assert.Equal(0, second.PlaylistsCreated);
        Assert.Equal(4, second.PlaylistsUpdated);
        Assert.Equal(0, second.TracksAdded);
    }

    [Fact]
    public void 重复导入时可以选择不合并()
    {
        (PlaylistStore store, CollectionImporter importer) = CreateImporter();

        importer.Import(ReadCollections(fixture), fixture.Library);
        int before = store.Playlists.Count;

        CollectionImportResult result = importer.Import(
            ReadCollections(fixture),
            fixture.Library,
            new CollectionImportOptions { MergeIntoExisting = false });

        Assert.Equal(4, result.PlaylistsCreated);
        Assert.Equal(before + 4, store.Playlists.Count);
    }

    [Fact]
    public void 可以保留空收藏夹并可加名称后缀()
    {
        (PlaylistStore store, CollectionImporter importer) = CreateImporter();

        CollectionImportResult result = importer.Import(
            ReadCollections(fixture),
            fixture.Library,
            new CollectionImportOptions
            {
                SkipEmptyCollections = false,
                NameSuffix = "（收藏夹）",
                SourceFile = "collection.db",
            });

        Assert.Equal(5, result.PlaylistsCreated);
        Assert.Equal(0, result.SkippedEmpty);
        Assert.NotNull(store.FindByName("空收藏夹（收藏夹）"));
        Assert.Equal("collection.db", store.FindByName("我的最爱（收藏夹）")!.SourceFile);
    }

    [Fact]
    public void 同名用户自建列表不应被导入覆盖()
    {
        (PlaylistStore store, CollectionImporter importer) = CreateImporter();

        Playlist mine = store.Create("我的最爱");
        store.AddTracks(mine.Id, ["s9999|mine.mp3"]);

        CollectionImportResult result = importer.Import(ReadCollections(fixture), fixture.Library);

        // 自建列表保持原样，导入结果另建同名(1)列表
        Assert.Equal(4, result.PlaylistsCreated);
        Assert.Equal(["s9999|mine.mp3"], store.Get(mine.Id)!.TrackIds);
        Assert.Contains(store.Playlists, static playlist => playlist.Name == "我的最爱 (1)" && playlist.IsImported);
    }

    [Fact]
    public void 导入结果应统计曲目与缺失数量()
    {
        (_, CollectionImporter importer) = CreateImporter();

        CollectionImportResult result = importer.Import(ReadCollections(fixture), fixture.Library);

        Assert.Equal(7, result.ImportedTracks);
        Assert.Equal(7, result.TracksAdded);
        Assert.Equal(1, result.MissingBeatmaps);
        Assert.All(result.Details, static detail => Assert.True(detail.BeatmapCount >= detail.MatchedBeatmaps));
    }
}
