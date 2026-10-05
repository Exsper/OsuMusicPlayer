namespace OsuMusicPlayer.Core.Models;

using OsuMusicPlayer.Core.Enums;

/// <summary>
/// 音乐库中的一首“曲目”。
/// 同一个谱面集（mapset）里所有使用<em>同一个音频文件</em>的难度会被合并成一条曲目，
/// 因此列表里一首歌只会出现一次，而不是每个难度一行。
/// </summary>
public sealed class MusicTrack
{
    private string? _searchIndex;

    /// <summary>稳定标识：<c>s{MapSetId}|{音频文件名}</c>（MapSetId 无效时用文件夹名）。</summary>
    public string Id { get; init; } = string.Empty;

    // ---- 展示信息 ----
    public string Title { get; init; } = string.Empty;
    public string TitleUnicode { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public string ArtistUnicode { get; init; } = string.Empty;
    public string Creator { get; init; } = string.Empty;
    public string Tags { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;

    // ---- 谱面集信息 ----
    public int MapSetId { get; init; }
    public int PrimaryMapId { get; init; }
    public byte State { get; init; }
    public PlayMode PlayMode { get; init; }
    public double StarsNomod { get; init; }
    public double MainBpm { get; init; }
    public int PreviewTime { get; init; }
    public int TotalTime { get; init; }
    public int BeatmapCount { get; init; }
    public IReadOnlyList<string> Difficulties { get; init; } = [];
    public IReadOnlyList<int> BeatmapIds { get; init; } = [];
    public IReadOnlyList<string> BeatmapHashes { get; init; } = [];

    // ---- 文件位置 ----
    /// <summary>主音频所在文件夹（osu! 的谱面集文件夹名，可能被合并了多个）。</summary>
    public string Directory { get; init; } = string.Empty;

    /// <summary>参与合并的所有文件夹名。</summary>
    public IReadOnlyList<string> Directories { get; init; } = [];

    public string AudioFileName { get; init; } = string.Empty;
    public string AudioFilePath { get; init; } = string.Empty;
    public bool AudioFileExists { get; init; }

    /// <summary>音频文件名在 osu!.db 中记录错误、由文件夹内实际文件回退得到。</summary>
    public bool AudioNameFromFallback { get; init; }

    /// <summary>代表难度的 .osu 文件路径（用于读取背景图等信息）。</summary>
    public string OsuFilePath { get; init; } = string.Empty;

    /// <summary>音频时长，未知时为 null（播放或分析后填充，并由时长缓存持久化）。</summary>
    public TimeSpan? Duration { get; set; }

    // ---- 计算属性 ----
    /// <summary>默认显示写法（罗马化，缺失时回退原文）。需要切换显示写法时请用 <see cref="GetDisplayArtist"/>。</summary>
    public string DisplayArtist => GetDisplayArtist(TrackNameDisplay.Romanized);

    /// <summary>默认显示写法（罗马化，缺失时回退原文）。需要切换显示写法时请用 <see cref="GetDisplayTitle"/>。</summary>
    public string DisplayTitle => GetDisplayTitle(TrackNameDisplay.Romanized);

    public string DisplayName => $"{DisplayArtist} - {DisplayTitle}";

    /// <summary>按指定写法返回艺术家名（该写法为空时回退到另一种写法，避免出现空白）。</summary>
    public string GetDisplayArtist(TrackNameDisplay display)
        => display == TrackNameDisplay.Original
            ? FirstNotEmpty(ArtistUnicode, Artist)
            : FirstNotEmpty(Artist, ArtistUnicode);

    /// <summary>按指定写法返回标题（该写法为空时回退到另一种写法，避免出现空白）。</summary>
    public string GetDisplayTitle(TrackNameDisplay display)
        => display == TrackNameDisplay.Original
            ? FirstNotEmpty(TitleUnicode, Title)
            : FirstNotEmpty(Title, TitleUnicode);

    /// <summary>按指定写法返回“艺术家 - 标题”。</summary>
    public string GetDisplayName(TrackNameDisplay display)
        => $"{GetDisplayArtist(display)} - {GetDisplayTitle(display)}";

    private static string FirstNotEmpty(string preferred, string fallback)
        => !string.IsNullOrWhiteSpace(preferred) ? preferred : fallback ?? string.Empty;


    public string MapSetUrl => MapSetId > 0 ? $"https://osu.ppy.sh/s/{MapSetId}" : string.Empty;

    public string StateText => State switch
    {
        0 => "未更新",
        1 => "未提交",
        2 => "待定",
        3 => "未知",
        4 => "Ranked",
        5 => "Approved",
        6 => "Qualified",
        7 => "Loved",
        _ => "未知",
    };

    public string ModeText => PlayMode switch
    {
        PlayMode.Osu => "std",
        PlayMode.Taiko => "taiko",
        PlayMode.CatchTheBeat => "ctb",
        PlayMode.OsuMania => "mania",
        _ => "?",
    };

    /// <summary>预生成的小写检索文本（标题/艺术家/谱师/标签/来源/文件夹等）。</summary>
    public string SearchIndex => _searchIndex ??= BuildSearchIndex();

    private string BuildSearchIndex()
    {
        var parts = new List<string>(16)
        {
            Title,
            TitleUnicode,
            Artist,
            ArtistUnicode,
            Creator,
            Tags,
            Source,
            Directory,
            AudioFileName,
            DisplayName,
            MapSetId > 0 ? MapSetId.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty,
        };

        parts.AddRange(Difficulties);

        return string.Join('\n', parts.Where(static p => !string.IsNullOrWhiteSpace(p))).ToLowerInvariant();
    }

    public override string ToString() => DisplayName;
}
