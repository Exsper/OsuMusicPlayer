namespace OsuMusicPlayer.App.Dialogs;

/// <summary>显示一段可复制的长文本（扫描结果、导入结果等）。</summary>
public sealed class TextReportDialog : Form
{
    private TextReportDialog(string title, string summary, string details)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(520, 320);
        ClientSize = new Size(660, 460);
        ShowInTaskbar = false;

        TextBox summaryBox = new()
        {
            Dock = DockStyle.Top,
            Height = 84,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Control,
            Text = summary,
            TabStop = false,
        };

        TextBox detailsBox = new()
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9f),
            Text = details,
        };

        Panel buttons = new() { Dock = DockStyle.Bottom, Height = 44 };

        Button ok = new()
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Width = 96,
            Height = 30,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            Location = new Point(buttons.Width - 108, 6),
        };

        Button copy = new()
        {
            Text = "复制",
            Width = 96,
            Height = 30,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            Location = new Point(buttons.Width - 210, 6),
        };

        copy.Click += (_, _) =>
        {
            if (details.Length > 0 || summary.Length > 0)
            {
                Clipboard.SetText(summary + Environment.NewLine + details);
            }
        };

        buttons.Controls.Add(ok);
        buttons.Controls.Add(copy);

        Controls.Add(detailsBox);
        Controls.Add(summaryBox);
        Controls.Add(buttons);

        AcceptButton = ok;
        CancelButton = ok;
    }

    public static void Show(IWin32Window owner, string title, string summary, string details = "")
    {
        using TextReportDialog dialog = new(title, summary, details);
        dialog.ShowDialog(owner);
    }
}
