namespace WineCabinet;

internal static class RenameItemDialog
{
    internal static string? Show(IWin32Window owner, string name, bool selectExtension)
    {
        using var dialog = new Form
        {
            Text = "重命名", FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(380, 125),
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false
        };
        var input = new TextBox { Text = name, Location = new Point(18, 20), Width = 344 };
        var confirm = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(198, 76), Width = 78 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(284, 76), Width = 78 };
        dialog.Controls.AddRange(new Control[] { input, confirm, cancel });
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
