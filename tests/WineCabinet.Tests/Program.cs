using WineCabinet;

namespace WineCabinet.Tests;

internal static class Program
{
    [STAThread]
    private static int Main() => Run() ? 0 : 1;

    private static bool Run()
    {
        var testRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WineCabinetSelfTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var startupPath = @"C:\Apps\WineCabinet\Wine Cabinet.exe";
            var startupCommand = $"\"{startupPath}\" --startup";
            Require(StartupManager.EnsureEnabled(startupPath, false, () => startupCommand,
                    _ => throw new UnauthorizedAccessException("Registry write denied")) is null,
                "an existing startup command must work when registry writes are denied");
            Require(StartupManager.EnsureEnabled(startupPath, true,
                    () => throw new UnauthorizedAccessException("Registry read denied"),
                    _ => throw new UnauthorizedAccessException("Registry write denied")) is null,
                "logon startup must not access the registry even when both read and write are denied");
            string? writtenCommand = null;
            Require(StartupManager.EnsureEnabled(startupPath, false, () => null,
                    command => writtenCommand = command) is null && writtenCommand == startupCommand,
                "first manual launch must register a quoted path with the startup argument");
            writtenCommand = null;
            Require(StartupManager.EnsureEnabled(startupPath, false, () => $"\"{startupPath}\"",
                    command => writtenCommand = command) is null && writtenCommand == startupCommand,
                "legacy startup entries must gain the startup argument");
            Require(StartupManager.EnsureEnabled(startupPath, false, () => null,
                    _ => throw new UnauthorizedAccessException("Registry write denied")) == "Registry write denied",
                "a failed manual registration must still report its error");
            Require(StartupManager.SelectExecutablePath(@"C:\Source\Wine Cabinet.exe", startupPath, true) == startupPath,
                "a development build must preserve the installed application's startup path");
            Require(StartupManager.SelectExecutablePath(startupPath, @"C:\Missing\Wine Cabinet.exe", false) == startupPath,
                "a portable application must register itself when no installed copy exists");

            var items = Enumerable.Range(0, 12).Select(i => new ShortcutItem
            {
                Name = $"Item{i}", Path = $"virtual:{i}", IsShellItem = true, LaunchCount = i
            }).ToList();
            var library = ShortcutLibrary.CreateForTesting(testRoot, items);
            var originalSecond = items[1];
            var originalTenth = items[9];
            library.Swap(originalSecond, originalTenth);
            Require(library.Items[1] == originalTenth && library.Items[9] == originalSecond, "swap must exchange only two indexes");
            Require(library.Items.All(x => x.LaunchCount == 0), "manual swap must reset usage counts");

            var firstPageItem = library.Items[2];
            library.RecordLaunch(firstPageItem);
            library.RecordLaunch(firstPageItem);
            library.RecordLaunch(firstPageItem);
            Require(library.Items[2] == firstPageItem, "first-page launches must not reposition an item");

            var laterItem = library.Items[10];
            library.RecordLaunch(laterItem);
            library.RecordLaunch(laterItem);
            Require(library.Items.ToList().IndexOf(laterItem) == 10, "later-page item must wait for three launches");
            library.RecordLaunch(laterItem);
            Require(library.Items.ToList().IndexOf(laterItem) == 7, "later-page item must enter the last slot of page one");

            var sixteenItems = Enumerable.Range(0, 20).Select(i => new ShortcutItem
            {
                Name = $"Sixteen{i}", Path = $"sixteen:{i}", IsShellItem = true
            }).ToList();
            var sixteenLibrary = ShortcutLibrary.CreateForTesting(
                System.IO.Path.Combine(testRoot, "SixteenGrid"), sixteenItems);
            var visibleFromOldSecondPage = sixteenLibrary.Items[10];
            sixteenLibrary.RecordLaunch(visibleFromOldSecondPage, 15);
            sixteenLibrary.RecordLaunch(visibleFromOldSecondPage, 15);
            sixteenLibrary.RecordLaunch(visibleFromOldSecondPage, 15);
            Require(sixteenLibrary.Items[10] == visibleFromOldSecondPage,
                "an item visible on the 16-grid first page must keep its position");
            var sixteenLaterItem = sixteenLibrary.Items[17];
            sixteenLibrary.RecordLaunch(sixteenLaterItem, 15);
            sixteenLibrary.RecordLaunch(sixteenLaterItem, 15);
            sixteenLibrary.RecordLaunch(sixteenLaterItem, 15);
            Require(sixteenLibrary.Items.ToList().IndexOf(sixteenLaterItem) == 14,
                "a later 16-grid page item must enter the last slot of its first page");

            Require(GridLayoutRules.ItemsPerPage(3) == 8 && GridLayoutRules.CenterSlot(3) == 4,
                "the nine-grid layout must reserve its center and expose eight items");
            Require(GridLayoutRules.ItemsPerPage(4) == 15 && GridLayoutRules.CenterSlot(4) == 5,
                "the 16-grid layout must reserve the original center position and expose 15 items");
            Require(GridLayoutRules.ItemSlots(4).Take(8).SequenceEqual(new[] { 0, 1, 2, 4, 6, 8, 9, 10 }),
                "the first eight 16-grid slots must preserve the original 3x3 positions");
            Require(GridLayoutRules.ItemSlots(4).Skip(8).SequenceEqual(new[] { 3, 7, 11, 12, 13, 14, 15 }),
                "the additional 16-grid slots must fill the new right column and bottom row");
            Require(GridLayoutRules.ItemSlots(4).Append(GridLayoutRules.CenterSlot(4)).Order().SequenceEqual(Enumerable.Range(0, 16)),
                "the 16-grid layout must render every cell, including unused item cells");

            var settingsPath = System.IO.Path.Combine(testRoot, "Settings", "settings.json");
            var settings = GridLayoutSettings.LoadFrom(settingsPath);
            Require(settings.Dimension == 3, "missing layout settings must default to nine-grid mode");
            Require(settings.SetDimension(4), "16-grid preference must be persisted");
            Require(GridLayoutSettings.LoadFrom(settingsPath).Dimension == 4,
                "persisted 16-grid preference must be restored");

            var placementPath = System.IO.Path.Combine(testRoot, "Placement", LauncherPlacement.FileName);
            Require(LauncherPlacement.LoadFrom(placementPath).Location is null,
                "a missing placement file must not restore a launcher position");
            Require(LauncherPlacement.LoadFrom(placementPath).TrySave(new Point(-120, 340)),
                "the launcher position must be persisted");
            Require(LauncherPlacement.LoadFrom(placementPath).Location == new Point(-120, 340),
                "a persisted launcher position must be restored");
            File.WriteAllText(placementPath, "{ \"X\": ");
            Require(LauncherPlacement.LoadFrom(placementPath).Location is null,
                "an unreadable placement file must fall back to the default position");

            var window = new Size(96, 96);
            var primaryArea = new Rectangle(0, 0, 1920, 1040);
            var secondArea = new Rectangle(1920, 0, 1280, 1024);
            var workingAreas = new[] { primaryArea, secondArea };
            Require(LauncherPlacementRules.DefaultLocation(window, 177, primaryArea) == new Point(1677, 472),
                "the default position must keep the icon at the right edge, vertically centered");
            Require(LauncherPlacementRules.TryRestore(new Point(100, 100), window, workingAreas, out var restored) &&
                    restored == new Point(100, 100),
                "a position on a connected screen must be restored exactly");
            Require(LauncherPlacementRules.TryRestore(new Point(2400, 200), window, workingAreas, out var secondary) &&
                    secondary == new Point(2400, 200),
                "a position on a secondary screen must be restored exactly");
            Require(!LauncherPlacementRules.TryRestore(new Point(5000, 5000), window, workingAreas, out _),
                "a position on a disconnected screen must fall back to the default");
            Require(!LauncherPlacementRules.TryRestore(new Point(-60, 100), window, workingAreas, out _),
                "a mostly off-screen position must fall back to the default");
            Require(LauncherPlacementRules.TryRestore(new Point(-40, 100), window, workingAreas, out var clamped) &&
                    clamped == new Point(0, 100),
                "a mostly visible position must be clamped into the work area");

            Require(AppStorage.DirectoryName == "WineCabinet" &&
                    AppStorage.Directory == System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        AppStorage.DirectoryName),
                "the data directory must be the product-named folder under the local application data directory");

