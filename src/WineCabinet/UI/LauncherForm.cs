using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WineCabinet;

internal sealed class LauncherForm : Form
{
    private const int IconSize = 96;
    private readonly ShortcutLibrary _library;
    private readonly FlyoutForm _flyout;
    private readonly EmbeddedImage? _launcherArtwork;
    private readonly Image? _launcherImage;
    private readonly EventHandler? _animationHandler;
    private readonly ContextMenuStrip _appMenu;
    private readonly LauncherPlacement _placement;
    private readonly NotifyIcon? _trayIcon;
    private readonly Icon? _trayIconImage;
    private LibraryFileWatcher? _fileWatcher;
    private bool _hasExplicitPlacement;
    private Point _initialLocation;
    private bool _dragging;
    private bool _dragMoved;
    private Point _pointerDownAt;
    private Point _dragOffset;

    public LauncherForm(ShortcutLibrary library) : this(library, LauncherPlacement.Load())
    {
    }

    internal LauncherForm(ShortcutLibrary library, LauncherPlacement placement)
    {
        _library = library;
        _placement = placement;
        _hasExplicitPlacement = placement.Location.HasValue;
        _appMenu = AppMenu.Create();
        _flyout = new FlyoutForm(library, this);
        Text = "Wine Cabinet";
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        TopMost = false;
        AllowDrop = true;
        DoubleBuffered = true;
        ClientSize = new Size(IconSize, IconSize);
        MinimumSize = new Size(IconSize, IconSize);
        MaximumSize = new Size(IconSize, IconSize);
        Size = new Size(IconSize, IconSize);
        BackColor = Color.FromArgb(250, 250, 252);
        StartPosition = FormStartPosition.Manual;

        _launcherArtwork = EmbeddedImage.LoadLauncher();
        _launcherImage = _launcherArtwork?.Image;
        if (_launcherImage is not null)
        {
            _animationHandler = (_, _) =>
            {
                if (!IsDisposed && IsHandleCreated)
                    try { BeginInvoke(Invalidate); } catch (InvalidOperationException) { }
            };
            ImageAnimator.Animate(_launcherImage, _animationHandler);
        }

        Load += (_, _) =>
        {
            var workingAreas = Screen.AllScreens.Select(screen => screen.WorkingArea).ToArray();
            var rightExtent = _flyout.CurrentExpandedSize.Width - FlyoutForm.AnchorOffset;
            Location = _placement.Location is { } saved &&
                       LauncherPlacementRules.TryRestore(saved, Size, workingAreas, out var restored)
                ? restored
                : LauncherPlacementRules.DefaultLocation(Size, rightExtent, Screen.PrimaryScreen!.WorkingArea);
            _initialLocation = Location;
            UpdateRegion();
        };
        Shown += (_, _) => _fileWatcher ??= new LibraryFileWatcher(_library, this);
        SizeChanged += (_, _) => UpdateRegion();
        Paint += PaintLauncher;
        MouseLeave += (_, _) =>
        {
            if (!_dragging && _flyout.Visible) ScheduleFlyoutHide();
        };
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseCaptureChanged += (_, _) =>
        {
            if (!Capture && _dragging) EndExternalMove();
        };
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        FormClosed += (_, _) =>
        {
            PersistPlacement();
            _fileWatcher?.Dispose();
            _trayIcon?.Dispose();
            _trayIconImage?.Dispose();
            _flyout.Close();
            _appMenu.Dispose();
            var region = Region;
            Region = null;
            region?.Dispose();
            if (_launcherImage is not null)
            {
                if (_animationHandler is not null) ImageAnimator.StopAnimate(_launcherImage, _animationHandler);
                _launcherArtwork?.Dispose();
            }
        };

        _appMenu.Opening += (_, _) => _flyout.SetContextMenuOpen(true);
        _appMenu.Closed += (_, _) => _flyout.SetContextMenuOpen(false);
        _appMenu.Items.Add("打开图标面板", null, (_, _) => ShowFlyout());
        _flyout.AddGridModeMenuItem(_appMenu);
        _appMenu.Items.Add(new ToolStripSeparator());
        _appMenu.Items.Add("Exit Wine Cabinet", null, (_, _) => ExitApplication());
        ContextMenuStrip = _appMenu;

        // The tray icon is optional: a notification-area failure must not stop the launcher.
        _trayIcon = TryCreateTrayIcon(_launcherImage, _appMenu, out _trayIconImage);
    }

