namespace OsuMusicPlayer.App;

using OsuMusicPlayer.App.Dialogs;
using OsuMusicPlayer.Core;
using OsuMusicPlayer.Core.IO;
using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Services;

/// <summary>左侧播放列表栏：全部音乐、我喜欢的音乐、自定义列表、收藏夹导入与拖放。</summary>
public sealed partial class MainForm
{
    private sealed record PlaylistEntry(string Id, string Name, int Count, bool IsFavorites)
    {
        public override string ToString() => Count > 0 ? $"{Name} ({Count})" : Name;
    }

    private void RefreshPlaylistList()
    {
        RunOnUi(() =>
        {
            if (_suppressPlaylistEvents || IsDisposed)
            {
                return;
            }

            _suppressPlaylistEvents = true;

            try
            {
                _playlistList.BeginUpdate();
                _playlistList.Items.Clear();
                _playlistList.Items.Add(new PlaylistEntry(AllTracksId, AllTracksName, _library.Tracks.Count, false));

                Playlist favorites = _playlistStore.EnsureFavorites();
                _playlistList.Items.Add(new PlaylistEntry(favorites.Id, "♥ " + favorites.Name, favorites.Count, true));

                foreach (Playlist playlist in _playlistStore.UserPlaylists)
                {
                    _playlistList.Items.Add(new PlaylistEntry(playlist.Id, playlist.Name, playlist.Count, false));
                }

                int index = IndexOfPlaylist(_currentPlaylistId);
                _playlistList.SelectedIndex = index >= 0 ? index : 0;
                _playlistList.EndUpdate();
            }
            finally
            {
                _suppressPlaylistEvents = false;
            }

            _deletePlaylistItem.Enabled = _currentPlaylistId is not (AllTracksId or PlaylistStore.FavoritesId);
            _renamePlaylistItem.Enabled = _currentPlaylistId is not (AllTracksId or PlaylistStore.FavoritesId);
            _clearPlaylistItem.Enabled = _currentPlaylistId != AllTracksId;
            _deletePlaylistButton.Enabled = _deletePlaylistItem.Enabled;
            _renamePlaylistButton.Enabled = _renamePlaylistItem.Enabled;
        });
    }

