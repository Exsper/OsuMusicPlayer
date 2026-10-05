namespace OsuMusicPlayer.Core.IO;

/// <summary>
/// 读取 osu! stable 的用户配置 <c>osu!&lt;用户名&gt;.cfg</c>，
/// 用于解析自定义的谱面目录（BeatmapDirectory）。
/// </summary>
public static class OsuConfigReader
{
    private const string DefaultSongsDirectoryName = "Songs";
    private const string LazerSongsDirectoryName = "files";

    /// <summary>解析谱面歌曲目录的绝对路径。</summary>
    public static string ResolveSongsDirectory(string osuDirectory)
    {
        List<string> candidates = [];

        string? configured = TryGetBeatmapDirectory(osuDirectory);

        if (!string.IsNullOrWhiteSpace(configured))
        {
            candidates.Add(Path.IsPathRooted(configured) ? configured : Path.Combine(osuDirectory, configured));
        }

        candidates.Add(Path.Combine(osuDirectory, DefaultSongsDirectoryName));
        candidates.Add(Path.Combine(osuDirectory, LazerSongsDirectoryName));

        foreach (string candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        // 目录还不存在时返回默认值，让调用方决定是否提示。
        return Path.Combine(osuDirectory, DefaultSongsDirectoryName);
    }

    /// <summary>从配置文件中读取 BeatmapDirectory 设置项。</summary>
    public static string? TryGetBeatmapDirectory(string osuDirectory)
    {
        string? configPath = FindConfigFile(osuDirectory);

        if (configPath is null)
        {
            return null;
        }

        try
        {
            foreach (string line in File.ReadLines(configPath))
            {
                if (!line.StartsWith("BeatmapDirectory", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int separator = line.IndexOf('=');

                if (separator < 0)
                {
                    continue;
                }

                string value = line[(separator + 1)..].Trim();

                return value.Length == 0 ? null : value;
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// 查找配置文件：优先 <c>osu!&lt;当前用户名&gt;.cfg</c>，否则使用目录下任意 <c>osu!*.cfg</c>。
    /// </summary>
    public static string? FindConfigFile(string osuDirectory)
    {
        if (string.IsNullOrWhiteSpace(osuDirectory) || !Directory.Exists(osuDirectory))
        {
            return null;
        }

        string userName = Environment.UserName;
        string? sanitized = userName?.Replace(".", string.Empty);

        if (!string.IsNullOrWhiteSpace(sanitized))
        {
            string expected = Path.Combine(osuDirectory, $"osu!{sanitized}.cfg");

            if (File.Exists(expected))
            {
                return expected;
            }
        }

        try
        {
            return Directory
                .EnumerateFiles(osuDirectory, "osu!*.cfg", SearchOption.TopDirectoryOnly)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
    }
}
