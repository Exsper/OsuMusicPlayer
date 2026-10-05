namespace OsuMusicPlayer.Core.IO;

/// <summary>在谱面集文件夹里寻找可播放的音频文件（osu!.db 中记录的名称失效时的回退方案）。</summary>
public static class AudioFileLocator
{
    /// <summary>本播放器可直接解码的音频扩展名，按优先级排列。</summary>
    public static readonly string[] PlayableExtensions = [".mp3", ".ogg", ".wav", ".aiff", ".aif"];

    public static bool IsPlayableExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string extension = Path.GetExtension(path);

        return PlayableExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>返回文件夹中优先级最高的音频文件完整路径，找不到返回 null。</summary>
    public static string? FindAudioFile(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        string[] files;

        try
        {
            files = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        foreach (string extension in PlayableExtensions)
        {
            string? match = files.FirstOrDefault(
                file => string.Equals(Path.GetExtension(file), extension, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}
