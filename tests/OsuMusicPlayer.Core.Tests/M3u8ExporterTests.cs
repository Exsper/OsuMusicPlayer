namespace OsuMusicPlayer.Core.Tests;

using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Services;
using Xunit;

/// <summary>M3U8 导出。</summary>
public sealed class M3u8ExporterTests
{
    [Fact]
    public void 导出内容应包含扩展信息与文件路径()
    {
        List<MusicTrack> tracks =
        [
            new MusicTrack
            {
                Id = "s1|a.mp3",
                Title = "Title A",
                Artist = "Artist A",
                AudioFilePath = @"C:\osu!\Songs\1 Artist A - Title A\a.mp3",
                Duration = TimeSpan.FromSeconds(125),
            },
            new MusicTrack
            {
                Id = "s2|b.ogg",
                Title = "Title B",
                Artist = "Artist B",
                AudioFilePath = @"C:\osu!\Songs\2 Artist B - Title B\b.ogg",
                TotalTime = 201000,
            },
        ];

        string content = M3u8Exporter.Build(tracks);
        string[] lines = content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("#EXTM3U", lines[0]);
        Assert.Equal("#EXTINF:125,Artist A - Title A", lines[1]);
        Assert.Equal(@"C:\osu!\Songs\1 Artist A - Title A\a.mp3", lines[2]);
        Assert.Equal("#EXTINF:201,Artist B - Title B", lines[3]);
        Assert.Equal(@"C:\osu!\Songs\2 Artist B - Title B\b.ogg", lines[4]);
    }

    [Fact]
    public void 未知时长使用负值占位()
    {
        MusicTrack track = new()
        {
            Id = "s3|c.mp3",
            Title = "C",
            Artist = "A",
            AudioFilePath = @"C:\c.mp3",
        };

        Assert.Contains("#EXTINF:-1,A - C", M3u8Exporter.Build([track]), StringComparison.Ordinal);
    }

    [Fact]
    public void 写入文件应采用UTF8无BOM()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("列表.m3u8");

        M3u8Exporter.Write(
            [new MusicTrack { Id = "s4|d.mp3", Title = "中日文", Artist = "テスト", AudioFilePath = @"C:\d.mp3" }],
            path);

        byte[] bytes = File.ReadAllBytes(path);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Contains("中日文", File.ReadAllText(path), StringComparison.Ordinal);
    }
}
