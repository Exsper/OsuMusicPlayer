namespace OsuMusicPlayer.Core.Models;

/// <summary>一次音乐库扫描的结果统计。</summary>
public sealed record LibraryStatistics(
    int BeatmapCount = 0,
    int TrackCount = 0,
    int TracksWithAudio = 0,
    int TracksWithoutAudio = 0,
    int SkippedBeatmaps = 0,
    TimeSpan ScanDuration = default,
    DateTime ScannedAt = default)
{
    public static LibraryStatistics Empty { get; } = new();
}

/// <summary>
/// 音乐库：一个 osu! stable 安装里所有可播放的曲目（已按谱面集合并）。
/// </summary>
public sealed class MusicLibrary
{
    private Dictionary<string, MusicTrack>? _byId;
    private Dictionary<string, MusicTrack>? _byBeatmapHash;
    private Dictionary<int, List<MusicTrack>>? _byMapSetId;

    public MusicLibrary(string osuDirectory, string songsDirectory, IReadOnlyList<MusicTrack> tracks, LibraryStatistics statistics)
    {
        OsuDirectory = osuDirectory;
        SongsDirectory = songsDirectory;
        Tracks = tracks;
        Statistics = statistics;
    }

    /// <summary>osu! 安装目录（含 osu!.db）。</summary>
    public string OsuDirectory { get; }

    /// <summary>谱面歌曲目录（默认 &lt;osu!&gt;\Songs，可在 osu!.cfg 中自定义）。</summary>
    public string SongsDirectory { get; }

    public IReadOnlyList<MusicTrack> Tracks { get; }

    public LibraryStatistics Statistics { get; }

    public bool IsLoaded => Tracks.Count > 0;

    public static MusicLibrary Empty { get; } = new(string.Empty, string.Empty, [], LibraryStatistics.Empty);

    public MusicTrack? FindById(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        _byId ??= BuildIdIndex();

        return _byId.TryGetValue(id, out MusicTrack? track) ? track : null;
    }

    /// <summary>把谱面难度列表中的索引映射到曲目（用于播放列表 / 收藏夹导入）。</summary>
    public IReadOnlyList<MusicTrack> FindByTrackIds(IEnumerable<string> trackIds)
    {
        List<MusicTrack> result = [];

        foreach (string id in trackIds)
        {
            MusicTrack? track = FindById(id);

            if (track is not null)
            {
                result.Add(track);
            }
        }

        return result;
    }

    /// <summary>按谱面难度 MD5 查找所属曲目（收藏夹导入用）。</summary>
    public MusicTrack? FindByBeatmapHash(string? md5)
    {
        if (string.IsNullOrWhiteSpace(md5))
        {
            return null;
        }

        _byBeatmapHash ??= BuildHashIndex();

        return _byBeatmapHash.TryGetValue(md5, out MusicTrack? track) ? track : null;
    }

    public IReadOnlyList<MusicTrack> FindByMapSetId(int mapSetId)
    {
        _byMapSetId ??= BuildMapSetIndex();

        return _byMapSetId.TryGetValue(mapSetId, out List<MusicTrack>? tracks) ? tracks : [];
    }

    private Dictionary<string, MusicTrack> BuildIdIndex()
    {
        Dictionary<string, MusicTrack> index = new(StringComparer.OrdinalIgnoreCase);

        foreach (MusicTrack track in Tracks)
        {
            index.TryAdd(track.Id, track);
        }

        return index;
    }

    private Dictionary<string, MusicTrack> BuildHashIndex()
    {
        Dictionary<string, MusicTrack> index = new(StringComparer.OrdinalIgnoreCase);

        foreach (MusicTrack track in Tracks)
        {
            foreach (string hash in track.BeatmapHashes)
            {
                if (!string.IsNullOrWhiteSpace(hash))
                {
                    index.TryAdd(hash, track);
                }
            }
        }

        return index;
    }

    private Dictionary<int, List<MusicTrack>> BuildMapSetIndex()
    {
        Dictionary<int, List<MusicTrack>> index = [];

        foreach (MusicTrack track in Tracks)
        {
            if (track.MapSetId <= 0)
            {
                continue;
            }

            if (!index.TryGetValue(track.MapSetId, out List<MusicTrack>? list))
            {
                index[track.MapSetId] = list = [];
            }

            list.Add(track);
        }

        return index;
    }
}

/// <summary>扫描进度。</summary>
public sealed record LibraryBuildProgress(string Stage, string Message, int Percent);
