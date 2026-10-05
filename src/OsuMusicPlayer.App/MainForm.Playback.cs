namespace OsuMusicPlayer.App;

using System.Globalization;
using OsuMusicPlayer.Core;
using OsuMusicPlayer.Core.Enums;
using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Playback;

/// <summary>播放控制、正在播放面板与播放错误处理。</summary>
public sealed partial class MainForm
{
    private static readonly string[] _backgroundExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif"];

    private void UpdateTransportUi()
    {
        RunOnUi(() =>
        {
            bool hasTrack = _playback.HasTrack;
            bool hasQueue = _playback.Queue.Count > 0;

            _playPauseButton.Text = _playback.IsPlaying ? "⏸" : "▶";
            _playPauseButton.Enabled = hasTrack || _visibleTracks.Count > 0;
            _stopButton.Enabled = hasTrack;
            _previousButton.Enabled = hasQueue;
            _nextButton.Enabled = hasQueue;
            _seekBar.Enabled = hasTrack;

            if (!_seeking)
            {
                double duration = _playback.Duration;
                double position = _playback.Position;

                if (duration > 0.05)
                {
                    _seekBar.Value = (int)Math.Clamp(position / duration * _seekBar.Maximum, _seekBar.Minimum, _seekBar.Maximum);
                }
                else
                {
                    _seekBar.Value = _seekBar.Minimum;
                }

                UpdatePositionLabels(position, duration);
            }

            if (hasTrack)
            {
                _positionTimer.Enabled = _playback.IsPlaying;
            }
            else
            {
                _positionTimer.Enabled = false;
            }
        });
    }

    private void OnCurrentTrackChanged()
    {
        MusicTrack? track = _playback.Current;

        _nowPlaying.ShowTrack(track, null);

        if (track is not null)
        {
            _ = LoadCoverAsync(track);
            FollowCurrentTrackInList(track);

            if (_settings.PlayFromPreviewPoint && track.PreviewTime > 0 && _playback.IsPlaying)
            {
                _playback.Seek(track.PreviewTime / 1000d);
            }
        }

        UpdateTransportUi();
    }

    private void FollowCurrentTrackInList(MusicTrack track)
    {
        int index = _visibleTracks.FindIndex(item => string.Equals(item.Id, track.Id, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            return;
        }

        try
        {
            _trackList.SelectedIndices.Clear();
            _trackList.SelectedIndices.Add(index);
            _trackList.EnsureVisible(index);
        }
        catch (ArgumentOutOfRangeException)
        {
            // 列表刚好在刷新时忽略。
        }
    }

    private async Task LoadCoverAsync(MusicTrack track)
    {
        string? backgroundPath = await Task.Run(() => FindBackgroundImagePath(track));

        Image? image = null;

        if (backgroundPath is not null)
        {
            try
            {
                byte[] bytes = await File.ReadAllBytesAsync(backgroundPath);

                using MemoryStream stream = new(bytes);
                image = Image.FromStream(stream);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                image = null;
            }
        }

        if (_playback.Current?.Id == track.Id && !IsDisposed)
        {
            _nowPlaying.ShowTrack(track, image);
        }
        else
        {
            image?.Dispose();
        }
    }

    /// <summary>从谱面的 .osu 文件里找出背景图路径。</summary>
    private static string? FindBackgroundImagePath(MusicTrack track)
    {
        if (string.IsNullOrWhiteSpace(track.OsuFilePath) || !File.Exists(track.OsuFilePath))
        {
            return null;
        }

        try
        {
            string directory = Path.GetDirectoryName(track.OsuFilePath) ?? string.Empty;

            foreach (string line in File.ReadLines(track.OsuFilePath))
            {
                if (!_backgroundExtensions.Any(extension => line.Contains(extension, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                string[] parts = line.Split(',');

                if (parts.Length < 3)
                {
                    continue;
                }

                string candidate = Path.Combine(directory, parts[2].Trim().Trim('"'));

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private void UpdatePositionFromPlayback()
    {
        if (_seeking)
        {
            return;
        }

        double duration = _playback.Duration;
        double position = _playback.Position;

        if (duration > 0.05)
        {
            _seekBar.Value = (int)Math.Clamp(position / duration * _seekBar.Maximum, _seekBar.Minimum, _seekBar.Maximum);
        }

        UpdatePositionLabels(position, duration);
    }

    private void UpdatePositionLabels(double position, double duration)
    {
        _positionLabel.Text = FormatClock(position);
        _durationLabel.Text = FormatClock(duration);
    }

    private static string FormatClock(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return "0:00";
        }

        TimeSpan time = TimeSpan.FromSeconds(seconds);

        return time.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalMinutes}:{time.Seconds:00}");
    }

    private void OnVolumeChanged()
    {
        _playback.Volume = _volumeBar.Value / 100f;
        _volumeLabel.Text = $"音量 {_volumeBar.Value}%";
    }

    private void PlaySelectedTrack()
    {
        List<MusicTrack> selected = GetSelectedTracks();

        if (selected.Count == 0)
        {
            SetStatus("请先在列表中选择要播放的曲目。");
            return;
        }

        if (_visibleTracks.Count == 0)
        {
            return;
        }

        int index = _trackList.SelectedIndices.Count > 0 ? _trackList.SelectedIndices[0] : 0;
        PlayQueueAt(_visibleTracks, index);
    }

    private void PlayQueueAt(IReadOnlyList<MusicTrack> queue, int index)
    {
        if (queue.Count == 0)
        {
            return;
        }

        _playback.SetQueue(queue, Math.Clamp(index, 0, queue.Count - 1));
        UpdateTransportUi();
    }

    private void TogglePlayPause()
    {
        if (!_playback.OutputAvailable && !_playback.IsPlaying)
        {
            ShowWarning("没有可用的音频输出设备", "系统没有可用的音频输出设备，无法播放（仍可浏览、搜索与管理播放列表）。");
            return;
        }

        if (!_playback.HasTrack)
        {
            if (_visibleTracks.Count > 0)
            {
                PlayQueueAt(_visibleTracks, 0);
            }

            return;
        }

        _playback.TogglePlayPause();
        UpdateTransportUi();
    }

    private void StopPlayback()
    {
        _playback.Stop();
        UpdateTransportUi();
    }

    private void PlayNext()
    {
        _playback.Next();
        UpdateTransportUi();
    }

    private void PlayPrevious()
    {
        _playback.Previous();
        UpdateTransportUi();
    }

    private void SetPlaybackMode(PlaybackMode mode)
    {
        _playback.Mode = mode;
        _settings.PlaybackMode = mode;

        _suppressModeChange = true;
        _modeBox.SelectedIndex = Math.Clamp((int)mode, 0, 3);
        _suppressModeChange = false;

        _modeSequentialItem.Checked = mode == PlaybackMode.Sequential;
        _modeRepeatAllItem.Checked = mode == PlaybackMode.RepeatAll;
        _modeRepeatOneItem.Checked = mode == PlaybackMode.RepeatOne;
        _modeShuffleItem.Checked = mode == PlaybackMode.Shuffle;

        SetStatus($"播放模式：{_modeBox.Text}");
    }

    private void OnPlaybackError(PlaybackErrorEventArgs args)
    {
        string name = args.Track is null ? "音频" : args.Track.GetDisplayName(_settings.NameDisplay);

        if (args.Error is AudioOutputUnavailableException)
        {
            ShowWarning("无法播放", args.Error.Message);
            SetStatus("播放失败：没有可用的音频输出设备。");
            return;
        }

        SetStatus($"播放失败：{name}（{args.Error.Message}）");
    }
}
