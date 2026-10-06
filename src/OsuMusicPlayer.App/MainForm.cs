namespace OsuMusicPlayer.App;

using OsuMusicPlayer.App.Dialogs;
using OsuMusicPlayer.Audio;
using OsuMusicPlayer.Core.Enums;
using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Playback;
using OsuMusicPlayer.Core.Services;

/// <summary>osu! stable 独立音乐播放器的主窗口。</summary>
public sealed partial class MainForm : Form
{
    private const string AllTracksId = "__all__";
    private const string AllTracksName = "全部音乐";

    private readonly CommandLineOptions _options;
    private readonly SettingsStore _settingsStore;
    private readonly PlaylistStore _playlistStore;
    private readonly DurationCache _durationCache;
    private readonly LibraryCache _libraryCache;
    private readonly CollectionImporter _collectionImporter;
    private readonly PlaybackController _playback;

    private readonly System.Windows.Forms.Timer _searchTimer = new() { Interval = 300 };
    private readonly System.Windows.Forms.Timer _positionTimer = new() { Interval = 250 };

    private AppSettings _settings;
    private MusicLibrary _library = MusicLibrary.Empty;
    private List<MusicTrack> _visibleTracks = [];
    private string _currentPlaylistId = AllTracksId;
    private bool _seeking;
    private bool _suppressModeChange;
    private bool _suppressNameDisplay;
    private bool _suppressPlaylistEvents;
    private CancellationTokenSource? _analysisCts;
    private bool _ready;

    public MainForm(CommandLineOptions options)
    {
        _options = options;
        _settingsStore = new SettingsStore();
        _settings = options.ResetSettings ? new AppSettings() : _settingsStore.Load();

        if (!string.IsNullOrWhiteSpace(options.OsuDirectory))
        {
            _settings.OsuDirectory = options.OsuDirectory;
        }

        _playlistStore = new PlaylistStore(AppPaths.PlaylistsFile);
        _durationCache = new DurationCache(AppPaths.DurationsFile);
        _libraryCache = new LibraryCache(AppPaths.LibraryCacheFile);
        _collectionImporter = new CollectionImporter(_playlistStore);

        try
        {
            _playback = new PlaybackController(new NAudioPlayer());
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"无法初始化音频引擎：{ex.Message}", ex);
        }

        AppPaths.EnsureCreated();

        BuildUi();
        ApplySettingsToUi();
        HookEvents();
        LoadStores();

        _playback.Volume = _settings.Volume;
        _playback.Mode = _settings.PlaybackMode;

        // 恢复上次使用的播放列表。
        string? lastPlaylist = _settings.LastPlaylistId;

        if (!string.IsNullOrWhiteSpace(lastPlaylist)
            && (lastPlaylist == AllTracksId || _playlistStore.Get(lastPlaylist) is not null))
        {
            _currentPlaylistId = lastPlaylist;
        }

