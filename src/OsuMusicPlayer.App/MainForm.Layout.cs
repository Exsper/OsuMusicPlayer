namespace OsuMusicPlayer.App;

using OsuMusicPlayer.App.Controls;
using OsuMusicPlayer.Core.Enums;
using OsuMusicPlayer.Core.Services;

/// <summary>主窗口的界面构建（全部用代码布局，不使用设计器文件）。</summary>
public sealed partial class MainForm
{
    private readonly MenuStrip _menu = new();
    private readonly ToolStripMenuItem _fileMenu = new("文件(&F)");
    private readonly ToolStripMenuItem _playlistMenu = new("播放列表(&P)");
    private readonly ToolStripMenuItem _playbackMenu = new("播放(&B)");
    private readonly ToolStripMenuItem _helpMenu = new("帮助(&H)");

    private readonly ToolStripMenuItem _selectOsuFolderItem = new("选择 osu! 目录…");
    private readonly ToolStripMenuItem _autoDetectItem = new("自动检测 osu! 目录");
    private readonly ToolStripMenuItem _rescanItem = new("重新扫描音乐库\tF5");
    private readonly ToolStripMenuItem _importCollectionsItem = new("导入 osu! 收藏夹\tCtrl+I");
    private readonly ToolStripMenuItem _importCollectionFileItem = new("从 collection.db 文件导入…");
    private readonly ToolStripMenuItem _exportM3u8Item = new("导出当前列表为 M3U8…");
    private readonly ToolStripMenuItem _openDataFolderItem = new("打开数据目录");
    private readonly ToolStripMenuItem _exitItem = new("退出");

    private readonly ToolStripMenuItem _newPlaylistItem = new("新建播放列表…\tCtrl+N");
    private readonly ToolStripMenuItem _renamePlaylistItem = new("重命名播放列表…");
    private readonly ToolStripMenuItem _deletePlaylistItem = new("删除播放列表");
    private readonly ToolStripMenuItem _clearPlaylistItem = new("清空播放列表");
    private readonly ToolStripMenuItem _showFavoritesItem = new("显示“我喜欢的音乐”");

    private readonly ToolStripMenuItem _togglePlayItem = new("播放 / 暂停\t空格");
    private readonly ToolStripMenuItem _stopItem = new("停止");
    private readonly ToolStripMenuItem _previousItem = new("上一首\tCtrl+←");
    private readonly ToolStripMenuItem _nextItem = new("下一首\tCtrl+→");
    private readonly ToolStripMenuItem _modeSequentialItem = new("顺序播放");
    private readonly ToolStripMenuItem _modeRepeatAllItem = new("列表循环");
    private readonly ToolStripMenuItem _modeRepeatOneItem = new("单曲循环");
    private readonly ToolStripMenuItem _modeShuffleItem = new("随机播放");
    private readonly ToolStripMenuItem _analyzeDurationsItem = new("分析当前列表时长…");
    private readonly ToolStripMenuItem _cancelAnalysisItem = new("取消时长分析") { Enabled = false };

    private readonly ToolStripMenuItem _aboutItem = new("关于");
    private readonly ToolStripMenuItem _shortcutsItem = new("快捷键说明");

    private readonly ListBox _playlistList = new();
    private readonly Button _newPlaylistButton = new();
    private readonly Button _renamePlaylistButton = new();
    private readonly Button _deletePlaylistButton = new();
    private readonly Button _importCollectionsButton = new();
    private readonly Button _importFileButton = new();
    private readonly Button _exportM3u8Button = new();

    private readonly TextBox _searchBox = new();
    private readonly ComboBox _searchFieldBox = new();
    private readonly CheckBox _onlyPlayableCheck = new();
    private readonly Label _resultLabel = new();
    private readonly ListView _trackList = new();

    private readonly NowPlayingPanel _nowPlaying = new();

    private readonly Button _previousButton = new();
    private readonly Button _playPauseButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _nextButton = new();
    private readonly TrackBar _seekBar = new();
    private readonly TrackBar _volumeBar = new();
    private readonly Label _positionLabel = new();
    private readonly Label _durationLabel = new();
    private readonly Label _volumeLabel = new();
    private readonly ComboBox _modeBox = new();

    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly ToolStripStatusLabel _libraryLabel = new();
    private readonly ToolStripProgressBar _progressBar = new();

