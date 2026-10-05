namespace OsuMusicPlayer.App.Controls;

using System.ComponentModel;
using OsuMusicPlayer.Core.Models;

/// <summary>右下角的“正在播放”面板：封面、标题、艺术家、谱面集信息与难度列表。</summary>
public sealed class NowPlayingPanel : UserControl
{
    private const int CoverHeight = 150;

    private readonly TableLayoutPanel _layout = new();
    private readonly PictureBox _cover = new();
    private readonly Label _title = new();
    private readonly Label _artist = new();
    private readonly Label _meta = new();
    private readonly LinkLabel _mapSetLink = new();
    private readonly ListBox _difficulties = new();
    private readonly Label _difficultyHeader = new();
    private readonly Label _path = new();
    private readonly ToolTip _toolTip = new();
    private bool _showCover = true;

    public NowPlayingPanel()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(12);
        BackColor = SystemColors.Control;

        _layout.Dock = DockStyle.Fill;
        _layout.ColumnCount = 1;
        _layout.RowCount = 8;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, CoverHeight));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));

        _cover.Dock = DockStyle.Fill;
        _cover.SizeMode = PictureBoxSizeMode.Zoom;
        _cover.BackColor = Color.FromArgb(38, 38, 44);
        _cover.Visible = false;

        _title.Dock = DockStyle.Fill;
        _title.Font = new Font(Font.FontFamily, 11.5f, FontStyle.Bold);
        _title.AutoEllipsis = true;
        _title.TextAlign = ContentAlignment.BottomLeft;
        _title.Text = "尚未播放任何曲目";

        _artist.Dock = DockStyle.Fill;
        _artist.AutoEllipsis = true;
        _artist.ForeColor = SystemColors.GrayText;

        _meta.Dock = DockStyle.Fill;
        _meta.AutoEllipsis = true;

        _mapSetLink.Dock = DockStyle.Fill;
        _mapSetLink.Text = "打开 osu! 谱面页";
        _mapSetLink.Visible = false;
        _mapSetLink.LinkClicked += (_, _) => OpenMapSetPage();

        _difficultyHeader.Dock = DockStyle.Fill;
        _difficultyHeader.Text = "难度";
        _difficultyHeader.ForeColor = SystemColors.GrayText;
        _difficultyHeader.TextAlign = ContentAlignment.BottomLeft;

        _difficulties.Dock = DockStyle.Fill;
        _difficulties.BorderStyle = BorderStyle.FixedSingle;
        _difficulties.IntegralHeight = false;
        _difficulties.SelectionMode = SelectionMode.None;

        _path.Dock = DockStyle.Fill;
        _path.AutoEllipsis = true;
        _path.ForeColor = SystemColors.GrayText;
        _path.Font = new Font(Font.FontFamily, 8f);

        _layout.Controls.Add(_cover, 0, 0);
        _layout.Controls.Add(_title, 0, 1);
        _layout.Controls.Add(_artist, 0, 2);
        _layout.Controls.Add(_meta, 0, 3);
        _layout.Controls.Add(_mapSetLink, 0, 4);
        _layout.Controls.Add(_difficultyHeader, 0, 5);
        _layout.Controls.Add(_difficulties, 0, 6);
        _layout.Controls.Add(_path, 0, 7);

        Controls.Add(_layout);
    }

    /// <summary>是否显示封面（设置里可关闭）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public bool ShowCover
    {
        get => _showCover;
        set
        {
            _showCover = value;
            UpdateCoverRow();
        }
    }

    public void ShowTrack(MusicTrack? track, Image? cover)
    {
        Image? previous = _cover.Image;
        _cover.Image = cover;
        previous?.Dispose();

        if (track is null)
        {
            _title.Text = "尚未播放任何曲目";
            _artist.Text = "双击列表中的歌曲即可开始播放";
            _meta.Text = string.Empty;
            _path.Text = string.Empty;
            _mapSetLink.Visible = false;
            _difficulties.Items.Clear();
            UpdateCoverRow();
            UpdateDifficultyRow(visible: false);
            return;
        }

        UpdateDifficultyRow(visible: true);

        _title.Text = track.DisplayTitle;
        _artist.Text = track.DisplayArtist
            + (string.IsNullOrWhiteSpace(track.Creator) ? string.Empty : $"  ·  谱师 {track.Creator}");

        string stars = track.StarsNomod > 0 ? $"  ·  ★ {track.StarsNomod:0.##}" : string.Empty;
        string bpm = track.MainBpm > 0 ? $"  ·  {track.MainBpm:0.#} BPM" : string.Empty;
        string set = track.MapSetId > 0 ? $"#{track.MapSetId}" : "本地谱面";

        _meta.Text = $"{set}  ·  {track.ModeText}{stars}{bpm}{Environment.NewLine}"
            + $"{track.StateText}  ·  {track.Difficulties.Count} 个难度  ·  {track.AudioFileName}"
            + (track.AudioFileExists ? string.Empty : "（音频缺失）");

        _difficulties.BeginUpdate();
        _difficulties.Items.Clear();

        foreach (string difficulty in track.Difficulties)
        {
            _difficulties.Items.Add(difficulty);
        }

        _difficulties.EndUpdate();

        _path.Text = track.AudioFilePath;
        _toolTip.SetToolTip(_path, track.AudioFilePath);
        _toolTip.SetToolTip(_title, $"{track.DisplayArtist} - {track.DisplayTitle}");

        _mapSetLink.Visible = track.MapSetId > 0;
        _mapSetLink.Tag = track.MapSetUrl;

        UpdateCoverRow();
    }

    private void UpdateCoverRow()
    {
        bool visible = _showCover && _cover.Image is not null;

        _cover.Visible = visible;
        _layout.RowStyles[0].Height = visible ? CoverHeight : 0f;
    }

    private void UpdateDifficultyRow(bool visible)
    {
        _difficultyHeader.Visible = visible;
        _difficulties.Visible = visible;
        _layout.RowStyles[5].Height = visible ? 22f : 0f;
        _layout.RowStyles[6].Height = visible ? 100f : 0f;
        _layout.RowStyles[6].SizeType = visible ? SizeType.Percent : SizeType.Absolute;
    }

    private void OpenMapSetPage()
    {
        if (_mapSetLink.Tag is not string url || url.Length == 0)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 打不开浏览器时忽略。
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cover.Image?.Dispose();
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }
}
