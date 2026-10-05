namespace OsuMusicPlayer.Core.Enums;

/// <summary>
/// 列表中“标题 / 艺术家”的显示写法。
/// osu!.db 同时保存罗马化（拉丁）字段与原文（Unicode）字段，本设置决定优先显示哪一个。
/// </summary>
public enum TrackNameDisplay
{
    /// <summary>罗马化（拉丁）写法，缺失时回退到原文。这是 osu! 客户端主界面与旧版本的默认行为。</summary>
    Romanized = 0,

    /// <summary>原文（Unicode，例如日文/中文），缺失时回退到罗马化写法。</summary>
    Original = 1,
}
