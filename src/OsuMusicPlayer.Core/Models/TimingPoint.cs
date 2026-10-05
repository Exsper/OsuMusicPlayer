namespace OsuMusicPlayer.Core.Models;

/// <summary>osu!.db 中的一条 timing point。</summary>
/// <param name="BpmDuration">单拍时长（毫秒）。</param>
/// <param name="Offset">时间轴偏移（毫秒）。</param>
/// <param name="InheritsBpm">是否是继承（绿线）时间点。</param>
public sealed record TimingPoint(double BpmDuration, double Offset, bool InheritsBpm);