        UpdateTransportUi();
        _nowPlaying.ShowTrack(null, null);
        RefreshPlaylistList();
        ApplyFilter();
    }

    /// <summary>截图诊断模式（<c>--screenshot</c>）使用的输出路径。</summary>
    private string? ScreenshotPath => _options.ScreenshotPath;

    private void HookEvents()
    {
        _playlistStore.Changed += (_, _) => RunOnUi(RefreshPlaylistList);

        _playback.CurrentTrackChanged += (_, _) => RunOnUi(OnCurrentTrackChanged);
        _playback.StateChanged += (_, _) => RunOnUi(UpdateTransportUi);
        _playback.PlaybackError += (_, args) => RunOnUi(() => OnPlaybackError(args));

        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            ApplyFilter();
        };

        _positionTimer.Tick += (_, _) => UpdatePositionFromPlayback();

        Shown += OnShown;
        FormClosing += OnFormClosing;
        Resize += (_, _) => LayoutSplitters();
    }

    private void LoadStores()
    {
        try
        {
            _playlistStore.Load();
        }
        catch (InvalidDataException ex)
        {
            SetStatus($"播放列表文件损坏，已忽略：{ex.Message}");
        }

        try
        {
            _durationCache.Load();
        }
        catch (InvalidDataException ex)
        {
            SetStatus($"时长缓存损坏，已忽略：{ex.Message}");
        }
    }

    private void ApplySettingsToUi()
    {
        Width = Math.Max(MinimumSize.Width, _settings.WindowWidth);
        Height = Math.Max(MinimumSize.Height, _settings.WindowHeight);

        if (_settings.WindowX != int.MinValue && _settings.WindowY != int.MinValue)
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(_settings.WindowX, _settings.WindowY);
        }

        if (_settings.WindowMaximized)
        {
            WindowState = FormWindowState.Maximized;
        }

        _suppressModeChange = true;
        _modeBox.SelectedIndex = Math.Clamp((int)_settings.PlaybackMode, 0, 3);
        _suppressModeChange = false;

        _onlyPlayableCheck.Checked = _settings.OnlyPlayable;
        _searchFieldBox.SelectedIndex = Math.Clamp((int)_settings.SearchField, 0, 5);
        _nowPlaying.ShowCover = _settings.ShowCover;
        _showCoverItem.Checked = _settings.ShowCover;
        ApplyNameDisplay(_settings.NameDisplay);

        int volume = (int)Math.Round(Math.Clamp(_settings.Volume, 0f, 1f) * 100);
        _volumeBar.Value = Math.Clamp(volume, _volumeBar.Minimum, _volumeBar.Maximum);
    }

    /// <summary>
    /// 切换“标题 / 艺术家”显示写法（原文 ↔ 罗马化），并刷新列表、正在播放面板与菜单选中状态。
    /// </summary>
    private void SetNameDisplay(TrackNameDisplay display)
    {
        if (_settings.NameDisplay == display && _nameDisplayCheck.Text.Length > 0)
        {
            return;
        }

        _settings.NameDisplay = display;
        ApplyNameDisplay(display);
        _trackList.Invalidate();

        SetStatus(display == TrackNameDisplay.Original
            ? "标题与艺术家已切换为原文（Unicode）写法。"
            : "标题与艺术家已切换为罗马化（拉丁）写法。");
    }

    private void ApplyNameDisplay(TrackNameDisplay display)
    {
        _suppressNameDisplay = true;
        _nameDisplayCheck.Checked = display == TrackNameDisplay.Original;
        _suppressNameDisplay = false;

        _nameDisplayCheck.Text = display == TrackNameDisplay.Original ? "显示：原文" : "显示：罗马化";
        _nameDisplayRomanizedItem.Checked = display == TrackNameDisplay.Romanized;
        _nameDisplayOriginalItem.Checked = display == TrackNameDisplay.Original;
        _nowPlaying.NameDisplay = display;
    }

    private void ToggleNameDisplay()
        => SetNameDisplay(_settings.NameDisplay == TrackNameDisplay.Original
            ? TrackNameDisplay.Romanized
            : TrackNameDisplay.Original);

    private async void OnShown(object? sender, EventArgs e)
    {
        LayoutSplitters();
        BeginInvoke(LayoutSplitters);
        _ready = true;

        if (!string.IsNullOrWhiteSpace(_settings.OsuDirectory))
        {
            // 默认直接读音乐库缓存（秒级）；只有第一次使用或缓存失效才重新扫描谱面。
            await LoadLibraryAsync();
        }
        else
        {
            SetStatus("尚未指定 osu! 目录：请使用“文件 → 选择 osu! 目录…”或“自动检测 osu! 目录”。");
        }

        if (_playback.OutputAvailable)
        {
            _libraryLabel.Text = "音频输出就绪";
        }
        else
        {
            _libraryLabel.Text = "未检测到音频输出设备";
        }

        if (ScreenshotPath is not null)
        {
            // 截图诊断模式：自动播放第一首曲目，便于验证“正在播放”面板。
            if (_visibleTracks.Count > 0)
            {
                try
                {
                    _trackList.SelectedIndices.Clear();
                    _trackList.SelectedIndices.Add(0);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // 忽略。
                }

                PlayQueueAt(_visibleTracks, 0);
            }

            StartScreenshotCapture();
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        _analysisCts?.Cancel();
        SaveSettings();

        try
        {
            _playlistStore.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 忽略保存失败。
        }

        try
        {
            _durationCache.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        _playback.Dispose();
    }

    private void SaveSettings()
    {
        _settings.Volume = _playback.Volume;
        _settings.PlaybackMode = _playback.Mode;
        _settings.OnlyPlayable = _onlyPlayableCheck.Checked;
        _settings.SearchField = (SearchField)Math.Max(0, _searchFieldBox.SelectedIndex);
        _settings.LastPlaylistId = _currentPlaylistId;

        Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.WindowWidth = Math.Max(MinimumSize.Width, bounds.Width);
        _settings.WindowHeight = Math.Max(MinimumSize.Height, bounds.Height);
        _settings.WindowX = bounds.X;
        _settings.WindowY = bounds.Y;
        _settings.WindowMaximized = WindowState == FormWindowState.Maximized;

        _settingsStore.Save(_settings);
    }

    /// <summary>
    /// 把后台线程（音频回调）上的操作切回 UI 线程。
    /// </summary>
    private void RunOnUi(Action action)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (!InvokeRequired)
        {
            action();
            return;
        }

        try
        {
            BeginInvoke(action);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // 窗口正在销毁。
        }
    }

    private void SetStatus(string message) => RunOnUi(() => _statusLabel.Text = message);

    private void ShowWarning(string title, string message)
        => MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void ShowInformation(string title, string message)
        => MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!_ready)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        switch (keyData)
        {
            case Keys.Space when !IsTextInputFocused():
                TogglePlayPause();
                return true;

            case Keys.F5:
                _ = RescanAsync();
                return true;

            case Keys.Control | Keys.F:
                _searchBox.Focus();
                _searchBox.SelectAll();
                return true;

            case Keys.Control | Keys.I:
                ImportOsuCollections();
                return true;

            case Keys.Control | Keys.N:
                CreatePlaylistInteractive();
                return true;

            case Keys.Control | Keys.T:
                ToggleNameDisplay();
                return true;

            case Keys.Control | Keys.O:
                _ = SelectOsuFolderAsync();
                return true;

            case Keys.Control | Keys.Right:
                PlayNext();
                return true;

            case Keys.Control | Keys.Left:
                PlayPrevious();
                return true;

            case Keys.Enter when _trackList.Focused:
                PlaySelectedTrack();
                return true;

            case Keys.Delete when _trackList.Focused:
                RemoveSelectedFromPlaylist();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// 判断当前是否正在编辑文本。
    /// 注意：<see cref="Form.ActiveControl"/> 只返回窗口直属子控件（搜索框嵌在
    /// 分隔容器 / TableLayoutPanel 里时得到的是容器），必须沿容器链找到真正的焦点控件，
    /// 否则空格会被当成播放 / 暂停快捷键。
    /// </summary>
    private bool IsTextInputFocused()
        => GetFocusedControl() is TextBoxBase or ComboBox { DropDownStyle: ComboBoxStyle.DropDown };

    private Control? GetFocusedControl()
    {
        Control? control = ActiveControl;

        while (control is IContainerControl container)
        {
            Control? next = container.ActiveControl;

            if (next is null || ReferenceEquals(next, control))
            {
                break;
            }

            control = next;
        }

        return control;
    }

    private void ShowAbout()
    {
        string details = string.Join(
            Environment.NewLine,
            "osu! 音乐播放器 1.0",
            string.Empty,
            "功能：",
            "  · 读取 osu! stable 的 osu!.db，把同一谱面集使用同一音频的难度合并为一首曲目",
            "  · 从全部音乐中按标题/艺术家/谱师/标签/难度/谱面集 ID 搜索",
            "  · 标题与艺术家可在原文（Unicode）与罗马化写法之间一键切换（Ctrl+T）",
            "  · 创建、重命名、删除自定义播放列表，收藏喜欢的曲目",
            "  · 把 osu! 收藏夹（collection.db）导入为播放列表",
            "  · 播放 mp3 / ogg / wav，支持顺序、列表循环、单曲循环与随机播放",
            string.Empty,
            $"数据目录：{AppPaths.DataDirectory}",
            $"当前 osu! 目录：{_settings.OsuDirectory ?? "（未设置）"}",
            string.Empty,
            "osu!.db / collection.db 的解析参考了 CollectionManager（MIT，Piotrekol）。");

        TextReportDialog.Show(this, "关于 osu! 音乐播放器", details);
    }

    private void ShowShortcuts()
    {
        string details = string.Join(
            Environment.NewLine,
            "空格          播放 / 暂停",
            "Ctrl+← / →    上一首 / 下一首",
            "Enter         播放选中的曲目",
            "Delete        从当前播放列表移除选中曲目",
            "Ctrl+F        聚焦搜索框",
            "Ctrl+N        新建播放列表",
            "Ctrl+T        在原文（Unicode）与罗马化写法之间切换标题 / 艺术家",
            "Ctrl+I        导入 osu! 收藏夹",
            "Ctrl+O        选择 osu! 目录",
            "F5            重新扫描音乐库（并更新缓存）",
            "双击列表       播放该曲目",
            "拖动曲目       拖到左侧播放列表即可加入");

        TextReportDialog.Show(this, "快捷键说明", details);
    }

    private void OpenDataFolder()
    {
        try
        {
            AppPaths.EnsureCreated();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppPaths.DataDirectory) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowWarning("打开失败", ex.Message);
        }
    }

    private void OpenInExplorer(MusicTrack track)
    {
        if (!File.Exists(track.AudioFilePath))
        {
            ShowWarning("文件不存在", $"找不到音频文件：{track.AudioFilePath}");
            return;
        }

        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{track.AudioFilePath}\"");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowWarning("打开失败", ex.Message);
        }
    }

    private void CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            SetStatus("已复制到剪贴板。");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ArgumentException)
        {
            ShowWarning("复制失败", ex.Message);
        }
    }

    private void StartScreenshotCapture()
    {
        System.Windows.Forms.Timer timer = new() { Interval = 2500 };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();

            try
            {
                string path = Path.GetFullPath(ScreenshotPath!);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                Console.WriteLine(
                    $"[诊断] 主分隔条 宽度={_mainSplit?.Width} 距离={_mainSplit?.SplitterDistance}；"
                    + $"列表分隔条 宽度={_trackSplit?.Width} 距离={_trackSplit?.SplitterDistance} "
                    + $"左={_trackSplit?.Panel1.Width} 右={_trackSplit?.Panel2.Width} 正在播放面板={_nowPlaying.Width}x{_nowPlaying.Height}");

                WindowCapture.Capture(this, path);
                Console.WriteLine($"截图已保存：{path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"截图失败：{ex.Message}");
            }

            Close();
        };

        timer.Start();
    }
}
