namespace OsuMusicPlayer.Core.Services;

using OsuMusicPlayer.Core.Models;

/// <summary>曲目列表可排序的列。</summary>
public enum TrackSortColumn
{
    Title = 0,
    Artist = 1,
    Creator = 2,
    MapSetId = 3,
    BeatmapCount = 4,
    Duration = 5,
    PlayMode = 6,
    Stars = 7,
    Bpm = 8,
    State = 9,
    Directory = 10,
}

/// <summary>曲目排序。</summary>
public static class TrackSorter
{
    public static void Sort(List<MusicTrack> tracks, TrackSortColumn column, bool ascending)
    {
        Comparison<MusicTrack> comparison = BuildComparison(column);

        if (!ascending)
        {
            Comparison<MusicTrack> inner = comparison;
            comparison = (a, b) => inner(b, a);
        }

        tracks.Sort(comparison);
    }

    public static Comparison<MusicTrack> BuildComparison(TrackSortColumn column) => column switch
    {
        TrackSortColumn.Title => static (a, b) => CompareText(a.DisplayTitle, b.DisplayTitle),
        TrackSortColumn.Artist => static (a, b) => CompareText(a.DisplayArtist, b.DisplayArtist),
        TrackSortColumn.Creator => static (a, b) => CompareText(a.Creator, b.Creator),
        TrackSortColumn.MapSetId => static (a, b) => a.MapSetId.CompareTo(b.MapSetId),
        TrackSortColumn.BeatmapCount => static (a, b) => a.BeatmapCount.CompareTo(b.BeatmapCount),
        TrackSortColumn.Duration => static (a, b) => CompareDuration(a.Duration, b.Duration),
        TrackSortColumn.PlayMode => static (a, b) => a.PlayMode.CompareTo(b.PlayMode),
        TrackSortColumn.Stars => static (a, b) => a.StarsNomod.CompareTo(b.StarsNomod),
        TrackSortColumn.Bpm => static (a, b) => a.MainBpm.CompareTo(b.MainBpm),
        TrackSortColumn.State => static (a, b) => a.State.CompareTo(b.State),
        TrackSortColumn.Directory => static (a, b) => CompareText(a.Directory, b.Directory),
        _ => static (a, b) => CompareText(a.DisplayTitle, b.DisplayTitle),
    };

    private static int CompareText(string? left, string? right)
        => string.Compare(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static int CompareDuration(TimeSpan? left, TimeSpan? right)
    {
        // 未知时长统一排在后面（升序时）。
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return 1;
        }

        if (right is null)
        {
            return -1;
        }

        return left.Value.CompareTo(right.Value);
    }
}
