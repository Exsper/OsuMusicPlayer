namespace OsuMusicPlayer.Core.Playback;

using OsuMusicPlayer.Core.Enums;
using OsuMusicPlayer.Core.Models;

/// <summary>
/// 播放控制器：把播放队列与音频输出连接起来，负责自动下一首、播放失败跳过、播放模式与音量。
/// 事件可能在后台线程触发，界面需要自行切换到 UI 线程。
/// </summary>
public sealed class PlaybackController : IDisposable
{
    private readonly IAudioPlayer _player;
    private readonly object _sync = new();
    private bool _disposed;

    public PlaybackController(IAudioPlayer player, Random? random = null)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        Queue = new PlaybackQueue(random);
        _player.PlaybackStopped += OnPlaybackStopped;
    }

    /// <summary>当前曲目发生变化（含自动下一首）。</summary>
    public event EventHandler? CurrentTrackChanged;

    /// <summary>播放状态发生变化（播放/暂停/停止/模式/音量）。</summary>
    public event EventHandler? StateChanged;

    /// <summary>某首曲目播放失败。</summary>
    public event EventHandler<PlaybackErrorEventArgs>? PlaybackError;

    public PlaybackQueue Queue { get; }

    public MusicTrack? Current => Queue.Current;

    public IReadOnlyList<MusicTrack> Items => Queue.Items;

    public int CurrentIndex => Queue.CurrentIndex;

    public bool HasTrack => Current is not null;

    public bool IsPlaying => _player.State == AudioPlayerState.Playing;

    public bool IsPaused => _player.State == AudioPlayerState.Paused;

    public bool OutputAvailable => _player.OutputAvailable;

    public string? CurrentFilePath => _player.CurrentFilePath;

    public double Position => _player.Position;

    public double Duration => _player.Duration;

    public PlaybackMode Mode
    {
        get => Queue.Mode;
        set
        {
            if (Queue.Mode == value)
            {
                return;
            }

            Queue.Mode = value;
            RaiseStateChanged();
        }
    }

    public float Volume
    {
        get => _player.Volume;
        set
        {
            float clamped = Math.Clamp(value, 0f, 1f);

            if (Math.Abs(_player.Volume - clamped) < 0.0001f)
            {
                return;
            }

            _player.Volume = clamped;
            RaiseStateChanged();
        }
    }

    /// <summary>设置播放队列并（可选）立即开始播放。</summary>
    public void SetQueue(IEnumerable<MusicTrack> tracks, int startIndex = 0, bool autoPlay = true)
    {
        List<MusicTrack> list = tracks as List<MusicTrack> ?? [.. tracks];

        Queue.SetItems(list, startIndex);
        RaiseCurrentTrackChanged();

        if (autoPlay && list.Count > 0)
        {
            StartAt(Queue.CurrentIndex);
        }
        else
        {
            StopInternal();
            RaiseStateChanged();
        }
    }

    /// <summary>播放队列中指定下标的曲目。</summary>
    public void PlayAt(int index)
    {
        if (!Queue.MoveTo(index))
        {
            return;
        }

        StartAt(index);
    }

    /// <summary>按当前音乐库顺序播放指定曲目（用于“双击列表中的某一首”）。</summary>
    public void PlayTrackInQueue(IEnumerable<MusicTrack> queueSource, MusicTrack track)
    {
        List<MusicTrack> list = queueSource as List<MusicTrack> ?? [.. queueSource];
        int index = list.FindIndex(item => string.Equals(item.Id, track.Id, StringComparison.OrdinalIgnoreCase));

        SetQueue(list, index < 0 ? 0 : index);
    }

    /// <summary>播放/暂停切换；当前没有曲目时从队列开头开始。</summary>
    public void TogglePlayPause()
    {
        if (Current is null)
        {
            if (Queue.Count > 0)
            {
                StartAt(Math.Max(0, Queue.CurrentIndex));
            }

            return;
        }

        if (IsPaused)
        {
            try
            {
                _player.Play();
            }
            catch (Exception ex)
            {
                RaisePlaybackError(Current, ex);
                return;
            }

            RaiseStateChanged();
            return;
        }

        if (IsPlaying)
        {
            _player.Pause();
            RaiseStateChanged();
            return;
        }

        StartAt(Queue.CurrentIndex);
    }

    public void Play()
    {
        if (Current is null)
        {
            if (Queue.Count > 0)
            {
                StartAt(0);
            }

            return;
        }

        if (IsPaused)
        {
            try
            {
                _player.Play();
                RaiseStateChanged();
            }
            catch (Exception ex)
            {
                RaisePlaybackError(Current, ex);
            }

            return;
        }

        if (!IsPlaying)
        {
            StartAt(Queue.CurrentIndex);
        }
    }

    public void Pause()
    {
        if (!IsPlaying)
        {
            return;
        }

        _player.Pause();
        RaiseStateChanged();
    }

    public void Stop()
    {
        StopInternal();
        RaiseStateChanged();
    }

    public void Next() => Advance(TrackAdvanceReason.UserRequested);

    public void Previous()
    {
        int index = Queue.PreviousIndex();

        if (index >= 0)
        {
            StartAt(index);
        }
    }

    public void Seek(double seconds)
    {
        try
        {
            _player.Seek(seconds);
        }
        catch (Exception ex)
        {
            RaisePlaybackError(Current, ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _player.PlaybackStopped -= OnPlaybackStopped;
        _player.Dispose();
    }

    /// <summary>从指定下标开始播放，自动跳过无法播放的曲目。</summary>
    private void StartAt(int index)
    {
        if (_disposed)
        {
            return;
        }

        if (Queue.Count == 0)
        {
            StopInternal();
            RaiseStateChanged();
            return;
        }

        int target = index;
        int attempts = 0;

        while (target >= 0 && target < Queue.Count && attempts <= Queue.Count)
        {
            if (!Queue.MoveTo(target))
            {
                break;
            }

            MusicTrack track = Queue.Items[target];
            RaiseCurrentTrackChanged();

            // 单曲循环时同一首直接从头再来，避免重复打开文件。
            if (attempts == 0 && TryRestartCurrent(track))
            {
                return;
            }

            try
            {
                _player.Load(track.AudioFilePath);
                _player.Volume = Volume;
                _player.Play();

                if (track.Duration is null && _player.Duration > 0)
                {
                    track.Duration = TimeSpan.FromSeconds(_player.Duration);
                }

                RaiseStateChanged();
                return;
            }
            catch (AudioOutputUnavailableException ex)
            {
                // 没有输出设备时不做“跳过所有曲目”的处理，直接停下来提示用户。
                RaisePlaybackError(track, ex);
                StopInternal();
                RaiseStateChanged();
                return;
            }
            catch (Exception ex)
            {
                attempts++;
                RaisePlaybackError(track, ex);
                target = Queue.NextIndex(TrackAdvanceReason.PlaybackFailed);
            }
        }

        StopInternal();
        RaiseStateChanged();
    }

    private bool TryRestartCurrent(MusicTrack track)
    {
        if (_player.CurrentFilePath is null
            || !string.Equals(_player.CurrentFilePath, track.AudioFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            _player.Seek(0);
            _player.Play();
            RaiseStateChanged();

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Advance(TrackAdvanceReason reason)
    {
        int next = Queue.NextIndex(reason);

        if (next < 0)
        {
            StopInternal();
            RaiseStateChanged();
            return;
        }

        StartAt(next);
    }

    private void StopInternal()
    {
        try
        {
            _player.Stop();
            _player.Seek(0);
        }
        catch (Exception)
        {
            // 停止失败（例如设备被拔出）时忽略，界面仍然回到停止状态。
        }
    }

    private void OnPlaybackStopped(object? sender, PlaybackStoppedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            if (!e.IsNaturalEnd)
            {
                if (e.Error is not null)
                {
                    RaisePlaybackError(Current, e.Error);
                }

                RaiseStateChanged();
                return;
            }

            Advance(TrackAdvanceReason.TrackFinished);
        }
    }

    private void RaiseCurrentTrackChanged() => CurrentTrackChanged?.Invoke(this, EventArgs.Empty);

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private void RaisePlaybackError(MusicTrack? track, Exception error)
        => PlaybackError?.Invoke(this, new PlaybackErrorEventArgs(track, error));
}
