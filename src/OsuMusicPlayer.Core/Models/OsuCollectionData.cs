namespace OsuMusicPlayer.Core.Models;

/// <summary>一条 osu! 收藏夹（collection.db 中的一个条目）。</summary>
public sealed record OsuCollectionData(string Name, IReadOnlyList<string> BeatmapHashes)
{
    public int Count => BeatmapHashes.Count;

    public override string ToString() => $"{Name} ({Count})";
}
