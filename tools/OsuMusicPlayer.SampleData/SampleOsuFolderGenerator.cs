namespace OsuMusicPlayer.SampleData;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OsuMusicPlayer.Core.Enums;
using OsuMusicPlayer.Core.IO;
using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Services;

/// <summary>示例 osu! 文件夹的生成选项。</summary>
public sealed record SampleOsuFolderOptions
{
    /// <summary>生成目录（会被当作 osu! 安装目录）。</summary>
    public required string TargetDirectory { get; init; }

    /// <summary>目录已存在时是否先清空。</summary>
    public bool Overwrite { get; init; } = true;

    /// <summary>是否生成谱面背景图。</summary>
    public bool IncludeBackgrounds { get; init; } = true;

    /// <summary>生成的音频时长（秒）。</summary>
    public double AudioSeconds { get; init; } = 4.0;
}

/// <summary>生成结果摘要。</summary>
public sealed record SampleOsuFolderResult(
    string OsuDirectory,
    string SongsDirectory,
    int BeatmapCount,
    int ExpectedTrackCount,
    int ExpectedPlayableTrackCount,
    IReadOnlyList<string> CollectionNames);

/// <summary>
/// 生成一个“假的” osu! stable 安装目录，用于演示、手工试用与自动化测试：
/// <c>osu!.db</c>、<c>osu!sample.cfg</c>、<c>collection.db</c>、<c>Songs\*.wav</c>、<c>*.osu</c> 与背景图。
/// </summary>
public static class SampleOsuFolderGenerator
{
    public static SampleOsuFolderResult Generate(SampleOsuFolderOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TargetDirectory);

        string osuDirectory = Path.GetFullPath(options.TargetDirectory);

        if (Directory.Exists(osuDirectory))
        {
            if (!options.Overwrite)
            {
                throw new InvalidOperationException($"目录已存在：{osuDirectory}");
            }

            Directory.Delete(osuDirectory, recursive: true);
        }

        string songsDirectory = Path.Combine(osuDirectory, "Songs");
        Directory.CreateDirectory(songsDirectory);

        List<OsuBeatmap> beatmaps = [];
        HashSet<string> expectedTracks = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> expectedPlayable = new(StringComparer.OrdinalIgnoreCase);
        int folderCount = 0;

        foreach (SampleSet set in SampleSets)
        {
            string folderName = Sanitize($"{set.MapSetId} {set.Artist} - {set.Title}");
            string folderPath = Path.Combine(songsDirectory, folderName);

            Directory.CreateDirectory(folderPath);
            folderCount++;

            if (!set.MissingAudio)
            {
                WriteAudio(folderPath, set.AudioFileName, set.Frequency, options.AudioSeconds);

                if (set.SecondAudioFileName is not null)
                {
                    WriteAudio(folderPath, set.SecondAudioFileName, set.Frequency * 1.5, options.AudioSeconds);
                }
            }

            if (options.IncludeBackgrounds)
            {
                BmpWriter.WriteGradient(Path.Combine(folderPath, "background.bmp"), 320, 180, set.BackgroundRed, set.BackgroundGreen, set.BackgroundBlue);
            }

            for (int i = 0; i < set.Difficulties.Count; i++)
            {
                SampleDifficulty difficulty = set.Difficulties[i];
                string audioFileName = set.SecondAudioFileName is not null && i % 2 == 1
                    ? set.SecondAudioFileName
                    : set.AudioFileName;

                int mapId = (set.MapSetId * 10) + i + 1;
                string osuFileName = Sanitize($"{set.Artist} - {set.Title} ({difficulty.Name}).osu");

                WriteOsuFile(
                    Path.Combine(folderPath, osuFileName),
                    audioFileName,
                    set,
                    difficulty,
                    mapId,
                    options.IncludeBackgrounds);

                OsuBeatmap beatmap = CreateBeatmap(set, difficulty, folderName, osuFileName, audioFileName, mapId);
                beatmaps.Add(beatmap);

                string trackKey = MusicLibraryBuilder.BuildTrackKey(beatmap, audioFileName);
                expectedTracks.Add(trackKey);

                if (!set.MissingAudio)
                {
                    expectedPlayable.Add(trackKey);
                }
            }
        }

