namespace OsuMusicPlayer.Core.Models;

/// <summary>播放列表来源类型。</summary>
public static class PlaylistKind
{
    /// <summary>用户自建播放列表。</summary>
    public const string Custom = "custom";

    /// <summary>内置的“我喜欢的音乐”。</summary>
    public const string Favorites = "favorites";

    /// <summary>由 osu! 收藏夹导入。</summary>
    public const string Collection = "collection";
}

/// <summary>一个播放列表（只保存曲目标识，曲目详情始终来自当前音乐库）。</summary>
public sealed class Playlist
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = PlaylistKind.Custom;

    /// <summary>曲目标识（<see cref="MusicTrack.Id"/>），顺序即播放顺序。</summary>
    public List<string> TrackIds { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>导入来源文件（如 collection.db 路径）。</summary>
    public string? SourceFile { get; set; }

    public bool IsFavorites => string.Equals(Kind, PlaylistKind.Favorites, StringComparison.OrdinalIgnoreCase);

    public bool IsImported => string.Equals(Kind, PlaylistKind.Collection, StringComparison.OrdinalIgnoreCase);

    public int Count => TrackIds.Count;

    public Playlist Clone()
    {
        return new Playlist
        {
            Id = Id,
            Name = Name,
            Kind = Kind,
            TrackIds = [.. TrackIds],
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            SourceFile = SourceFile,
        };
    }

    public override string ToString() => Count > 0 ? $"{Name} ({Count})" : Name;
}
