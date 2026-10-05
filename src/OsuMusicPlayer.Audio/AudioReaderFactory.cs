namespace OsuMusicPlayer.Audio;

using NAudio.Vorbis;
using NAudio.Wave;

/// <summary>根据扩展名创建合适的音频读取器（.ogg 用 Vorbis，其余交给 NAudio 的 AudioFileReader）。</summary>
public static class AudioReaderFactory
{
    /// <summary>创建可定位的 <see cref="WaveStream"/>；失败时抛出异常由调用方包装。</summary>
    public static WaveStream Create(string filePath)
    {
        string extension = Path.GetExtension(filePath);

        if (extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase))
        {
            return new VorbisWaveReader(filePath);
        }

        // AudioFileReader 支持 mp3 / wav / aiff（并自动处理采样格式转换）。
        return new AudioFileReader(filePath);
    }
}
