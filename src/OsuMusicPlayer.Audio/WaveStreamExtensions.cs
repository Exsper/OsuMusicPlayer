namespace OsuMusicPlayer.Audio;

using NAudio.Wave;

/// <summary>WaveStream 定位辅助（参考 CollectionManager（MIT）的 WaveStreamExtensions）。</summary>
public static class WaveStreamExtensions
{
    /// <summary>把位置对齐到采样块边界并裁剪到合法范围。</summary>
    public static void SetPosition(this WaveStream stream, long position)
    {
        long adjustment = position % stream.WaveFormat.BlockAlign;
        long newPosition = Math.Max(0, Math.Min(stream.Length, position - adjustment));

        stream.Position = newPosition;
    }

    /// <summary>按秒定位。</summary>
    public static void SetPosition(this WaveStream stream, double seconds)
        => stream.SetPosition((long)(seconds * stream.WaveFormat.AverageBytesPerSecond));

    public static void SetPosition(this WaveStream stream, TimeSpan time) => stream.SetPosition(time.TotalSeconds);

    /// <summary>相对当前位置偏移。</summary>
    public static void Skip(this WaveStream stream, double offset)
        => stream.SetPosition(stream.Position + (long)(offset * stream.WaveFormat.AverageBytesPerSecond));
}
