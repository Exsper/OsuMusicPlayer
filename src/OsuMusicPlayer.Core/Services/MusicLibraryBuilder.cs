namespace OsuMusicPlayer.Core.Services;

using System.Diagnostics;
using OsuMusicPlayer.Core.Enums;
using OsuMusicPlayer.Core.IO;
using OsuMusicPlayer.Core.Models;

/// <summary>
/// 把 osu!.db 中成千上万张谱面整理成“一首歌一条”的音乐库。
/// <para>
/// 合并规则：同一个谱面集（MapSetId）中使用同一个音频文件的难度只会生成一条曲目。
/// 若同一谱面集的不同难度使用不同音频（少见），则各自成为一条曲目。
/// </para>
/// </summary>
public sealed class MusicLibraryBuilder
{
    /// <summary>扫描并构建音乐库。</summary>
    public MusicLibrary Build(
        string osuDirectory,
        IProgress<LibraryBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(osuDirectory))
        {
            throw new ArgumentException("osu! 目录不能为空。", nameof(osuDirectory));
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        string databasePath = Path.Combine(osuDirectory, OsuPathLocator.DatabaseFileName);

        if (!File.Exists(databasePath))
        {
            throw new OsuDatabaseNotFoundException(osuDirectory);
        }

        progress?.Report(new LibraryBuildProgress("定位", "正在定位歌曲目录…", 2));

        string songsDirectory = OsuConfigReader.ResolveSongsDirectory(osuDirectory);

        progress?.Report(new LibraryBuildProgress("读取数据库", "正在读取 osu!.db …", 5));

        OsuDatabaseData database = StableOsuDatabaseReader.ReadDatabase(
            databasePath,
            cancellationToken,
            new StringProgressAdapter(message => progress?.Report(new LibraryBuildProgress("读取数据库", message, 30))));

        return BuildFromDatabase(database, osuDirectory, songsDirectory, progress, stopwatch, cancellationToken);
    }

    /// <summary>由已解析的 osu!.db 内容构建音乐库（测试与自检使用）。</summary>
    public MusicLibrary BuildFromDatabase(
        OsuDatabaseData database,
        string osuDirectory,
        string songsDirectory,
        IProgress<LibraryBuildProgress>? progress = null,
        Stopwatch? stopwatch = null,
        CancellationToken cancellationToken = default)
    {
        stopwatch ??= Stopwatch.StartNew();

        progress?.Report(new LibraryBuildProgress("整理", $"正在合并 {database.Beatmaps.Count} 张谱面…", 60));

        Dictionary<string, TrackAccumulator> accumulators = new(StringComparer.Ordinal);
        int skippedBeatmaps = 0;

        foreach (OsuBeatmap beatmap in database.Beatmaps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string audioFileName = beatmap.AudioFileName?.Trim() ?? string.Empty;

            if (audioFileName.Length == 0)
            {
                // 没有音频文件名的谱面无法播放，直接跳过。
                skippedBeatmaps++;
                continue;
            }

            string key = BuildTrackKey(beatmap, audioFileName);

            if (!accumulators.TryGetValue(key, out TrackAccumulator? accumulator))
            {
                accumulators[key] = accumulator = new TrackAccumulator(key, audioFileName);
            }

            accumulator.Add(beatmap);
        }

        progress?.Report(new LibraryBuildProgress("检查文件", "正在检查音频文件…", 75));

        List<MusicTrack> tracks = new(accumulators.Count);
        int index = 0;

        foreach (TrackAccumulator accumulator in accumulators.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if ((index++ & 0x1F) == 0)
            {
                int percent = 75 + (int)(20.0 * index / Math.Max(1, accumulators.Count));
                progress?.Report(new LibraryBuildProgress("检查文件", $"已处理 {index}/{accumulators.Count} 首曲目…", Math.Min(percent, 96)));
            }

            tracks.Add(BuildTrack(accumulator, songsDirectory));
        }

        tracks.Sort(static (a, b) =>
        {
            int result = string.Compare(a.DisplayArtist, b.DisplayArtist, StringComparison.OrdinalIgnoreCase);

            if (result != 0)
            {
                return result;
            }

            result = string.Compare(a.DisplayTitle, b.DisplayTitle, StringComparison.OrdinalIgnoreCase);

            return result != 0 ? result : string.Compare(a.Directory, b.Directory, StringComparison.OrdinalIgnoreCase);
        });

        int withAudio = tracks.Count(static track => track.AudioFileExists);
        stopwatch.Stop();

        LibraryStatistics statistics = new(
            BeatmapCount: database.Beatmaps.Count,
            TrackCount: tracks.Count,
            TracksWithAudio: withAudio,
            TracksWithoutAudio: tracks.Count - withAudio,
            SkippedBeatmaps: skippedBeatmaps,
            ScanDuration: stopwatch.Elapsed,
            ScannedAt: DateTime.Now);

        progress?.Report(new LibraryBuildProgress("完成", $"共 {tracks.Count} 首曲目（{withAudio} 首可播放）。", 100));

        return new MusicLibrary(osuDirectory, songsDirectory, tracks, statistics);
    }

