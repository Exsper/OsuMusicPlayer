namespace OsuMusicPlayer.Core.Tests;

using OsuMusicPlayer.Core.Enums;
using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Services;
using OsuMusicPlayer.SampleData;
using Xunit;

/// <summary>音乐库构建与“同一谱面集合并为一首”的核心行为。</summary>
[Collection("sample")]
public sealed class MusicLibraryBuilderTests(SampleLibraryFixture fixture)
{
    [Fact]
    public void 曲目总数应与预期一致()
    {
        MusicLibrary library = fixture.Library;

        Assert.Equal(20, library.Statistics.BeatmapCount);
        Assert.Equal(fixture.Sample.ExpectedTrackCount, library.Tracks.Count);
        Assert.Equal(fixture.Sample.ExpectedPlayableTrackCount, library.Statistics.TracksWithAudio);
        Assert.Equal(1, library.Statistics.TracksWithoutAudio);
        Assert.Equal(1, library.Statistics.SkippedBeatmaps);
        Assert.Single(library.FindByMapSetId(1005));
    }

    [Fact]
    public void 同一谱面集的多个难度应合并为一首曲目()
    {
        MusicTrack track = fixture.TrackOfMapSet(1001);

        Assert.Single(fixture.Library.FindByMapSetId(1001));
        Assert.Equal(4, track.BeatmapCount);
        Assert.Equal(4, track.Difficulties.Count);
        Assert.Equal("audio.wav", track.AudioFileName);
        Assert.True(track.AudioFileExists);
        Assert.False(track.AudioNameFromFallback);
        Assert.Equal("Exit This Earth's Atomosphere", track.Title);
        Assert.Equal("Camellia", track.Artist);
        Assert.Equal("Sotarks", track.Creator);
        Assert.Equal(1001, track.MapSetId);
        Assert.Equal(42000, track.PreviewTime);
        Assert.Equal(PlayMode.Osu, track.PlayMode);
        Assert.Equal(6.8, track.StarsNomod, 0.001);
        Assert.Contains("electronic", track.Tags, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("s1001|audio.wav", track.Id);
    }

    [Fact]
    public void 同一谱面集内不同音频应各自成为一条曲目()
    {
        IReadOnlyList<MusicTrack> tracks = fixture.Library.FindByMapSetId(1006);

        Assert.Equal(2, tracks.Count);
        Assert.Equal(["audio-a.wav", "audio-b.wav"], tracks.Select(static t => t.AudioFileName).Order().ToArray());
        Assert.All(tracks, static track => Assert.Equal(1, track.BeatmapCount));
        Assert.All(tracks, static track => Assert.True(track.AudioFileExists));
    }

    [Fact]
    public void 音频缺失的曲目应被标记为不可播放()
    {
        MusicTrack track = fixture.TrackOfMapSet(1007);

        Assert.False(track.AudioFileExists);
        Assert.True(File.Exists(track.AudioFilePath) == false);
        Assert.Equal(2, track.BeatmapCount);
        Assert.Equal("Loved", track.StateText);
    }

    [Fact]
    public void 应按难度MD5与曲目ID反查曲目()
    {
        MusicTrack track = fixture.TrackOfMapSet(1002);

        Assert.Same(track, fixture.Library.FindById(track.Id));
        Assert.Null(fixture.Library.FindById("s9999|nope.mp3"));

        foreach (string hash in track.BeatmapHashes)
        {
            Assert.Same(track, fixture.Library.FindByBeatmapHash(hash));
        }

        Assert.Null(fixture.Library.FindByBeatmapHash("00000000000000000000000000000000"));
        Assert.Empty(fixture.Library.FindByMapSetId(424242));
    }

    [Fact]
    public void 中文与日文元数据应正确解析()
    {
        MusicTrack track = fixture.TrackOfMapSet(1003);

        Assert.Equal("幽閉サテライト", track.Artist);
        Assert.Equal("色は匂へど散りぬるを", track.Title);
        Assert.Contains("色は匂へど散りぬるを", track.SearchIndex, StringComparison.Ordinal);
    }

    [Fact]
    public void 列表应按艺术家排序()
    {
        IReadOnlyList<MusicTrack> tracks = fixture.Library.Tracks;

        for (int i = 1; i < tracks.Count; i++)
        {
            int comparison = string.Compare(
                tracks[i - 1].DisplayArtist,
                tracks[i].DisplayArtist,
                StringComparison.OrdinalIgnoreCase);

            Assert.True(comparison <= 0, $"第 {i} 项排序不正确：{tracks[i - 1].DisplayArtist} 在 {tracks[i].DisplayArtist} 之前");
        }
    }

    [Fact]
    public void 扫描过程应报告进度()
    {
        CollectingProgress progress = new();

        new MusicLibraryBuilder().Build(fixture.Sample.OsuDirectory, progress);

        Assert.NotEmpty(progress.Reports);
        Assert.Contains(progress.Reports, static report => report.Percent == 100);
        Assert.Contains(progress.Reports, static report => report.Stage == "读取数据库");
    }

    private sealed class CollectingProgress : IProgress<LibraryBuildProgress>
    {
        public List<LibraryBuildProgress> Reports { get; } = [];

        public void Report(LibraryBuildProgress value) => Reports.Add(value);
    }

    [Fact]
    public void 音频文件名失效时应回退到文件夹中的实际音频()
    {
        using TempDirectory temp = new();
        SampleOsuFolderResult sample = SampleOsuFolderGenerator.Generate(new SampleOsuFolderOptions
        {
            TargetDirectory = temp.Combine("osu"),
            AudioSeconds = 0.5,
        });

        MusicLibrary before = new MusicLibraryBuilder().Build(sample.OsuDirectory);
        MusicTrack track = before.FindByMapSetId(1001)[0];
        string folder = Path.Combine(sample.SongsDirectory, track.Directory);

        File.Delete(Path.Combine(folder, "audio.wav"));
        WavWriter.WriteSine(Path.Combine(folder, "renamed-audio.wav"), 440, TimeSpan.FromSeconds(0.5));

        MusicLibrary after = new MusicLibraryBuilder().Build(sample.OsuDirectory);
        MusicTrack rebuilt = after.FindByMapSetId(1001)[0];

        Assert.True(rebuilt.AudioFileExists);
        Assert.True(rebuilt.AudioNameFromFallback);
        Assert.Equal("renamed-audio.wav", rebuilt.AudioFileName);
        Assert.True(File.Exists(rebuilt.AudioFilePath));
    }

    [Fact]
    public void 应遵循配置文件中的自定义谱面目录()
    {
        using TempDirectory temp = new();
        string osuDirectory = temp.Combine("osu");

        SampleOsuFolderGenerator.Generate(new SampleOsuFolderOptions
        {
            TargetDirectory = osuDirectory,
            AudioSeconds = 0.5,
        });

        string custom = Path.Combine(osuDirectory, "MySongs");
        Directory.Move(Path.Combine(osuDirectory, "Songs"), custom);
        File.WriteAllText(Path.Combine(osuDirectory, "osu!sample.cfg"), "BeatmapDirectory = MySongs" + Environment.NewLine);

        MusicLibrary library = new MusicLibraryBuilder().Build(osuDirectory);

        Assert.Equal(custom, library.SongsDirectory);
        Assert.Equal(fixture.Sample.ExpectedPlayableTrackCount, library.Statistics.TracksWithAudio);
    }
}
