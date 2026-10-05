namespace OsuMusicPlayer.Core;

/// <summary>本程序所有自定义异常的基类。</summary>
public abstract class OsuMusicPlayerException : Exception
{
    protected OsuMusicPlayerException(string message) : base(message)
    {
    }

    protected OsuMusicPlayerException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>指定的目录不是 osu! stable 安装目录（缺少 osu!.db）。</summary>
public sealed class OsuDatabaseNotFoundException(string path)
    : OsuMusicPlayerException($"在 \"{path}\" 中找不到 osu!.db，请确认选择的是 osu! stable 安装目录。")
{
    public string Path { get; } = path;
}

/// <summary>osu!.db 版本过旧或内容损坏。</summary>
public sealed class InvalidOsuDatabaseException(string message, Exception? innerException = null)
    : OsuMusicPlayerException(message, innerException);

/// <summary>collection.db 不是有效的 osu! 收藏夹文件。</summary>
public sealed class InvalidCollectionFileException(string message, Exception? innerException = null)
    : OsuMusicPlayerException(message, innerException);

/// <summary>音频文件无法打开或解码。</summary>
public sealed class AudioLoadException : OsuMusicPlayerException
{
    public AudioLoadException(string message, string filePath, Exception? innerException = null)
        : base(message, innerException)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }
}
