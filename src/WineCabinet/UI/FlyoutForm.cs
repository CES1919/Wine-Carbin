using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace WineCabinet;

internal sealed class FlyoutForm : Form
{
    private const string InternalDragFormat = "WineCabinet.ShortcutItem";
    internal const int AnchorOffset = 177;
    private static readonly Size NineGridSize = new(354, 354);
    private static readonly Size SixteenGridSize = new(464, 464);
    private readonly ShortcutLibrary _library;
    private readonly LauncherForm _launcher;
    private readonly GridLayoutSettings _layoutSettings;
    private LibraryFileWatcher? _fileWatcher;
    private readonly TableLayoutPanel _grid;
    private readonly PageBadge _pageIndicator;
    private readonly System.Windows.Forms.Timer _hideTimer;
    private readonly System.Windows.Forms.Timer _animationTimer;
    private readonly System.Windows.Forms.Timer _dragEdgeTimer;
    private readonly EmbeddedImage? _centerArtwork;
    private readonly Image? _centerImage;
    private readonly EventHandler? _centerAnimationHandler;
    private DateTime _animationStarted;
    private Rectangle _animationFrom;
    private Rectangle _animationTo;
    private Point _anchorCenter;
    private bool _expanding;
    private bool _contextMenuOpen;
    private ShortcutItem? _dragCandidate;
    private Point _dragStartScreen;
    private bool _dragStarted;
    private bool _internalDropCompleted;
    private bool _suppressNextLaunch;
    private ShortcutItem? _pendingDropTarget;
    private bool _edgePageArmed;
    private int _page;
    private int _gridDimension;
    internal bool AllowClose;

    public Size CurrentExpandedSize => _gridDimension == 4 ? SixteenGridSize : NineGridSize;
    private int ItemsPerPage => GridLayoutRules.ItemsPerPage(_gridDimension);
    private int CenterSlot => GridLayoutRules.CenterSlot(_gridDimension);
    private int[] ItemSlots => GridLayoutRules.ItemSlots(_gridDimension);

    public FlyoutForm(ShortcutLibrary library, LauncherForm launcher)
    {
        _library = library;
        _launcher = launcher;
        _layoutSettings = GridLayoutSettings.Load();
        _gridDimension = _layoutSettings.Dimension;
        Text = "Wine Cabinet";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = false;
        StartPosition = FormStartPosition.Manual;
        ClientSize = CurrentExpandedSize;
        BackColor = Color.FromArgb(248, 248, 250);
        Opacity = 0.98;
        DoubleBuffered = true;

        _centerArtwork = EmbeddedImage.LoadLauncher();
        _centerImage = _centerArtwork?.Image;
        if (_centerImage is not null)
        {
            _centerAnimationHandler = (_, _) =>
            {
                if (!IsDisposed && IsHandleCreated)
                    try { BeginInvoke(InvalidateCenterImage); } catch (InvalidOperationException) { }
            };
            ImageAnimator.Animate(_centerImage, _centerAnimationHandler);
        }

        _grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        ConfigureGrid();
        Controls.Add(_grid);

        _pageIndicator = new PageBadge
        {
            Text = "1 / 1",
            Size = new Size(48, 20),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI Variable Text", 8f, FontStyle.Regular),
            ForeColor = Color.FromArgb(72, 72, 76),
            BackColor = Color.White,
            Cursor = Cursors.Default
        };
        Controls.Add(_pageIndicator);
        WireInteraction(_pageIndicator);
        LayoutPageIndicator();

        _hideTimer = new System.Windows.Forms.Timer { Interval = 420 };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (!_contextMenuOpen && !_dragStarted && !Bounds.Contains(Cursor.Position) && !_launcher.Bounds.Contains(Cursor.Position)) StartCollapse();
        };
        _animationTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _animationTimer.Tick += AnimateStep;
        _dragEdgeTimer = new System.Windows.Forms.Timer { Interval = 80 };
        _dragEdgeTimer.Tick += (_, _) => CheckDragEdge();