            var bounds = new Rectangle(100, 100, 300, 300);
            Require(DragNavigation.Decide(bounds, new Point(200, 90), 1, 2) == DragEdgeAction.PreviousPage, "top edge must navigate back");
            Require(DragNavigation.Decide(bounds, new Point(200, 410), 1, 2) == DragEdgeAction.NextPage, "bottom edge must navigate forward");
            Require(DragNavigation.Decide(bounds, new Point(90, 200), 1, 2) == DragEdgeAction.Remove, "left edge must remove");
            Require(DragNavigation.Decide(bounds, new Point(200, 90), 0, 2) == DragEdgeAction.Remove, "top of first page must remove");
            Require(DragNavigation.Decide(bounds, new Point(200, 410), 2, 2) == DragEdgeAction.Remove, "bottom of last page must remove");
            Require(InputRules.ShouldLaunch(MouseButtons.Left), "left click must launch");
            Require(!InputRules.ShouldLaunch(MouseButtons.Right), "right click must never launch");

            using var launcherResource = typeof(ShortcutLibrary).Assembly
                .GetManifestResourceStream(EmbeddedImage.LauncherResourceName);
            Require(launcherResource is not null, "launcher artwork must be embedded in the application assembly");
            using var launcherImage = Image.FromStream(launcherResource!);
            Require(launcherImage.Width > 0 && launcherImage.Height > 0,
                "embedded launcher artwork must have valid dimensions");
            Require(launcherImage.GetFrameCount(System.Drawing.Imaging.FrameDimension.Time) > 1,
                "directly embedded launcher GIF must retain its animation frames");

