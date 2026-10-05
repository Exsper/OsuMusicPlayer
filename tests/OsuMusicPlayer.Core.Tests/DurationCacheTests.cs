namespace OsuMusicPlayer.Core.Tests;

using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Services;
using Xunit;

/// <summary>曲目时长缓存。</summary>
public sealed class DurationCacheTests
{
    [Fact]
    public void 记录与读取时长()
    {
        DurationCache cache = new();

        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.Set("a", TimeSpan.FromSeconds(123.456)));
        Assert.True(cache.TryGet("a", out TimeSpan duration));
        Assert.Equal(123.456, duration.TotalSeconds, 0.01);

        // 相同数值不会重复写入
        Assert.False(cache.Set("a", TimeSpan.FromSeconds(123.456)));
        Assert.True(cache.Set("a", TimeSpan.FromSeconds(200)));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void 忽略非法值()
    {
        DurationCache cache = new();

        Assert.False(cache.Set(string.Empty, TimeSpan.FromSeconds(10)));
        Assert.False(cache.Set("a", TimeSpan.Zero));
        Assert.False(cache.Set("a", TimeSpan.FromSeconds(-5)));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void 应用到曲目并跳过已有时长()
    {
        DurationCache cache = new();
        cache.Set("a", TimeSpan.FromSeconds(60));
        cache.Set("b", TimeSpan.FromSeconds(90));

        List<MusicTrack> tracks =
        [
            new MusicTrack { Id = "a" },
            new MusicTrack { Id = "b", Duration = TimeSpan.FromSeconds(1) },
            new MusicTrack { Id = "c" },
        ];

        Assert.Equal(1, cache.ApplyTo(tracks));
        Assert.Equal(60, tracks[0].Duration!.Value.TotalSeconds, 0.01);
        Assert.Equal(1, tracks[1].Duration!.Value.TotalSeconds, 0.01);
        Assert.Null(tracks[2].Duration);
    }

    [Fact]
    public void 保存与读取()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("durations.json");

        DurationCache cache = new(path);
        cache.Load();
        cache.Set("s1001|audio.wav", TimeSpan.FromSeconds(95.5));
        cache.Save();

        DurationCache reloaded = new(path);
        reloaded.Load();

        Assert.Equal(1, reloaded.Count);
        Assert.True(reloaded.TryGet("s1001|audio.wav", out TimeSpan duration));
        Assert.Equal(95.5, duration.TotalSeconds, 0.01);

        Assert.True(reloaded.Remove("s1001|audio.wav"));
        Assert.Equal(0, reloaded.Count);
    }
}
