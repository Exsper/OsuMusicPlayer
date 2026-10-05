namespace OsuMusicPlayer.App;

using System.Globalization;
using OsuMusicPlayer.App.Dialogs;
using OsuMusicPlayer.Audio;
using OsuMusicPlayer.Core.Enums;
using OsuMusicPlayer.Core.IO;
using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Services;

/// <summary>拖到播放列表上的曲目标识（拖放用）。</summary>
public sealed class TrackDragData(IReadOnlyList<string> trackIds)
{
    public IReadOnlyList<string> TrackIds { get; } = trackIds;
}

/// <summary>音乐库扫描、搜索过滤与曲目列表的显示/操作。</summary>
public sealed partial class MainForm
{
    private bool _scanning;

    // ---------- 扫描 ----------

    private async Task SelectOsuFolderAsync()
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "请选择 osu! stable 安装目录（包含 osu!.db 的目录）",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };

        if (!string.IsNullOrWhiteSpace(_settings.OsuDirectory) && Directory.Exists(_settings.OsuDirectory))
        {
            dialog.SelectedPath = _settings.OsuDirectory;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (!OsuPathLocator.IsStableInstall(dialog.SelectedPath))
        {
            ShowWarning("目录无效", $"在 \"{dialog.SelectedPath}\" 中没有找到 osu!.db，请选择 osu! stable 的安装目录。");
            return;
        }

        _settings.OsuDirectory = dialog.SelectedPath;
        await RescanAsync();
    }

    private void AutoDetectOsuFolder()
    {
        string? found = OsuPathLocator.FindStableInstall();

        if (found is null)
        {
            ShowWarning(
                "未检测到 osu!",
                "没有自动找到 osu! stable 安装目录。" + Environment.NewLine
                + "请先启动一次 osu!，或手动使用“文件 → 选择 osu! 目录…”。");
            return;
        }

        _settings.OsuDirectory = found;
        _ = LoadLibraryAsync();
    }

    private async Task RescanAsync()
    {
        if (_scanning)
        {
            return;
        }

        string? osuDirectory = _settings.OsuDirectory;

        if (string.IsNullOrWhiteSpace(osuDirectory))
        {
            ShowWarning("尚未设置 osu! 目录", "请先通过“文件 → 选择 osu! 目录…”指定 osu! stable 安装目录。");
            return;
        }

        if (!OsuPathLocator.IsStableInstall(osuDirectory))
        {
            ShowWarning("目录无效", $"在 \"{osuDirectory}\" 中没有找到 osu!.db。");
            return;
        }

        _scanning = true;
        SetBusy(true, "正在读取 osu!.db …");

        // 先松开旧音乐库：重新扫描时会立刻读到一整份 osu!.db（几万个临时对象），
        // 若旧库仍被引用，两者会同时驻留，扫描峰值内存直接翻倍。
        // 曲目列表也跟着清空（只在扫描期间，扫描失败会恢复）。
        MusicLibrary previousLibrary = _library;
        _library = MusicLibrary.Empty;
        _visibleTracks = [];
        _trackList.VirtualListSize = 0;
        _trackList.Invalidate();
        _resultLabel.Text = "正在扫描音乐库…";

        try
        {
            Progress<LibraryBuildProgress> progress = new(report => RunOnUi(() =>
            {
                _statusLabel.Text = $"{report.Stage}：{report.Message}";
                _progressBar.Value = Math.Clamp(report.Percent, _progressBar.Minimum, _progressBar.Maximum);
            }));

            MusicLibrary library = await Task.Run(() => new MusicLibraryBuilder().Build(osuDirectory, progress));

            // 扫描产生的大量临时对象（osu!.db 的全部谱面、timing points、合并累加器，以及旧音乐库）
            // 对 GC 来说都是垃圾，但它们只占当时存活对象的一小部分，GC 往往要等堆再涨几百 MB 才回收，
            // 表现为任务管理器里内存先涨到几百 MB ~ 1 GB、过一阵又自己掉下来。
            // 这里在新库已经装好之后主动完整回收一次（几十万 ~ 百万对象约几百毫秒），
            // 把内存立刻还给系统；不放在扫描线程里，避免拉长扫描耗时。
            LibraryScanMemory.ReleaseAfterScan();

            ApplyLibrary(library);

            // 记下这次扫描结果，下次启动直接读缓存，不必重新遍历谱面。
            bool cached = _libraryCache.Save(osuDirectory, library);

            SetStatus(BuildScanSummary(library) + (cached ? " 已缓存，下次启动可直接载入。" : " 缓存写入失败，下次启动仍需重新扫描。"));
        }
        catch (Exception ex) when (ex is OsuMusicPlayer.Core.OsuMusicPlayerException or IOException or UnauthorizedAccessException)
        {
            // 扫描失败时保留上一次的音乐库与列表，不要把界面清空。
            _library = previousLibrary;
            RefreshPlaylistList();
            ApplyFilter();
            ShowWarning("扫描失败", ex.Message);
            SetStatus("扫描失败：" + ex.Message);
        }
        finally
        {
            _scanning = false;
            SetBusy(false, null);
        }
    }

    /// <summary>
    /// 启动时载入音乐库：有可用缓存就直接读缓存（秒级），没有或失效才扫描；
    /// <c>osu!.db</c> 更新过时先用旧缓存，并提示按 F5 重新扫描。
    /// </summary>
    private async Task LoadLibraryAsync()
    {
        string? osuDirectory = _settings.OsuDirectory;

        if (string.IsNullOrWhiteSpace(osuDirectory) || !OsuPathLocator.IsStableInstall(osuDirectory))
        {
            SetStatus("尚未指定 osu! 目录：请使用“文件 → 选择 osu! 目录…”或“自动检测 osu! 目录”。");
            return;
        }

        SetBusy(true, "正在载入音乐库缓存…");

        LibraryCacheLoadResult cached;

        try
        {
            cached = await Task.Run(() => _libraryCache.TryLoad(osuDirectory));
        }
        finally
        {
            SetBusy(false, null);
        }

        if (cached.Library is null)
        {
            SetStatus($"{cached.Reason ?? "没有可用的音乐库缓存。"} 正在扫描 osu!.db …");
            await RescanAsync();
            return;
        }

        ApplyLibrary(cached.Library);

        SetStatus(cached.Freshness == LibraryCacheFreshness.Current
            ? BuildScanSummary(cached.Library) + " 本次直接载入缓存（按 F5 可重新扫描）。"
            : $"osu!.db 已更新（{cached.Reason}），当前显示的是缓存内容，按 F5 可重新扫描并更新缓存。");
    }

    /// <summary>把一份音乐库装到界面上（缓存载入与重新扫描共用）。</summary>
    private void ApplyLibrary(MusicLibrary library)
    {
        _library = library;
        _durationCache.ApplyTo(library.Tracks);

        // 这里刻意 **不** 自动清理播放列表中的失效条目：
        // 一旦扫描的是另一个 osu! 目录（或音乐库暂时不完整），自动清理会静默删掉用户播放列表的内容。
        // 现在只统计数量并提示，由用户通过“播放列表 → 清理失效条目…”显式确认后再清理。
        int stale = CountStalePlaylistEntries(library);

        RefreshPlaylistList();
        ApplyFilter();

        _libraryLabel.Text = $"曲目 {library.Tracks.Count} · 可播放 {library.Statistics.TracksWithAudio} · 缺失 {library.Statistics.TracksWithoutAudio}";

        if (stale > 0)
        {
            SetStatus($"播放列表中还有 {stale} 条曲目不在当前音乐库里（可用“播放列表 → 清理失效条目…”清理）。");
        }
    }

    private static string BuildScanSummary(MusicLibrary library)
        => $"已导入 {library.Statistics.BeatmapCount} 张谱面，合并为 {library.Tracks.Count} 首曲目"
            + $"（可播放 {library.Statistics.TracksWithAudio} 首，音频缺失 {library.Statistics.TracksWithoutAudio} 首）"
            + $"，耗时 {library.Statistics.ScanDuration.TotalSeconds:0.0} 秒。";

    /// <summary>删除音乐库缓存：当前界面不动，下次启动会重新扫描。</summary>
    private void ClearLibraryCache()
    {
        _libraryCache.Clear();
        SetStatus("已删除音乐库缓存，下次启动会重新扫描 osu!.db。");
    }

    private void SetBusy(bool busy, string? message)
    {
        RunOnUi(() =>
        {
            _progressBar.Visible = busy;

            if (busy)
            {
                _progressBar.Value = 0;

                if (message is not null)
                {
                    _statusLabel.Text = message;
                }
            }

            _rescanItem.Enabled = !busy;
            _selectOsuFolderItem.Enabled = !busy;
            _clearLibraryCacheItem.Enabled = !busy;
        });
    }

    // ---------- 过滤与显示 ----------

    private IEnumerable<MusicTrack> GetSourceTracks()
    {
        if (_currentPlaylistId == AllTracksId)
        {
            return _library.Tracks;
        }

        Playlist? playlist = _playlistStore.Get(_currentPlaylistId);

        return playlist is null ? [] : _library.FindByTrackIds(playlist.TrackIds);
    }

    private void ApplyFilter()
    {
        IEnumerable<MusicTrack> source = GetSourceTracks();
        int total = source.Count();

        TrackSearchQuery query = new()
        {
            Text = _searchBox.Text,
            Field = (SearchField)Math.Clamp(_searchFieldBox.SelectedIndex, 0, 5),
            OnlyPlayable = _onlyPlayableCheck.Checked,
        };

        List<MusicTrack> tracks = [.. TrackSearcher.Search(source, query)];
        TrackSorter.Sort(tracks, _settings.SortColumn, _settings.SortAscending);

        _visibleTracks = tracks;

        _trackList.BeginUpdate();
        _trackList.VirtualListSize = tracks.Count;
        _trackList.EndUpdate();
        _trackList.Invalidate();

        _resultLabel.Text = tracks.Count == total
            ? $"共 {total} 首"
            : $"匹配 {tracks.Count} / {total} 首";
    }

    private void RestartSearchTimer()
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void TrackList_RetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
    {
        if (e.ItemIndex < 0 || e.ItemIndex >= _visibleTracks.Count)
        {
            e.Item = new ListViewItem(string.Empty);
            return;
        }

        MusicTrack track = _visibleTracks[e.ItemIndex];
        TrackNameDisplay nameDisplay = _settings.NameDisplay;

        ListViewItem item = new((e.ItemIndex + 1).ToString(CultureInfo.InvariantCulture));
        item.SubItems.Add(track.GetDisplayTitle(nameDisplay));
        item.SubItems.Add(track.GetDisplayArtist(nameDisplay));
        item.SubItems.Add(track.Creator);
        item.SubItems.Add(track.MapSetId > 0 ? track.MapSetId.ToString(CultureInfo.InvariantCulture) : "-");
        item.SubItems.Add(track.BeatmapCount.ToString(CultureInfo.InvariantCulture));
        item.SubItems.Add(FormatDuration(track));
        item.SubItems.Add(track.ModeText);
        item.SubItems.Add(track.StarsNomod > 0 ? track.StarsNomod.ToString("0.##", CultureInfo.CurrentCulture) : "-");
        item.SubItems.Add(track.MainBpm > 0 ? track.MainBpm.ToString("0.#", CultureInfo.CurrentCulture) : "-");
        item.SubItems.Add(track.StateText);
        item.Tag = track;
        item.ToolTipText = BuildTrackTooltip(track, nameDisplay);

        if (!track.AudioFileExists)
        {
            item.ForeColor = Color.Gray;
        }
        else if (_playlistStore.IsFavorite(track.Id))
        {
            item.ForeColor = Color.FromArgb(190, 60, 110);
        }

        e.Item = item;
    }

    private static string FormatDuration(MusicTrack track)
    {
        if (track.Duration is { } duration && duration > TimeSpan.Zero)
        {
            return $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";
        }

        // 还没分析时长时，用 osu!.db 里记录的谱面时长作为估算值。
        if (track.TotalTime > 0)
        {
            TimeSpan estimate = TimeSpan.FromMilliseconds(track.TotalTime);

            return $"≈{(int)estimate.TotalMinutes}:{estimate.Seconds:00}";
        }

        return "-";
    }

    private static string BuildTrackTooltip(MusicTrack track, TrackNameDisplay nameDisplay)
    {
        List<string> lines =
        [
            track.GetDisplayName(nameDisplay),
            $"谱师：{track.Creator}",
            $"原文：{track.ArtistUnicode} - {track.TitleUnicode}",
            $"罗马化：{track.Artist} - {track.Title}",
            track.MapSetId > 0 ? $"谱面集：https://osu.ppy.sh/s/{track.MapSetId}" : "本地谱面（无谱面集 ID）",
            $"难度（{track.Difficulties.Count}）：{string.Join(" / ", track.Difficulties)}",
            $"音频：{track.AudioFileName}{(track.AudioFileExists ? string.Empty : "（缺失）")}",
        ];

        if (!string.IsNullOrWhiteSpace(track.Tags))
        {
            lines.Add($"标签：{track.Tags}");
        }

        lines.Add(track.AudioFilePath);

        return string.Join(Environment.NewLine, lines);
    }

    private void TrackList_ColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (e.Column == 0)
        {
            return;
        }

        TrackSortColumn column = e.Column switch
        {
            1 => TrackSortColumn.Title,
            2 => TrackSortColumn.Artist,
            3 => TrackSortColumn.Creator,
            4 => TrackSortColumn.MapSetId,
            5 => TrackSortColumn.BeatmapCount,
            6 => TrackSortColumn.Duration,
            7 => TrackSortColumn.PlayMode,
            8 => TrackSortColumn.Stars,
            9 => TrackSortColumn.Bpm,
            10 => TrackSortColumn.State,
            _ => TrackSortColumn.Artist,
        };

        if (_settings.SortColumn == column)
        {
            _settings.SortAscending = !_settings.SortAscending;
        }
        else
        {
            _settings.SortColumn = column;
            _settings.SortAscending = true;
        }

        ApplyFilter();
    }

    private void TrackList_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape when _searchBox.Text.Length > 0:
                _searchBox.Clear();
                e.Handled = true;
                break;

            case Keys.C when e.Control:
                CopySelectionAsText();
                e.Handled = true;
                break;
        }
    }

    private void CopySelectionAsText()
    {
        List<MusicTrack> selected = GetSelectedTracks();

        if (selected.Count == 0)
        {
            return;
        }

        CopyToClipboard(string.Join(
            Environment.NewLine,
            selected.Select(track => track.GetDisplayName(_settings.NameDisplay))));
    }

    private void TrackList_ItemDrag(object? sender, ItemDragEventArgs e)
    {
        List<string> ids = [.. GetSelectedTracks().Select(static track => track.Id)];

        if (ids.Count == 0)
        {
            return;
        }

        _trackList.DoDragDrop(new TrackDragData(ids), DragDropEffects.Copy);
    }

    private List<MusicTrack> GetSelectedTracks()
    {
        List<MusicTrack> result = [];

        foreach (int index in _trackList.SelectedIndices)
        {
            if (index >= 0 && index < _visibleTracks.Count)
            {
                result.Add(_visibleTracks[index]);
            }
        }

        return result;
    }

    // ---------- 右键菜单 ----------

    private void BuildTrackContextMenu()
    {
        _trackContextMenu.Items.Clear();

        List<MusicTrack> selected = GetSelectedTracks();

        if (selected.Count == 0)
        {
            _trackContextMenu.Items.Add(new ToolStripMenuItem("（未选择曲目）") { Enabled = false });
            return;
        }

        _trackContextMenu.Items.Add(new ToolStripMenuItem("播放", null, (_, _) => PlaySelectedTrack()));

        ToolStripMenuItem addTo = new("添加到播放列表");

        foreach (Playlist playlist in _playlistStore.UserPlaylists)
        {
            Playlist captured = playlist;
            addTo.DropDownItems.Add(new ToolStripMenuItem(
                $"{captured.Name} ({captured.Count})",
                null,
                (_, _) => AddSelectedToPlaylist(captured.Id)));
        }

        addTo.DropDownItems.Add(new ToolStripSeparator());
        addTo.DropDownItems.Add(new ToolStripMenuItem("新建播放列表…", null, (_, _) =>
        {
            string? name = TextInputDialog.Show(this, "新建播放列表", "播放列表名称：", _playlistStore.CreateUniqueName("新建播放列表"));

            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            Playlist created = _playlistStore.Create(name);
            AddSelectedToPlaylist(created.Id);
        }));

        _trackContextMenu.Items.Add(addTo);

        bool allFavorite = selected.All(track => _playlistStore.IsFavorite(track.Id));

        _trackContextMenu.Items.Add(new ToolStripMenuItem(
            allFavorite ? "取消“喜欢”" : "添加到“我喜欢的音乐”",
            null,
            (_, _) => ToggleFavoriteSelected()));

        if (_currentPlaylistId != AllTracksId)
        {
            _trackContextMenu.Items.Add(new ToolStripMenuItem("从当前播放列表移除", null, (_, _) => RemoveSelectedFromPlaylist()));
        }

        _trackContextMenu.Items.Add(new ToolStripSeparator());
        _trackContextMenu.Items.Add(new ToolStripMenuItem("在资源管理器中显示", null, (_, _) => OpenInExplorer(selected[0])));
        _trackContextMenu.Items.Add(new ToolStripMenuItem("复制“艺术家 - 标题”", null, (_, _) => CopySelectionAsText()));

        if (selected[0].MapSetId > 0)
        {
            _trackContextMenu.Items.Add(new ToolStripMenuItem(
                "复制 osu! 谱面集链接",
                null,
                (_, _) => CopyToClipboard(selected[0].MapSetUrl)));

            _trackContextMenu.Items.Add(new ToolStripMenuItem("打开 osu! 谱面页", null, (_, _) => OpenUrl(selected[0].MapSetUrl)));
        }

        _trackContextMenu.Items.Add(new ToolStripSeparator());
        _trackContextMenu.Items.Add(new ToolStripMenuItem(
            selected.Count == 1 ? "分析该曲目时长" : $"分析所选 {selected.Count} 首的时长",
            null,
            async (_, _) => await AnalyzeDurationsAsync(selected)));
    }

    private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowWarning("打开失败", ex.Message);
        }
    }

    // ---------- 时长分析 ----------

    private async Task AnalyzeDurationsAsync(IReadOnlyList<MusicTrack>? scope = null)
    {
        if (_analysisCts is not null)
        {
            SetStatus("时长分析正在进行中。");
            return;
        }

        List<MusicTrack> targets = [.. (scope ?? _visibleTracks)
            .Where(static track => track.AudioFileExists && track.Duration is null)];

        if (targets.Count == 0)
        {
            ShowInformation("时长分析", "当前列表中所有可播放曲目都已有已知时长。");
            return;
        }

        _analysisCts = new CancellationTokenSource();
        CancellationToken token = _analysisCts.Token;

        _cancelAnalysisItem.Enabled = true;
        _progressBar.Visible = true;
        _progressBar.Maximum = targets.Count;
        _progressBar.Value = 0;

        int analyzed = 0;

        Progress<int> progressReporter = new(done => RunOnUi(() =>
        {
            _progressBar.Value = Math.Clamp(done, 0, _progressBar.Maximum);
            _statusLabel.Text = $"正在分析时长 {done}/{targets.Count} …";
        }));

        IProgress<int> progress = progressReporter;

        try
        {
            await Task.Run(() =>
            {
                int done = 0;

                foreach (MusicTrack track in targets)
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    if (AudioFileProbe.TryGetDuration(track.AudioFilePath, out TimeSpan duration, out _))
                    {
                        track.Duration = duration;
                        _durationCache.Set(track.Id, duration);
                        analyzed++;
                    }

                    done++;

                    if (done % 5 == 0 || done == targets.Count)
                    {
                        progress.Report(done);
                    }
                }
            }, token);

            _durationCache.Save();
            _trackList.Invalidate();

            SetStatus(token.IsCancellationRequested
                ? $"时长分析已取消，已分析 {analyzed} 首。"
                : $"时长分析完成：{analyzed}/{targets.Count} 首。");
        }
        catch (OperationCanceledException)
        {
            SetStatus($"时长分析已取消，已分析 {analyzed} 首。");
        }
        finally
        {
            _analysisCts.Dispose();
            _analysisCts = null;
            _cancelAnalysisItem.Enabled = false;
            _progressBar.Visible = false;
        }
    }

    // ---------- 导出 ----------

    private void ExportCurrentListAsM3u8()
    {
        if (_visibleTracks.Count == 0)
        {
            ShowInformation("导出 M3U8", "当前列表没有曲目可导出。");
            return;
        }

        string suggested = (_currentPlaylistId == AllTracksId ? AllTracksName : _playlistStore.Get(_currentPlaylistId)?.Name) ?? "播放列表";

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            suggested = suggested.Replace(invalid, '_');
        }

        using SaveFileDialog dialog = new()
        {
            Filter = "M3U8 播放列表|*.m3u8|所有文件|*.*",
            FileName = suggested + ".m3u8",
            Title = "导出为 M3U8",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            M3u8Exporter.Write(_visibleTracks, dialog.FileName, _settings.NameDisplay);
            SetStatus($"已导出 {_visibleTracks.Count} 首曲目到 {dialog.FileName}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowWarning("导出失败", ex.Message);
        }
    }
}
