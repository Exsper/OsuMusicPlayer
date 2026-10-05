namespace OsuMusicPlayer.Core.IO;

using OsuMusicPlayer.Core.Models;

/// <summary>
/// 读写 osu! stable 的 <c>collection.db</c>（收藏夹）。
/// 文件格式：int32 版本时间戳、int32 收藏夹数量，随后每个收藏夹是
/// “字符串名称、int32 谱面数量、若干字符串 MD5”。
/// 参考 CollectionManager（MIT）的 OsuCollectionHandler。
/// </summary>
public sealed class OsuCollectionReader
{
    private const int MaxCollections = 1_000_000;
    private const int MaxBeatmapsPerCollection = 1_000_000;

    public IReadOnlyList<OsuCollectionData> Read(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("找不到指定的收藏夹文件。", filePath);
        }

        using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        return Read(stream);
    }

    public IReadOnlyList<OsuCollectionData> Read(Stream stream)
    {
        using OsuBinaryReader reader = new(stream, System.Text.Encoding.UTF8);

        List<OsuCollectionData> collections = [];

        try
        {
            _ = reader.ReadInt32();                 // 最后保存时间（ticks）
            int count = reader.ReadInt32();

            if (count < 0 || count > MaxCollections)
            {
                throw new InvalidCollectionFileException($"收藏夹数量无效（{count}）。");
            }

            for (int i = 0; i < count; i++)
            {
                string name = reader.ReadString();
                int beatmapCount = reader.ReadInt32();

                if (beatmapCount < 0 || beatmapCount > MaxBeatmapsPerCollection)
                {
                    throw new InvalidCollectionFileException($"收藏夹 \"{name}\" 的谱面数量无效（{beatmapCount}）。");
                }

                List<string> hashes = new(Math.Min(beatmapCount, 4096));

                for (int j = 0; j < beatmapCount; j++)
                {
                    string hash = reader.ReadString();

                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        hashes.Add(hash);
                    }
                }

                collections.Add(new OsuCollectionData(string.IsNullOrWhiteSpace(name) ? $"未命名收藏夹 {i + 1}" : name, hashes));
            }
        }
        catch (InvalidCollectionFileException)
        {
            throw;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or ArgumentException)
        {
            throw new InvalidCollectionFileException("该文件不是有效的 osu! 收藏夹（collection.db）。", ex);
        }

        return collections;
    }

    /// <summary>写出收藏夹文件（用于导出与测试）。</summary>
    public void Write(IEnumerable<OsuCollectionData> collections, string filePath)
    {
        using FileStream stream = new(filePath, FileMode.Create, FileAccess.Write);

        Write(collections, stream);
    }

    public void Write(IEnumerable<OsuCollectionData> collections, Stream stream)
    {
        List<OsuCollectionData> list = [.. collections];

        using OsuBinaryWriter writer = new(stream, System.Text.Encoding.UTF8);

        // osu! stable 这里写的是一个 int32 时间戳（版本号），不是 64 位 ticks。
        writer.Write(unchecked((int)DateTime.UtcNow.Ticks));
        writer.Write(list.Count);

        foreach (OsuCollectionData collection in list)
        {
            writer.Write(collection.Name);
            writer.Write(collection.BeatmapHashes.Count);

            foreach (string hash in collection.BeatmapHashes)
            {
                writer.Write(hash);
            }
        }
    }
}
