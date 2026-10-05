namespace OsuMusicPlayer.Core.Models;

using OsuMusicPlayer.Core.Enums;

/// <summary>
/// osu! stable <c>osu!.db</c> 中的一张谱面（一个难度）。
/// 字段布局参考 CollectionManager（MIT）的 Beatmap/StableOsuDatabaseReader。
/// </summary>
public sealed class OsuBeatmap
{
    // ---- 元数据 ----
    public string ArtistRoman { get; set; } = string.Empty;
    public string ArtistUnicode { get; set; } = string.Empty;
    public string TitleRoman { get; set; } = string.Empty;
    public string TitleUnicode { get; set; } = string.Empty;
    public string Creator { get; set; } = string.Empty;
    public string DiffName { get; set; } = string.Empty;
    public string AudioFileName { get; set; } = string.Empty;
    public string Md5 { get; set; } = string.Empty;
    public string OsuFileName { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string LetterBox { get; set; } = string.Empty;
    public string Dir { get; set; } = string.Empty;

    // ---- 状态与难度 ----
    public byte State { get; set; }
    public short Circles { get; set; }
    public short Sliders { get; set; }
    public short Spinners { get; set; }
    public DateTime EditDate { get; set; }
    public float ApproachRate { get; set; }
    public float CircleSize { get; set; }
    public float HpDrainRate { get; set; }
    public float OverallDifficulty { get; set; }
    public double SliderVelocity { get; set; }

    /// <summary>四种模式的星数，索引为 <see cref="PlayMode"/>。</summary>
    public StarRating[] ModStars { get; } = [new(), new(), new(), new()];

    // ---- 时间 ----
    public int DrainingTime { get; set; }
    public int TotalTime { get; set; }

    /// <summary>试听起始时间（毫秒），负值表示未设置。</summary>
    public int PreviewTime { get; set; }

    public TimingPoint[]? TimingPoints { get; set; }
    public double MinBpm { get; set; }
    public double MaxBpm { get; set; }
    public double MainBpm { get; set; }

    // ---- 标识 ----
    public int MapId { get; set; }
    public int MapSetId { get; set; }
    public int ThreadId { get; set; }

    // ---- 成绩 ----
    public OsuGrade OsuGrade { get; set; } = OsuGrade.Null;
    public OsuGrade TaikoGrade { get; set; } = OsuGrade.Null;
    public OsuGrade CatchGrade { get; set; } = OsuGrade.Null;
    public OsuGrade ManiaGrade { get; set; } = OsuGrade.Null;

    // ---- 其余本地状态 ----
    public short Offset { get; set; }
    public float StackLeniency { get; set; }
    public PlayMode PlayMode { get; set; }
    public short AudioOffset { get; set; }
    public bool Played { get; set; }
    public DateTime LastPlayed { get; set; }
    public bool IsOsz2 { get; set; }
    public DateTime LastSync { get; set; }
    public bool DisableHitsounds { get; set; }
    public bool DisableSkin { get; set; }
    public bool DisableSb { get; set; }
    public bool DisableVideo { get; set; }
    public bool VisualOverride { get; set; }
    public int LastModification { get; set; }
    public byte ManiaScrollSpeed { get; set; }

    /// <summary>优先使用罗马字（拉丁）标题。</summary>
    public string Artist => !string.IsNullOrWhiteSpace(ArtistRoman) ? ArtistRoman : ArtistUnicode;

    /// <summary>优先使用罗马字（拉丁）标题。</summary>
    public string Title => !string.IsNullOrWhiteSpace(TitleRoman) ? TitleRoman : TitleUnicode;

    public string MapSetUrl => MapSetId > 0 ? $"https://osu.ppy.sh/s/{MapSetId}" : string.Empty;

    public float GetStars(PlayMode mode, int mods = 0) => ModStars[(int)mode][mods];

    public double StarsNomod => GetStars(PlayMode);

    public override string ToString() => $"{Artist} - {Title} [{DiffName}]";
}
