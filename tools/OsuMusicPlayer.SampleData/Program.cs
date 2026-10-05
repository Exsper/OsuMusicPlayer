namespace OsuMusicPlayer.SampleData;

/// <summary>
/// 示例数据生成工具：<c>OsuMusicPlayer.SampleData generate &lt;目录&gt;</c>。
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        string command = args[0].ToLowerInvariant();

        if (command is not ("generate" or "gen" or "g"))
        {
            Console.Error.WriteLine($"未知命令：{args[0]}");
            PrintHelp();
            return 2;
        }

        if (args.Length < 2)
        {
            Console.Error.WriteLine("请提供生成目录，例如：OsuMusicPlayer.SampleData generate .testdata\\sample-osu");
            return 2;
        }

        double seconds = 4.0;

        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] is "--seconds" or "-s" && i + 1 < args.Length && double.TryParse(args[i + 1], out double parsed))
            {
                seconds = parsed;
            }
        }

        try
        {
            SampleOsuFolderResult result = SampleOsuFolderGenerator.Generate(new SampleOsuFolderOptions
            {
                TargetDirectory = args[1],
                AudioSeconds = seconds,
            });

            Console.WriteLine("已生成示例 osu! 目录：");
            Console.WriteLine($"  osu! 目录   : {result.OsuDirectory}");
            Console.WriteLine($"  歌曲目录    : {result.SongsDirectory}");
            Console.WriteLine($"  谱面数量    : {result.BeatmapCount}");
            Console.WriteLine($"  预期曲目数  : {result.ExpectedTrackCount}（其中可播放 {result.ExpectedPlayableTrackCount}）");
            Console.WriteLine($"  收藏夹      : {string.Join("、", result.CollectionNames)}");
            Console.WriteLine();
            Console.WriteLine($"可以用它启动播放器：dotnet run --project src\\OsuMusicPlayer.App -- --osu-dir \"{result.OsuDirectory}\"");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"生成失败：{ex.Message}");
            return 1;
        }
    }

    private static bool IsHelp(string value) => value is "-h" or "--help" or "help" or "/?" or "-?";

    private static void PrintHelp()
    {
        Console.WriteLine("osu! 音乐播放器示例数据生成工具");
        Console.WriteLine();
        Console.WriteLine("用法：");
        Console.WriteLine("  OsuMusicPlayer.SampleData generate <目录> [--seconds <音频时长秒数>]");
        Console.WriteLine();
        Console.WriteLine("会生成 osu!.db、osu!sample.cfg、collection.db、Songs\\*.wav、*.osu 与谱面背景图，");
        Console.WriteLine("用于在没有安装 osu! 的机器上演示/测试本播放器。");
    }
}
