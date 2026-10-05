namespace OsuMusicPlayer.App;

using System.Runtime.InteropServices;

/// <summary>程序入口。</summary>
internal static class Program
{
    private const int AttachParentProcess = -1;

    [STAThread]
    private static int Main(string[] args)
    {
        CommandLineOptions options = CommandLineOptions.Parse(args);

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        // 必须在任何 AppPaths 访问之前设置，确保自检 / 截图不会动到真实数据目录。
        if (!string.IsNullOrWhiteSpace(options.DataDirectory))
        {
            try
            {
                Environment.SetEnvironmentVariable(
                    AppPaths.DataDirectoryEnvironmentVariable,
                    Path.GetFullPath(options.DataDirectory));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                Console.Error.WriteLine($"指定的数据目录无效：{options.DataDirectory}（{ex.Message}）");
                return 2;
            }
        }

        if (options.SelfTest)
        {
            AttachConsole(AttachParentProcess);
            return SelfTest.Run(options.OsuDirectory);
        }

        if (options.ScreenshotPath is not null)
        {
            AttachConsole(AttachParentProcess);
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception);

        try
        {
            Application.Run(new MainForm(options));
            return 0;
        }
        catch (Exception ex)
        {
            ReportCrash(ex);
            MessageBox.Show(
                $"启动失败：{ex.Message}{Environment.NewLine}{Environment.NewLine}{ex}",
                "osu! 音乐播放器",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            return 1;
        }
    }

    private static void ReportCrash(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            AppPaths.EnsureCreated();
            string path = Path.Combine(AppPaths.DataDirectory, "crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // 记录失败时忽略。
        }

        Console.Error.WriteLine(exception);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("osu! 音乐播放器 - 把 osu! stable 的音乐按谱面集合并后播放");
        Console.WriteLine();
        Console.WriteLine("用法：");
        Console.WriteLine("  OsuMusicPlayer.exe [--osu-dir <osu! 安装目录>]");
        Console.WriteLine("  OsuMusicPlayer.exe --self-test [--osu-dir <目录>] [--data-dir <数据目录>]");
        Console.WriteLine("  OsuMusicPlayer.exe --screenshot <输出图片> [--osu-dir <目录>] [--data-dir <数据目录>]");
        Console.WriteLine();
        Console.WriteLine("参数：");
        Console.WriteLine("  -d, --osu-dir <目录>   启动时直接扫描该 osu! stable 目录（含 osu!.db）");
        Console.WriteLine("      --data-dir <目录>  使用指定的数据目录（设置/播放列表/时长缓存），");
        Console.WriteLine($"                         默认 {AppPaths.DataDirectory}；也可用环境变量 {AppPaths.DataDirectoryEnvironmentVariable} 指定");
        Console.WriteLine("      --self-test        无界面自检：扫描音乐库、搜索、导入收藏夹、播放列表读写与音频探测");
        Console.WriteLine("  -s, --screenshot <文件> 启动后加载音乐库、截图并退出（用于界面验证）");
        Console.WriteLine("      --reset            忽略已保存的设置与窗口位置");
        Console.WriteLine("  -h, --help             显示本帮助");
        Console.WriteLine();
        Console.WriteLine($"数据目录：{AppPaths.DataDirectory}");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);
}
