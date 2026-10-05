namespace OsuMusicPlayer.Core.Playback;

using OsuMusicPlayer.Core.Enums;
using OsuMusicPlayer.Core.Models;

/// <summary>
/// 播放队列：维护“当前播放列表 + 当前下标 + 播放模式（含随机播放顺序）”。
/// 只做纯逻辑，方便单元测试。
/// </summary>
public sealed class PlaybackQueue
{
    private readonly List<MusicTrack> _items = [];
    private readonly Random _random;
    private int[] _shuffleOrder = [];
    private int _shufflePosition = -1;
    private PlaybackMode _mode = PlaybackMode.Sequential;

    public PlaybackQueue(Random? random = null)
    {
        _random = random ?? new Random();
    }

    public IReadOnlyList<MusicTrack> Items => _items;

    public int Count => _items.Count;

    public int CurrentIndex { get; private set; } = -1;

    public MusicTrack? Current => CurrentIndex >= 0 && CurrentIndex < _items.Count ? _items[CurrentIndex] : null;

    public PlaybackMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
            {
                return;
            }

            _mode = value;

            if (_mode == PlaybackMode.Shuffle)
            {
                Reshuffle();
                AlignShuffleToCurrent();
            }
            else
            {
                _shuffleOrder = [];
                _shufflePosition = -1;
            }
        }
    }

    public void SetItems(IReadOnlyList<MusicTrack> items, int startIndex = 0)
    {
        _items.Clear();
        _items.AddRange(items);

        CurrentIndex = _items.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _items.Count - 1);

        if (_mode == PlaybackMode.Shuffle)
        {
            Reshuffle();
            AlignShuffleToCurrent();
        }
        else
        {
            _shuffleOrder = [];
            _shufflePosition = -1;
        }
    }

    public void Clear()
    {
        _items.Clear();
        _shuffleOrder = [];
        _shufflePosition = -1;
        CurrentIndex = -1;
    }

    public bool MoveTo(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return false;
        }

        CurrentIndex = index;

        if (_mode == PlaybackMode.Shuffle && _shuffleOrder.Length == _items.Count)
        {
            int position = Array.IndexOf(_shuffleOrder, index);

            if (position >= 0)
            {
                _shufflePosition = position;
            }
        }

        return true;
    }

    public int IndexOfTrack(string? trackId)
        => string.IsNullOrWhiteSpace(trackId)
            ? -1
            : _items.FindIndex(track => string.Equals(track.Id, trackId, StringComparison.OrdinalIgnoreCase));

    /// <summary>计算下一首的下标；-1 表示应当停止播放。</summary>
    public int NextIndex(TrackAdvanceReason reason)
    {
        if (_items.Count == 0)
        {
            return -1;
        }

        // 单曲循环：只有“自然播完”才回到同一首，用户点“下一首”仍然换歌。
        if (_mode == PlaybackMode.RepeatOne && reason == TrackAdvanceReason.TrackFinished)
        {
            return CurrentIndex;
        }

        switch (_mode)
        {
            case PlaybackMode.Sequential:
            {
                int next = CurrentIndex + 1;
                return next < _items.Count ? next : -1;
            }

            case PlaybackMode.RepeatAll:
            {
                return (CurrentIndex + 1) % _items.Count;
            }

            case PlaybackMode.Shuffle:
            {
                if (_shuffleOrder.Length != _items.Count)
                {
                    Reshuffle();
                }

                int lastPlayed = CurrentIndex;
                _shufflePosition++;

                if (_shufflePosition >= _shuffleOrder.Length)
                {
                    // 一轮放完，重新洗牌，并尽量避免紧接着重复上一首。
                    Reshuffle(avoidFirst: lastPlayed);
                    _shufflePosition = 0;
                }

                return _shuffleOrder[_shufflePosition];
            }

            default:
            {
                int next = CurrentIndex + 1;
                return next < _items.Count ? next : 0;
            }
        }
    }

    /// <summary>上一首的下标；第一首时会返回当前下标（重新播放当前曲目）。</summary>
    public int PreviousIndex()
    {
        if (_items.Count == 0)
        {
            return -1;
        }

        if (_mode == PlaybackMode.Shuffle && _shuffleOrder.Length == _items.Count)
        {
            _shufflePosition = Math.Max(0, _shufflePosition - 1);
            return _shuffleOrder[_shufflePosition];
        }

        return CurrentIndex <= 0 ? CurrentIndex : CurrentIndex - 1;
    }

    /// <summary>当前的随机播放顺序（副本，供测试与调试）。</summary>
    public IReadOnlyList<int> ShuffleOrder => _shuffleOrder;

    private void Reshuffle(int? avoidFirst = null)
    {        _shuffleOrder = new int[_items.Count];

        for (int i = 0; i < _shuffleOrder.Length; i++)
        {
            _shuffleOrder[i] = i;
        }

        for (int i = _shuffleOrder.Length - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (_shuffleOrder[i], _shuffleOrder[j]) = (_shuffleOrder[j], _shuffleOrder[i]);
        }

        if (avoidFirst is int avoid && _shuffleOrder.Length > 1 && _shuffleOrder[0] == avoid)
        {
            int swapWith = 1 + _random.Next(_shuffleOrder.Length - 1);
            (_shuffleOrder[0], _shuffleOrder[swapWith]) = (_shuffleOrder[swapWith], _shuffleOrder[0]);
        }

        _shufflePosition = -1;
    }

    /// <summary>
    /// 把随机顺序旋转成“当前曲目排在第一位”，这样一轮随机播放会依次走完其余曲目，
    /// 不会因为当前曲目恰好在序列中间而提前重新洗牌。
    /// </summary>
    private void AlignShuffleToCurrent()
    {
        if (_shuffleOrder.Length == 0 || CurrentIndex < 0)
        {
            _shufflePosition = -1;
            return;
        }

        int position = Array.IndexOf(_shuffleOrder, CurrentIndex);

        if (position > 0)
        {
            int[] rotated = new int[_shuffleOrder.Length];

            for (int i = 0; i < _shuffleOrder.Length; i++)
            {
                rotated[i] = _shuffleOrder[(position + i) % _shuffleOrder.Length];
            }

            _shuffleOrder = rotated;
        }

        _shufflePosition = 0;
    }
}