        // 额外放一张“没有音频文件名”的谱面，用来验证解析时会跳过它。
        beatmaps.Add(new OsuBeatmap
        {
            ArtistRoman = "Sample",
            TitleRoman = "Broken Entry",
            ArtistUnicode = "Sample",
            TitleUnicode = "Broken Entry",
            Creator = "sample",
            DiffName = "No Audio",
            AudioFileName = string.Empty,
            OsuFileName = "broken.osu",
            Md5 = Md5("broken"),
            Dir = Sanitize($"{SampleSets[0].MapSetId} {SampleSets[0].Artist} - {SampleSets[0].Title}"),
            MapId = 999999,
            MapSetId = 1099,
            TotalTime = 1000,
            PreviewTime = -1,
            State = 1,
        });

        OsuDatabaseData database = new(
            FileDate: StableOsuDatabaseReader.LatestOsuDbVersion,
            FolderCount: folderCount,
            AccountUnlocked: true,
            UnlockDate: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Username: "sample",
            Beatmaps: beatmaps,
            Permissions: 0);

        StableOsuDatabaseWriter.WriteDatabase(database, Path.Combine(osuDirectory, OsuPathLocator.DatabaseFileName));

        File.WriteAllText(
            Path.Combine(osuDirectory, "osu!sample.cfg"),
            "BeatmapDirectory = Songs" + Environment.NewLine + "VolumeUniversal = 80" + Environment.NewLine,
            Encoding.UTF8);

        List<OsuCollectionData> collections = BuildCollections(beatmaps);

        new OsuCollectionReader().Write(collections, Path.Combine(osuDirectory, "collection.db"));

