namespace OsuMusicPlayer.Core.Services;

using OsuMusicPlayer.Core.IO;
using OsuMusicPlayer.Core.Models;

/// <summary>曲目时长缓存（<c>%AppData%\OsuMusicPlayer\durations.json</c>），避免每次启动都重新解析音频头。</summary>
public sealed class DurationCache
{
    private const int CurrentVersion = 1;

    private readonly Dictionary<string, double> _durations = new(StringComparer.OrdinalIgnoreCase);

    public DurationCache(string? filePath = null)
    {
        FilePath = filePath;
    }

    public string? FilePath { get; }

    public int Count => _durations.Count;

    public void Load()
    {
        _durations.Clear();

        if (string.IsNullOrWhiteSpace(FilePath))
        {
            return;
        }

        DurationFile? file = JsonFile.Load<DurationFile>(FilePath);

        if (file?.Durations is null)
        {
            return;
        }

        foreach (KeyValuePair<string, double> pair in file.Durations)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value > 0)
            {
                _durations[pair.Key] = pair.Value;
            }
        }
    }

    public void Save()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            return;
        }

        JsonFile.Save(FilePath, new DurationFile(CurrentVersion, new Dictionary<string, double>(_durations, StringComparer.OrdinalIgnoreCase)));
    }

    public bool TryGet(string trackId, out TimeSpan duration)
    {
        if (_durations.TryGetValue(trackId, out double seconds) && seconds > 0)
        {
            duration = TimeSpan.FromSeconds(seconds);
            return true;
        }

        duration = default;
        return false;
    }

    /// <summary>记录时长，返回是否发生了变化。</summary>
    public bool Set(string trackId, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(trackId) || duration <= TimeSpan.Zero)
        {
            return false;
        }

        double seconds = Math.Round(duration.TotalSeconds, 3);

        if (_durations.TryGetValue(trackId, out double existing) && Math.Abs(existing - seconds) < 0.01)
        {
            return false;
        }

        _durations[trackId] = seconds;
        return true;
    }

    public bool Remove(string trackId) => _durations.Remove(trackId);

    /// <summary>把缓存里的时长套用到音乐库的曲目上。</summary>
    public int ApplyTo(IEnumerable<MusicTrack> tracks)
    {
        int applied = 0;

        foreach (MusicTrack track in tracks)
        {
            if (track.Duration is not null)
            {
                continue;
            }

            if (TryGet(track.Id, out TimeSpan duration))
            {
                track.Duration = duration;
                applied++;
            }
        }

        return applied;
    }

    private sealed record DurationFile(int Version, Dictionary<string, double> Durations);
}
