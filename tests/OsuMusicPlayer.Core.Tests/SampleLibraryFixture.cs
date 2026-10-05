namespace OsuMusicPlayer.Core.Tests;

using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Services;
using OsuMusicPlayer.SampleData;
using Xunit;

/// <summary>一次性生成示例 osu! 目录并构建音乐库，供所有测试类共享（xunit 集合夹具）。</summary>
public sealed class SampleLibraryFixture : IDisposable
{
    public SampleLibraryFixture()
    {
        Directory = Path.Combine(Path.GetTempPath(), "osump-sample-" + Guid.NewGuid().ToString("N")[..8]);

        Sample = SampleOsuFolderGenerator.Generate(new SampleOsuFolderOptions
        {
            TargetDirectory = Directory,
            AudioSeconds = 2.0,
        });

        Library = new MusicLibraryBuilder().Build(Sample.OsuDirectory);
    }

    public string Directory { get; }

    public SampleOsuFolderResult Sample { get; }

    public MusicLibrary Library { get; }

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // 测试清理失败不影响结果。
        }
    }

    public MusicTrack TrackOfMapSet(int mapSetId, int index = 0)
        => Library.FindByMapSetId(mapSetId)[index];
}

[CollectionDefinition("sample")]
public sealed class SampleCollection : ICollectionFixture<SampleLibraryFixture>;

/// <summary>带自动清理的临时目录。</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory(string prefix = "osump-test")
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            prefix + "-" + Guid.NewGuid().ToString("N")[..8]);

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

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