        return new SampleOsuFolderResult(
            osuDirectory,
            songsDirectory,
            beatmaps.Count,
            expectedTracks.Count,
            expectedPlayable.Count,
            [.. collections.Select(static collection => collection.Name)]);
    }

    /// <summary>示例收藏夹：包含正常、含缺失谱面与被跳过的空收藏夹。</summary>
    private static List<OsuCollectionData> BuildCollections(IReadOnlyList<OsuBeatmap> beatmaps)
    {
        List<OsuBeatmap> Set(int mapSetId) => [.. beatmaps.Where(beatmap => beatmap.MapSetId == mapSetId && beatmap.Md5.Length > 0)];

        return
        [
            new OsuCollectionData("我的最爱", [.. Set(1001).Select(static b => b.Md5), .. Set(1002).Select(static b => b.Md5)]),
            new OsuCollectionData("练习曲", [.. Set(1003).Select(static b => b.Md5), .. Set(1004).Select(static b => b.Md5)]),
            new OsuCollectionData("含缺失谱面", [Md5("missing-beatmap"), .. Set(1005).Select(static b => b.Md5)]),
            new OsuCollectionData("空收藏夹", []),
            new OsuCollectionData("双音频谱面集", [.. Set(1006).Select(static b => b.Md5)]),
        ];
    }

    private static OsuBeatmap CreateBeatmap(
        SampleSet set,
        SampleDifficulty difficulty,
        string folderName,
        string osuFileName,
        string audioFileName,
        int mapId)
    {
        OsuBeatmap beatmap = new()
        {
            ArtistRoman = set.Artist,
            ArtistUnicode = set.ArtistUnicode,
            TitleRoman = set.Title,
            TitleUnicode = set.TitleUnicode,
            Creator = set.Creator,
            DiffName = difficulty.Name,
            AudioFileName = audioFileName,
            OsuFileName = osuFileName,
            Md5 = Md5($"{set.MapSetId}:{difficulty.Name}"),
            Dir = folderName,
            MapId = mapId,
            MapSetId = set.MapSetId,
            ThreadId = 0,
            State = set.State,
            PlayMode = difficulty.Mode,
            Tags = set.Tags,
            Source = set.Source,
            ApproachRate = (float)difficulty.ApproachRate,
            CircleSize = 4,
            HpDrainRate = 5,
            OverallDifficulty = (float)(difficulty.ApproachRate - 1),
            SliderVelocity = 1.6,
            Circles = 300,
            Sliders = 80,
            Spinners = 2,
            DrainingTime = difficulty.TotalTime,
            TotalTime = difficulty.TotalTime,
            PreviewTime = set.PreviewTime,
            EditDate = new DateTime(2024, 5, 1, 12, 0, 0, DateTimeKind.Utc),
            TimingPoints = [new TimingPoint(300, 0, true), new TimingPoint(300, 1000, true)],
            MinBpm = 200,
            MaxBpm = 200,
            MainBpm = 200,
            LastModification = 0,
        };

        beatmap.ModStars[(int)difficulty.Mode].Add(0, (float)difficulty.Stars);

        return beatmap;
    }

    private static void WriteOsuFile(
        string path,
        string audioFileName,
        SampleSet set,
        SampleDifficulty difficulty,
        int mapId,
        bool includeBackground)
    {
        StringBuilder builder = new();

        builder.AppendLine("osu file format v14");
        builder.AppendLine();
        builder.AppendLine("[General]");
        builder.AppendLine($"AudioFilename: {audioFileName}");
        builder.AppendLine("AudioLeadIn: 0");
        builder.AppendLine($"PreviewTime: {set.PreviewTime}");
        builder.AppendLine("Countdown: 0");
        builder.AppendLine("SampleSet: Soft");
        builder.AppendLine("Mode: " + (int)difficulty.Mode);
        builder.AppendLine();
        builder.AppendLine("[Metadata]");
        builder.AppendLine($"Title:{set.Title}");
        builder.AppendLine($"TitleUnicode:{set.TitleUnicode}");
        builder.AppendLine($"Artist:{set.Artist}");
        builder.AppendLine($"ArtistUnicode:{set.ArtistUnicode}");
        builder.AppendLine($"Creator:{set.Creator}");
        builder.AppendLine($"Version:{difficulty.Name}");
        builder.AppendLine($"Source:{set.Source}");
        builder.AppendLine($"Tags:{set.Tags}");
        builder.AppendLine($"BeatmapID:{mapId}");
        builder.AppendLine($"BeatmapSetID:{set.MapSetId}");
        builder.AppendLine();
        builder.AppendLine("[Difficulty]");
        builder.AppendLine("HPDrainRate:5");
        builder.AppendLine("CircleSize:4");
        builder.AppendLine("OverallDifficulty:" + (difficulty.ApproachRate - 1).ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("ApproachRate:" + difficulty.ApproachRate.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("SliderMultiplier:1.6");
        builder.AppendLine("SliderTickRate:1");
        builder.AppendLine();
        builder.AppendLine("[Events]");
        builder.AppendLine("//Background and Video events");

        if (includeBackground)
        {
            builder.AppendLine("0,0,\"background.bmp\",0,0");
        }

        builder.AppendLine("//Break Periods");
        builder.AppendLine("//Storyboard Layer 0 (Background)");
        builder.AppendLine();
        builder.AppendLine("[TimingPoints]");
        builder.AppendLine("0,300,4,2,1,80,1,0");
        builder.AppendLine();
        builder.AppendLine("[HitObjects]");
        builder.AppendLine("256,192,1000,1,0,0:0:0:0:");

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private static void WriteAudio(string folderPath, string fileName, double frequency, double seconds)
        => WavWriter.WriteSine(
            Path.Combine(folderPath, fileName),
            frequency,
            TimeSpan.FromSeconds(seconds));

    private static string Md5(string value)
        => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Sanitize(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder builder = new(name.Length);

        foreach (char c in name)
        {
            builder.Append(invalid.Contains(c) ? '_' : c);
        }

        return builder.ToString();
    }

    private sealed record SampleDifficulty(
        string Name,
        double Stars,
        PlayMode Mode = PlayMode.Osu,
        double ApproachRate = 8,
        int TotalTime = 195000);

    private sealed record SampleSet(
        int MapSetId,
        string Artist,
        string Title,
        string ArtistUnicode,
        string TitleUnicode,
        string Creator,
        string Tags,
        string Source,
        string AudioFileName,
        double Frequency,
        int PreviewTime,
        List<SampleDifficulty> Difficulties,
        byte State = 4,
        bool MissingAudio = false,
        string? SecondAudioFileName = null)
    {
        public byte BackgroundRed { get; init; } = 80;
        public byte BackgroundGreen { get; init; } = 120;
        public byte BackgroundBlue { get; init; } = 200;
    }

    private static readonly List<SampleSet> SampleSets =
    [
        new SampleSet(
            1001, "Camellia", "Exit This Earth's Atomosphere", "Camellia", "Exit This Earth's Atomosphere",
            "Sotarks", "electronic drumstep featured artist", "osu!",
            "audio.wav", 220, 42000,
            [
                new SampleDifficulty("Easy", 2.1, ApproachRate: 5),
                new SampleDifficulty("Hard", 4.6),
                new SampleDifficulty("Insane", 5.9),
                new SampleDifficulty("Extra", 6.8, ApproachRate: 9.4),
            ])
        {
            BackgroundRed = 200,
            BackgroundGreen = 60,
            BackgroundBlue = 90,
        },

        new SampleSet(
            1002, "xi", "FREEDOM DiVE", "xi", "FREEDOM DiVE",
            "Nakagawa-Kanon", "gothic dramatic techno", "BMS",
            "audio.wav", 261, 51000,
            [
                new SampleDifficulty("BEGINNER", 3.4, ApproachRate: 6),
                new SampleDifficulty("FOUR DIMENSIONS", 7.2, ApproachRate: 9.6),
                new SampleDifficulty("FOUR DIMENSIONS+", 7.9, ApproachRate: 10),
            ])
        {
            BackgroundRed = 90,
            BackgroundGreen = 40,
            BackgroundBlue = 160,
        },

        new SampleSet(
            1003, "幽閉サテライト", "色は匂へど散りぬるを", "幽閉サテライト", "色は匂へど散りぬるを",
            "Yukey", "touhou vocal senya", "東方Project",
            "audio.wav", 330, 63000,
            [
                new SampleDifficulty("Normal", 3.1, ApproachRate: 6.5),
                new SampleDifficulty("Lunatic", 5.2),
            ])
        {
            BackgroundRed = 220,
            BackgroundGreen = 190,
            BackgroundBlue = 70,
        },

        new SampleSet(
            1004, "Yooh", "Ancient Arcadia", "Yooh", "Ancient Arcadia",
            "Mirash", "artcore orchestral", "SDVX",
            "audio.wav", 174, 38000,
            [
                new SampleDifficulty("Novice", 2.4, ApproachRate: 5),
                new SampleDifficulty("Advanced", 4.1),
                new SampleDifficulty("Exhaust", 5.5),
                new SampleDifficulty("Max", 6.3),
                new SampleDifficulty("Max+", 6.7, ApproachRate: 9.2),
            ])
        {
            BackgroundRed = 60,
            BackgroundGreen = 150,
            BackgroundBlue = 110,
        },

        new SampleSet(
            1005, "DragonForce", "Through the Fire and Flames", "DragonForce", "Through the Fire and Flames",
            "Lesjuh", "power metal guitar hero", "Guitar Hero",
            "audio.wav", 392, 90000,
            [
                new SampleDifficulty("Hard", 6.0),
            ],
            State: 5)
        {
            BackgroundRed = 210,
            BackgroundGreen = 110,
            BackgroundBlue = 40,
        },

        // 同一谱面集里两个难度使用不同音频：应当生成两条曲目。
        new SampleSet(
            1006, "Kobaryo", "Bookmaker (2D Version)", "Kobaryo", "Bookmaker (2D Version)",
            "Frenzy", "speedcore", "osu!",
            "audio-a.wav", 466, 30000,
            [
                new SampleDifficulty("Normal", 3.8),
                new SampleDifficulty("Another", 6.4),
            ],
            SecondAudioFileName: "audio-b.wav")
        {
            BackgroundRed = 140,
            BackgroundGreen = 80,
            BackgroundBlue = 200,
        },

        // 音频文件缺失：应当被标记为不可播放。
        new SampleSet(
            1007, "Silentroom", "Nhelv", "Silentroom", "Nhelv",
            "Petal", "neurofunk", "osu!",
            "audio.wav", 300, 45000,
            [
                new SampleDifficulty("Hard", 5.1),
                new SampleDifficulty("Ultra", 6.6),
            ],
            State: 7,
            MissingAudio: true)
        {
            BackgroundRed = 40,
            BackgroundGreen = 170,
            BackgroundBlue = 170,
        },
    ];
}