    /// <summary>曲目标识：优先谱面集 ID，其次文件夹名；再叠加音频文件名。</summary>
    public static string BuildTrackKey(OsuBeatmap beatmap, string audioFileName)
        => BuildTrackKey(beatmap.MapSetId, beatmap.Dir, audioFileName);

    public static string BuildTrackKey(int mapSetId, string? directory, string audioFileName)
    {
        string setKey = mapSetId > 0
            ? "s" + mapSetId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "d" + (directory ?? string.Empty).Trim().ToLowerInvariant();

        return setKey + "|" + audioFileName.Trim().ToLowerInvariant();
    }

    private static MusicTrack BuildTrack(TrackAccumulator accumulator, string songsDirectory)
    {
        IReadOnlyList<OsuBeatmap> beatmaps = accumulator.Beatmaps;

        OsuBeatmap representative = beatmaps.FirstOrDefault(
            static b => !string.IsNullOrWhiteSpace(b.ArtistRoman) && !string.IsNullOrWhiteSpace(b.TitleRoman))
            ?? beatmaps[0];

        List<string> directories = [.. beatmaps
            .Select(static b => b.Dir?.Trim() ?? string.Empty)
            .Where(static d => d.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        (string audioFilePath, string directory, string audioFileName, bool audioExists, bool fromFallback) =
            ResolveAudioFile(directories, accumulator.AudioFileName, songsDirectory);

        List<string> difficulties = [.. beatmaps
            .Select(static b => b.DiffName?.Trim() ?? string.Empty)
            .Where(static d => d.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        difficulties.Sort(StringComparer.OrdinalIgnoreCase);

        List<string> hashes = [.. beatmaps
            .Select(static b => b.Md5?.Trim() ?? string.Empty)
            .Where(static h => h.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        List<int> beatmapIds = [.. beatmaps
            .Select(static b => b.MapId)
            .Where(static id => id > 0)
            .Distinct()
            .Order()];

        List<string> tags = [.. beatmaps
            .SelectMany(static b => (b.Tags ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        PlayMode playMode = beatmaps
            .GroupBy(static b => b.PlayMode)
            .OrderByDescending(static group => group.Count())
            .Select(static group => group.Key)
            .First();

        double stars = beatmaps
            .Select(b => (double)b.GetStars(playMode))
            .Where(static value => value > 0)
            .DefaultIfEmpty(0)
            .Max();

        string? osuFilePath = ResolveOsuFilePath(representative, directories, songsDirectory);

        return new MusicTrack
        {
            Id = accumulator.Key,
            Title = representative.TitleRoman?.Trim() ?? string.Empty,
            TitleUnicode = representative.TitleUnicode?.Trim() ?? string.Empty,
            Artist = representative.ArtistRoman?.Trim() ?? string.Empty,
            ArtistUnicode = representative.ArtistUnicode?.Trim() ?? string.Empty,
            Creator = MostCommonString(beatmaps.Select(static b => b.Creator?.Trim() ?? string.Empty)),
            Tags = string.Join(' ', tags),
            Source = MostCommonString(beatmaps.Select(static b => b.Source?.Trim() ?? string.Empty)),
            MapSetId = MostCommonInt(beatmaps.Select(static b => b.MapSetId).Where(static id => id > 0)),
            PrimaryMapId = beatmapIds.Count > 0 ? beatmapIds[0] : 0,
            State = beatmaps.Max(static b => b.State),
            PlayMode = playMode,
            StarsNomod = stars,
            MainBpm = beatmaps.Select(static b => b.MainBpm).FirstOrDefault(static bpm => bpm > 0),
            PreviewTime = Math.Max(0, beatmaps.Select(static b => b.PreviewTime).FirstOrDefault(static time => time > 0)),
            TotalTime = beatmaps.Max(static b => b.TotalTime),
            BeatmapCount = hashes.Count > 0 ? hashes.Count : beatmaps.Count,
            Difficulties = difficulties,
            BeatmapIds = beatmapIds,
            BeatmapHashes = hashes,
            Directory = directory,
            Directories = directories,
            AudioFileName = audioFileName,
            AudioFilePath = audioFilePath,
            AudioFileExists = audioExists,
            AudioNameFromFallback = fromFallback,
            OsuFilePath = osuFilePath ?? string.Empty,
        };
    }

    private static (string AudioFilePath, string Directory, string AudioFileName, bool Exists, bool FromFallback) ResolveAudioFile(
        IReadOnlyList<string> directories,
        string declaredAudioFileName,
        string songsDirectory)
    {
        foreach (string directory in directories)
        {
            string candidate = Path.Combine(songsDirectory, directory, declaredAudioFileName);

            if (File.Exists(candidate))
            {
                return (candidate, directory, declaredAudioFileName, true, false);
            }
        }

        // osu!.db 中记录的音频名失效时，退回到在文件夹里找第一个可播放的音频。
        foreach (string directory in directories)
        {
            string? found = AudioFileLocator.FindAudioFile(Path.Combine(songsDirectory, directory));

            if (found is not null)
            {
                return (found, directory, Path.GetFileName(found), true, true);
            }
        }

        string fallbackDirectory = directories.Count > 0 ? directories[0] : string.Empty;

        return (Path.Combine(songsDirectory, fallbackDirectory, declaredAudioFileName), fallbackDirectory, declaredAudioFileName, false, false);
    }

    private static string? ResolveOsuFilePath(OsuBeatmap representative, IReadOnlyList<string> directories, string songsDirectory)
    {
        if (!string.IsNullOrWhiteSpace(representative.OsuFileName))
        {
            string candidate = Path.Combine(songsDirectory, representative.Dir?.Trim() ?? string.Empty, representative.OsuFileName.Trim());

            if (File.Exists(candidate))
            {
                return candidate;
            }

            foreach (string directory in directories)
            {
                candidate = Path.Combine(songsDirectory, directory, representative.OsuFileName.Trim());

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return candidate;
        }

        return null;
    }

    private static string MostCommonString(IEnumerable<string> values)
    {
        string result = string.Empty;
        int best = 0;

        foreach (IGrouping<string, string> group in values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(static value => value, StringComparer.OrdinalIgnoreCase))
        {
            int count = group.Count();

            if (count > best)
            {
                best = count;
                result = group.Key;
            }
        }

        return result;
    }

    private static int MostCommonInt(IEnumerable<int> values)
    {
        int result = 0;
        int best = 0;

        foreach (IGrouping<int, int> group in values.GroupBy(static value => value))
        {
            int count = group.Count();

            if (count > best)
            {
                best = count;
                result = group.Key;
            }
        }

        return result;
    }

    private sealed class TrackAccumulator(string key, string audioFileName)
    {
        private readonly List<OsuBeatmap> _beatmaps = [];

        public string Key { get; } = key;

        public string AudioFileName { get; } = audioFileName;

        public IReadOnlyList<OsuBeatmap> Beatmaps => _beatmaps;

        public void Add(OsuBeatmap beatmap) => _beatmaps.Add(beatmap);
    }

    private sealed class StringProgressAdapter(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
