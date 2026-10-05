namespace OsuMusicPlayer.Core.IO;

using OsuMusicPlayer.Core.Enums;
using OsuMusicPlayer.Core.Models;

/// <summary>
/// 写出 osu! stable 的 <c>osu!.db</c>（主要用于生成示例数据与测试往返）。
/// 参考 CollectionManager（MIT）的 StableOsuDatabaseWriter。
/// </summary>
public static class StableOsuDatabaseWriter
{
    private static readonly int[] _modOrder = [0, 64, 256, 2, 66, 258, 16, 80, 272];

    public static void WriteDatabase(OsuDatabaseData database, string filePath)
    {
        using FileStream fileStream = new(filePath, FileMode.Create, FileAccess.Write);

        WriteDatabase(database, fileStream);
    }

    public static void WriteDatabase(OsuDatabaseData database, Stream outputStream)
    {
        using OsuBinaryWriter writer = new(outputStream);

        writer.Write(database.FileDate);
        writer.Write(database.FolderCount);
        writer.Write(database.AccountUnlocked);
        writer.Write(database.UnlockDate.Ticks);
        writer.Write(database.Username);
        writer.Write(database.Beatmaps.Count);

        foreach (OsuBeatmap beatmap in database.Beatmaps)
        {
            WriteBeatmap(beatmap, writer);
        }

        writer.Write(database.Permissions);
    }

    private static void WriteBeatmap(OsuBeatmap beatmap, OsuBinaryWriter writer)
    {
        writer.Write(beatmap.ArtistRoman);
        writer.Write(beatmap.ArtistUnicode);
        writer.Write(beatmap.TitleRoman);
        writer.Write(beatmap.TitleUnicode);
        writer.Write(beatmap.Creator);
        writer.Write(beatmap.DiffName);
        writer.Write(beatmap.AudioFileName);
        writer.Write(beatmap.Md5);
        writer.Write(beatmap.OsuFileName);
        writer.Write(beatmap.State);
        writer.Write(beatmap.Circles);
        writer.Write(beatmap.Sliders);
        writer.Write(beatmap.Spinners);
        writer.Write(beatmap.EditDate);
        writer.Write(beatmap.ApproachRate);
        writer.Write(beatmap.CircleSize);
        writer.Write(beatmap.HpDrainRate);
        writer.Write(beatmap.OverallDifficulty);
        writer.Write(beatmap.SliderVelocity);

        for (int playMode = 0; playMode < 4; playMode++)
        {
            WriteStars(beatmap.ModStars[playMode], writer);
        }

        writer.Write(beatmap.DrainingTime);
        writer.Write(beatmap.TotalTime);
        writer.Write(beatmap.PreviewTime);

        WriteTimingPoints(beatmap.TimingPoints, writer);

        writer.Write(beatmap.MapId);
        writer.Write(beatmap.MapSetId);
        writer.Write(beatmap.ThreadId);
        writer.Write((byte)beatmap.OsuGrade);
        writer.Write((byte)beatmap.TaikoGrade);
        writer.Write((byte)beatmap.CatchGrade);
        writer.Write((byte)beatmap.ManiaGrade);
        writer.Write(beatmap.Offset);
        writer.Write(beatmap.StackLeniency);
        writer.Write((byte)beatmap.PlayMode);
        writer.Write(beatmap.Source);
        writer.Write(beatmap.Tags);
        writer.Write(beatmap.AudioOffset);
        writer.Write(beatmap.LetterBox);
        writer.Write(!beatmap.Played);
        writer.Write(beatmap.LastPlayed);
        writer.Write(beatmap.IsOsz2);
        writer.Write(beatmap.Dir);
        writer.Write(beatmap.LastSync);
        writer.Write(beatmap.DisableHitsounds);
        writer.Write(beatmap.DisableSkin);
        writer.Write(beatmap.DisableSb);
        writer.Write(beatmap.DisableVideo);
        writer.Write(beatmap.VisualOverride);
        writer.Write(beatmap.LastModification);
        writer.Write(beatmap.ManiaScrollSpeed);
    }

    private static void WriteStars(StarRating starRating, OsuBinaryWriter writer)
    {
        if (starRating.Count == 0)
        {
            writer.Write(0);
            return;
        }

        writer.Write(starRating.Count);

        foreach (int mods in _modOrder)
        {
            if (starRating.ContainsKey(mods))
            {
                WriteStar(writer, mods, starRating[mods]);
            }
        }

        foreach (KeyValuePair<int, float> leftover in starRating.Entries)
        {
            if (!_modOrder.Contains(leftover.Key))
            {
                WriteStar(writer, leftover.Key, leftover.Value);
            }
        }

        static void WriteStar(OsuBinaryWriter writer, int mods, float stars)
        {
            writer.Write((byte)8);   // int
            writer.Write(mods);
            writer.Write((byte)12);  // float
            writer.Write(stars);
        }
    }

    private static void WriteTimingPoints(TimingPoint[]? timingPoints, OsuBinaryWriter writer)
    {
        if (timingPoints is null)
        {
            writer.Write(-1);
            return;
        }

        writer.Write(timingPoints.Length);

        foreach (TimingPoint timingPoint in timingPoints)
        {
            writer.Write(timingPoint.BpmDuration);
            writer.Write(timingPoint.Offset);
            writer.Write(timingPoint.InheritsBpm);
        }
    }
}
