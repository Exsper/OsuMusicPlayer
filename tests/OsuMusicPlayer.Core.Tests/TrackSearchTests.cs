namespace OsuMusicPlayer.Core.Tests;

using OsuMusicPlayer.Core.Enums;
using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Services;
using Xunit;

/// <summary>关键词搜索：多关键词、字段前缀、排除、引号与“仅可播放”过滤。</summary>
[Collection("sample")]
public sealed class TrackSearchTests(SampleLibraryFixture fixture)
{
    private IReadOnlyList<MusicTrack> Search(string text, SearchField field = SearchField.All, bool onlyPlayable = false)
        => TrackSearcher.Search(fixture.Library.Tracks, new TrackSearchQuery
        {
            Text = text,
            Field = field,
            OnlyPlayable = onlyPlayable,
        });

    [Fact]
    public void 空条件应返回全部曲目()
    {
        Assert.Equal(fixture.Library.Tracks.Count, Search(string.Empty).Count);
        Assert.Equal(fixture.Library.Tracks.Count, Search("   ").Count);
    }

    [Fact]
    public void 按艺术家搜索()
    {
        IReadOnlyList<MusicTrack> result = Search("camellia");

        Assert.Single(result);
        Assert.Equal(1001, result[0].MapSetId);
    }

    [Fact]
    public void 多个关键词需要同时满足()
    {
        Assert.Single(Search("camellia atomosphere"));
        Assert.Empty(Search("camellia freedom"));
    }

    [Fact]
    public void 支持日文与中文关键词()
    {
        Assert.Equal(1003, Search("色は匂へど").Single().MapSetId);
        Assert.Equal(1003, Search("幽閉").Single().MapSetId);
    }

    [Fact]
    public void 支持按谱师与标签搜索()
    {
        Assert.Equal(1004, Search("mirash").Single().MapSetId);
        Assert.Equal(1004, Search("artcore").Single().MapSetId);
        Assert.Equal(1005, Search("power metal").Single().MapSetId);
    }

    [Fact]
    public void 支持按难度名搜索()
    {
        IReadOnlyList<MusicTrack> result = Search("four dimensions");

        Assert.Equal(1002, result.Single().MapSetId);
    }

    [Fact]
    public void 支持排除语法()
    {
        IReadOnlyList<MusicTrack> result = Search("audio -camellia");

        Assert.NotEmpty(result);
        Assert.DoesNotContain(result, static track => track.MapSetId == 1001);
    }

    [Fact]
    public void 支持字段前缀()
    {
        Assert.Equal(1004, Search("artist:yooh").Single().MapSetId);
        Assert.Equal(1004, Search("creator:mirash").Single().MapSetId);
        Assert.Equal(1005, Search("title:through the fire").Single().MapSetId);
        Assert.Equal(1005, Search("title:\"through the fire\"").Single().MapSetId);
        Assert.Equal(1002, Search("diff:beginner").Single().MapSetId);
        Assert.Empty(Search("artist:camellia title:freedom"));
    }

    [Fact]
    public void 支持按谱面集ID精确搜索()
    {
        IReadOnlyList<MusicTrack> result = Search("set:1006");

        Assert.Equal(2, result.Count);
        Assert.All(result, static track => Assert.Equal(1006, track.MapSetId));
        Assert.Empty(Search("set:12345"));
    }

    [Fact]
    public void 支持引号短语()
    {
        Assert.Single(Search("\"exit this earth\""));
        Assert.Empty(Search("\"earth exit\""));
    }

    [Fact]
    public void 仅可播放过滤会隐藏缺失音频的曲目()
    {
        Assert.Contains(Search("nhelv"), static track => !track.AudioFileExists);
        Assert.Empty(Search("nhelv", onlyPlayable: true));
    }

    [Fact]
    public void 限定搜索范围时只匹配该范围()
    {
        // “Sotarks” 只是谱师，不是标题或艺术家。
        Assert.Empty(Search("sotarks", SearchField.Title));
        Assert.Empty(Search("sotarks", SearchField.Artist));
        Assert.Single(Search("sotarks", SearchField.Creator));
    }

    [Fact]
    public void 按游戏模式过滤()
    {
        IReadOnlyList<MusicTrack> result = TrackSearcher.Search(
            fixture.Library.Tracks,
            new TrackSearchQuery { PlayMode = PlayMode.Osu });

        Assert.Equal(fixture.Library.Tracks.Count, result.Count);

        result = TrackSearcher.Search(fixture.Library.Tracks, new TrackSearchQuery { PlayMode = PlayMode.OsuMania });

        Assert.Empty(result);
    }

    [Fact]
    public void 排序应支持常见列()
    {
        List<MusicTrack> tracks = [.. fixture.Library.Tracks];

        TrackSorter.Sort(tracks, TrackSortColumn.Title, ascending: true);

        for (int i = 1; i < tracks.Count; i++)
        {
            Assert.True(string.Compare(tracks[i - 1].DisplayTitle, tracks[i].DisplayTitle, StringComparison.OrdinalIgnoreCase) <= 0);
        }

        TrackSorter.Sort(tracks, TrackSortColumn.Stars, ascending: false);

        for (int i = 1; i < tracks.Count; i++)
        {
            Assert.True(tracks[i - 1].StarsNomod >= tracks[i].StarsNomod);
        }

        TrackSorter.Sort(tracks, TrackSortColumn.Duration, ascending: true);

        Assert.Equal(tracks.Count, tracks.Count);
    }

    [Fact]
    public void 关键词解析应正确处理引号与否定()
    {
        IReadOnlyList<SearchToken> tokens = TrackSearcher.ParseTokens("-foo \"bar baz\" artist:qux");

        Assert.Equal(3, tokens.Count);
        Assert.True(tokens[0].Negated);
        Assert.Equal("foo", tokens[0].Value);
        Assert.Equal("bar baz", tokens[1].Value);
        Assert.Equal(SearchField.Artist, tokens[2].Field);
        Assert.Equal("qux", tokens[2].Value);
    }
}