            using (var trayIcon = TrayIconFactory.Create(launcherImage, new Size(16, 16)))
            {
                Require(trayIcon.Width == 16 && trayIcon.Height == 16,
                    "the tray icon must use the requested notification-area size");
                using var trayBitmap = trayIcon.ToBitmap();
                var hasVisiblePixel = false;
                for (var y = 0; y < trayBitmap.Height && !hasVisiblePixel; y++)
                    for (var x = 0; x < trayBitmap.Width; x++)
                        if (trayBitmap.GetPixel(x, y).A > 0)
                        {
                            hasVisiblePixel = true;
                            break;
                        }
                Require(hasVisiblePixel, "the tray icon must contain visible pixels");
            }
            using (var fallbackTrayIcon = TrayIconFactory.Create(null, new Size(8, 8)))
                Require(fallbackTrayIcon.Width == 8 && fallbackTrayIcon.Height == 8,
                    "the tray icon must have a fallback when the artwork is unavailable");

            Require(
                ArtworkRenderer.CalculateContainRectangle(new Size(200, 100), new Rectangle(0, 0, 100, 100)) ==
                new RectangleF(0, 25, 100, 50),
                "landscape artwork must be fully visible with vertical padding");
            Require(
                ArtworkRenderer.CalculateContainRectangle(new Size(100, 200), new Rectangle(0, 0, 100, 100)) ==
                new RectangleF(25, 0, 50, 100),
                "portrait artwork must be fully visible with horizontal padding");
            Require(
                ArtworkRenderer.CalculateContainRectangle(new Size(100, 100), new Rectangle(2, 2, 92, 92)) ==
                new RectangleF(2, 2, 92, 92),
                "square artwork must fill the available bounds without cropping");

