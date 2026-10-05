namespace OsuMusicPlayer.App.Dialogs;

/// <summary>简单的单行文本输入对话框（WinForms 没有内置 InputBox）。</summary>
public sealed class TextInputDialog : Form
{
    private readonly TextBox _textBox = new();

    private TextInputDialog(string title, string prompt, string initialValue, string okText)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 128);
        Padding = new Padding(12);

        Label label = new()
        {
            Text = prompt,
            Dock = DockStyle.Top,
            Height = 24,
            AutoEllipsis = true,
        };

        _textBox.Dock = DockStyle.Top;
        _textBox.Text = initialValue;
        _textBox.SelectAll();
        _textBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                DialogResult = DialogResult.OK;
                Close();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };

        Panel buttons = new() { Dock = DockStyle.Bottom, Height = 40 };

        Button ok = new()
        {
            Text = okText,
            DialogResult = DialogResult.OK,
            Width = 96,
            Height = 30,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            Location = new Point(buttons.Width - 210, 4),
        };

        Button cancel = new()
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Width = 96,
            Height = 30,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            Location = new Point(buttons.Width - 108, 4),
        };

        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        Panel spacer = new() { Dock = DockStyle.Top, Height = 8 };

        Controls.Add(_textBox);
        Controls.Add(spacer);
        Controls.Add(label);
        Controls.Add(buttons);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    public static string? Show(IWin32Window owner, string title, string prompt, string initialValue = "", string okText = "确定")
    {
        using TextInputDialog dialog = new(title, prompt, initialValue, okText);

        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._textBox.Text.Trim() : null;
    }
}
