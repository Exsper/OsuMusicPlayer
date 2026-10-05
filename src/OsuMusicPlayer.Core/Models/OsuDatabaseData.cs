namespace OsuMusicPlayer.Core.Models;

/// <summary>osu!.db 的完整内容（表头 + 所有谱面）。</summary>
public sealed record OsuDatabaseData(
    int FileDate,
    int FolderCount,
    bool AccountUnlocked,
    DateTime UnlockDate,
    string Username,
    List<OsuBeatmap> Beatmaps,
    int Permissions)
{
    /// <summary>写入数据库时的谱面数量。</summary>
    public int NumberOfBeatmaps => Beatmaps.Count;

    public static OsuDatabaseData Empty { get; } = new(0, 0, false, DateTime.MinValue, string.Empty, [], -1);
}
