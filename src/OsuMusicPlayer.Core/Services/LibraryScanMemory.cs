namespace OsuMusicPlayer.Core.Services;

/// <summary>
/// 扫描音乐库相关的内存回收。
/// <para>
/// <see cref="MusicLibraryBuilder.Build(string, IProgress{LibraryBuildProgress}, CancellationToken)"/>
/// 会一次性读取整份 <c>osu!.db</c>：几万张谱面、每张谱面的 timing points、合并用的累加器，
/// 以及上一份音乐库，这些在扫描结束后都变成垃圾。但它们只占当时存活对象的很小一部分，
/// GC 往往要等到堆再涨几百 MB 才回收，表现为任务管理器里内存先涨到几百 MB ~ 1 GB、
/// 过一阵又自己掉下去（看起来像泄漏，其实不是）。
/// </para>
/// <para>
/// 因此界面在“新库已装好、旧库已松开”之后调用 <see cref="ReleaseAfterScan"/>，
/// 立刻把这些临时对象还给系统，代价只是一次几百毫秒的完整 GC。
/// </para>
/// </summary>
public static class LibraryScanMemory
{
    /// <summary>完整回收一代到二代的所有可回收对象（阻塞式、压缩大对象堆）。</summary>
    public static void ReleaseAfterScan()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        // 先等终结器队列清空，再收一次，避免刚提升的对象又活到下一轮。
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }
}