            var managedDirectory = System.IO.Path.Combine(testRoot, "Items");
            var desktopDirectory = System.IO.Path.Combine(testRoot, "Desktop");
            Directory.CreateDirectory(managedDirectory);
            Directory.CreateDirectory(desktopDirectory);
            var storedPath = System.IO.Path.Combine(managedDirectory, "sample.lnk");
            var originalPath = System.IO.Path.Combine(desktopDirectory, "sample.lnk");
            File.WriteAllText(storedPath, "test");
            var managedItem = new ShortcutItem { Name = "sample", Path = storedPath, OriginalPath = originalPath };
            var restoreLibrary = ShortcutLibrary.CreateForTesting(System.IO.Path.Combine(testRoot, "RestoreLibrary"), new List<ShortcutItem> { managedItem });
            restoreLibrary.Restore(managedItem);
            Require(File.Exists(originalPath) && !File.Exists(storedPath) && restoreLibrary.Items.Count == 0,
                "external removal must restore a managed item and remove it from the grid");

            // Query the real Shell handlers without invoking destructive system commands.
            using var menuOwner = new Form();
            using (var thisPcMenu = ShellContextMenu.Create(ShellDropHelper.ThisPc, menuOwner.Handle))
            {
                var verbs = thisPcMenu.GetVerbs();
                Require(verbs.Contains("properties", StringComparer.OrdinalIgnoreCase), "This PC must expose its native properties command");
                Require(verbs.Contains("manage", StringComparer.OrdinalIgnoreCase), "This PC must expose its native manage command");
            }
            using (var recycleMenu = ShellContextMenu.Create(ShellDropHelper.RecycleBin, menuOwner.Handle))
                Require(recycleMenu.GetVerbs().Contains("properties", StringComparer.OrdinalIgnoreCase),
                    "Recycle Bin must expose its native properties command");

            var shortcutPath = Path.Combine(managedDirectory, "application.lnk");
            var scriptShellType = Type.GetTypeFromProgID("WScript.Shell")!;
            dynamic scriptShell = Activator.CreateInstance(scriptShellType)!;
            dynamic shortcut = scriptShell.CreateShortcut(shortcutPath);
            try
            {
                shortcut.TargetPath = Environment.ProcessPath!;
                shortcut.Save();
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(scriptShell);
            }
            using (var shortcutMenu = ShellContextMenu.Create(shortcutPath, menuOwner.Handle))
            {
                var verbs = shortcutMenu.GetVerbs();
                Require(verbs.Contains("open", StringComparer.OrdinalIgnoreCase) && verbs.Contains("properties", StringComparer.OrdinalIgnoreCase),
                    "application shortcuts must expose their real open and properties commands");
                Require(verbs.Contains("runas", StringComparer.OrdinalIgnoreCase), "executable shortcuts must expose the native elevation command");
            }

            var renamePath = Path.Combine(managedDirectory, "before.txt");
            File.WriteAllText(renamePath, "preserved contents");
            var renameItem = new ShortcutItem { Name = "before", Path = renamePath, OriginalPath = Path.Combine(desktopDirectory, "before.txt") };
            var renameLibrary = ShortcutLibrary.CreateForTesting(Path.Combine(testRoot, "RenameLibrary"), new List<ShortcutItem> { renameItem });
            using (var fileMenu = ShellContextMenu.Create(renamePath, menuOwner.Handle))
            {
                var verbs = fileMenu.GetVerbs();
                Require(verbs.Contains("copy", StringComparer.OrdinalIgnoreCase) && verbs.Contains("delete", StringComparer.OrdinalIgnoreCase),
                    "files must expose native copy and delete commands");
                var newPath = fileMenu.Rename(menuOwner.Handle, "after.txt");
                renameLibrary.UpdateShellRename(renameItem, newPath);
                Require(File.ReadAllText(newPath) == "preserved contents" && !File.Exists(renamePath), "Shell rename must retain the file contents");
                Require(renameItem.Name == "after" && renameItem.OriginalPath == Path.Combine(desktopDirectory, "after.txt"),
                    $"Shell rename must update both the displayed name and desktop restore name (returned '{newPath}', name '{renameItem.Name}', restore '{renameItem.OriginalPath}', expected restore '{Path.Combine(desktopDirectory, "after.txt")}')");
            }
            renameLibrary.Restore(renameItem);
            Require(File.Exists(Path.Combine(desktopDirectory, "after.txt")), "a renamed item must restore with its new name");

