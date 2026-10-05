namespace OsuMusicPlayer.Core.Enums;

/// <summary>播放顺序模式。</summary>
public enum PlaybackMode
{
    /// <summary>顺序播放：播放到列表末尾后停止。</summary>
    Sequential = 0,

    /// <summary>列表循环：播放到列表末尾后从头继续。</summary>
    RepeatAll = 1,

    /// <summary>单曲循环：一曲结束后重新播放同一首。</summary>
    RepeatOne = 2,

    /// <summary>随机播放：随机顺序遍历整个列表，遍历完再重新洗牌。</summary>
    Shuffle = 3,
}

/// <summary>请求切换曲目的原因。</summary>
public enum TrackAdvanceReason
{
    /// <summary>当前曲目自然播放结束。</summary>
    TrackFinished,

    /// <summary>用户主动点击“下一首”。</summary>
    UserRequested,

    /// <summary>当前曲目无法播放（文件缺失/解码失败），需要跳过。</summary>
    PlaybackFailed,
}