        MouseEnter += (_, _) => CancelHide();
        MouseLeave += (_, _) => ScheduleHide();
        MouseWheel += OnMouseWheel;
        Deactivate += (_, _) => ScheduleHide();
        SizeChanged += (_, _) => { UpdateRegion(); LayoutPageIndicator(); };
        FormClosing += (_, e) => { if (!AllowClose) { e.Cancel = true; StartCollapse(); } };
        FormClosed += (_, _) =>
        {
            _fileWatcher?.Dispose();
            if (_centerImage is not null)
            {
                if (_centerAnimationHandler is not null) ImageAnimator.StopAnimate(_centerImage, _centerAnimationHandler);
                _centerArtwork?.Dispose();
            }
        };
        Shown += (_, _) => _fileWatcher ??= new LibraryFileWatcher(_library, this);
        _library.Changed += (_, _) => Rebuild();
        Rebuild();
        UpdateRegion();
    }

    public void ShowExpanded(Point center)
    {
        CancelHide();
        _anchorCenter = center;
        var desired = ExpandedBounds(center);
        _animationTimer.Stop();
        _expanding = true;
        _animationStarted = DateTime.UtcNow;
        _animationFrom = Visible ? Bounds : CollapsedBounds(center);
        _animationTo = desired;
        if (!Visible)
        {
            Bounds = _animationFrom;
            Opacity = 0.25;
            Show(_launcher);
        }
        _animationTimer.Start();
    }

    public void Reposition(Point center)
    {
        _anchorCenter = center;
        if (!Visible || _animationTimer.Enabled) return;
        var desired = ExpandedBounds(center);
        Location = desired.Location;
    }

    public void ScheduleHide() { if (_contextMenuOpen || _dragStarted) return; _hideTimer.Stop(); _hideTimer.Start(); }
    public void CancelHide() => _hideTimer.Stop();

    /// <summary>Collapses the panel immediately, playing the normal animation.</summary>
    public void Collapse()
    {
        if (!Visible) return;
        CancelHide();
        StartCollapse();
    }

    public void SetContextMenuOpen(bool open)
    {
        _contextMenuOpen = open;
        if (open) CancelHide();
        else ScheduleHide();
    }

    public void AddGridModeMenuItem(ContextMenuStrip menu)
    {
        var item = new ToolStripMenuItem();
        void updateText() => item.Text = _gridDimension == 4 ? "切换回九宫格" : "扩展为 16 宫格";
        updateText();
        menu.Opening += (_, _) => updateText();
        item.Click += (_, _) => ToggleGridMode();
        menu.Items.Add(item);
    }

    private void ToggleGridMode()
    {
        var targetDimension = _gridDimension == 4 ? 3 : 4;
        _launcher.BeginInvoke(new Action(() => ApplyGridDimension(targetDimension)));
    }

    private void ApplyGridDimension(int dimension)
    {
        if (_gridDimension == dimension) return;
        var wasVisible = Visible;
        _gridDimension = dimension;
        _layoutSettings.SetDimension(dimension);
        _page = 0;
        ConfigureGrid();
        if (!wasVisible)
            ClientSize = CurrentExpandedSize;
        Rebuild();
        if (wasVisible)
            ShowExpanded(_anchorCenter);
    }

    private void ConfigureGrid()
    {
        _grid.ColumnStyles.Clear();
        _grid.RowStyles.Clear();
        _grid.ColumnCount = _gridDimension;
        _grid.RowCount = _gridDimension;
        for (var index = 0; index < _gridDimension; index++)
        {
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / _gridDimension));
            _grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / _gridDimension));
        }
    }

    private void StartCollapse()
    {
        if (!Visible) return;
        _animationTimer.Stop();
        _expanding = false;
        _animationStarted = DateTime.UtcNow;
        _animationFrom = Bounds;
        _animationTo = CollapsedBounds(_anchorCenter);
        _animationTimer.Start();
    }

    private void AnimateStep(object? sender, EventArgs e)
    {
        var raw = Math.Clamp((DateTime.UtcNow - _animationStarted).TotalMilliseconds / 190d, 0d, 1d);
        var eased = _expanding ? 1d - Math.Pow(1d - raw, 3d) : raw * raw;
        Bounds = Interpolate(_animationFrom, _animationTo, eased);
        Opacity = _expanding ? 0.25 + 0.73 * eased : 0.98 - 0.73 * eased;
        if (raw < 1d) return;
        _animationTimer.Stop();
        if (!_expanding) { Hide(); Opacity = 0.98; }
    }

    private void Rebuild()
    {
        var maxPage = Math.Max(0, (_library.Items.Count - 1) / ItemsPerPage);
        _page = Math.Min(_page, maxPage);
        _grid.SuspendLayout();
        while (_grid.Controls.Count > 0) _grid.Controls[0].Dispose();
        var items = _library.Items.Skip(_page * ItemsPerPage).Take(ItemsPerPage).ToList();
        var slotCount = _gridDimension * _gridDimension;
        for (var slot = 0; slot < slotCount; slot++)
        {
            Control tile;
            if (slot == CenterSlot) tile = CreateCenterTile();
            else
            {
                var index = Array.IndexOf(ItemSlots, slot);
                tile = index >= 0 && index < items.Count ? CreateItemTile(items[index]) : CreateEmptyTile();
            }
            _grid.Controls.Add(tile, slot % _gridDimension, slot / _gridDimension);
        }
        _grid.ResumeLayout();
        _pageIndicator.Text = $"{_page + 1} / {maxPage + 1}";
        _pageIndicator.BringToFront();
    }

    private Control CreateCenterTile()
    {
        var tile = NewTile(Color.White, Cursors.Default);
        var artwork = new ArtworkView { Image = _centerImage, Dock = DockStyle.Fill, BackColor = Color.Transparent };
        tile.Controls.Add(artwork);
        WireInteraction(tile, artwork);
        foreach (var control in new Control[] { tile, artwork })
        {
            control.MouseDown += BeginCenterMove;
        }
        var menu = CreateCenterMenu();
        tile.ContextMenuStrip = artwork.ContextMenuStrip = menu;
        return tile;
    }

    private Control CreateItemTile(ShortcutItem item)
    {
        var tile = NewTile(Color.FromArgb(242, 242, 247), Cursors.Hand);
        var icon = new PictureBox { Size = new Size(44, 44), Location = new Point(30, 14), SizeMode = PictureBoxSizeMode.Zoom, Image = GetIcon(item) };
        var name = new Label { Text = item.Name, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(43, 43, 47), Font = new Font("Segoe UI Variable Text", 8.75f, FontStyle.Regular), Location = new Point(6, 63), Size = new Size(92, 28) };
        tile.Controls.Add(icon); tile.Controls.Add(name);
        void launch(object? _, MouseEventArgs e)
        {
            if (!InputRules.ShouldLaunch(e.Button)) return;
            if (_suppressNextLaunch) { _suppressNextLaunch = false; return; }
            Launch(item);
        }
        tile.MouseClick += launch; icon.MouseClick += launch; name.MouseClick += launch;
        WireInteraction(tile, icon, name);
        WireItemDrag(tile, icon, name, item);
        WireDropTarget(tile, icon, name, item);

        // A cancelled WinForms menu supplies both right-click and Shift+F10 routing.
        // Defer the native menu until the original mouse/context message has returned.
        var menu = new ContextMenuStrip();
        menu.Items.Add("Windows Shell");
        menu.Opening += (_, e) =>
        {
            e.Cancel = true;
            if (_contextMenuOpen) return;
            var position = tile.RectangleToScreen(tile.ClientRectangle).Contains(Cursor.Position)
                ? Cursor.Position : tile.PointToScreen(new Point(tile.Width / 2, tile.Height / 2));
            var extended = (ModifierKeys & Keys.Shift) != 0;
            SetContextMenuOpen(true);
            BeginInvoke(new Action(() => ShowItemContextMenu(item, position, extended)));
        };
        tile.Disposed += (_, _) => menu.Dispose();
        tile.ContextMenuStrip = icon.ContextMenuStrip = name.ContextMenuStrip = menu;
        return tile;
    }

    private void ShowItemContextMenu(ShortcutItem item, Point position, bool extended)
    {
        var fallbackShown = false;
        try
        {
            if (IsDisposed || !_library.Items.Contains(item)) return;
            ShellContextMenu shell;
            try { shell = ShellContextMenu.Create(item.Path, Handle, extended); }
            catch (Exception ex)
            {
                ShowFallbackItemMenu(item, position, ex.Message);
                fallbackShown = true;
                return;
            }
            using (shell)
            {
                switch (shell.Show(Handle, position, item.OriginalPath is null && !item.IsShellItem ? "从图标面板移除" : "恢复到桌面"))
                {
                    case ItemMenuAction.Restore: Restore(item); break;
                    case ItemMenuAction.MoveEarlier: _library.Reorder(item, -1); break;
                    case ItemMenuAction.MoveLater: _library.Reorder(item, 1); break;
                    case ItemMenuAction.Opened: _library.RecordLaunch(item, ItemsPerPage); StartCollapse(); break;
                    case ItemMenuAction.Rename:
                        var name = item.IsShellItem ? item.Name : Path.GetFileName(item.Path);
                        var renamed = RenameItemDialog.Show(this, name, item.IsShellItem || Directory.Exists(item.Path));
                        if (renamed is not null)
                            _library.UpdateShellRename(item, shell.Rename(Handle, renamed), item.IsShellItem ? renamed : null);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"无法执行此项目的操作：{ex.Message}", "Wine Cabinet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { if (!IsDisposed && !fallbackShown) SetContextMenuOpen(false); }
    }

    private void ShowFallbackItemMenu(ShortcutItem item, Point position, string reason)
    {
        var menu = AppMenu.Create();
        menu.Items.Add("Windows 菜单暂不可用")!.Enabled = false;
        menu.Items.Add("查看原因", null, (_, _) => MessageBox.Show(this, reason, "Wine Cabinet", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("打开", null, (_, _) => Launch(item));
        if (CanRunElevated(item)) menu.Items.Add("以管理员身份运行", null, (_, _) => RunElevated(item));
        if (!item.IsShellItem) menu.Items.Add("打开所在位置", null, (_, _) => OpenContainingFolder(item));
        menu.Items.Add("向前移动", null, (_, _) => _library.Reorder(item, -1));
        menu.Items.Add("向后移动", null, (_, _) => _library.Reorder(item, 1));
        menu.Items.Add(item.OriginalPath is null && !item.IsShellItem ? "从图标面板移除" : "恢复到桌面", null, (_, _) => Restore(item));
        if (!item.IsShellItem) menu.Items.Add("属性", null, (_, _) => ShellActions.ShowProperties(item.Path, Handle));
        menu.Opening += (_, _) => SetContextMenuOpen(true);
        menu.Closed += (_, _) =>
        {
            SetContextMenuOpen(false);
            BeginInvoke(new Action(menu.Dispose));
        };
        menu.Show(position);
        SetContextMenuOpen(true);
    }

    private RoundedTile NewTile(Color color, Cursor cursor) => new() { Dock = DockStyle.Fill, Margin = new Padding(5), BackColor = color, Cursor = cursor };

    private Control CreateEmptyTile()
    {
        var tile = NewTile(Color.White, Cursors.Default);
        tile.BorderColor = Color.FromArgb(218, 218, 223);
        WireInteraction(tile);
        return tile;
    }

    private void WireInteraction(params Control[] controls)
    {
        foreach (var control in controls)
        {
            control.MouseEnter += TileEnter;
            control.MouseLeave += TileLeave;
            control.MouseWheel += OnMouseWheel;
        }
    }

    private void WireItemDrag(Control tile, Control icon, Control name, ShortcutItem item)
    {
        foreach (var control in new[] { tile, icon, name })
        {
            control.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                _dragCandidate = item;
                _dragStartScreen = Cursor.Position;
                _dragStarted = false;
            };
            control.MouseMove += (_, e) =>
            {
                if (_dragCandidate is null || _dragStarted || (e.Button & MouseButtons.Left) == 0) return;
                var dragSize = SystemInformation.DragSize;
                if (Math.Abs(Cursor.Position.X - _dragStartScreen.X) < dragSize.Width / 2 &&
                    Math.Abs(Cursor.Position.Y - _dragStartScreen.Y) < dragSize.Height / 2) return;
                StartItemDrag(item);
            };
            control.MouseUp += (_, _) => { if (!_dragStarted) _dragCandidate = null; };
        }
    }

    private void StartItemDrag(ShortcutItem item)
    {
        _dragStarted = true;
        _internalDropCompleted = false;
        _pendingDropTarget = null;
        _suppressNextLaunch = true;
        CancelHide();
        var data = new DataObject();
        data.SetData(InternalDragFormat, item.Name);
        _edgePageArmed = true;
        _dragEdgeTimer.Start();
        try { DoDragDrop(data, DragDropEffects.Move); }
        finally { _dragEdgeTimer.Stop(); }
        if (_internalDropCompleted && _pendingDropTarget is not null)
            _library.Swap(item, _pendingDropTarget);
        else if (DragNavigation.Decide(Bounds, Cursor.Position, _page, Math.Max(0, (_library.Items.Count - 1) / ItemsPerPage)) == DragEdgeAction.Remove)
            ReturnToDesktop(item);
        _dragCandidate = null;
        _dragStarted = false;
        BeginInvoke(() => _suppressNextLaunch = false);
        ScheduleHide();
    }

    private void CheckDragEdge()
    {
        if (!_dragStarted) return;
        var maxPage = Math.Max(0, (_library.Items.Count - 1) / ItemsPerPage);
        var action = DragNavigation.Decide(Bounds, Cursor.Position, _page, maxPage);
        if (action == DragEdgeAction.None)
        {
            _edgePageArmed = true;
            return;
        }
        if (!_edgePageArmed) return;
        if (action == DragEdgeAction.PreviousPage)
        {
            _page--;
            _edgePageArmed = false;
            Rebuild();
        }
        else if (action == DragEdgeAction.NextPage)
        {
            _page++;
            _edgePageArmed = false;
            Rebuild();
        }
    }

    private void ReturnToDesktop(ShortcutItem item)
    {
        try
        {
            if (!item.IsShellItem && !File.Exists(item.Path) && !Directory.Exists(item.Path))
                _library.CompleteExternalMove(item);
            else
                _library.Restore(item);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法返回桌面：{ex.Message}", "Wine Cabinet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void WireDropTarget(Control tile, Control? icon, Control? name, ShortcutItem target)
    {
        var controls = new List<Control> { tile };
        if (icon is not null) controls.Add(icon);
        if (name is not null) controls.Add(name);
        foreach (var control in controls)
        {
            control.AllowDrop = true;
            control.DragEnter += (_, e) =>
            {
                if (e.Data?.GetDataPresent(InternalDragFormat) == true) e.Effect = DragDropEffects.Move;
            };
            control.DragOver += (_, e) =>
            {
                if (e.Data?.GetDataPresent(InternalDragFormat) == true) e.Effect = DragDropEffects.Move;
            };
            control.DragDrop += (_, e) =>
            {
                if (e.Data?.GetDataPresent(InternalDragFormat) != true) return;
                _internalDropCompleted = true;
                _pendingDropTarget = target;
                e.Effect = DragDropEffects.Move;
            };
        }
    }

    private void BeginCenterMove(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        CancelHide();
        _launcher.BeginExternalMove(Cursor.Position);
    }

    private void TileEnter(object? sender, EventArgs e)
    {
        CancelHide();
        var tile = FindTile(sender as Control);
        if (tile is not null && tile.Cursor == Cursors.Hand) tile.BackColor = Color.FromArgb(229, 229, 234);
    }

    private void TileLeave(object? sender, EventArgs e)
    {
        var tile = FindTile(sender as Control);
        if (tile is not null && tile.Cursor == Cursors.Hand && !tile.ClientRectangle.Contains(tile.PointToClient(Cursor.Position))) tile.BackColor = Color.FromArgb(242, 242, 247);
        ScheduleHide();
    }

    private RoundedTile? FindTile(Control? control)
    {
        while (control is not null && control.Parent != _grid) control = control.Parent;
        return control as RoundedTile;
    }

    private void OnMouseWheel(object? sender, MouseEventArgs e)
    {
        ChangePageByWheel(e.Delta);
    }

    private void ChangePageByWheel(int delta)
    {
        var maxPage = Math.Max(0, (_library.Items.Count - 1) / ItemsPerPage);
        var next = Math.Clamp(_page + (delta < 0 ? 1 : -1), 0, maxPage);
        if (next == _page) return;
        _page = next;
        Rebuild();
    }

    private void InvalidateCenterImage()
    {
        if (_centerImage is not null) ImageAnimator.UpdateFrames(_centerImage);
        foreach (Control control in _grid.Controls)
            if (control is RoundedTile tile && tile.Controls.OfType<ArtworkView>().Any()) tile.Invalidate(true);
    }

    private static Image GetIcon(ShortcutItem item)
    {
        if (item.IsShellItem)
        {
            var shellIcon = ShellDropHelper.GetIcon(item.Path);
            if (shellIcon is not null) return shellIcon;
        }
        try { using var icon = Icon.ExtractAssociatedIcon(item.Path); if (icon is not null) return icon.ToBitmap(); } catch { }
        var bitmap = new Bitmap(44, 44);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new LinearGradientBrush(new Rectangle(0, 0, 44, 44), Color.FromArgb(0, 122, 255), Color.FromArgb(90, 200, 250), 45);
        g.FillEllipse(brush, 2, 2, 40, 40);
        return bitmap;
    }

    private void Launch(ShortcutItem item)
    {
        try
        {
            _library.RecordLaunch(item, ItemsPerPage);
            if (item.IsShellItem) Process.Start(new ProcessStartInfo("explorer.exe", $"shell:{item.Path}") { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
            StartCollapse();
        }
        catch (Exception ex) { MessageBox.Show($"无法打开：{ex.Message}", "Wine Cabinet", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void Restore(ShortcutItem item)
    {
        try { MessageBox.Show(_library.Restore(item), "Wine Cabinet", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        catch (Exception ex) { MessageBox.Show($"恢复失败：{ex.Message}", "Wine Cabinet", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private static void OpenContainingFolder(ShortcutItem item)
    {
        try
        {
            var path = item.Path;
            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开所在位置：{ex.Message}", "Wine Cabinet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static bool CanRunElevated(ShortcutItem item)
    {
        if (item.IsShellItem) return false;
        var extension = System.IO.Path.GetExtension(item.Path);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase);
    }

    private static void RunElevated(ShortcutItem item)
    {
        try { Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true, Verb = "runas" }); }
        catch (Exception ex) when (ex is not System.ComponentModel.Win32Exception { NativeErrorCode: 1223 })
        { MessageBox.Show($"无法以管理员身份运行：{ex.Message}", "Wine Cabinet", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private ContextMenuStrip CreateCenterMenu()
    {
        var menu = AppMenu.Create();
        menu.Opening += (_, _) => SetContextMenuOpen(true);
        menu.Closed += (_, _) => SetContextMenuOpen(false);
        AddGridModeMenuItem(menu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit Wine Cabinet", null, (_, _) => _launcher.ExitApplication());
        return menu;
    }

    private void UpdateRegion()
    {
        var radius = Math.Min(28, Math.Max(12, Math.Min(Width, Height) / 5));
        using var path = RoundedRectangle(new Rectangle(0, 0, Width, Height), radius);
        Region = new Region(path);
    }

    private void LayoutPageIndicator()
    {
        _pageIndicator.Location = new Point(
            Math.Max(2, ClientSize.Width - _pageIndicator.Width - 13),
            Math.Max(2, ClientSize.Height - _pageIndicator.Height - 13));
        _pageIndicator.Visible = ClientSize.Width > 200 && ClientSize.Height > 200;
    }

    protected override CreateParams CreateParams
    {
        get { const int CsDropShadow = 0x00020000; var cp = base.CreateParams; cp.ClassStyle |= CsDropShadow; return cp; }
    }

    private Rectangle ExpandedBounds(Point center)
    {
        var size = CurrentExpandedSize;
        var area = Screen.FromPoint(center).WorkingArea;
        var desired = new Rectangle(center.X - AnchorOffset, center.Y - AnchorOffset, size.Width, size.Height);
        desired.X = Math.Clamp(desired.X, area.Left + 8, area.Right - desired.Width - 8);
        desired.Y = Math.Clamp(desired.Y, area.Top + 8, area.Bottom - desired.Height - 8);
        return desired;
    }

    private static Rectangle CollapsedBounds(Point center) => new(center.X - 27, center.Y - 27, 54, 54);
    private static Rectangle Interpolate(Rectangle from, Rectangle to, double amount) => new(
        (int)Math.Round(from.X + (to.X - from.X) * amount), (int)Math.Round(from.Y + (to.Y - from.Y) * amount),
        Math.Max(1, (int)Math.Round(from.Width + (to.Width - from.Width) * amount)), Math.Max(1, (int)Math.Round(from.Height + (to.Height - from.Height) * amount)));

    private static GraphicsPath RoundedRectangle(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }

    private sealed class RoundedTile : Panel
    {
        internal Color? BorderColor;

        public RoundedTile()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnResize(EventArgs eventargs) { base.OnResize(eventargs); if (Width < 2 || Height < 2) return; using var path = RoundedRectangle(ClientRectangle, 16); Region = new Region(path); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (BorderColor is not Color borderColor || Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), 16);
            using var pen = new Pen(borderColor);
            e.Graphics.DrawPath(pen, path);
        }
    }

    private sealed class ArtworkView : Control
    {
        internal Image? Image;
        public ArtworkView() => SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (Image is null) return; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedRectangle(ClientRectangle, 12); e.Graphics.SetClip(path);
            ArtworkRenderer.DrawContained(e.Graphics, Image, ClientRectangle);
        }
    }

    private sealed class PageBadge : Label
    {
        public PageBadge() => SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width < 2 || Height < 2) return;
            using var path = RoundedRectangle(ClientRectangle, Height / 2);
            Region = new Region(path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2);
            using var pen = new Pen(Color.FromArgb(218, 218, 223));
            e.Graphics.DrawPath(pen, path);
        }
    }

}
