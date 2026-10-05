namespace OsuMusicPlayer.Core.Tests;

using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Services;
using Xunit;

/// <summary>自定义播放列表的增删改查与持久化。</summary>
[Collection("sample")]
public sealed class PlaylistStoreTests(SampleLibraryFixture fixture)
{
    private static MusicTrack Track(string id) => new() { Id = id, Title = id, Artist = "test" };

    [Fact]
    public void 新建的存储应自带收藏夹()
    {
        PlaylistStore store = new();

        store.Load();

        Playlist favorites = Assert.Single(store.Playlists);
        Assert.True(favorites.IsFavorites);
        Assert.Equal(PlaylistStore.FavoritesId, favorites.Id);
        Assert.Equal(PlaylistStore.FavoritesName, favorites.Name);
    }

    [Fact]
    public void 收藏夹不可删除()
    {
        PlaylistStore store = new();
        store.Load();

        Assert.False(store.Delete(PlaylistStore.FavoritesId));
        Assert.Single(store.Playlists);
    }

    [Fact]
    public void 创建重命名与删除()
    {
        PlaylistStore store = new();
        store.Load();

        Playlist playlist = store.Create("练习用");
        Assert.Equal(2, store.Playlists.Count);
        Assert.Equal("练习用", store.FindByName("练习用")!.Name);

        Assert.True(store.Rename(playlist.Id, "练习用 2"));
        Assert.Equal("练习用 2", store.Get(playlist.Id)!.Name);

        Assert.True(store.Delete(playlist.Id));
        Assert.Null(store.Get(playlist.Id));
        Assert.Null(store.FindByName("练习用 2"));
    }

    [Fact]
    public void 同名播放列表应自动生成唯一名称()
    {
        PlaylistStore store = new();
        store.Load();

        store.Create("列表");
        string unique = store.CreateUniqueName("列表");

        Assert.Equal("列表 (1)", unique);
        Assert.True(store.NameExists("列表"));
        Assert.False(store.NameExists("列表 (1)"));
    }

    [Fact]
    public void 加入曲目时自动去重()
    {
        PlaylistStore store = new();
        store.Load();
        Playlist playlist = store.Create("测试");

        Assert.Equal(3, store.AddTracks(playlist.Id, ["a", "b", "c"]));
        Assert.Equal(1, store.AddTracks(playlist.Id, ["c", "d"]));
        Assert.Equal(4, store.Get(playlist.Id)!.TrackIds.Count);
        Assert.Equal(0, store.AddTracks("not-exists", ["x"]));
    }

    [Fact]
    public void 支持移除移动与清空()
    {
        PlaylistStore store = new();
        store.Load();
        Playlist playlist = store.Create("测试");
        store.AddTracks(playlist.Id, ["a", "b", "c", "d"]);

        Assert.True(store.MoveTrack(playlist.Id, 0, 2));
        Assert.Equal(["b", "c", "a", "d"], store.Get(playlist.Id)!.TrackIds);

        Assert.False(store.MoveTrack(playlist.Id, 5, 0));
        Assert.True(store.RemoveAt(playlist.Id, 0));
        Assert.Equal(["c", "a", "d"], store.Get(playlist.Id)!.TrackIds);

        Assert.Equal(2, store.RemoveTracks(playlist.Id, ["a", "d"]));
        Assert.Equal(["c"], store.Get(playlist.Id)!.TrackIds);

        Assert.Equal(1, store.ClearTracks(playlist.Id));
        Assert.Empty(store.Get(playlist.Id)!.TrackIds);
    }

    [Fact]
    public void 替换曲目顺序()
    {
        PlaylistStore store = new();
        store.Load();
        Playlist playlist = store.Create("测试");

        store.ReplaceTracks(playlist.Id, ["x", "y", "x", "z"]);

        Assert.Equal(["x", "y", "z"], store.Get(playlist.Id)!.TrackIds);
    }

