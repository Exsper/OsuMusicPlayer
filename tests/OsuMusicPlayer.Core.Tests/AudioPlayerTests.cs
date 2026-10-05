namespace OsuMusicPlayer.Core.Tests;

using OsuMusicPlayer.Audio;
using OsuMusicPlayer.Core;
using OsuMusicPlayer.Core.Playback;
using OsuMusicPlayer.SampleData;
using Xunit;

/// <summary>真实音频文件的读取、定位与（在有声卡时）播放。</summary>
public sealed class AudioPlayerTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _audioFile;

    public AudioPlayerTests()
    {
        _audioFile = _temp.Combine("sample.wav");
        WavWriter.WriteSine(_audioFile, 440, TimeSpan.FromSeconds(3));
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void 应能读取WAV时长()
    {
        Assert.True(AudioFileProbe.TryGetDuration(_audioFile, out TimeSpan duration, out string? error), error);
        Assert.Equal(3, duration.TotalSeconds, 0.05);
        Assert.NotNull(AudioFileProbe.DescribeFormat(_audioFile));
    }

    [Fact]
    public void 不存在的文件应返回失败而不是抛出()
    {
        Assert.False(AudioFileProbe.TryGetDuration(_temp.Combine("nope.wav"), out _, out string? error));
        Assert.NotNull(error);
    }

    [Fact]
    public void 播放器应能在没有输出设备时也读取时长与定位()
    {
        using NAudioPlayer player = new();

        player.Load(_audioFile);

        Assert.Equal(_audioFile, player.CurrentFilePath);
        Assert.Equal(3, player.Duration, 0.05);
        Assert.Equal(AudioPlayerState.Stopped, player.State);

        player.Seek(1.5);
        Assert.Equal(1.5, player.Position, 0.05);

        player.Seek(999);
        Assert.True(player.Position <= player.Duration);

        player.Stop();
        Assert.Equal(0, player.Position, 0.05);
    }

    [Fact]
    public void 载入不存在的文件应抛出AudioLoadException()
    {
        using NAudioPlayer player = new();

        AudioLoadException exception = Assert.Throws<AudioLoadException>(() => player.Load(_temp.Combine("missing.wav")));
        Assert.Contains("不存在", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 载入非音频文件应抛出AudioLoadException()
    {
        string path = _temp.Combine("not-audio.mp3");
        File.WriteAllText(path, "definitely not audio");

        using NAudioPlayer player = new();

        Assert.Throws<AudioLoadException>(() => player.Load(path));
    }

    [Fact]
    public void 有输出设备时应能真正播放()
    {
        using NAudioPlayer player = new();

        if (!player.OutputAvailable)
        {
            // 无声卡/远程会话下跳过（Load/Duration 已由上面的测试覆盖）。
            return;
        }

        player.Volume = 0.05f;
        player.Load(_audioFile);
        player.Play();

        Assert.Equal(AudioPlayerState.Playing, player.State);

        Thread.Sleep(300);
        Assert.True(player.Position > 0.05, $"播放位置应前进，实际 {player.Position}");

        player.Pause();
        Assert.Equal(AudioPlayerState.Paused, player.State);

        player.Play();
        player.Stop();
        Assert.Equal(AudioPlayerState.Stopped, player.State);
    }

    [Fact]
    public void 播放结束后应报告自然结束()
    {
        using NAudioPlayer player = new();

        if (!player.OutputAvailable)
        {
            return;
        }

        using ManualResetEventSlim finished = new(false);
        bool natural = false;

        player.PlaybackStopped += (_, args) =>
        {
            natural = args.IsNaturalEnd;
            finished.Set();
        };

        // 生成一段很短的音频，等待自然播完。
        string shortFile = _temp.Combine("short.wav");
        WavWriter.WriteSine(shortFile, 880, TimeSpan.FromMilliseconds(300));

        player.Volume = 0.02f;
        player.Load(shortFile);
        player.Play();

        Assert.True(finished.Wait(TimeSpan.FromSeconds(15)), "未收到播放结束事件");
        Assert.True(natural, "应被识别为自然播放结束");
    }

    [Fact]
    public void 主动停止不应被识别为自然结束()
    {
        using NAudioPlayer player = new();

        if (!player.OutputAvailable)
        {
            return;
        }

        using ManualResetEventSlim stopped = new(false);
        bool natural = true;

        player.PlaybackStopped += (_, args) =>
        {
            natural = args.IsNaturalEnd;
            stopped.Set();
        };

        string longFile = _temp.Combine("long.wav");
        WavWriter.WriteSine(longFile, 220, TimeSpan.FromSeconds(20));

        player.Volume = 0.02f;
        player.Load(longFile);
        player.Play();
        Thread.Sleep(200);
        player.Stop();

        Assert.True(stopped.Wait(TimeSpan.FromSeconds(10)), "未收到播放停止事件");
        Assert.False(natural, "主动停止不应被识别为自然结束");
    }
}
