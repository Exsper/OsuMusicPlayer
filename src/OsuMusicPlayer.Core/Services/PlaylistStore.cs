namespace OsuMusicPlayer.Core.Services;

using OsuMusicPlayer.Core.IO;
using OsuMusicPlayer.Core.Models;

/// <summary>
/// 播放列表的增删改查与持久化（<c>%AppData%\OsuMusicPlayer\playlists.json</c>）。
/// 传入 <c>null</c> 路径时只做内存操作（单元测试用）。
/// </summary>
public sealed class PlaylistStore
{
    /// <summary>内置“我喜欢的音乐”的固定 ID。</summary>
    public const string FavoritesId = "favorites";

    public const string FavoritesName = "我喜欢的音乐";

    private const int CurrentVersion = 1;

    private readonly List<Playlist> _playlists = [];

    public PlaylistStore(string? filePath = null)
    {
        FilePath = filePath;
    }

    /// <summary>任意列表发生变化时触发（用于刷新界面）。</summary>
    public event EventHandler? Changed;

    public string? FilePath { get; }

    public IReadOnlyList<Playlist> Playlists => _playlists;

    /// <summary>用户自建 + 导入的播放列表（不含内置收藏夹）。</summary>
    public IEnumerable<Playlist> UserPlaylists => _playlists.Where(static playlist => !playlist.IsFavorites);

    public void Load()
    {
        _playlists.Clear();

        PlaylistFile? file = null;

        if (!string.IsNullOrWhiteSpace(FilePath))
        {
            file = JsonFile.Load<PlaylistFile>(FilePath);
        }

        if (file?.Playlists is not null)
        {
            foreach (Playlist playlist in file.Playlists)
            {
                playlist.TrackIds ??= [];

                if (string.IsNullOrWhiteSpace(playlist.Id))
                {
                    playlist.Id = Guid.NewGuid().ToString("N");
                }

                if (!string.IsNullOrWhiteSpace(playlist.Name))
                {
                    _playlists.Add(playlist);
                }
            }
        }

        EnsureFavorites();
        RaiseChanged();
    }

