namespace OsuMusicPlayer.Core.IO;

using OsuMusicPlayer.Core.Enums;
using OsuMusicPlayer.Core.Models;

/// <summary>
/// 读取 osu! stable 的 <c>osu!.db</c>。
/// 字段布局参考 CollectionManager（MIT）的 StableOsuDatabaseReader。
/// </summary>
public static class StableOsuDatabaseReader
{
    /// <summary>osu! stable 当前写出的数据库版本号（2019-11-05）。</summary>
    public const int LatestOsuDbVersion = 20191105;

    public static OsuDatabaseData ReadDatabase(
        string filePath,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("找不到指定的 osu!.db 文件。", filePath);
        }

        using FileStream fileStream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        return ReadDatabase(fileStream, cancellationToken, progress);
    }

    public static OsuDatabaseData ReadDatabase(
        Stream inputStream,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        using OsuBinaryReader binaryReader = new(inputStream);

        try
        {
            (int fileDate, int folderCount, bool accountUnlocked, DateTime unlockDate, string username, int beatmapCount) = ReadHeader(binaryReader);

            if (fileDate < LatestOsuDbVersion)
            {
                throw new InvalidOsuDatabaseException(
                    $"osu!.db 版本过旧（{fileDate}），请先启动一次 osu! stable 客户端再试。");
            }

            List<OsuBeatmap> beatmaps = new(beatmapCount > 0 ? beatmapCount : 0);

            for (int i = 0; i < beatmapCount; i++)
            {
                if ((i & 0xFF) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report($"正在读取谱面 {i}/{beatmapCount} …");
                }

                beatmaps.Add(ReadBeatmap(binaryReader));
            }

            int permissions = binaryReader.ReadInt32();
            progress?.Report($"已读取 {beatmaps.Count} 张谱面（{folderCount} 个谱面集）。");

            return new OsuDatabaseData(fileDate, folderCount, accountUnlocked, unlockDate, username, beatmaps, permissions);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidOsuDatabaseException("osu!.db 内容不完整或已损坏。", ex);
        }
    }

    private static (int FileDate, int FolderCount, bool AccountUnlocked, DateTime UnlockDate, string Username, int BeatmapCount) ReadHeader(OsuBinaryReader reader)
    {
        int fileDate = reader.ReadInt32();
        int folderCount = reader.ReadInt32();
        bool accountUnlocked = reader.ReadBoolean();
        DateTime unlockDate = ReadRawDateTime(reader);
        string username = reader.ReadString();
        int beatmapCount = reader.ReadInt32();

        if (beatmapCount < 0)
        {
            throw new InvalidOsuDatabaseException($"osu!.db 表头中的谱面数量无效（{beatmapCount}）。");
        }

        return (fileDate, folderCount, accountUnlocked, unlockDate, username, beatmapCount);
    }

    private static DateTime ReadRawDateTime(OsuBinaryReader reader)
    {
        long ticks = reader.ReadInt64();

        return ticks < 0L || ticks > DateTime.MaxValue.Ticks
            ? DateTime.MinValue
            : new DateTime(ticks, DateTimeKind.Utc);
    }

    private static OsuBeatmap ReadBeatmap(OsuBinaryReader reader)
    {
        OsuBeatmap beatmap = new()
        {
            ArtistRoman = reader.ReadString(),
            ArtistUnicode = reader.ReadString(),
            TitleRoman = reader.ReadString(),
            TitleUnicode = reader.ReadString(),
            Creator = reader.ReadString(),
            DiffName = reader.ReadString(),
            AudioFileName = reader.ReadString(),
            Md5 = reader.ReadString(),
            OsuFileName = reader.ReadString(),
            State = reader.ReadByte(),
            Circles = reader.ReadInt16(),
            Sliders = reader.ReadInt16(),
            Spinners = reader.ReadInt16(),
            EditDate = reader.ReadDateTime(),
            ApproachRate = reader.ReadSingle(),
            CircleSize = reader.ReadSingle(),
            HpDrainRate = reader.ReadSingle(),
            OverallDifficulty = reader.ReadSingle(),
            SliderVelocity = reader.ReadDouble(),
        };

        const int playModeCount = 4;

        for (int playMode = 0; playMode < playModeCount; playMode++)
        {
            beatmap.ModStars[playMode] = ReadStars(reader);
        }

        beatmap.DrainingTime = reader.ReadInt32();
        beatmap.TotalTime = reader.ReadInt32();
        beatmap.PreviewTime = reader.ReadInt32();

        beatmap.TimingPoints = ReadTimingPoints(reader);

        if (beatmap.TimingPoints is not null)
        {
            (beatmap.MinBpm, beatmap.MaxBpm, beatmap.MainBpm) = CalculateBpm(beatmap.TimingPoints, beatmap.TotalTime);
        }

        beatmap.MapId = reader.ReadInt32();
        beatmap.MapSetId = reader.ReadInt32();
        beatmap.ThreadId = reader.ReadInt32();
        beatmap.OsuGrade = (OsuGrade)reader.ReadByte();
        beatmap.TaikoGrade = (OsuGrade)reader.ReadByte();
        beatmap.CatchGrade = (OsuGrade)reader.ReadByte();
        beatmap.ManiaGrade = (OsuGrade)reader.ReadByte();
        beatmap.Offset = reader.ReadInt16();
        beatmap.StackLeniency = reader.ReadSingle();
        beatmap.PlayMode = (PlayMode)reader.ReadByte();
        beatmap.Source = reader.ReadString();
        beatmap.Tags = reader.ReadString();
        beatmap.AudioOffset = reader.ReadInt16();
        beatmap.LetterBox = reader.ReadString();
        beatmap.Played = !reader.ReadBoolean();
        beatmap.LastPlayed = reader.ReadDateTime();
        beatmap.IsOsz2 = reader.ReadBoolean();
        beatmap.Dir = reader.ReadString();
        beatmap.LastSync = reader.ReadDateTime();
        beatmap.DisableHitsounds = reader.ReadBoolean();
        beatmap.DisableSkin = reader.ReadBoolean();
        beatmap.DisableSb = reader.ReadBoolean();
        beatmap.DisableVideo = reader.ReadBoolean();
        beatmap.VisualOverride = reader.ReadBoolean();
        beatmap.LastModification = reader.ReadInt32();
        beatmap.ManiaScrollSpeed = reader.ReadByte();

        return beatmap;
    }

    private static TimingPoint[]? ReadTimingPoints(OsuBinaryReader reader)
    {
        int count = reader.ReadInt32();

        if (count < 0)
        {
            return null;
        }

        TimingPoint[] timingPoints = new TimingPoint[count];

        for (int i = 0; i < count; i++)
        {
            timingPoints[i] = new TimingPoint(reader.ReadDouble(), reader.ReadDouble(), reader.ReadBoolean());
        }

        return timingPoints;
    }

    private static StarRating ReadStars(OsuBinaryReader reader)
    {
        StarRating rating = new();
        int combinations = reader.ReadInt32();

        if (combinations <= 0)
        {
            return rating;
        }

        for (int i = 0; i < combinations; i++)
        {
            int mods = (int)Convert.ChangeType(reader.ReadConditionalValue() ?? 0, typeof(int), System.Globalization.CultureInfo.InvariantCulture);
            float stars = (float)Convert.ChangeType(
                reader.ReadConditionalValue() ?? 0f,
                typeof(float),
                System.Globalization.CultureInfo.InvariantCulture);

            if (!rating.ContainsKey(mods) || rating[mods] < stars)
            {
                rating[mods] = stars;
            }
        }

        return rating;
    }

    /// <summary>根据 timing points 估算最小/最大/主要 BPM（与 osu! 客户端显示口径接近）。</summary>
    public static (double Min, double Max, double Main) CalculateBpm(TimingPoint[] timingPoints, int totalBeatmapTime)
    {
        if (timingPoints.Length == 0)
        {
            return (0, 0, 0);
        }

        double minBpmLength = double.MinValue;
        double maxBpmLength = double.MaxValue;
        double currentBpmLength = 0;
        double lastTime = totalBeatmapTime;
        Dictionary<double, int> bpmTimes = [];

        for (int i = timingPoints.Length - 1; i >= 0; i--)
        {
            TimingPoint timingPoint = timingPoints[i];

            if (timingPoint.InheritsBpm)
            {
                currentBpmLength = timingPoint.BpmDuration;
            }

            if (currentBpmLength == 0 || timingPoint.Offset > lastTime || (!timingPoint.InheritsBpm && i > 0))
            {
                continue;
            }

            minBpmLength = Math.Max(minBpmLength, currentBpmLength);
            maxBpmLength = Math.Min(maxBpmLength, currentBpmLength);

            if (!bpmTimes.TryGetValue(currentBpmLength, out int duration))
            {
                bpmTimes[currentBpmLength] = 0;
            }

            bpmTimes[currentBpmLength] = duration + (int)(lastTime - (i == 0 ? 0 : timingPoint.Offset));
            lastTime = timingPoint.Offset;
        }

        if (minBpmLength == double.MinValue || maxBpmLength == double.MaxValue)
        {
            return (0, 0, 0);
        }

        double maxBpm = Math.Round(60000 / maxBpmLength);
        double minBpm = Math.Round(60000 / minBpmLength);
        double mainBpm = 0;

        if (Math.Abs(maxBpm - minBpm) < double.Epsilon)
        {
            mainBpm = maxBpm;
        }
        else if (bpmTimes.Count != 0)
        {
            double dominant = bpmTimes.Aggregate((a, b) => a.Value > b.Value ? a : b).Key;
            mainBpm = Math.Round(60000 / dominant);
        }

        return (minBpm, maxBpm, mainBpm);
    }
}