    private readonly ToolTip _toolTip = new();
    private readonly ContextMenuStrip _trackContextMenu = new();

    private void BuildUi()
    {
        SuspendLayout();

        Text = "osu! 音乐播放器";
        MinimumSize = new Size(1020, 620);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        BuildMenu();
        BuildStatusStrip();

        SplitContainer mainSplit = new()
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = 6,
        };

        mainSplit.Panel1.Controls.Add(BuildPlaylistPanel());
        mainSplit.Panel2.Controls.Add(BuildRightPanel());

        _mainSplit = mainSplit;

        Controls.Add(mainSplit);
        Controls.Add(_statusStrip);
        Controls.Add(_menu);
        MainMenuStrip = _menu;

        _trackContextMenu.Opening += (_, _) => BuildTrackContextMenu();

        ResumeLayout(performLayout: true);
    }

    private SplitContainer? _mainSplit;
    private SplitContainer? _trackSplit;
    private bool _trackSplitInitialized;
    private bool _layingOutSplitters;

    /// <summary>在窗口完成布局后设置分隔条位置（构造时窗口尺寸还是 0，直接设置会抛异常）。</summary>
    private void LayoutSplitters()
    {
        if (_layingOutSplitters)
        {
            return;
        }

        _layingOutSplitters = true;

        try
        {
            TrySetSplitter(_mainSplit, panel1Min: 190, panel2Min: 520, panel1Distance: 232, panel2Width: null);

            // 主分隔条改宽后，内部的分隔容器需要先完成布局，否则算出来的宽度还是旧的。
            PerformLayout();

            // 只自动设置一次：之后用户拖动分隔条的结果会被保留。
            if (!_trackSplitInitialized && _trackSplit is { Width: > 0 })
            {
                _trackSplitInitialized = TrySetSplitter(_trackSplit, panel1Min: 380, panel2Min: 240, panel1Distance: null, panel2Width: 320);
            }
        }
        finally
        {
            _layingOutSplitters = false;
        }
    }

    private static bool TrySetSplitter(SplitContainer? split, int panel1Min, int panel2Min, int? panel1Distance, int? panel2Width)
    {
        if (split is null || split.Width <= 0)
        {
            return false;
        }

        try
        {
            split.Panel1MinSize = panel1Min;
            split.Panel2MinSize = panel2Min;

            int maximum = split.Width - split.Panel2MinSize - split.SplitterWidth;

            if (maximum < split.Panel1MinSize)
            {
                return false;
            }

            int desired = panel2Width is int width
                ? split.Width - width - split.SplitterWidth
                : panel1Distance ?? split.SplitterDistance;

            split.SplitterDistance = Math.Clamp(desired, split.Panel1MinSize, maximum);

            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            // 尺寸还没准备好时忽略，等待下一次布局。
            return false;
        }
    }

    private void BuildMenu()
    {
        _selectOsuFolderItem.Click += async (_, _) => await SelectOsuFolderAsync();
        _autoDetectItem.Click += (_, _) => AutoDetectOsuFolder();
        _rescanItem.Click += async (_, _) => await RescanAsync();
        _importCollectionsItem.Click += (_, _) => ImportOsuCollections();
        _importCollectionFileItem.Click += (_, _) => ImportCollectionFile();
        _exportM3u8Item.Click += (_, _) => ExportCurrentListAsM3u8();
        _openDataFolderItem.Click += (_, _) => OpenDataFolder();
        _exitItem.Click += (_, _) => Close();

        _newPlaylistItem.Click += (_, _) => CreatePlaylistInteractive();
        _renamePlaylistItem.Click += (_, _) => RenameSelectedPlaylist();
        _deletePlaylistItem.Click += (_, _) => DeleteSelectedPlaylist();
        _clearPlaylistItem.Click += (_, _) => ClearSelectedPlaylist();
        _showFavoritesItem.Click += (_, _) => SelectPlaylist(PlaylistStore.FavoritesId);

        _togglePlayItem.Click += (_, _) => TogglePlayPause();
        _stopItem.Click += (_, _) => StopPlayback();
        _previousItem.Click += (_, _) => PlayPrevious();
        _nextItem.Click += (_, _) => PlayNext();
        _modeSequentialItem.Click += (_, _) => SetPlaybackMode(PlaybackMode.Sequential);
        _modeRepeatAllItem.Click += (_, _) => SetPlaybackMode(PlaybackMode.RepeatAll);
        _modeRepeatOneItem.Click += (_, _) => SetPlaybackMode(PlaybackMode.RepeatOne);
        _modeShuffleItem.Click += (_, _) => SetPlaybackMode(PlaybackMode.Shuffle);
        _analyzeDurationsItem.Click += async (_, _) => await AnalyzeDurationsAsync();
        _cancelAnalysisItem.Click += (_, _) => _analysisCts?.Cancel();

        _aboutItem.Click += (_, _) => ShowAbout();
        _shortcutsItem.Click += (_, _) => ShowShortcuts();

        _fileMenu.DropDownItems.AddRange(
        [
            _selectOsuFolderItem,
            _autoDetectItem,
            _rescanItem,
            new ToolStripSeparator(),
            _importCollectionsItem,
            _importCollectionFileItem,
            new ToolStripSeparator(),
            _exportM3u8Item,
            _openDataFolderItem,
            new ToolStripSeparator(),
            _exitItem,
        ]);

        _playlistMenu.DropDownItems.AddRange(
        [
            _newPlaylistItem,
            _renamePlaylistItem,
            _deletePlaylistItem,
            _clearPlaylistItem,
            new ToolStripSeparator(),
            _showFavoritesItem,
        ]);

        _playbackMenu.DropDownItems.AddRange(
        [
            _togglePlayItem,
            _stopItem,
            _previousItem,
            _nextItem,
            new ToolStripSeparator(),
            _modeSequentialItem,
            _modeRepeatAllItem,
            _modeRepeatOneItem,
            _modeShuffleItem,
            new ToolStripSeparator(),
            _analyzeDurationsItem,
            _cancelAnalysisItem,
        ]);

        _helpMenu.DropDownItems.AddRange([_shortcutsItem, _aboutItem]);

        _menu.Items.AddRange([_fileMenu, _playlistMenu, _playbackMenu, _helpMenu]);
    }

    private Control BuildPlaylistPanel()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8, 8, 4, 8),
        };

        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f));

        Label header = new()
        {
            Text = "播放列表",
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _playlistList.Dock = DockStyle.Fill;
        _playlistList.IntegralHeight = false;
        _playlistList.AllowDrop = true;
        _playlistList.SelectedIndexChanged += (_, _) => OnPlaylistSelectionChanged();
        _playlistList.DragEnter += PlaylistList_DragEnter;
        _playlistList.DragDrop += PlaylistList_DragDrop;

        TableLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
        };

        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 33f));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 33f));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 34f));

        ConfigureButton(_newPlaylistButton, "新建", (_, _) => CreatePlaylistInteractive());
        ConfigureButton(_renamePlaylistButton, "重命名", (_, _) => RenameSelectedPlaylist());
        ConfigureButton(_deletePlaylistButton, "删除", (_, _) => DeleteSelectedPlaylist());
        ConfigureButton(_exportM3u8Button, "导出 M3U8", (_, _) => ExportCurrentListAsM3u8());
        ConfigureButton(_importCollectionsButton, "导入收藏夹", (_, _) => ImportOsuCollections());
        ConfigureButton(_importFileButton, "导入 db 文件", (_, _) => ImportCollectionFile());

        buttons.Controls.Add(_newPlaylistButton, 0, 0);
        buttons.Controls.Add(_renamePlaylistButton, 1, 0);
        buttons.Controls.Add(_deletePlaylistButton, 0, 1);
        buttons.Controls.Add(_exportM3u8Button, 1, 1);
        buttons.Controls.Add(_importCollectionsButton, 0, 2);
        buttons.Controls.Add(_importFileButton, 1, 2);

        _toolTip.SetToolTip(_importCollectionsButton, "把 osu! 安装目录下 collection.db 里的收藏夹导入为播放列表");
        _toolTip.SetToolTip(_importFileButton, "从任意 collection.db / .db 收藏夹文件导入");
        _toolTip.SetToolTip(_exportM3u8Button, "把当前显示的列表导出为 M3U8 播放列表");

        panel.Controls.Add(header, 0, 0);
        panel.Controls.Add(_playlistList, 0, 1);
        panel.Controls.Add(buttons, 0, 2);

        return panel;
    }

    private static void ConfigureButton(Button button, string text, EventHandler onClick)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(2);
        button.Click += onClick;
    }

    private Control BuildRightPanel()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(4, 8, 8, 8),
        };

        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f));

        panel.Controls.Add(BuildSearchBar(), 0, 0);

        SplitContainer trackSplit = new()
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 6,
        };

        trackSplit.Panel1.Controls.Add(BuildTrackList());
        trackSplit.Panel2.Controls.Add(_nowPlaying);
        trackSplit.Resize += (_, _) => LayoutSplitters();

        _trackSplit = trackSplit;

        panel.Controls.Add(trackSplit, 0, 1);
        panel.Controls.Add(BuildPlayerBar(), 0, 2);

        return panel;
    }

    private Control BuildSearchBar()
    {
        TableLayoutPanel bar = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
        };

        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210f));

        Label label = new()
        {
            Text = "搜索",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _searchBox.Dock = DockStyle.Fill;
        _searchBox.PlaceholderText = "关键词（空格分隔多个词，支持 artist: title: creator: tag: diff: set: 前缀）";
        _searchBox.TextChanged += (_, _) => RestartSearchTimer();

        _searchFieldBox.Dock = DockStyle.Fill;
        _searchFieldBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _searchFieldBox.Items.AddRange(["全部字段", "仅标题", "仅艺术家", "仅谱师", "仅标签", "仅难度名"]);
        _searchFieldBox.SelectedIndex = 0;
        _searchFieldBox.SelectedIndexChanged += (_, _) => ApplyFilter();

        _onlyPlayableCheck.Dock = DockStyle.Fill;
        _onlyPlayableCheck.Text = "仅显示可播放";
        _onlyPlayableCheck.Checked = true;
        _onlyPlayableCheck.CheckedChanged += (_, _) => ApplyFilter();

        _resultLabel.Dock = DockStyle.Fill;
        _resultLabel.TextAlign = ContentAlignment.MiddleRight;
        _resultLabel.ForeColor = SystemColors.GrayText;

        bar.Controls.Add(label, 0, 0);
        bar.Controls.Add(_searchBox, 1, 0);
        bar.Controls.Add(_searchFieldBox, 2, 0);
        bar.Controls.Add(_onlyPlayableCheck, 3, 0);
        bar.Controls.Add(_resultLabel, 4, 0);

        return bar;
    }

    private Control BuildTrackList()
    {
        _trackList.Dock = DockStyle.Fill;
        _trackList.View = View.Details;
        _trackList.FullRowSelect = true;
        _trackList.HideSelection = false;
        _trackList.MultiSelect = true;
        _trackList.VirtualMode = true;
        _trackList.ShowItemToolTips = true;
        _trackList.AllowColumnReorder = false;
        _trackList.ContextMenuStrip = _trackContextMenu;

        _trackList.Columns.Add("#", 48, HorizontalAlignment.Right);
        _trackList.Columns.Add("标题", 280, HorizontalAlignment.Left);
        _trackList.Columns.Add("艺术家", 190, HorizontalAlignment.Left);
        _trackList.Columns.Add("谱师", 130, HorizontalAlignment.Left);
        _trackList.Columns.Add("谱面集", 80, HorizontalAlignment.Left);
        _trackList.Columns.Add("难度", 52, HorizontalAlignment.Right);
        _trackList.Columns.Add("时长", 68, HorizontalAlignment.Right);
        _trackList.Columns.Add("模式", 58, HorizontalAlignment.Left);
        _trackList.Columns.Add("星数", 54, HorizontalAlignment.Right);
        _trackList.Columns.Add("BPM", 60, HorizontalAlignment.Right);
        _trackList.Columns.Add("状态", 70, HorizontalAlignment.Left);

        _trackList.RetrieveVirtualItem += TrackList_RetrieveVirtualItem;
        _trackList.ItemActivate += (_, _) => PlaySelectedTrack();
        _trackList.ColumnClick += TrackList_ColumnClick;
        _trackList.KeyDown += TrackList_KeyDown;
        _trackList.ItemDrag += TrackList_ItemDrag;

        return _trackList;
    }

    private Control BuildPlayerBar()
    {
        TableLayoutPanel bar = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 11,
            RowCount = 1,
        };

        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142f));

        ConfigureTransportButton(_previousButton, "⏮", "上一首 (Ctrl+←)", (_, _) => PlayPrevious());
        ConfigureTransportButton(_playPauseButton, "▶", "播放 / 暂停 (空格)", (_, _) => TogglePlayPause());
        ConfigureTransportButton(_stopButton, "⏹", "停止", (_, _) => StopPlayback());
        ConfigureTransportButton(_nextButton, "⏭", "下一首 (Ctrl+→)", (_, _) => PlayNext());

        _positionLabel.Dock = DockStyle.Fill;
        _positionLabel.TextAlign = ContentAlignment.MiddleRight;
        _positionLabel.Text = "0:00";

        _seekBar.Dock = DockStyle.Fill;
        _seekBar.Minimum = 0;
        _seekBar.Maximum = 1000;
        _seekBar.TickStyle = TickStyle.None;
        _seekBar.MouseDown += (_, _) => _seeking = true;
        _seekBar.MouseUp += (_, _) =>
        {
            _playback.Seek(_seekBar.Value / 1000d * Math.Max(0, _playback.Duration));
            _seeking = false;
        };
        _seekBar.Scroll += (_, _) =>
        {
            if (_seeking)
            {
                UpdatePositionLabels(_seekBar.Value / (double)_seekBar.Maximum * Math.Max(0, _playback.Duration), Math.Max(0, _playback.Duration));
            }
        };

        _durationLabel.Dock = DockStyle.Fill;
        _durationLabel.TextAlign = ContentAlignment.MiddleLeft;
        _durationLabel.Text = "0:00";

        _volumeLabel.Dock = DockStyle.Fill;
        _volumeLabel.TextAlign = ContentAlignment.MiddleRight;
        _volumeLabel.Text = "音量";

        _volumeBar.Dock = DockStyle.Fill;
        _volumeBar.Minimum = 0;
        _volumeBar.Maximum = 100;
        _volumeBar.TickStyle = TickStyle.None;
        _volumeBar.Value = 80;
        _volumeBar.ValueChanged += (_, _) => OnVolumeChanged();

        _modeBox.Dock = DockStyle.Fill;
        _modeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _modeBox.Items.AddRange(["顺序播放", "列表循环", "单曲循环", "随机播放"]);
        _modeBox.SelectedIndex = 1;
        _modeBox.SelectedIndexChanged += (_, _) =>
        {
            if (!_suppressModeChange)
            {
                SetPlaybackMode((PlaybackMode)_modeBox.SelectedIndex);
            }
        };

        bar.Controls.Add(_previousButton, 0, 0);
        bar.Controls.Add(_playPauseButton, 1, 0);
        bar.Controls.Add(_stopButton, 2, 0);
        bar.Controls.Add(_nextButton, 3, 0);
        bar.Controls.Add(_positionLabel, 4, 0);
        bar.Controls.Add(_seekBar, 5, 0);
        bar.Controls.Add(_durationLabel, 6, 0);
        bar.Controls.Add(_volumeLabel, 7, 0);
        bar.Controls.Add(_volumeBar, 8, 0);
        bar.Controls.Add(new Label { Text = "模式", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 9, 0);
        bar.Controls.Add(_modeBox, 10, 0);

        return bar;
    }

    private static void ConfigureTransportButton(Button button, string text, string tooltip, EventHandler onClick)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(2);
        button.Font = new Font("Segoe UI Symbol", 12f);
        button.Click += onClick;
    }

    private void BuildStatusStrip()
    {
        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.Text = "就绪";

        _libraryLabel.TextAlign = ContentAlignment.MiddleRight;

        _progressBar.Visible = false;
        _progressBar.Width = 160;

        _statusStrip.Items.AddRange([_statusLabel, _libraryLabel, _progressBar]);
    }
}
