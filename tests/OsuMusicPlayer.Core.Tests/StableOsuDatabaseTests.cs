namespace OsuMusicPlayer.Core.Tests;

using OsuMusicPlayer.Core;
using OsuMusicPlayer.Core.Enums;
using OsuMusicPlayer.Core.IO;
using OsuMusicPlayer.Core.Models;
using Xunit;

/// <summary>osu!.db 的写入/读取往返与异常处理。</summary>
public sealed class StableOsuDatabaseTests
{
    [Fact]
    public void 写入再读取应保留全部关键字段()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("osu!.db");

        OsuBeatmap beatmap = new()
        {
            ArtistRoman = "Yooh",
            ArtistUnicode = "幽閉サテライト",
            TitleRoman = "Ancient Arcadia",
            TitleUnicode = "色は匂へど散りぬるを",
            Creator = "Mirash",
            DiffName = "Max+",
            AudioFileName = "audio.mp3",
            OsuFileName = "Yooh - Ancient Arcadia (Mirash) [Max+].osu",
            Md5 = "0123456789abcdef0123456789abcdef",
            Dir = "1004 Yooh - Ancient Arcadia",
            State = 4,
            Circles = 321,
            Sliders = 45,
            Spinners = 2,
            EditDate = new DateTime(2024, 5, 1, 12, 0, 0, DateTimeKind.Utc),
            ApproachRate = 9.2f,
            CircleSize = 4f,
            HpDrainRate = 5f,
            OverallDifficulty = 8.3f,
            SliderVelocity = 1.8,
            DrainingTime = 180000,
            TotalTime = 195000,
            PreviewTime = 41000,
            TimingPoints = [new TimingPoint(300, 0, true), new TimingPoint(150, 5000, false)],
            MapId = 1234567,
            MapSetId = 1004,
            ThreadId = 999,
            PlayMode = PlayMode.OsuMania,
            Source = "SDVX",
            Tags = "artcore orchestral",
            Played = true,
            LastPlayed = new DateTime(2024, 6, 1, 8, 30, 0, DateTimeKind.Utc),
            LastSync = new DateTime(2024, 6, 2, 8, 30, 0, DateTimeKind.Utc),
            DisableVideo = true,
            ManiaScrollSpeed = 24,
        };

        beatmap.ModStars[(int)PlayMode.Osu].Add(0, 6.7f);
        beatmap.ModStars[(int)PlayMode.Osu].Add(64, 7.4f);
        beatmap.ModStars[(int)PlayMode.OsuMania].Add(0, 5.5f);

        OsuDatabaseData database = new(
            StableOsuDatabaseReader.LatestOsuDbVersion,
            FolderCount: 1,
            AccountUnlocked: true,
            UnlockDate: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Username: "tester",
            Beatmaps: [beatmap],
            Permissions: 0);

        StableOsuDatabaseWriter.WriteDatabase(database, path);

        OsuDatabaseData read = StableOsuDatabaseReader.ReadDatabase(path);

        Assert.Equal(1, read.NumberOfBeatmaps);
        Assert.Equal("tester", read.Username);
        Assert.True(read.AccountUnlocked);
        Assert.Equal(1, read.FolderCount);
        Assert.Equal(0, read.Permissions);

        OsuBeatmap actual = read.Beatmaps[0];

        Assert.Equal(beatmap.ArtistRoman, actual.ArtistRoman);
        Assert.Equal(beatmap.ArtistUnicode, actual.ArtistUnicode);
        Assert.Equal(beatmap.TitleUnicode, actual.TitleUnicode);
        Assert.Equal(beatmap.Creator, actual.Creator);
        Assert.Equal(beatmap.DiffName, actual.DiffName);
        Assert.Equal(beatmap.AudioFileName, actual.AudioFileName);
        Assert.Equal(beatmap.OsuFileName, actual.OsuFileName);
        Assert.Equal(beatmap.Md5, actual.Md5);
        Assert.Equal(beatmap.Dir, actual.Dir);
        Assert.Equal(beatmap.State, actual.State);
        Assert.Equal(beatmap.PreviewTime, actual.PreviewTime);
        Assert.Equal(beatmap.MapId, actual.MapId);
        Assert.Equal(beatmap.MapSetId, actual.MapSetId);
        Assert.Equal(PlayMode.OsuMania, actual.PlayMode);
        Assert.Equal(beatmap.Tags, actual.Tags);
        Assert.Equal(6.7f, actual.GetStars(PlayMode.Osu));
        Assert.Equal(7.4f, actual.GetStars(PlayMode.Osu, 64));
        Assert.Equal(5.5f, actual.GetStars(PlayMode.OsuMania));
        Assert.Equal(2, actual.TimingPoints!.Length);
        Assert.Equal(200, actual.MainBpm, 0.001);
        Assert.Equal(200, actual.MinBpm, 0.001);
        Assert.Equal(200, actual.MaxBpm, 0.001);
    }

    [Fact]
    public void 空数据库也能正常往返()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("osu!.db");

        OsuDatabaseData database = new(
            StableOsuDatabaseReader.LatestOsuDbVersion, 0, false, DateTime.MinValue, "empty", [], -1);

        StableOsuDatabaseWriter.WriteDatabase(database, path);

        OsuDatabaseData read = StableOsuDatabaseReader.ReadDatabase(path);

        Assert.Empty(read.Beatmaps);
    }

    [Fact]
    public void 版本过旧时应给出明确错误()
    {
        using MemoryStream stream = new();

        using (OsuBinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(20180101);
            writer.Write(0);
            writer.Write(false);
            writer.Write(0L);
            writer.Write("old");
            writer.Write(0);
            writer.Write(-1);
        }

        stream.Position = 0;

        InvalidOsuDatabaseException exception = Assert.Throws<InvalidOsuDatabaseException>(
            () => StableOsuDatabaseReader.ReadDatabase(stream));

        Assert.Contains("版本过旧", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 读取不存在的文件应抛出FileNotFound()
    {
        Assert.Throws<FileNotFoundException>(
            () => StableOsuDatabaseReader.ReadDatabase(Path.Combine(Path.GetTempPath(), "definitely-missing-osu.db")));
    }

    [Fact]
    public void 构建器对非osu目录应给出明确错误()
    {
        using TempDirectory temp = new();

        Assert.Throws<OsuDatabaseNotFoundException>(() => new Core.Services.MusicLibraryBuilder().Build(temp.Path));
    }

    [Fact]
    public void Bpm计算应识别主要Bpm()
    {
        TimingPoint[] timingPoints =
        [
            new TimingPoint(300, 0, true),
            new TimingPoint(300, 30000, true),
            new TimingPoint(150, 60000, true),
        ];

        (double min, double max, double main) = StableOsuDatabaseReader.CalculateBpm(timingPoints, 90000);

        Assert.Equal(200, min, 0.001);
        Assert.Equal(400, max, 0.001);
        Assert.Equal(200, main, 0.001);
    }
}
