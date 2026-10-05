namespace OsuMusicPlayer.App;

using System.Globalization;
using System.Text;
using OsuMusicPlayer.Audio;
using OsuMusicPlayer.Core.Enums;
using OsuMusicPlayer.Core.IO;
using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Playback;
using OsuMusicPlayer.Core.Services;

/// <summary>
/// 无界面自检（<c>--self-test</c>）：一次性验证音乐库解析、曲目合并、搜索、
/// 收藏夹导入、播放列表持久化与音频解码是否正常，用于自动化验证与问题排查。
/// </summary>
internal static class SelfTest
{
    public static int Run(string? osuDirectory)
    {
        List<string> log = [];
        StringBuilder report = new();
        int failures = 0;

        void Write(string line)
        {
            log.Add(line);
            report.AppendLine(line);
            Console.WriteLine(line);
        }

        void Check(string name, bool condition, string? detail = null)
        {
            if (condition)
            {
                Write($"  [通过] {name}{(detail is null ? string.Empty : $" - {detail}")}");
            }
            else
            {
                failures++;
                Write($"  [失败] {name}{(detail is null ? string.Empty : $" - {detail}")}");
            }
        }

        Write("=== osu! 音乐播放器自检 ===");
        Write($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Write($"数据目录：{AppPaths.DataDirectory}");

        string? directory = osuDirectory;

        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = OsuPathLocator.FindStableInstall();
            Write($"未指定 --osu-dir，自动检测结果：{directory ?? "（未找到）"}");
        }

        if (string.IsNullOrWhiteSpace(directory) || !OsuPathLocator.IsStableInstall(directory))
        {
            Write(string.Empty);
            Write("找不到可用的 osu! stable 目录，无法自检。");
            Write("可以先用示例数据生成工具创建一份测试数据：");
            Write("  dotnet run --project tools\\OsuMusicPlayer.SampleData -- generate .testdata\\sample-osu");
            Write("  OsuMusicPlayer.exe --self-test --osu-dir .testdata\\sample-osu");
            SaveReport(report);
            return 2;
        }

        Write($"osu! 目录：{directory}");
        Write($"歌曲目录：{OsuConfigReader.ResolveSongsDirectory(directory)}");
        Write(string.Empty);

        // 1. 音乐库
        MusicLibrary library;

        try
        {
            MusicLibraryBuilder builder = new();
            library = builder.Build(directory, new CollectingProgress(message => Write($"  · {message}")));
        }
        catch (Exception ex)
        {
            Write($"音乐库扫描失败：{ex.Message}");
            SaveReport(report);
            return 1;
        }

        Write("1) 音乐库解析与谱面集合并");
        Check("读取到谱面", library.Statistics.BeatmapCount > 0, $"{library.Statistics.BeatmapCount} 张谱面");
        Check("生成曲目", library.Tracks.Count > 0, $"{library.Tracks.Count} 首（可播放 {library.Statistics.TracksWithAudio}）");
        Check("合并生效（曲目数少于谱面数）", library.Tracks.Count <= library.Statistics.BeatmapCount);

        MusicTrack? merged = library.Tracks.FirstOrDefault(static track => track.BeatmapCount > 1);
        Check("存在多难度合并的曲目", merged is not null, merged is null ? null : $"{merged.DisplayName}（{merged.BeatmapCount} 个难度）");

        MusicTrack? playable = library.Tracks.FirstOrDefault(static track => track.AudioFileExists);
        Check("存在可播放曲目", playable is not null, playable?.AudioFilePath);

        if (playable is null)
        {
            Write("没有可播放的曲目，跳过后续播放相关检查。");
            SaveReport(report);
            return failures == 0 ? 0 : 1;
        }

        Write(string.Empty);
        Write("2) 搜索");
        IReadOnlyList<MusicTrack> byArtist = TrackSearcher.Search(library.Tracks, new TrackSearchQuery { Text = playable.DisplayArtist });
        Check("按艺术家搜索", byArtist.Count > 0, $"“{playable.DisplayArtist}”命中 {byArtist.Count} 首");

        IReadOnlyList<MusicTrack> byTitle = TrackSearcher.Search(library.Tracks, new TrackSearchQuery { Text = $"title:\"{playable.DisplayTitle}\"" });
        Check("按标题精确搜索", byTitle.Count > 0, $"命中 {byTitle.Count} 首");

        IReadOnlyList<MusicTrack> onlyPlayable = TrackSearcher.Search(
            library.Tracks,
            new TrackSearchQuery { Text = string.Empty, OnlyPlayable = true });
        Check("仅可播放过滤", onlyPlayable.All(static track => track.AudioFileExists), $"{onlyPlayable.Count} 首");

        Write(string.Empty);
        Write("3) 播放列表");
        using (TempDirectoryScope temp = new())
        {
            string playlistFile = Path.Combine(temp.Path, "playlists.json");
            PlaylistStore store = new(playlistFile);
            store.Load();
            Playlist custom = store.Create("自检列表");
            int added = store.AddTracks(custom.Id, library.Tracks.Take(3).Select(static track => track.Id));
            store.ToggleFavorite(library.Tracks[0].Id);
            store.Save();

            PlaylistStore reloaded = new(playlistFile);
            reloaded.Load();

            Check("创建播放列表", reloaded.FindByName("自检列表") is not null);
            Check("写入并读回曲目", added == Math.Min(3, library.Tracks.Count) && reloaded.FindByName("自检列表")!.Count == added, $"{added} 首");
            Check("收藏功能", reloaded.IsFavorite(library.Tracks[0].Id));

            Write(string.Empty);
            Write("4) 收藏夹导入");
            string collectionFile = Path.Combine(directory, "collection.db");

            if (File.Exists(collectionFile))
            {
                IReadOnlyList<OsuCollectionData> collections = new OsuCollectionReader().Read(collectionFile);
                CollectionImporter importer = new(reloaded);
                CollectionImportResult result = importer.Import(collections, library, new CollectionImportOptions { SourceFile = collectionFile });

                Check("读取收藏夹", collections.Count > 0, $"{collections.Count} 个");
                Check("导入为播放列表", result.PlaylistsCreated + result.PlaylistsUpdated > 0,
                    $"新建 {result.PlaylistsCreated}，更新 {result.PlaylistsUpdated}，曲目 {result.TracksAdded}，缺失谱面 {result.MissingBeatmaps}");
            }
            else
            {
                Write("  [跳过] 该目录没有 collection.db");
            }
        }

        Write(string.Empty);
        Write("5) 音频");
        Check("读取音频时长", AudioFileProbe.TryGetDuration(playable.AudioFilePath, out TimeSpan duration, out string? probeError),
            probeError ?? $"{playable.DisplayTitle} = {duration.TotalSeconds:0.0} 秒 / {AudioFileProbe.DescribeFormat(playable.AudioFilePath)}");

        using (NAudioPlayer player = new())
        {
            Check("音频输出设备", player.OutputAvailable, player.OutputAvailable ? "可用" : "未检测到（不影响浏览与搜索）");

            try
            {
                player.Volume = 0.05f;
                player.Load(playable.AudioFilePath);
                Check("打开音频文件", player.Duration > 0, $"{player.Duration:0.0} 秒");

                player.Seek(Math.Min(1.0, player.Duration / 2));
                Check("定位播放位置", player.Position > 0, $"{player.Position:0.00} 秒");

                if (player.OutputAvailable)
                {
                    player.Play();
                    Thread.Sleep(250);
                    double advanced = player.Position;
                    player.Stop();
                    Check("实际播放推进", advanced > 0.05, $"播放 250ms 后位置 {advanced:0.00} 秒");
                }
            }
            catch (Exception ex)
            {
                Check("音频播放流程", false, ex.Message);
            }
        }

        Write(string.Empty);
        Write(failures == 0 ? "自检结果：全部通过 ✅" : $"自检结果：{failures} 项失败 ❌");

        SaveReport(report);

        return failures == 0 ? 0 : 1;
    }

    private static void SaveReport(StringBuilder report)
    {
        try
        {
            AppPaths.EnsureCreated();
            string path = Path.Combine(AppPaths.DataDirectory, "self-test-report.txt");
            File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
            Console.WriteLine($"报告已保存：{path}");
        }
        catch (Exception)
        {
            // 忽略。
        }
    }

    private sealed class CollectingProgress(Action<string> report) : IProgress<LibraryBuildProgress>
    {
        private int _lastPercent = -10;

        public void Report(LibraryBuildProgress value)
        {
            if (value.Percent >= _lastPercent + 25 || value.Percent >= 100)
            {
                _lastPercent = value.Percent;
                report($"{value.Stage}（{value.Percent.ToString(CultureInfo.InvariantCulture)}%）：{value.Message}");
            }
        }
    }

    private sealed class TempDirectoryScope : IDisposable
    {
        public TempDirectoryScope()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "osump-selftest-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
                // 忽略。
            }
        }
    }
}
