namespace OsuMusicPlayer.Core.Services;

using System.Text;
using OsuMusicPlayer.Core.Enums;
using OsuMusicPlayer.Core.IO;
using OsuMusicPlayer.Core.Models;

/// <summary>
/// 音乐库磁盘缓存（<c>%AppData%\OsuMusicPlayer\library.bin</c> + <c>library.json</c>）。
/// <para>
/// 读取整份 <c>osu!.db</c> 并合并曲目需要十几秒（3 万首曲目、10 万张谱面），
/// 因此扫描一次后把结果存下来，下次启动直接读缓存，只有用户主动扫描、更换 osu! 目录
/// 或缓存失效时才重新扫描。
/// </para>
/// <para>
/// 缓存是否可用由 <c>osu!.db</c> 的“指纹”（路径、字节数、最后写入时间）决定：
/// 指纹一致就直接用，指纹变了才需要手动重新扫描。
/// </para>
/// </summary>
public sealed class LibraryCache
{
    /// <summary>缓存格式版本；字段或合并规则变化时递增，旧缓存会自动失效。</summary>
    public const int CurrentVersion = 1;

    private readonly string _headerPath;
    private readonly string? _payloadPath;

    public LibraryCache(string? cachePath)
    {
        if (string.IsNullOrWhiteSpace(cachePath))
        {
            _headerPath = string.Empty;
            _payloadPath = null;
            return;
        }

        _headerPath = Path.ChangeExtension(cachePath, ".json");
        _payloadPath = Path.ChangeExtension(cachePath, ".bin");
    }

    /// <summary>缓存是否可用（没有配置缓存路径时为 false）。</summary>
    public bool IsEnabled => _payloadPath is not null;

    /// <summary>缓存头文件路径（不存在时为 null）。</summary>
    public string? HeaderPath => _headerPath.Length > 0 ? _headerPath : null;

    public LibraryCacheLoadResult TryLoad(string osuDirectory)
    {
        if (_payloadPath is null)
        {
            return LibraryCacheLoadResult.NotCached("未启用音乐库缓存。");
        }

        LibraryCacheHeader? header = TryReadHeader();

        if (header is null)
        {
            return LibraryCacheLoadResult.NotCached("尚未缓存音乐库。");
        }

        if (header.Version != CurrentVersion)
        {
            return LibraryCacheLoadResult.NotCached($"缓存格式已过期（v{header.Version}）。");
        }

        if (!SameDirectory(header.OsuDirectory, osuDirectory))
        {
            return LibraryCacheLoadResult.NotCached("缓存来自另一个 osu! 目录。");
        }

        string databasePath = Path.Combine(osuDirectory, OsuPathLocator.DatabaseFileName);
        FileFingerprint current = FileFingerprint.Of(databasePath);

        if (!current.Exists)
        {
            return LibraryCacheLoadResult.NotCached($"找不到 {OsuPathLocator.DatabaseFileName}。");
        }

        if (!header.Database.Matches(current))
        {
            // osu! 更新过谱面库：先照常快速启动，由用户按 F5 决定何时重新扫描。
            bool cached = TryReadLibrary(header, out MusicLibrary? library);

            return cached && library is not null
                ? new LibraryCacheLoadResult(library, LibraryCacheFreshness.Outdated, "osu!.db 的修改时间或大小与缓存时不同")
                : LibraryCacheLoadResult.NotCached("缓存已失效，需要重新扫描。");
        }

        return TryReadLibrary(header, out MusicLibrary? fresh) && fresh is not null
            ? new LibraryCacheLoadResult(fresh, LibraryCacheFreshness.Current, null)
            : LibraryCacheLoadResult.NotCached("缓存已失效，需要重新扫描。");
    }

