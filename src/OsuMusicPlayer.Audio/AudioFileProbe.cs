namespace OsuMusicPlayer.Audio;

/// <summary>不打开输出设备、只读取音频头信息（用于时长分析与自检）。</summary>
public static class AudioFileProbe
{
    /// <summary>尝试读取音频时长。</summary>
    public static bool TryGetDuration(string filePath, out TimeSpan duration, out string? error)
    {
        duration = TimeSpan.Zero;
        error = null;

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            error = "音频文件不存在。";
            return false;
        }

        try
        {
            using NAudio.Wave.WaveStream stream = AudioReaderFactory.Create(filePath);
            duration = stream.TotalTime;

            if (duration <= TimeSpan.Zero)
            {
                error = "无法确定音频时长。";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>读取音频格式描述（自检输出用）。</summary>
    public static string? DescribeFormat(string filePath)
    {
        try
        {
            using NAudio.Wave.WaveStream stream = AudioReaderFactory.Create(filePath);

            return $"{stream.WaveFormat.SampleRate} Hz / {stream.WaveFormat.Channels} ch / {stream.WaveFormat.BitsPerSample} bit";
        }
        catch (Exception)
        {
            return null;
        }
    }
}
