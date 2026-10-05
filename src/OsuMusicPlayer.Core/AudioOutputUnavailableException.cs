namespace OsuMusicPlayer.Core;

/// <summary>没有可用的音频输出设备（例如无声卡、远程会话）。</summary>
public sealed class AudioOutputUnavailableException(string message)
    : OsuMusicPlayerException(message);