    /// <summary>写入缓存；写失败不影响使用（只是下次启动仍要重新扫描）。</summary>
    /// <returns>是否写入成功。</returns>
    public bool Save(string osuDirectory, MusicLibrary library)
    {
        if (_payloadPath is null || _headerPath.Length == 0)
        {
            return false;
        }

        try
        {
            WritePayload(_payloadPath, library);

            FileFingerprint fingerprint = FileFingerprint.Of(
                Path.Combine(osuDirectory, OsuPathLocator.DatabaseFileName));

            LibraryCacheHeader header = new(
                CurrentVersion,
                DateTime.Now,
                osuDirectory,
                library.SongsDirectory,
                fingerprint,
                library.Statistics);

            JsonFile.Save(_headerPath, header);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>删除缓存文件（下次启动会重新扫描）。</summary>
    public void Clear()
    {
        foreach (string path in new[] { _headerPath, _payloadPath ?? string.Empty })
        {
            if (path.Length == 0)
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 删不掉就算了：下次启动会因为格式或指纹不匹配而重新扫描。
            }
        }
    }

    private LibraryCacheHeader? TryReadHeader()
    {
        try
        {
            return JsonFile.Load<LibraryCacheHeader>(_headerPath);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private bool TryReadLibrary(LibraryCacheHeader header, out MusicLibrary? library)
    {
        library = null;

        if (_payloadPath is null || !File.Exists(_payloadPath))
        {
            return false;
        }

        try
        {
            using FileStream stream = new(_payloadPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: false);

            if (reader.ReadInt32() != header.Version)
            {
                return false;
            }

            int count = reader.ReadInt32();

            if (count < 0 || count != header.TrackCount)
            {
                return false;
            }

            List<MusicTrack> tracks = new(count);

            for (int i = 0; i < count; i++)
            {
                tracks.Add(ReadTrack(reader));
            }

            library = new MusicLibrary(header.OsuDirectory, header.SongsDirectory, tracks, header.Statistics);
            return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or InvalidDataException or OutOfMemoryException or ArgumentException)
        {
            library = null;
            return false;
        }
    }

    private static void WritePayload(string payloadPath, MusicLibrary library)
    {
        string directory = Path.GetDirectoryName(payloadPath) ?? string.Empty;

        if (directory.Length > 0)
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = payloadPath + ".tmp";

        using (FileStream stream = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: false))
        {
            writer.Write(CurrentVersion);
            writer.Write(library.Tracks.Count);

            foreach (MusicTrack track in library.Tracks)
            {
                WriteTrack(writer, track);
            }
        }

        File.Move(temporaryPath, payloadPath, overwrite: true);
    }

    private static void WriteTrack(BinaryWriter writer, MusicTrack track)
    {
        WriteString(writer, track.Id);
        WriteString(writer, track.Title);
        WriteString(writer, track.TitleUnicode);
        WriteString(writer, track.Artist);
        WriteString(writer, track.ArtistUnicode);
        WriteString(writer, track.Creator);
        WriteString(writer, track.Tags);
        WriteString(writer, track.Source);
        WriteString(writer, track.Directory);
        WriteString(writer, track.AudioFileName);
        WriteString(writer, track.AudioFilePath);
        WriteString(writer, track.OsuFilePath);

        writer.Write(track.MapSetId);
        writer.Write(track.PrimaryMapId);
        writer.Write(track.State);
        writer.Write((byte)track.PlayMode);
        writer.Write(track.StarsNomod);
        writer.Write(track.MainBpm);
        writer.Write(track.PreviewTime);
        writer.Write(track.TotalTime);
        writer.Write(track.BeatmapCount);
        writer.Write(track.AudioFileExists);
        writer.Write(track.AudioNameFromFallback);

        WriteStrings(writer, track.Difficulties);
        WriteStrings(writer, track.Directories);
        WriteStrings(writer, track.BeatmapHashes);

        writer.Write(track.BeatmapIds.Count);

        foreach (int id in track.BeatmapIds)
        {
            writer.Write(id);
        }

        writer.Write(track.Duration is { } duration ? duration.TotalSeconds : -1d);
    }

    private static MusicTrack ReadTrack(BinaryReader reader)
    {
        string id = ReadString(reader) ?? string.Empty;

        return new MusicTrack
        {
            Id = id,
            Title = ReadString(reader) ?? string.Empty,
            TitleUnicode = ReadString(reader) ?? string.Empty,
            Artist = ReadString(reader) ?? string.Empty,
            ArtistUnicode = ReadString(reader) ?? string.Empty,
            Creator = ReadString(reader) ?? string.Empty,
            Tags = ReadString(reader) ?? string.Empty,
            Source = ReadString(reader) ?? string.Empty,
            Directory = ReadString(reader) ?? string.Empty,
            AudioFileName = ReadString(reader) ?? string.Empty,
            AudioFilePath = ReadString(reader) ?? string.Empty,
            OsuFilePath = ReadString(reader) ?? string.Empty,
            MapSetId = reader.ReadInt32(),
            PrimaryMapId = reader.ReadInt32(),
            State = reader.ReadByte(),
            PlayMode = (PlayMode)reader.ReadByte(),
            StarsNomod = reader.ReadDouble(),
            MainBpm = reader.ReadDouble(),
            PreviewTime = reader.ReadInt32(),
            TotalTime = reader.ReadInt32(),
            BeatmapCount = reader.ReadInt32(),
            AudioFileExists = reader.ReadBoolean(),
            AudioNameFromFallback = reader.ReadBoolean(),
            Difficulties = ReadStrings(reader),
            Directories = ReadStrings(reader),
            BeatmapHashes = ReadStrings(reader),
            BeatmapIds = ReadInt32List(reader),
            Duration = reader.ReadDouble() is var seconds && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : null,
        };
    }

    private static void WriteString(BinaryWriter writer, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            writer.Write(false);
            return;
        }

        writer.Write(true);
        writer.Write(value);
    }

    private static string? ReadString(BinaryReader reader) => reader.ReadBoolean() ? reader.ReadString() : null;

    private static void WriteStrings(BinaryWriter writer, IReadOnlyList<string> values)
    {
        writer.Write(values.Count);

        foreach (string value in values)
        {
            writer.Write(value);
        }
    }

    private static IReadOnlyList<string> ReadStrings(BinaryReader reader)
    {
        int count = reader.ReadInt32();

        if (count <= 0)
        {
            return [];
        }

        List<string> values = new(count);

        for (int i = 0; i < count; i++)
        {
            values.Add(reader.ReadString());
        }

        return values;
    }

    private static IReadOnlyList<int> ReadInt32List(BinaryReader reader)
    {
        int count = reader.ReadInt32();

        if (count <= 0)
        {
            return [];
        }

        List<int> values = new(count);

        for (int i = 0; i < count; i++)
        {
            values.Add(reader.ReadInt32());
        }

        return values;
    }

    /// <summary>比较目录是否指向同一个位置（忽略大小写与结尾的分隔符）。</summary>
    public static bool SameDirectory(string? left, string? right)
    {
        static string Normalize(string? value)
            => (value ?? string.Empty).Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>缓存内容的时效性。</summary>
public enum LibraryCacheFreshness
{
    /// <summary>没有可用缓存。</summary>
    None = 0,

    /// <summary><c>osu!.db</c> 与缓存时一致。</summary>
    Current = 1,

    /// <summary><c>osu!.db</c> 已更新，缓存是旧的（可以先用，等用户手动重新扫描）。</summary>
    Outdated = 2,
}

/// <summary>读取缓存的结果。</summary>
/// <param name="Library">读到的音乐库；没有可用缓存时为 null。</param>
/// <param name="Freshness">缓存时效性。</param>
/// <param name="Reason">没有缓存或缓存过期时的原因（用于状态提示）。</param>
public sealed record LibraryCacheLoadResult(MusicLibrary? Library, LibraryCacheFreshness Freshness, string? Reason)
{
    public bool HasLibrary => Library is not null;

    public static LibraryCacheLoadResult NotCached(string reason) => new(null, LibraryCacheFreshness.None, reason);
}

/// <summary>缓存头（人类可读，便于排查）。</summary>
internal sealed record LibraryCacheHeader(
    int Version,
    DateTime CachedAt,
    string OsuDirectory,
    string SongsDirectory,
    FileFingerprint Database,
    LibraryStatistics Statistics)
{
    public int TrackCount => Statistics.TrackCount;
}

/// <summary>文件指纹：路径 + 字节数 + 最后写入时间。</summary>
internal sealed record FileFingerprint(string Path, long Length, DateTime LastWriteTimeUtc)
{
    public bool Exists => Length > 0 || LastWriteTimeUtc != default;

    public static FileFingerprint Of(string path)
    {
        try
        {
            FileInfo info = new(path);

            return info.Exists
                ? new FileFingerprint(info.FullName, info.Length, info.LastWriteTimeUtc)
                : new FileFingerprint(path, 0, default);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new FileFingerprint(path, 0, default);
        }
    }

    public bool Matches(FileFingerprint other)
        => Length == other.Length
            && LastWriteTimeUtc == other.LastWriteTimeUtc
            && string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);
}