    private NotifyIcon? TryCreateTrayIcon(Image? artwork, ContextMenuStrip menu, out Icon? icon)
    {
        icon = null;
        try
        {
            icon = TrayIconFactory.Create(artwork, SystemInformation.SmallIconSize);
            var trayIcon = new NotifyIcon
            {
                Text = "Wine Cabinet",
                Icon = icon,
                ContextMenuStrip = menu,
                Visible = true
            };
            trayIcon.MouseClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left) ToggleFlyout();
            };
            return trayIcon;
        }
        catch
        {
            icon?.Dispose();
            icon = null;
            return null;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _flyout.AllowClose = true;
        base.OnFormClosing(e);
    }

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        if ((specified & BoundsSpecified.Size) != 0)
        {
            width = IconSize;
            height = IconSize;
        }
        base.SetBoundsCore(x, y, width, height, specified);
    }

    public void ShowFlyout()
    {
        _flyout.CancelHide();
        _flyout.ShowExpanded(new Point(Left + Width / 2, Top + Height / 2));
    }

    private void ToggleFlyout()
    {
        if (_flyout.Visible) _flyout.Collapse();
        else ShowFlyout();
    }

    public void ScheduleFlyoutHide() => _flyout.ScheduleHide();

    public void BeginExternalMove(Point screenPoint)
    {
        _dragging = true;
        _dragMoved = false;
        _pointerDownAt = screenPoint;
        _dragOffset = new Point(screenPoint.X - Left, screenPoint.Y - Top);
        Capture = true;
    }

    public void MoveExternal(Point screenPoint)
    {
        if (!_dragging) return;
        var area = Screen.FromPoint(screenPoint).WorkingArea;
        var next = new Point(
            Math.Clamp(screenPoint.X - _dragOffset.X, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(screenPoint.Y - _dragOffset.Y, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        Location = next;
        _flyout.Reposition(new Point(Left + Width / 2, Top + Height / 2));
    }

    public void EndExternalMove()
    {
        var moved = _dragMoved || Location != _initialLocation;
        _dragging = false;
        _dragMoved = false;
        if (Capture) Capture = false;
        if (moved) _hasExplicitPlacement = true;
        PersistPlacement();
    }

    private void PersistPlacement()
    {
        if (!_hasExplicitPlacement && Location == _initialLocation) return;
        if (_placement.Location == Location) return;
        _hasExplicitPlacement = true;
        _placement.TrySave(Location);
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _dragMoved = false;
        _pointerDownAt = Cursor.Position;
        _dragOffset = e.Location;
        Capture = true;
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_dragging) return;
        var wasClick = !_dragMoved;
        EndExternalMove();
        if (wasClick) ToggleFlyout();
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging || e.Button != MouseButtons.Left) return;
        if (!_dragMoved)
        {
            var dragSize = SystemInformation.DragSize;
            if (Math.Abs(Cursor.Position.X - _pointerDownAt.X) < dragSize.Width &&
                Math.Abs(Cursor.Position.Y - _pointerDownAt.Y) < dragSize.Height)
                return;
            _dragMoved = true;
        }
        MoveExternal(Cursor.Position);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true ||
            e.Data?.GetDataPresent(ShellDropHelper.ShellIdListFormat) == true)
        {
            EndExternalMove();
            e.Effect = DragDropEffects.Move;
        }
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        try
        {
            string? warning = null;
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                var result = _library.AddFiles(files);
                warning = result.warning;
            }
            else if (e.Data is not null)
            {
                var result = _library.AddDroppedShellItems(ShellDropHelper.ReadShellItems(e.Data));
                warning = result.warning;
            }
            ShowFlyout();
            if (warning is not null)
                MessageBox.Show(warning, "Wine Cabinet", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"收纳失败：{ex.Message}", "Wine Cabinet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void PaintLauncher(object? sender, PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (_launcherImage is not null) ImageAnimator.UpdateFrames(_launcherImage);
        using var clip = RoundedRectangle(new Rectangle(2, 2, Width - 4, Height - 4), 11);
        var state = e.Graphics.Save();
        e.Graphics.SetClip(clip);
        if (_launcherImage is not null)
            ArtworkRenderer.DrawContained(e.Graphics, _launcherImage, new Rectangle(2, 2, Width - 4, Height - 4));
        else
            e.Graphics.Clear(Color.FromArgb(255, 229, 224));
        e.Graphics.Restore(state);
        using var border = new Pen(Color.FromArgb(110, 255, 255, 255), 1.2f);
        e.Graphics.DrawPath(border, clip);
    }

    private void UpdateRegion()
    {
        if (Width < 2 || Height < 2) return;
        using var path = RoundedRectangle(ClientRectangle, 13);
        var previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    private static GraphicsPath RoundedRectangle(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (d < 1)
        {
            path.AddRectangle(rect);
            return path;
        }
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal void ExitApplication()
    {
        _flyout.AllowClose = true;
        Application.Exit();
    }
}
