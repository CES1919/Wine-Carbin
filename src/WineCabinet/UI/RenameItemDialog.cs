namespace WineCabinet;

internal static class RenameItemDialog
{
    internal static string? Show(IWin32Window owner, string name, bool selectExtension)
    {
        using var dialog = new Form
        {
            Text = "重命名",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Font = SystemFonts.MessageBoxFont,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Padding = new Padding(14)
        };
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2
        };
        var input = new TextBox
        {
            Text = name,
            MinimumSize = new Size(340, 0),
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 12)
        };
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(78, 28) };
        var confirm = new Button { Text = "确定", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(78, 28), Margin = new Padding(0, 0, 8, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(confirm);
        layout.Controls.Add(input, 0, 0);
        layout.Controls.Add(buttons, 0, 1);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = confirm;
        dialog.CancelButton = cancel;
        dialog.Shown += (_, _) =>
        {
            input.Focus();
            input.Select(0, selectExtension ? name.Length : Path.GetFileNameWithoutExtension(name).Length);
        };
        return dialog.ShowDialog(owner) == DialogResult.OK && !string.IsNullOrWhiteSpace(input.Text) ? input.Text.Trim() : null;
    }
}
