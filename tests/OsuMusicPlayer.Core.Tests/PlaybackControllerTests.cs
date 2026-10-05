namespace OsuMusicPlayer.Core.Tests;

using OsuMusicPlayer.Core;
using OsuMusicPlayer.Core.Enums;
using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Playback;
using Xunit;

/// <summary>播放队列与播放控制器的行为（自动下一首、循环、随机、失败跳过）。</summary>
public sealed class PlaybackControllerTests
{
    private static MusicTrack Track(string id) => new()
    {
        Id = id,
        Title = id,
        Artist = "tester",
        AudioFilePath = $@"C:\fake\{id}.wav",
        AudioFileExists = true,
    };

    private static List<MusicTrack> Tracks(params string[] ids) => [.. ids.Select(Track)];

    [Fact]
    public void 顺序播放到结尾后应停止()
    {
        FakeAudioPlayer player = new();
        using PlaybackController controller = new(player, new Random(1));

        controller.SetQueue(Tracks("a", "b"));

        Assert.Equal("a", controller.Current!.Id);
        Assert.True(controller.IsPlaying);

        player.RaiseNaturalEnd();

        Assert.Equal("b", controller.Current!.Id);

        player.RaiseNaturalEnd();

        Assert.Equal("b", controller.Current!.Id);
        Assert.False(controller.IsPlaying);
        Assert.Equal(2, player.LoadCount);
    }

    [Fact]
    public void 列表循环应回到第一首()
    {
        FakeAudioPlayer player = new();
        using PlaybackController controller = new(player) { Mode = PlaybackMode.RepeatAll };

        controller.SetQueue(Tracks("a", "b", "c"));
        player.RaiseNaturalEnd();
        player.RaiseNaturalEnd();
        player.RaiseNaturalEnd();

        Assert.Equal("a", controller.Current!.Id);
        Assert.True(controller.IsPlaying);
    }

    [Fact]
    public void 单曲循环应重播当前曲目()
    {
        FakeAudioPlayer player = new();
        using PlaybackController controller = new(player) { Mode = PlaybackMode.RepeatOne };

        controller.SetQueue(Tracks("a", "b"));
        player.RaiseNaturalEnd();

        Assert.Equal("a", controller.Current!.Id);
        Assert.True(controller.IsPlaying);

        // 用户主动点“下一首”时仍然换歌
        controller.Next();

        Assert.Equal("b", controller.Current!.Id);
    }

    [Fact]
    public void 随机播放一轮内不应重复()
    {
        FakeAudioPlayer player = new();
        using PlaybackController controller = new(player, new Random(12345)) { Mode = PlaybackMode.Shuffle };

        controller.SetQueue(Tracks("a", "b", "c", "d", "e"));

        List<string> played = [controller.Current!.Id];

        for (int i = 0; i < 4; i++)
        {
            player.RaiseNaturalEnd();
            played.Add(controller.Current!.Id);
        }

        Assert.Equal(5, played.Distinct().Count());

        // 一轮结束后重新洗牌，仍然能继续播放
        player.RaiseNaturalEnd();

        Assert.True(controller.IsPlaying);
        Assert.Equal(6, player.LoadCount);
    }

    [Fact]
    public void 随机顺序应覆盖全部曲目且不立刻重复上一首()
    {
        PlaybackQueue queue = new(new Random(2024)) { Mode = PlaybackMode.Shuffle };

        queue.SetItems(Tracks("a", "b", "c", "d", "e"), startIndex: 0);

        Assert.Equal(5, queue.ShuffleOrder.Count);
        Assert.Equal(5, queue.ShuffleOrder.Distinct().Count());
        Assert.Equal(0, queue.ShuffleOrder[0]);

        List<int> round = [queue.CurrentIndex];

        for (int i = 0; i < 4; i++)
        {
            int next = queue.NextIndex(TrackAdvanceReason.TrackFinished);
            queue.MoveTo(next);
            round.Add(queue.CurrentIndex);
        }

        Assert.Equal(5, round.Distinct().Count());

        // 第二轮的第一首不应与刚播完的曲目相同
        int firstOfSecondRound = queue.NextIndex(TrackAdvanceReason.TrackFinished);

        Assert.NotEqual(round[^1], firstOfSecondRound);
    }

