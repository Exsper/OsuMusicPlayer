namespace OsuMusicPlayer.Audio;

using NAudio.Wave;
using OsuMusicPlayer.Core;
using OsuMusicPlayer.Core.Playback;

/// <summary>
/// 基于 NAudio 的播放器：<see cref="AudioFileReader"/>（mp3/wav/aiff）与
/// <see cref="NAudio.Vorbis.VorbisWaveReader"/>（ogg），输出使用 <see cref="WaveOutEvent"/>。
/// </summary>
public sealed class NAudioPlayer : IAudioPlayer
{
    private readonly object _sync = new();
    private WaveStream? _reader;
    private WaveOutEvent? _output;
    private string? _filePath;
    private bool _stopRequested;
    private bool _disposed;
    private float _volume = 0.8f;

    public NAudioPlayer()
    {
        OutputAvailable = DetectOutputDevice();
    }

    public event EventHandler<PlaybackStoppedEventArgs>? PlaybackStopped;

    public bool OutputAvailable { get; }

    public AudioPlayerState State
    {
        get
        {
            lock (_sync)
            {
                if (_output is not null)
                {
                    return _output.PlaybackState switch
                    {
                        PlaybackState.Playing => AudioPlayerState.Playing,
                        PlaybackState.Paused => AudioPlayerState.Paused,
                        _ => AudioPlayerState.Stopped,
                    };
                }

                return AudioPlayerState.Stopped;
            }
        }
    }

    public string? CurrentFilePath
    {
        get
        {
            lock (_sync)
            {
                return _filePath;
            }
        }
    }

    public double Position
    {
        get
        {
            lock (_sync)
            {
                if (_reader is null)
                {
                    return 0;
                }

                try
                {
                    return _reader.CurrentTime.TotalSeconds;
                }
                catch (Exception)
                {
                    return 0;
                }
            }
        }
    }

    public double Duration
    {
        get
        {
            lock (_sync)
            {
                if (_reader is null)
                {
                    return 0;
                }

                try
                {
                    return _reader.TotalTime.TotalSeconds;
                }
                catch (Exception)
                {
                    return 0;
                }
            }
        }
    }

    public float Volume
    {
        get
        {
            lock (_sync)
            {
                return _volume;
            }
        }

        set
        {
            lock (_sync)
            {
                _volume = Math.Clamp(value, 0f, 1f);

                if (_output is not null)
                {
                    try
                    {
                        _output.Volume = _volume;
                    }
                    catch (Exception)
                    {
                        // 某些驱动不支持软件音量控制，忽略即可。
                    }
                }
            }
        }
    }

    public void Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new AudioLoadException("未指定音频文件。", filePath ?? string.Empty);
        }

        if (!File.Exists(filePath))
        {
            throw new AudioLoadException($"音频文件不存在：{Path.GetFileName(filePath)}", filePath);
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            StopOutput();
            DisposeReader();

            WaveStream reader;

            try
            {
                reader = AudioReaderFactory.Create(filePath);
            }
            catch (Exception ex)
            {
                throw new AudioLoadException($"无法打开音频文件：{Path.GetFileName(filePath)}（{ex.Message}）", filePath, ex);
            }

            _reader = reader;
            _filePath = filePath;

            if (!OutputAvailable)
            {
                // 没有输出设备时仍然保留读取器，便于显示时长与做时长分析。
                return;
            }

            try
            {
                _output ??= CreateOutput();
                _output.Init(_reader);
            }
            catch (Exception ex)
            {
                DisposeReader();
                _filePath = null;
                throw new AudioLoadException($"无法初始化音频输出：{ex.Message}", filePath, ex);
            }
        }
    }

    public void Play()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_reader is null)
            {
                throw new AudioLoadException("尚未载入音频文件。", _filePath ?? string.Empty);
            }

            if (!OutputAvailable || _output is null)
            {
                throw new AudioOutputUnavailableException("未检测到可用的音频输出设备，无法播放（仍可浏览、搜索与管理播放列表）。");
            }

            _stopRequested = false;

            try
            {
                _output.Play();
            }
            catch (Exception ex)
            {
                throw new AudioLoadException($"播放失败：{ex.Message}", _filePath ?? string.Empty, ex);
            }
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (_output?.PlaybackState != PlaybackState.Playing)
            {
                return;
            }

            try
            {
                _output.Pause();
            }
            catch (Exception)
            {
                // 忽略设备错误。
            }
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            StopOutput();

            if (_reader is not null)
            {
                try
                {
                    _reader.SetPosition(0d);
                }
                catch (Exception)
                {
                    // 忽略。
                }
            }
        }
    }

    public void Seek(double seconds)
    {
        lock (_sync)
        {
            if (_reader is null)
            {
                return;
            }

            double duration = Duration;
            double target = Math.Max(0, seconds);

            if (duration > 0.2)
            {
                // 留一点余量，避免刚好跳到文件末尾导致播放立即结束。
                target = Math.Clamp(target, 0, duration - 0.15);
            }

            try
            {
                _reader.SetPosition(target);
            }
            catch (Exception ex)
            {
                throw new AudioLoadException($"跳转播放位置失败：{ex.Message}", _filePath ?? string.Empty, ex);
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopOutput();
            DisposeReader();

            if (_output is not null)
            {
                _output.PlaybackStopped -= Output_PlaybackStopped;
                _output.Dispose();
                _output = null;
            }
        }
    }

    /// <summary>
    /// 探测是否存在可用的音频输出设备。
    /// NAudio 2.x 已移除 <c>WaveOut.DeviceCount</c>，这里通过尝试打开一次默认设备来判断。
    /// </summary>
    private static bool DetectOutputDevice()
    {
        try
        {
            using WaveOutEvent probe = new();
            SilenceProvider silence = new(new WaveFormat(44100, 16, 2));
            probe.Init(silence);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private WaveOutEvent CreateOutput()
    {
        WaveOutEvent output = new();
        output.PlaybackStopped += Output_PlaybackStopped;
        output.Volume = _volume;

        return output;
    }

    private void StopOutput()
    {
        if (_output is null)
        {
            return;
        }

        try
        {
            if (_output.PlaybackState != PlaybackState.Stopped)
            {
                _stopRequested = true;
                float volume = _output.Volume;
                _output.Volume = 0f;
                _output.Stop();
                _output.Volume = volume;
            }
        }
        catch (Exception)
        {
            // 忽略设备错误。
        }
        finally
        {
            _stopRequested = true;
        }
    }

    private void DisposeReader()
    {
        if (_reader is null)
        {
            return;
        }

        try
        {
            _reader.Dispose();
        }
        catch (Exception)
        {
            // 忽略。
        }

        _reader = null;
    }

    private void Output_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        bool naturalEnd;

        lock (_sync)
        {
            naturalEnd = !_stopRequested
                && e.Exception is null
                && _reader is not null
                && (IsAtEnd(_reader) || IsNearEnd(_reader));
        }

        PlaybackStopped?.Invoke(this, new PlaybackStoppedEventArgs(naturalEnd, e.Exception));
    }

    private static bool IsAtEnd(WaveStream reader)
    {
        try
        {
            return reader.Length > 0 && reader.Position >= reader.Length - (reader.WaveFormat.BlockAlign * 4);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>部分解码器在结尾会少读一小段数据，这里用播放位置再兜底判断一次。</summary>
    private static bool IsNearEnd(WaveStream reader)
    {
        try
        {
            TimeSpan total = reader.TotalTime;

            return total > TimeSpan.Zero && reader.CurrentTime >= total - TimeSpan.FromSeconds(0.6);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