    private int IndexOfPlaylist(string id)
    {
        for (int i = 0; i < _playlistList.Items.Count; i++)
        {
            if (_playlistList.Items[i] is PlaylistEntry entry && entry.Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private void OnPlaylistSelectionChanged()
    {
        if (_suppressPlaylistEvents || _playlistList.SelectedItem is not PlaylistEntry entry)
        {
            return;
        }

        _currentPlaylistId = entry.Id;
        _settings.LastPlaylistId = entry.Id;
        ApplyFilter();

        SetStatus(entry.Id == AllTracksId
            ? $"已显示全部音乐（{_library.Tracks.Count} 首）。"
            : $"已选择播放列表“{entry.Name}”，共 {entry.Count} 首。");
    }

    private void SelectPlaylist(string playlistId)
    {
        int index = IndexOfPlaylist(playlistId);

        if (index >= 0)
        {
            _playlistList.SelectedIndex = index;
        }
    }

    private void CreatePlaylistInteractive()
    {
        string? name = TextInputDialog.Show(this, "新建播放列表", "播放列表名称：", _playlistStore.CreateUniqueName("新建播放列表"));

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (_playlistStore.NameExists(name))
        {
            ShowWarning("名称重复", $"已存在名为“{name}”的播放列表。");
            return;
        }

        Playlist playlist = _playlistStore.Create(name);
        PersistPlaylists();
        RefreshPlaylistList();
        SelectPlaylist(playlist.Id);
        SetStatus($"已创建播放列表“{playlist.Name}”。");
    }

    private void RenameSelectedPlaylist()
    {
        Playlist? playlist = _playlistStore.Get(_currentPlaylistId);

        if (playlist is null || playlist.IsFavorites || playlist.Id == AllTracksId)
        {
            return;
        }

        string? name = TextInputDialog.Show(this, "重命名播放列表", "新的名称：", playlist.Name);

        if (string.IsNullOrWhiteSpace(name) || name == playlist.Name)
        {
            return;
        }

        if (_playlistStore.NameExists(name, playlist.Id))
        {
            ShowWarning("名称重复", $"已存在名为“{name}”的播放列表。");
            return;
        }

        _playlistStore.Rename(playlist.Id, name);
        PersistPlaylists();
        RefreshPlaylistList();
        SetStatus($"已重命名为“{name}”。");
    }

    private void DeleteSelectedPlaylist()
    {
        if (_currentPlaylistId == AllTracksId)
        {
            ShowWarning("无法删除", "“全部音乐”来自 osu! 音乐库，不能删除。");
            return;
        }

        Playlist? playlist = _playlistStore.Get(_currentPlaylistId);

        if (playlist is null)
        {
            return;
        }

        if (playlist.IsFavorites)
        {
            ShowWarning("无法删除", "“我喜欢的音乐”是内置播放列表，不能删除。");
            return;
        }

        DialogResult result = MessageBox.Show(
            this,
            $"确定删除播放列表“{playlist.Name}”吗？" + Environment.NewLine + "（只删除列表，不会删除任何音乐文件）",
            "删除播放列表",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
        {
            return;
        }

        if (_playlistStore.Delete(playlist.Id))
        {
            PersistPlaylists();
            _currentPlaylistId = AllTracksId;
            RefreshPlaylistList();
            ApplyFilter();
            SetStatus($"已删除播放列表“{playlist.Name}”。");
        }
    }

    private void ClearSelectedPlaylist()
    {
        Playlist? playlist = _playlistStore.Get(_currentPlaylistId);

        if (playlist is null)
        {
            return;
        }

        if (playlist.Id == AllTracksId)
        {
            ShowWarning("无法清空", "“全部音乐”来自 osu! 音乐库，不能清空。");
            return;
        }

        if (playlist.Count == 0)
        {
            return;
        }

        DialogResult result = MessageBox.Show(
            this,
            $"确定清空播放列表“{playlist.Name}”中的 {playlist.Count} 首曲目吗？",
            "清空播放列表",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
        {
            return;
        }

        int removed = _playlistStore.ClearTracks(playlist.Id);
        PersistPlaylists();
        RefreshPlaylistList();
        ApplyFilter();
        SetStatus($"已从“{playlist.Name}”中移除 {removed} 首曲目。");
    }

    private void AddSelectedToPlaylist(string playlistId)
    {
        List<string> ids = [.. GetSelectedTracks().Select(static track => track.Id)];

        if (ids.Count == 0)
        {
            return;
        }

        int added = _playlistStore.AddTracks(playlistId, ids);
        PersistPlaylists();
        RefreshPlaylistList();

        string name = _playlistStore.Get(playlistId)?.Name ?? "播放列表";
        SetStatus(added > 0
            ? $"已把 {added} 首曲目加入“{name}”（{ids.Count - added} 首已存在）。"
            : $"这 {ids.Count} 首曲目已全部在“{name}”中。");
    }

    private void RemoveSelectedFromPlaylist()
    {
        if (_currentPlaylistId == AllTracksId)
        {
            return;
        }

        List<string> ids = [.. GetSelectedTracks().Select(static track => track.Id)];

        if (ids.Count == 0)
        {
            return;
        }

        int removed = _playlistStore.RemoveTracks(_currentPlaylistId, ids);
        PersistPlaylists();
        RefreshPlaylistList();
        ApplyFilter();
        SetStatus($"已从播放列表移除 {removed} 首曲目。");
    }

    private void ToggleFavoriteSelected()
    {
        List<MusicTrack> selected = GetSelectedTracks();

        if (selected.Count == 0)
        {
            return;
        }

        bool allFavorite = selected.All(track => _playlistStore.IsFavorite(track.Id));
        int changed = 0;

        foreach (MusicTrack track in selected)
        {
            bool isFavorite = _playlistStore.IsFavorite(track.Id);

            if (isFavorite == allFavorite)
            {
                _playlistStore.ToggleFavorite(track.Id);
                changed++;
            }
        }

        PersistPlaylists();
        RefreshPlaylistList();
        _trackList.Invalidate();
        SetStatus(allFavorite ? $"已取消收藏 {changed} 首。" : $"已收藏 {changed} 首。");
    }

    private void PersistPlaylists()
    {
        try
        {
            _playlistStore.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("播放列表保存失败：" + ex.Message);
        }
    }

    // ---------- 收藏夹导入 ----------

    private void ImportOsuCollections()
    {
        string? osuDirectory = _settings.OsuDirectory;

        if (string.IsNullOrWhiteSpace(osuDirectory))
        {
            ShowWarning("尚未设置 osu! 目录", "请先指定 osu! 目录，或使用“从 collection.db 文件导入…”。");
            return;
        }

        string file = Path.Combine(osuDirectory, "collection.db");

        if (!File.Exists(file))
        {
            ShowWarning("找不到收藏夹", $"在 osu! 目录下没有找到 collection.db：{file}");
            return;
        }

        ImportCollectionsFromFile(file);
    }

    private void ImportCollectionFile()
    {
        using OpenFileDialog dialog = new()
        {
            Filter = "osu! 收藏夹|*.db;*.osdb|所有文件|*.*",
            Title = "选择 collection.db 收藏夹文件",
        };

        if (!string.IsNullOrWhiteSpace(_settings.OsuDirectory) && Directory.Exists(_settings.OsuDirectory))
        {
            dialog.InitialDirectory = _settings.OsuDirectory;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        ImportCollectionsFromFile(dialog.FileName);
    }

    private void ImportCollectionsFromFile(string filePath)
    {
        if (_library.Tracks.Count == 0)
        {
            ShowWarning("尚未加载音乐库", "请先扫描 osu! 音乐库，然后再导入收藏夹。");
            return;
        }

        try
        {
            IReadOnlyList<OsuCollectionData> collections = new OsuCollectionReader().Read(filePath);

            if (collections.Count == 0)
            {
                ShowInformation("导入收藏夹", "该文件中没有收藏夹。");
                return;
            }

            CollectionImportResult result = _collectionImporter.Import(
                collections,
                _library,
                new CollectionImportOptions { SourceFile = filePath });

            PersistPlaylists();
            RefreshPlaylistList();

            List<string> lines = [.. result.Details.Select(detail => detail.PlaylistId.Length == 0
                ? $"· {detail.CollectionName}：{detail.BeatmapCount} 张谱面全部缺失，已跳过"
                : $"· {detail.CollectionName}：{detail.TrackCount} 首曲目（{detail.MatchedBeatmaps}/{detail.BeatmapCount} 张谱面匹配，{(detail.Created ? "新建" : "更新")}，缺失 {detail.MissingBeatmaps}）")];

            string summary = string.Join(
                Environment.NewLine,
                $"已读取 {result.CollectionsRead} 个收藏夹：",
                $"新建播放列表 {result.PlaylistsCreated} 个，更新 {result.PlaylistsUpdated} 个，跳过空的 {result.SkippedEmpty} 个。",
                $"共加入 {result.TracksAdded} 首曲目；有 {result.MissingBeatmaps} 张谱面在本地找不到（通常是尚未下载的谱面）。");

            TextReportDialog.Show(this, "导入 osu! 收藏夹", summary, string.Join(Environment.NewLine, lines));
            SetStatus($"收藏夹导入完成：{result.PlaylistsCreated + result.PlaylistsUpdated} 个播放列表，{result.TracksAdded} 首曲目。");
        }
        catch (InvalidCollectionFileException ex)
        {
            ShowWarning("导入失败", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowWarning("导入失败", ex.Message);
        }
    }

    // ---------- 拖放 ----------

    private void PlaylistList_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(typeof(TrackDragData)) == true)
        {
            e.Effect = DragDropEffects.Copy;
        }
        else
        {
            e.Effect = DragDropEffects.None;
        }
    }

    private void PlaylistList_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(typeof(TrackDragData)) is not TrackDragData dragData || dragData.TrackIds.Count == 0)
        {
            return;
        }

        Point point = _playlistList.PointToClient(new Point(e.X, e.Y));
        int index = _playlistList.IndexFromPoint(point);

        if (index < 0 || index >= _playlistList.Items.Count || _playlistList.Items[index] is not PlaylistEntry entry)
        {
            return;
        }

        if (entry.Id == AllTracksId)
        {
            SetStatus("“全部音乐”来自 osu! 音乐库，不能把曲目加入其中。");
            return;
        }

        int added = _playlistStore.AddTracks(entry.Id, dragData.TrackIds);
        PersistPlaylists();
        RefreshPlaylistList();
        SelectPlaylist(entry.Id);
        SetStatus(added > 0
            ? $"已把 {added} 首曲目拖入“{entry.Name}”。"
            : $"这些曲目已全部在“{entry.Name}”中。");
    }
}
