namespace OsuMusicPlayer.App;

using OsuMusicPlayer.Core.Enums;
using OsuMusicPlayer.Core.Services;

/// <summary>程序的数据目录（设置、播放列表、时长缓存）。</summary>
public static class AppPaths
{
    /// <summary>可用环境变量覆盖数据目录（自检 / 截图 / 多份配置时使用，避免动到真实数据）。</summary>
    public const string DataDirectoryEnvironmentVariable = "OSUMP_DATA_DIR";

    public static string DataDirectory { get; } = ResolveDataDirectory();

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string PlaylistsFile => Path.Combine(DataDirectory, "playlists.json");

    public static string DurationsFile => Path.Combine(DataDirectory, "durations.json");

    public static void EnsureCreated() => Directory.CreateDirectory(DataDirectory);

    private static string ResolveDataDirectory()
    {
        string? custom = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(custom))
        {
            try
            {
                return Path.GetFullPath(custom);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // 路径非法时退回默认目录。
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OsuMusicPlayer");
    }
}

/// <summary>界面设置（持久化到 <see cref="AppPaths.SettingsFile"/>）。</summary>
public sealed class AppSettings
{
    public string? OsuDirectory { get; set; }

    public float Volume { get; set; } = 0.8f;

    public PlaybackMode PlaybackMode { get; set; } = PlaybackMode.RepeatAll;

    /// <summary>搜索时是否隐藏音频缺失的曲目。</summary>
    public bool OnlyPlayable { get; set; } = true;

    public SearchField SearchField { get; set; } = SearchField.All;

    /// <summary>列表与“正在播放”里标题 / 艺术家使用罗马化写法还是原文（Unicode）。</summary>
    public TrackNameDisplay NameDisplay { get; set; } = TrackNameDisplay.Romanized;

    public string? LastPlaylistId { get; set; }

    public TrackSortColumn SortColumn { get; set; } = TrackSortColumn.Artist;

    public bool SortAscending { get; set; } = true;

    /// <summary>播放时从 osu! 的试听时间点开始（默认从头播放）。</summary>
    public bool PlayFromPreviewPoint { get; set; }

    public int WindowWidth { get; set; } = 1280;

    public int WindowHeight { get; set; } = 800;

    public int WindowX { get; set; } = int.MinValue;

    public int WindowY { get; set; } = int.MinValue;

    public bool WindowMaximized { get; set; }

    public bool ShowCover { get; set; } = true;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}

/// <summary>设置的读写。</summary>
public sealed class SettingsStore(string? filePath = null)
{
    public string FilePath { get; } = filePath ?? AppPaths.SettingsFile;

    public AppSettings Load()
    {
        try
        {
            return Core.IO.JsonFile.Load<AppSettings>(FilePath) ?? new AppSettings();
        }
        catch (InvalidDataException)
        {
            // 设置文件损坏时回到默认值，不影响启动。
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Core.IO.JsonFile.Save(FilePath, settings);
        }
        catch (IOException)
        {
            // 保存失败不阻塞退出。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>命令行选项。</summary>
public sealed record CommandLineOptions
{
    public string? OsuDirectory { get; init; }

    /// <summary>覆盖数据目录（设置 / 播放列表 / 时长缓存）。</summary>
    public string? DataDirectory { get; init; }

    public bool SelfTest { get; init; }

    public string? ScreenshotPath { get; init; }

    public bool ShowHelp { get; init; }

    public bool ResetSettings { get; init; }

    public static CommandLineOptions Parse(string[] args)
    {
        string? osuDirectory = null;
        string? dataDirectory = null;
        string? screenshot = null;
        bool selfTest = false;
        bool help = false;
        bool reset = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            switch (arg)
            {
                case "-h":
                case "--help":
                case "/?":
                    help = true;
                    break;

                case "--self-test":
                case "--selftest":
                    selfTest = true;
                    break;

                case "--reset":
                    reset = true;
                    break;

                case "-d":
                case "--osu-dir":
                case "--osu":
                    if (i + 1 < args.Length)
                    {
                        osuDirectory = args[++i];
                    }

                    break;

                case "--data-dir":
                case "--data":
                    if (i + 1 < args.Length)
                    {
                        dataDirectory = args[++i];
                    }

                    break;

                case "-s":
                case "--screenshot":
                    if (i + 1 < args.Length)
                    {
                        screenshot = args[++i];
                    }

                    break;

                default:
                    if (!arg.StartsWith('-') && osuDirectory is null)
                    {
                        osuDirectory = arg;
                    }

                    break;
            }
        }

        return new CommandLineOptions
        {
            OsuDirectory = osuDirectory,
            DataDirectory = dataDirectory,
            SelfTest = selfTest,
            ScreenshotPath = screenshot,
            ShowHelp = help,
            ResetSettings = reset,
        };
    }
}
