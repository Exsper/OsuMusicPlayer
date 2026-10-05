namespace OsuMusicPlayer.Core.Tests;

using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Services;
using Xunit;

/// <summary>
/// 重新扫描（F5）时的内存行为。构建一份音乐库会一次性产生大量临时对象
/// （<c>osu!.db</c> 的全部谱面、timing points、合并累加器，以及上一份音乐库）。
/// 界面在换上新库之后调用 <see cref="LibraryScanMemory.ReleaseAfterScan"/> 立即回收它们；
/// 否则这些垃圾会赖在堆上，表现为“扫描完内存涨到几百 MB，再按一次 F5 又翻一倍”。
/// </summary>
[Collection("sample")]
public sealed class LibraryRebuildMemoryTests(SampleLibraryFixture fixture)
{
    /// <summary>示例数据很小，这个阈值只用于发现“每次扫描都固定留下几百 MB”的回归。</summary>
    private const long GrowthBudgetBytes = 64L * 1024 * 1024;

    [Fact]
    public void 反复扫描后托管堆不应持续增长()
    {
        string osuDirectory = fixture.Sample.OsuDirectory;

        // 第一次构建建立“基线”：构建后的残留垃圾。
        new MusicLibraryBuilder().Build(osuDirectory);
        long baseline = MeasureAfterRelease();

        // 再连续扫描两次：如果旧库或临时对象被某个引用钉住，堆会持续增长。
        for (int i = 0; i < 2; i++)
        {
            new MusicLibraryBuilder().Build(osuDirectory);
        }

        long after = MeasureAfterRelease();

        Assert.True(
            after - baseline <= GrowthBudgetBytes,
            $"连续扫描后托管堆增长了 {(after - baseline) / 1024 / 1024} MB（基线 {baseline / 1024 / 1024} MB，"
            + $"现在 {after / 1024 / 1024} MB），说明扫描产生的临时对象没有被回收。");
    }

    [Fact]
    public void 完整回收不应影响当前音乐库可用()
    {
        MusicLibraryBuilder builder = new();
        MusicTrack firstTrack = builder.Build(fixture.Sample.OsuDirectory).Tracks[0];

        // 只留下标识字符串，不持有旧库里的任何对象。
        string trackId = firstTrack.Id;
        string beatmapHash = firstTrack.BeatmapHashes[0];

        LibraryScanMemory.ReleaseAfterScan();

        MusicLibrary current = builder.Build(fixture.Sample.OsuDirectory);

        Assert.Equal(fixture.Sample.ExpectedTrackCount, current.Tracks.Count);
        Assert.NotNull(current.FindById(trackId));
        Assert.NotNull(current.FindByBeatmapHash(beatmapHash));
    }

    private static long MeasureAfterRelease()
    {
        LibraryScanMemory.ReleaseAfterScan();

        return GC.GetTotalMemory(forceFullCollection: true);
    }
}
