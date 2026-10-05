namespace OsuMusicPlayer.Core.Tests;

using OsuMusicPlayer.Core.Enums;
using OsuMusicPlayer.Core.Models;
using Xunit;

/// <summary>标题 / 艺术家的“原文 ↔ 罗马化”切换。</summary>
[Collection("sample")]
public sealed class TrackNameDisplayTests(SampleLibraryFixture fixture)
{
    private static MusicTrack CreateTrack(string artist, string artistUnicode, string title, string titleUnicode) => new()
    {
        Id = "test|a.mp3",
        Artist = artist,
        ArtistUnicode = artistUnicode,
        Title = title,
        TitleUnicode = titleUnicode,
    };

    [Fact]
    public void 两种写法都存在时应各取所需()
    {
        MusicTrack track = CreateTrack("Yuuhei Satellite", "幽閉サテライト", "Iro wa Nioedo Chirinuru wo", "色は匂へど散りぬるを");

        Assert.Equal("Yuuhei Satellite", track.GetDisplayArtist(TrackNameDisplay.Romanized));
        Assert.Equal("Iro wa Nioedo Chirinuru wo", track.GetDisplayTitle(TrackNameDisplay.Romanized));

        Assert.Equal("幽閉サテライト", track.GetDisplayArtist(TrackNameDisplay.Original));
        Assert.Equal("色は匂へど散りぬるを", track.GetDisplayTitle(TrackNameDisplay.Original));

        Assert.Equal("Yuuhei Satellite - Iro wa Nioedo Chirinuru wo", track.GetDisplayName(TrackNameDisplay.Romanized));
        Assert.Equal("幽閉サテライト - 色は匂へど散りぬるを", track.GetDisplayName(TrackNameDisplay.Original));
    }

    [Fact]
    public void 缺少原文时应回退到罗马化()
    {
        MusicTrack track = CreateTrack("Camellia", string.Empty, "Ghost", string.Empty);

        Assert.Equal("Camellia", track.GetDisplayArtist(TrackNameDisplay.Original));
        Assert.Equal("Ghost", track.GetDisplayTitle(TrackNameDisplay.Original));
    }

    [Fact]
    public void 缺少罗马化时应回退到原文()
    {
        MusicTrack track = CreateTrack(string.Empty, "幽閉サテライト", string.Empty, "色は匂へど散りぬるを");

        Assert.Equal("幽閉サテライト", track.GetDisplayArtist(TrackNameDisplay.Romanized));
        Assert.Equal("色は匂へど散りぬるを", track.GetDisplayTitle(TrackNameDisplay.Romanized));
    }

    [Fact]
    public void 两种写法都为空时不应抛异常()
    {
        MusicTrack track = CreateTrack(string.Empty, string.Empty, string.Empty, string.Empty);

        Assert.Equal(string.Empty, track.GetDisplayArtist(TrackNameDisplay.Original));
        Assert.Equal(string.Empty, track.GetDisplayTitle(TrackNameDisplay.Original));
        Assert.Equal(" - ", track.GetDisplayName(TrackNameDisplay.Romanized));
    }

    [Fact]
    public void 默认属性应等同于罗马化写法()
    {
        MusicTrack track = CreateTrack("Yuuhei Satellite", "幽閉サテライト", "Iro wa Nioedo Chirinuru wo", "色は匂へど散りぬるを");

        Assert.Equal(track.GetDisplayArtist(TrackNameDisplay.Romanized), track.DisplayArtist);
        Assert.Equal(track.GetDisplayTitle(TrackNameDisplay.Romanized), track.DisplayTitle);
        Assert.Equal(track.GetDisplayName(TrackNameDisplay.Romanized), track.DisplayName);
    }

    [Fact]
    public void 切换写法不应影响搜索结果()
    {
        MusicTrack track = fixture.TrackOfMapSet(1003);

        // 无论界面显示哪种写法，两种写法的关键词都能搜到同一首曲目。
        foreach (string keyword in new[] { "yuuhei", "幽閉", "iro wa nioedo", "色は匂へど" })
        {
            IReadOnlyList<MusicTrack> result = Core.Services.TrackSearcher.Search(
                fixture.Library.Tracks,
                new Core.Services.TrackSearchQuery { Text = keyword });

            Assert.Contains(track, result);
        }
    }

    [Fact]
    public void 示例数据中的日文曲目应能正确切换()
    {
        MusicTrack track = fixture.TrackOfMapSet(1003);

        Assert.Equal("Yuuhei Satellite - Iro wa Nioedo Chirinuru wo", track.GetDisplayName(TrackNameDisplay.Romanized));
        Assert.Equal("幽閉サテライト - 色は匂へど散りぬるを", track.GetDisplayName(TrackNameDisplay.Original));
    }
}
