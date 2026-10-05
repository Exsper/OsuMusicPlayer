namespace OsuMusicPlayer.Core.Services;

using OsuMusicPlayer.Core.Models;

/// <summary>把 osu! 收藏夹导入为播放列表的选项。</summary>
public sealed record CollectionImportOptions
{
    /// <summary>收藏夹里所有谱面在本地都找不到时，是否跳过不创建播放列表。</summary>
    public bool SkipEmptyCollections { get; init; } = true;

    /// <summary>已有同名（且同为收藏夹导入）的播放列表时，是更新它还是新建一个。</summary>
    public bool MergeIntoExisting { get; init; } = true;

    /// <summary>播放列表名后缀。</summary>
    public string NameSuffix { get; init; } = string.Empty;

    /// <summary>来源文件路径（记录到播放列表上）。</summary>
    public string? SourceFile { get; init; }
}

/// <summary>单个收藏夹的导入结果。</summary>
public sealed record CollectionImportDetail(
    string CollectionName,
    string PlaylistId,
    int BeatmapCount,
    int MatchedBeatmaps,
    int MissingBeatmaps,
    int TrackCount,
    int AddedTracks,
    bool Created);

/// <summary>导入总结果。</summary>
public sealed record CollectionImportResult(IReadOnlyList<CollectionImportDetail> Details)
{
    public static CollectionImportResult Empty { get; } = new([]);

    public int CollectionsRead => Details.Count;

    public int PlaylistsCreated => Details.Count(static detail => detail.Created);

    public int PlaylistsUpdated => Details.Count(static detail => !detail.Created && detail.PlaylistId.Length > 0);

    public int SkippedEmpty => Details.Count(static detail => detail.PlaylistId.Length == 0);

    public int TracksAdded => Details.Sum(static detail => detail.AddedTracks);

    public int MissingBeatmaps => Details.Sum(static detail => detail.MissingBeatmaps);

    public int ImportedTracks => Details.Sum(static detail => detail.TrackCount);
}

/// <summary>
/// 把 osu! 收藏夹（collection.db）转成播放列表：
/// 收藏夹保存的是谱面难度 MD5，这里映射回“曲目”，因此同一首歌的多个难度只会出现一次。
/// </summary>
public sealed class CollectionImporter(PlaylistStore store)
{
    public PlaylistStore Store { get; } = store;

    public CollectionImportResult Import(
        IEnumerable<OsuCollectionData> collections,
        MusicLibrary library,
        CollectionImportOptions? options = null)
    {
        options ??= new CollectionImportOptions();

        List<CollectionImportDetail> details = [];

        foreach (OsuCollectionData collection in collections)
        {
            string name = (collection.Name + options.NameSuffix).Trim();

            if (name.Length == 0)
            {
                name = "收藏夹";
            }

            List<string> trackIds = [];
            HashSet<string> seenTracks = new(StringComparer.OrdinalIgnoreCase);
            int matchedBeatmaps = 0;

            foreach (string hash in collection.BeatmapHashes)
            {
                MusicTrack? track = library.FindByBeatmapHash(hash);

                if (track is null)
                {
                    continue;
                }

                matchedBeatmaps++;

                if (seenTracks.Add(track.Id))
                {
                    trackIds.Add(track.Id);
                }
            }

            int missing = collection.BeatmapHashes.Count - matchedBeatmaps;

            if (trackIds.Count == 0 && options.SkipEmptyCollections)
            {
                details.Add(new CollectionImportDetail(name, string.Empty, collection.Count, matchedBeatmaps, missing, 0, 0, false));
                continue;
            }

            Playlist? existing = options.MergeIntoExisting ? FindImportTarget(name, options) : null;
            bool created;
            Playlist playlist;

            if (existing is not null)
            {
                playlist = existing;
                created = false;
            }
            else
            {
                playlist = Store.Create(
                    Store.NameExists(name) ? Store.CreateUniqueName(name) : name,
                    PlaylistKind.Collection,
                    sourceFile: options.SourceFile);

                created = true;
            }

            int added = Store.AddTracks(playlist.Id, trackIds);

            if (created && !string.IsNullOrEmpty(options.SourceFile))
            {
                playlist.SourceFile = options.SourceFile;
            }

            details.Add(new CollectionImportDetail(name, playlist.Id, collection.Count, matchedBeatmaps, missing, trackIds.Count, added, created));
        }

        return new CollectionImportResult(details);
    }

    private Playlist? FindImportTarget(string name, CollectionImportOptions options)
    {
        Playlist? playlist = Store.FindByName(name);

        // 只合并“同为收藏夹导入”的播放列表，避免覆盖用户自建的列表。
        if (playlist is null || !playlist.IsImported || playlist.IsFavorites)
        {
            return null;
        }

        if (string.IsNullOrEmpty(playlist.SourceFile) || string.IsNullOrEmpty(options.SourceFile))
        {
            return playlist;
        }

        return string.Equals(playlist.SourceFile, options.SourceFile, StringComparison.OrdinalIgnoreCase) ? playlist : null;
    }
}