    [Fact]
    public void 收藏切换()
    {
        PlaylistStore store = new();
        store.Load();

        Assert.False(store.IsFavorite("track-1"));
        Assert.True(store.ToggleFavorite("track-1"));
        Assert.True(store.IsFavorite("track-1"));
        Assert.Contains("track-1", store.Get(PlaylistStore.FavoritesId)!.TrackIds);

        Assert.False(store.ToggleFavorite("track-1"));
        Assert.False(store.IsFavorite("track-1"));
    }

    [Fact]
    public void 保存与读取应保持内容一致()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("playlists.json");

        PlaylistStore store = new(path);
        store.Load();
        store.Create("我的列表");
        Playlist playlist = store.FindByName("我的列表")!;
        store.AddTracks(playlist.Id, ["s1001|audio.wav", "s1002|audio.wav"]);
        store.ToggleFavorite("s1003|audio.wav");
        store.Save();

        PlaylistStore reloaded = new(path);
        reloaded.Load();

        Assert.Equal(2, reloaded.Playlists.Count);
        Assert.Equal(2, reloaded.FindByName("我的列表")!.TrackIds.Count);
        Assert.True(reloaded.IsFavorite("s1003|audio.wav"));
        Assert.Equal(2, reloaded.FindByName("我的列表")!.Count);
    }

    [Fact]
    public void 统计失效条目不修改任何播放列表()
    {
        PlaylistStore store = new();
        store.Load();

        MusicTrack track = fixture.Library.Tracks[0];
        Playlist playlist = store.Create("测试");
        store.AddTracks(playlist.Id, [track.Id, "s9999|ghost.mp3", "s9998|ghost2.mp3"]);
        store.ToggleFavorite("s9997|ghost3.mp3");

        // 统计只读：换 osu! 目录后不会自动删掉用户内容。
        Assert.Equal(3, store.CountStaleEntries(fixture.Library));
        Assert.Equal(2, store.CountStaleEntries(playlist.Id, fixture.Library));
        Assert.Equal(0, store.CountStaleEntries("not-exists", fixture.Library));
        Assert.Equal(3, store.Get(playlist.Id)!.TrackIds.Count);
        Assert.Single(store.Get(PlaylistStore.FavoritesId)!.TrackIds);

        // 只有显式调用 Prune 才会真正清理。
        Assert.Equal(3, store.Prune(fixture.Library));
        Assert.Equal(0, store.CountStaleEntries(fixture.Library));
        Assert.Equal([track.Id], store.Get(playlist.Id)!.TrackIds);
    }

    [Fact]
    public void 清理应删除音乐库中不存在的曲目()
    {
        PlaylistStore store = new();
        store.Load();

        MusicTrack track = fixture.Library.Tracks[0];
        Playlist playlist = store.Create("测试");
        store.AddTracks(playlist.Id, [track.Id, "s9999|ghost.mp3"]);
        store.ToggleFavorite("s9999|ghost.mp3");

        int pruned = store.Prune(fixture.Library);

        Assert.Equal(2, pruned);
        Assert.Equal([track.Id], store.Get(playlist.Id)!.TrackIds);
        Assert.Empty(store.Get(PlaylistStore.FavoritesId)!.TrackIds);
    }

    [Fact]
    public void 损坏的配置文件应给出明确错误()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("playlists.json");
        File.WriteAllText(path, "{ 这不是合法 JSON");

        PlaylistStore store = new(path);

        Assert.Throws<InvalidDataException>(() => store.Load());
    }

    [Fact]
    public void 播放列表只保存曲目标识()
    {
        Playlist playlist = new() { Name = "x" };
        playlist.TrackIds.Add(Track("t1").Id);
        playlist.TrackIds.Add(Track("t2").Id);

        Assert.Equal(2, playlist.Count);
        Assert.Equal(["t1", "t2"], playlist.Clone().TrackIds);
    }
}