    [Fact]
    public void 上一首在第一首时应重新播放当前()
    {
        FakeAudioPlayer player = new();
        using PlaybackController controller = new(player);

        controller.SetQueue(Tracks("a", "b", "c"), startIndex: 2);
        Assert.Equal("c", controller.Current!.Id);

        controller.Previous();
        Assert.Equal("b", controller.Current!.Id);

        controller.Previous();
        Assert.Equal("a", controller.Current!.Id);

        controller.Previous();
        Assert.Equal("a", controller.Current!.Id);
    }

    [Fact]
    public void 无法播放的曲目应被跳过并报告错误()
    {
        FakeAudioPlayer player = new();
        player.FailingPaths.Add(@"C:\fake\b.wav");

        using PlaybackController controller = new(player);
        List<PlaybackErrorEventArgs> errors = [];
        controller.PlaybackError += (_, args) => errors.Add(args);

        controller.SetQueue(Tracks("a", "b", "c"));

        Assert.Equal("a", controller.Current!.Id);

        player.RaiseNaturalEnd();

        Assert.Equal("c", controller.Current!.Id);
        Assert.True(controller.IsPlaying);
        Assert.Single(errors);
        Assert.Equal("b", errors[0].Track!.Id);
    }

    [Fact]
    public void 全部曲目都无法播放时应停止且不陷入死循环()
    {
        FakeAudioPlayer player = new();
        player.FailingPaths.Add(@"C:\fake\a.wav");
        player.FailingPaths.Add(@"C:\fake\b.wav");

        using PlaybackController controller = new(player);
        List<PlaybackErrorEventArgs> errors = [];
        controller.PlaybackError += (_, args) => errors.Add(args);

        controller.SetQueue(Tracks("a", "b"));

        Assert.False(controller.IsPlaying);
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void 没有输出设备时应只报一次错误()
    {
        FakeAudioPlayer player = new() { OutputAvailable = false, ThrowOutputUnavailableOnPlay = true };

        using PlaybackController controller = new(player);
        List<PlaybackErrorEventArgs> errors = [];
        controller.PlaybackError += (_, args) => errors.Add(args);

        controller.SetQueue(Tracks("a", "b", "c"));

        Assert.Single(errors);
        Assert.IsType<AudioOutputUnavailableException>(errors[0].Error);
        Assert.False(controller.IsPlaying);
    }

    [Fact]
    public void 播放暂停与继续()
    {
        FakeAudioPlayer player = new();
        using PlaybackController controller = new(player);

        controller.SetQueue(Tracks("a", "b"));
        Assert.True(controller.IsPlaying);

        controller.TogglePlayPause();
        Assert.True(controller.IsPaused);
        Assert.False(controller.IsPlaying);

        controller.TogglePlayPause();
        Assert.True(controller.IsPlaying);

        controller.Stop();
        Assert.False(controller.IsPlaying);
        Assert.False(controller.IsPaused);
        Assert.Equal(1, player.LoadCount);
    }

    [Fact]
    public void 在队列中直接播放指定曲目()
    {
        FakeAudioPlayer player = new();
        using PlaybackController controller = new(player);

        List<MusicTrack> tracks = Tracks("a", "b", "c");
        controller.PlayTrackInQueue(tracks, tracks[2]);

        Assert.Equal("c", controller.Current!.Id);
        Assert.Equal(2, controller.CurrentIndex);
        Assert.Equal(1, player.LoadCount);

        controller.PlayAt(0);
        Assert.Equal("a", controller.Current!.Id);
        Assert.Equal(2, player.LoadCount);

        controller.PlayAt(99);
        Assert.Equal("a", controller.Current!.Id);
    }

    [Fact]
    public void 音量应被限制在合法范围()
    {
        FakeAudioPlayer player = new();
        using PlaybackController controller = new(player);

        controller.Volume = 1.5f;
        Assert.Equal(1f, controller.Volume);

        controller.Volume = -3f;
        Assert.Equal(0f, controller.Volume);

        controller.Volume = 0.42f;
        Assert.Equal(0.42f, controller.Volume);
    }

    [Fact]
    public void 跳转与时长应转发到播放器()
    {
        FakeAudioPlayer player = new();
        using PlaybackController controller = new(player);

        controller.SetQueue(Tracks("a"));
        controller.Seek(42);

        Assert.Equal(42, player.Position);
        Assert.Equal(200, controller.Duration);
    }

    [Fact]
    public void 空队列不应播放()
    {
        FakeAudioPlayer player = new();
        using PlaybackController controller = new(player);

        controller.SetQueue([]);
        controller.Play();
        controller.Next();
        controller.TogglePlayPause();

        Assert.Null(controller.Current);
        Assert.False(controller.IsPlaying);
        Assert.Equal(0, player.LoadCount);
    }

    [Fact]
    public void 事件应通知界面()
    {
        FakeAudioPlayer player = new();
        using PlaybackController controller = new(player);

        int trackChanged = 0;
        int stateChanged = 0;
        controller.CurrentTrackChanged += (_, _) => trackChanged++;
        controller.StateChanged += (_, _) => stateChanged++;

        controller.SetQueue(Tracks("a", "b"));

        Assert.True(trackChanged > 0);
        Assert.True(stateChanged > 0);

        int before = stateChanged;
        controller.Mode = PlaybackMode.RepeatAll;
        Assert.True(stateChanged > before);
    }

    [Fact]
    public void 队列应能从当前曲目继续随机序列()
    {
        PlaybackQueue queue = new(new Random(7)) { Mode = PlaybackMode.Shuffle };

        queue.SetItems(Tracks("a", "b", "c", "d"), startIndex: 2);

        Assert.Equal(2, queue.CurrentIndex);
        Assert.Equal(4, queue.ShuffleOrder.Count);

        queue.Mode = PlaybackMode.Sequential;
        int next = queue.NextIndex(TrackAdvanceReason.TrackFinished);
        Assert.Equal(3, next);

        queue.MoveTo(next);
        queue.Mode = PlaybackMode.RepeatAll;
        Assert.Equal(0, queue.NextIndex(TrackAdvanceReason.TrackFinished));
        Assert.Equal(0, queue.NextIndex(TrackAdvanceReason.UserRequested));
    }

    /// <summary>不产生真实声音的假播放器。</summary>
    private sealed class FakeAudioPlayer : IAudioPlayer
    {
        private int _loadCount;

        public event EventHandler<PlaybackStoppedEventArgs>? PlaybackStopped;

        public HashSet<string> FailingPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool ThrowOutputUnavailableOnPlay { get; set; }

        public bool OutputAvailable { get; set; } = true;

        public int LoadCount => _loadCount;

        public AudioPlayerState State { get; private set; } = AudioPlayerState.Stopped;

        public string? CurrentFilePath { get; private set; }

        public double Position { get; private set; }

        public double Duration { get; set; } = 200;

        public float Volume { get; set; } = 0.8f;

        public void Load(string filePath)
        {
            if (FailingPaths.Contains(filePath))
            {
                throw new AudioLoadException($"测试：无法打开 {filePath}", filePath);
            }

            _loadCount++;
            CurrentFilePath = filePath;
            Position = 0;
        }

        public void Play()
        {
            if (ThrowOutputUnavailableOnPlay)
            {
                throw new AudioOutputUnavailableException("测试：没有输出设备");
            }

            State = AudioPlayerState.Playing;
        }

        public void Pause()
        {
            if (State == AudioPlayerState.Playing)
            {
                State = AudioPlayerState.Paused;
            }
        }

        public void Stop()
        {
            if (State == AudioPlayerState.Stopped)
            {
                return;
            }

            State = AudioPlayerState.Stopped;
            PlaybackStopped?.Invoke(this, new PlaybackStoppedEventArgs(false));
        }

        public void Seek(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                return;
            }

            Position = seconds;
        }

        public void RaiseNaturalEnd()
        {
            State = AudioPlayerState.Stopped;
            Position = Duration;
            PlaybackStopped?.Invoke(this, new PlaybackStoppedEventArgs(true));
        }

        public void Dispose()
        {
        }
    }
}
