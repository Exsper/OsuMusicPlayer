namespace OsuMusicPlayer.Core.IO;

/// <summary>
/// 定位 osu! stable 安装目录（进程 → 注册表文件关联 → 常见安装位置）。
/// 思路参考 CollectionManager（MIT）的 OsuPathResolver。
/// </summary>
public static class OsuPathLocator
{
    public const string DatabaseFileName = "osu!.db";

    /// <summary>目录是否为 osu! stable 安装目录（含 osu!.db）。</summary>
    public static bool IsStableInstall(string? directory)
        => !string.IsNullOrWhiteSpace(directory) && File.Exists(Path.Combine(directory, DatabaseFileName));

    /// <summary>依次尝试：正在运行的 osu!、注册表、常见安装目录。</summary>
    public static string? FindStableInstall()
    {
        string? fromProcess = FindFromRunningProcess();

        if (IsStableInstall(fromProcess))
        {
            return fromProcess;
        }

        string? fromRegistry = FindFromRegistry();

        if (IsStableInstall(fromRegistry))
        {
            return fromRegistry;
        }

        foreach (string candidate in GetCommonInstallDirectories())
        {
            if (IsStableInstall(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>如果 osu! 正在运行，从进程路径推断安装目录。</summary>
    public static string? FindFromRunningProcess()
    {
        try
        {
            foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcessesByName("osu!"))
            {
                try
                {
                    string? fileName = process.MainModule?.FileName;

                    if (string.IsNullOrEmpty(fileName))
                    {
                        continue;
                    }

                    string? directory = Path.GetDirectoryName(fileName);

                    if (IsStableInstall(directory))
                    {
                        return directory;
                    }
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // 32/64 位或权限问题，忽略该进程。
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // 无法枚举进程，忽略。
        }

        return null;
    }

    /// <summary>从 .osz 文件关联（HKCR\osustable.File.osz）推断安装目录。</summary>
    public static string? FindFromRegistry()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using Microsoft.Win32.RegistryKey? key =
                Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"osustable.File.osz\Shell\Open\Command");

            if (key?.GetValue(null) is not string command || command.Length < 2)
            {
                return null;
            }

            // 形如： "C:\osu!\osu!.exe" "%1"
            string exePath = command[1..].Replace("\" \"%1\"", string.Empty, StringComparison.Ordinal);
            string? directory = Path.GetDirectoryName(exePath);

            return IsStableInstall(directory) ? directory : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>常见的 osu! 安装目录候选。</summary>
    public static IEnumerable<string> GetCommonInstallDirectories()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        List<string> candidates =
        [
            Path.Combine(localAppData, "osu!"),
            @"C:\osu!",
            @"D:\osu!",
            @"E:\osu!",
            Path.Combine(programFiles, "osu!"),
        ];

        if (!string.IsNullOrEmpty(programFilesX86))
        {
            candidates.Add(Path.Combine(programFilesX86, "osu!"));
        }

        return candidates.Where(static path => !string.IsNullOrWhiteSpace(path));
    }
}