            var removedPath = Path.Combine(managedDirectory, "removed.txt");
            File.WriteAllText(removedPath, "temporary");
            var removedItem = new ShortcutItem { Name = "removed", Path = removedPath };
            var syncLibrary = ShortcutLibrary.CreateForTesting(Path.Combine(testRoot, "SyncLibrary"), new List<ShortcutItem> { removedItem });
            syncLibrary.ApplyFileSystemRemoval(removedPath);
            Require(syncLibrary.Items.Count == 1, "a removal notification must not remove a file that still exists");
            var movedPath = Path.Combine(managedDirectory, "moved.txt");
            File.Move(removedPath, movedPath);
            syncLibrary.ApplyFileSystemRename(removedPath, movedPath);
            Require(removedItem.Path == movedPath && removedItem.Name == "moved", "external rename must keep the stored path usable");
            File.Delete(movedPath);
            syncLibrary.ApplyFileSystemRemoval(movedPath);
            Require(syncLibrary.Items.Count == 0, "completed native deletion or cut/paste must remove the stale item");

            var watchedPath = Path.Combine(managedDirectory, "watched.txt");
            File.WriteAllText(watchedPath, "watched contents");
            var watchedItem = new ShortcutItem { Name = "watched", Path = watchedPath };
            var watchedLibrary = ShortcutLibrary.CreateForTesting(Path.Combine(testRoot, "WatchedLibrary"), new List<ShortcutItem> { watchedItem });
            using (var watcher = new LibraryFileWatcher(watchedLibrary, menuOwner))
            {
                var watchedRenamedPath = Path.Combine(managedDirectory, "watched-renamed.txt");
                File.Move(watchedPath, watchedRenamedPath);
                WaitFor(() => watchedItem.Path == watchedRenamedPath, "directory watcher must synchronize asynchronous rename");
                File.Delete(watchedRenamedPath);
                WaitFor(() => watchedLibrary.Items.Count == 0, "directory watcher must synchronize asynchronous deletion");
            }

            var uiArea = Screen.PrimaryScreen!.WorkingArea;
            var uiPlacementPath = Path.Combine(testRoot, "UiPlacement", LauncherPlacement.FileName);
            var savedLauncher = new Point(uiArea.Left + 40, uiArea.Top + 40);
            var movedLauncher = new Point(uiArea.Left + 120, uiArea.Top + 90);
            var closedLauncher = new Point(uiArea.Left + 160, uiArea.Top + 60);
            Require(LauncherPlacement.LoadFrom(uiPlacementPath).TrySave(savedLauncher),
                "the launcher position must be persisted before a restart");
            using (var launcher = new LauncherForm(
                       ShortcutLibrary.CreateForTesting(Path.Combine(testRoot, "UiLibrary"), new List<ShortcutItem>()),
                       LauncherPlacement.LoadFrom(uiPlacementPath)))
            {
                launcher.Show();
                Application.DoEvents();
                Require(launcher.Location == savedLauncher,
                    "a restart must restore the launcher to its saved position");
                launcher.Location = movedLauncher;
                launcher.EndExternalMove();
                Require(LauncherPlacement.LoadFrom(uiPlacementPath).Location == movedLauncher,
                    "finishing a move must persist the launcher position");
                launcher.Location = closedLauncher;
                launcher.Close();
                Application.DoEvents();
                Require(LauncherPlacement.LoadFrom(uiPlacementPath).Location == closedLauncher,
                    "closing the launcher must persist its final position");
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return false;
        }
        finally
        {
            try { if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true); } catch { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void WaitFor(Func<bool> condition, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Require(condition(), message);
    }
}