    public void Save()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            return;
        }

        JsonFile.Save(FilePath, new PlaylistFile(CurrentVersion, [.. _playlists]));
    }

    /// <summary>确保内置收藏夹存在并排在首位。</summary>
    public Playlist EnsureFavorites()
    {
        Playlist? favorites = Get(FavoritesId);

        if (favorites is null)
        {
            favorites = new Playlist
            {
                Id = FavoritesId,
                Name = FavoritesName,
                Kind = PlaylistKind.Favorites,
            };

            _playlists.Insert(0, favorites);
        }

        return favorites;
    }

    public Playlist? Get(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : _playlists.FirstOrDefault(playlist => playlist.Id == id);

    public Playlist? FindByName(string name)
        => _playlists.FirstOrDefault(playlist => string.Equals(playlist.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool NameExists(string name, string? exceptId = null)
        => _playlists.Any(playlist =>
            !string.Equals(playlist.Id, exceptId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(playlist.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>生成一个不重名的播放列表名（例如 “新建列表 (2)”）。</summary>
    public string CreateUniqueName(string baseName)
    {
        string name = string.IsNullOrWhiteSpace(baseName) ? "新建播放列表" : baseName.Trim();

        if (!NameExists(name))
        {
            return name;
        }

        for (int i = 1; i < 1000; i++)
        {
            string candidate = $"{name} ({i})";

            if (!NameExists(candidate))
            {
                return candidate;
            }
        }

        return $"{name} ({Guid.NewGuid():N})";
    }

    public Playlist Create(string? name = null, string kind = PlaylistKind.Custom, string? id = null, string? sourceFile = null)
    {
        string finalName = string.IsNullOrWhiteSpace(name) ? CreateUniqueName("新建播放列表") : name.Trim();

        Playlist playlist = new()
        {
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
            Name = finalName,
            Kind = kind,
            SourceFile = sourceFile,
        };

        _playlists.Add(playlist);
        RaiseChanged();

        return playlist;
    }

    public bool Rename(string id, string newName)
    {
        Playlist? playlist = Get(id);

        if (playlist is null || string.IsNullOrWhiteSpace(newName))
        {
            return false;
        }

        playlist.Name = newName.Trim();
        Touch(playlist);

        return true;
    }

    /// <summary>删除播放列表；内置收藏夹不可删除。</summary>
    public bool Delete(string id)
    {
        Playlist? playlist = Get(id);

        if (playlist is null || playlist.IsFavorites)
        {
            return false;
        }

        _playlists.Remove(playlist);
        RaiseChanged();

        return true;
    }

    /// <summary>加入曲目，返回真正新增的数量（已存在的会被忽略）。</summary>
    public int AddTracks(string playlistId, IEnumerable<string> trackIds)
    {
        Playlist? playlist = Get(playlistId);

        if (playlist is null)
        {
            return 0;
        }

        HashSet<string> existing = new(playlist.TrackIds, StringComparer.OrdinalIgnoreCase);
        int added = 0;

        foreach (string trackId in trackIds)
        {
            if (string.IsNullOrWhiteSpace(trackId) || !existing.Add(trackId))
            {
                continue;
            }

            playlist.TrackIds.Add(trackId);
            added++;
        }

        if (added > 0)
        {
            Touch(playlist);
        }

        return added;
    }

    /// <summary>用给定顺序整体替换播放列表内容（导入时使用）。</summary>
    public void ReplaceTracks(string playlistId, IEnumerable<string> trackIds)
    {
        Playlist? playlist = Get(playlistId);

        if (playlist is null)
        {
            return;
        }

        playlist.TrackIds = [.. trackIds.Distinct(StringComparer.OrdinalIgnoreCase)];
        Touch(playlist);
    }

    public int RemoveTracks(string playlistId, IEnumerable<string> trackIds)
    {
        Playlist? playlist = Get(playlistId);

        if (playlist is null)
        {
            return 0;
        }

        HashSet<string> toRemove = new(trackIds, StringComparer.OrdinalIgnoreCase);
        int removed = playlist.TrackIds.RemoveAll(toRemove.Contains);

        if (removed > 0)
        {
            Touch(playlist);
        }

        return removed;
    }

    public bool RemoveAt(string playlistId, int index)
    {
        Playlist? playlist = Get(playlistId);

        if (playlist is null || index < 0 || index >= playlist.TrackIds.Count)
        {
            return false;
        }

        playlist.TrackIds.RemoveAt(index);
        Touch(playlist);

        return true;
    }

    public bool MoveTrack(string playlistId, int fromIndex, int toIndex)
    {
        Playlist? playlist = Get(playlistId);

        if (playlist is null
            || fromIndex < 0 || fromIndex >= playlist.TrackIds.Count
            || toIndex < 0 || toIndex >= playlist.TrackIds.Count
            || fromIndex == toIndex)
        {
            return false;
        }

        string trackId = playlist.TrackIds[fromIndex];
        playlist.TrackIds.RemoveAt(fromIndex);
        playlist.TrackIds.Insert(toIndex, trackId);
        Touch(playlist);

        return true;
    }

    public int ClearTracks(string playlistId)
    {
        Playlist? playlist = Get(playlistId);

        if (playlist is null || playlist.TrackIds.Count == 0)
        {
            return 0;
        }

        int count = playlist.TrackIds.Count;
        playlist.TrackIds.Clear();
        Touch(playlist);

        return count;
    }

    public bool IsFavorite(string trackId) => Get(FavoritesId)?.TrackIds.Contains(trackId, StringComparer.OrdinalIgnoreCase) == true;

    /// <summary>切换“喜欢”状态，返回切换后是否已收藏。</summary>
    public bool ToggleFavorite(string trackId)
    {
        Playlist favorites = EnsureFavorites();

        if (favorites.TrackIds.RemoveAll(id => string.Equals(id, trackId, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            Touch(favorites);
            return false;
        }

        favorites.TrackIds.Add(trackId);
        Touch(favorites);

        return true;
    }

    /// <summary>
    /// 统计播放列表里已不在音乐库中的曲目数量（只读，不做任何修改）。
    /// 扫描音乐库时不会自动清理失效条目，避免换了 osu! 目录后静默丢失播放列表内容。
    /// </summary>
    public int CountStaleEntries(MusicLibrary library)
        => _playlists.Sum(playlist => CountStaleEntries(playlist.Id, library));

    /// <summary>统计指定播放列表里已不在音乐库中的曲目数量。</summary>
    public int CountStaleEntries(string playlistId, MusicLibrary library)
    {
        Playlist? playlist = Get(playlistId);

        return playlist is null ? 0 : playlist.TrackIds.Count(id => library.FindById(id) is null);
    }

    /// <summary>清掉播放列表中已不在音乐库里的曲目标识，返回清理数量。</summary>
    public int Prune(MusicLibrary library)
    {
        int pruned = 0;

        foreach (Playlist playlist in _playlists)
        {
            int removed = playlist.TrackIds.RemoveAll(id => library.FindById(id) is null);

            if (removed > 0)
            {
                pruned += removed;
                Touch(playlist);
            }
        }

        return pruned;
    }

    private void Touch(Playlist playlist)
    {
        playlist.UpdatedAt = DateTimeOffset.Now;
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private sealed record PlaylistFile(int Version, List<Playlist> Playlists);
}
