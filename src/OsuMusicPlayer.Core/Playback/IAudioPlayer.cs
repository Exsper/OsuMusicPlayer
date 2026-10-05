namespace OsuMusicPlayer.Core.Playback;

using OsuMusicPlayer.Core.Models;

/// <summary>播放器状态。</summary>
public enum AudioPlayerState
{
    Stopped,
    Playing,
    Paused,
}

/// <summary>播放停止事件参数。</summary>
public sealed class PlaybackStoppedEventArgs(bool isNaturalEnd, Exception? error = null) : EventArgs
{
    /// <summary>是否为“自然播放到结尾”（而非用户停止或切换曲目）。</summary>
    public bool IsNaturalEnd { get; } = isNaturalEnd;

    /// <summary>导致停止的异常（如果有）。</summary>
    public Exception? Error { get; } = error;
}

/// <summary>
/// 音频输出抽象：只负责“打开一个文件并播放”，播放列表/自动下一首由 <see cref="PlaybackController"/> 负责。
/// </summary>
public interface IAudioPlayer : IDisposable
{
    /// <summary>播放结束（含自然结束、被停止与出错）时触发，可能在后台线程上触发。</summary>
    event EventHandler<PlaybackStoppedEventArgs>? PlaybackStopped;

    /// <summary>是否存在可用的音频输出设备。</summary>
    bool OutputAvailable { get; }

    AudioPlayerState State { get; }

    /// <summary>当前已载入的文件路径。</summary>
    string? CurrentFilePath { get; }

    /// <summary>当前播放位置（秒）。</summary>
    double Position { get; }

    /// <summary>当前文件总时长（秒），未知时为 0。</summary>
    double Duration { get; }

    /// <summary>音量（0~1）。</summary>
    float Volume { get; set; }

    /// <summary>载入音频文件；失败时抛出 <see cref="OsuMusicPlayer.Core.AudioLoadException"/>。</summary>
    void Load(string filePath);

    /// <summary>开始/继续播放；没有输出设备时抛出 <see cref="OsuMusicPlayer.Core.AudioOutputUnavailableException"/>。</summary>
    void Play();

    void Pause();

    void Stop();

    void Seek(double seconds);
}

/// <summary>播放失败时通知界面。</summary>
public sealed class PlaybackErrorEventArgs(MusicTrack? track, Exception error) : EventArgs
{
    public MusicTrack? Track { get; } = track;

    public Exception Error { get; } = error;
}
