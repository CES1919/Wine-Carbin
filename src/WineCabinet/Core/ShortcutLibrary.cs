using System.Text.Json;

namespace WineCabinet;

internal sealed class ShortcutItem
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string? OriginalPath { get; set; }
    public bool IsShellItem { get; set; }
    public int LaunchCount { get; set; }
    public DateTime? LastLaunchedAt { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;
}

internal sealed class ShortcutLibrary
{
    private readonly string _root;
    private readonly string _jsonPath;
    private readonly List<ShortcutItem> _items;

    public event EventHandler? Changed;
    public IReadOnlyList<ShortcutItem> Items => _items;

    private ShortcutLibrary(string root, List<ShortcutItem>? items = null)
    {
        _root = root;
        _jsonPath = System.IO.Path.Combine(root, "library.json");
        _items = items ?? new List<ShortcutItem>();
        Directory.CreateDirectory(_root);
    }

    internal static ShortcutLibrary CreateForTesting(string root, List<ShortcutItem> items) => new(root, items);

    public static ShortcutLibrary Load() => LoadFrom(AppStorage.Directory);

    internal static ShortcutLibrary LoadFrom(string root)
    {
        var json = System.IO.Path.Combine(root, "library.json");
        try
        {
            if (File.Exists(json))
            {
                var items = JsonSerializer.Deserialize<List<ShortcutItem?>>(File.ReadAllText(json));
                var valid = items?
                    .Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Path) &&
                                (x.IsShellItem || File.Exists(x.Path) || Directory.Exists(x.Path)))
                    .Select(x => x!)
                    .ToList();
                var library = new ShortcutLibrary(root, valid);
                foreach (var shellItem in library.Items.Where(x => x.IsShellItem))
                    ShellDesktopIconManager.SetVisible(shellItem.Path, false);
                return library;
            }
        }
        catch
        {
            // A damaged config should not stop the launcher; preserve a backup for recovery.
            try
            {
                if (File.Exists(json))
                    File.Copy(json, json + ".corrupt", overwrite: true);
            }
            catch { }
        }
        return new ShortcutLibrary(root);
    }

    public (int added, string? warning) AddFiles(IEnumerable<string> paths)
    {
        var added = 0;
        var skipped = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var rawSource in paths)
            {
                if (string.IsNullOrWhiteSpace(rawSource)) continue;
                var source = NormalizeFileSystemPath(rawSource);
                if (!seen.Add(source)) continue;
                var fileName = System.IO.Path.GetFileName(source);
                if (string.IsNullOrEmpty(fileName) || (!File.Exists(source) && !Directory.Exists(source)) || !IsSupported(source))
                {
                    skipped.Add(string.IsNullOrEmpty(fileName) ? rawSource : fileName);
                    continue;
                }
                if (_items.Any(x => string.Equals(x.Path, source, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(x.OriginalPath, source, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var isDirectory = Directory.Exists(source);
                var storedPath = source;
                string? originalPath = null;
                try
                {
                    var desktopKind = GetDesktopKind(source);
                    if (desktopKind != DesktopKind.None)
                    {
                        var storage = System.IO.Path.Combine(_root, "Items");
                        Directory.CreateDirectory(storage);
                        var candidate = UniquePath(storage, fileName, isDirectory);
                        try
                        {
                            if (isDirectory) MoveDirectory(source, candidate);
                            else File.Move(source, candidate);
                            storedPath = candidate;
                            originalPath = source;
                        }
                        catch (UnauthorizedAccessException) when (desktopKind == DesktopKind.CommonDesktop)
                        {
                            // Common Desktop typically requires elevation to move; register in place instead.
                            storedPath = source;
                            originalPath = null;
                        }
                    }
                }
                catch
                {
                    skipped.Add(fileName);
                    continue;
                }

                _items.Add(new ShortcutItem
                {
                    Name = isDirectory
                        ? fileName
                        : System.IO.Path.GetFileNameWithoutExtension(source),
                    Path = storedPath,
                    OriginalPath = originalPath
                });
                added++;
            }
        }
        finally
        {
            if (added > 0) SaveAndNotify();
        }
        var warning = skipped.Count == 0 ? null : $"部分项目无法收纳，已跳过：{string.Join("、", skipped)}";
        return (added, warning);
    }

    public bool AddShellItem(string name, string parsingName)
    {
        if (string.IsNullOrWhiteSpace(parsingName))
            return false;
        if (_items.Any(x => x.IsShellItem && string.Equals(x.Path, parsingName, StringComparison.OrdinalIgnoreCase)))
            return false;
        ShellDesktopIconManager.SetVisible(parsingName, false);
        _items.Add(new ShortcutItem
        {
            Name = name,
            Path = parsingName,
            IsShellItem = true
        });
        SaveAndNotify();
        return true;
    }

    public (int added, string? warning) AddDroppedShellItems(IEnumerable<(string Name, string ParsingName)> droppedItems)
    {
        var files = new List<string>();
        var shellItems = new List<(string Name, string ParsingName)>();
        foreach (var item in droppedItems)
        {
            if (string.IsNullOrWhiteSpace(item.ParsingName)) continue;
            if (File.Exists(item.ParsingName) || Directory.Exists(item.ParsingName))
                files.Add(item.ParsingName);
            else
                shellItems.Add(item);
        }

        var added = 0;
        string? warning = null;
        if (files.Count > 0)
            (added, warning) = AddFiles(files);

        var shellAdded = 0;
        foreach (var shellItem in shellItems)
        {
            if (_items.Any(x => x.IsShellItem && string.Equals(x.Path, shellItem.ParsingName, StringComparison.OrdinalIgnoreCase)))
                continue;
            ShellDesktopIconManager.SetVisible(shellItem.ParsingName, false);
            _items.Add(new ShortcutItem
            {
                Name = shellItem.Name,
                Path = shellItem.ParsingName,
                IsShellItem = true
            });
            shellAdded++;
        }
        if (shellAdded > 0) SaveAndNotify();
        return (added + shellAdded, warning);
    }

    public void Remove(ShortcutItem item)
    {
        _items.Remove(item);
        SaveAndNotify();
    }

    public string Restore(ShortcutItem item)
    {
        if (item.IsShellItem)
        {
            ShellDesktopIconManager.SetVisible(item.Path, true);
            Remove(item);
            return $"已将“{item.Name}”恢复到桌面。";
        }
        if (item.OriginalPath is null)
        {
            Remove(item);
            return "已从图标面板移除（原文件未移动）。";
        }
        if (!File.Exists(item.Path) && !Directory.Exists(item.Path))
        {
            Remove(item);
            return "原托管文件已不存在，已从图标面板移除。";
        }

        var isDirectory = Directory.Exists(item.Path);
        var targetDirectory = System.IO.Path.GetDirectoryName(item.OriginalPath);
        if (string.IsNullOrEmpty(targetDirectory))
            targetDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        Directory.CreateDirectory(targetDirectory);
        var targetName = System.IO.Path.GetFileName(item.OriginalPath);
        if (string.IsNullOrEmpty(targetName))
            targetName = System.IO.Path.GetFileName(item.Path);
        var target = UniquePath(targetDirectory, targetName, isDirectory);
        if (isDirectory) MoveDirectory(item.Path, target);
        else File.Move(item.Path, target);
        _items.Remove(item);
        SaveAndNotify();
        return $"已恢复到：{target}";
    }

    public void Reorder(ShortcutItem item, int delta)
    {
        var oldIndex = _items.IndexOf(item);
        var newIndex = Math.Clamp(oldIndex + delta, 0, _items.Count - 1);
        if (oldIndex < 0 || oldIndex == newIndex) return;
        _items.RemoveAt(oldIndex);
        _items.Insert(newIndex, item);
        ResetUsageCounts();
        SaveAndNotify();
    }

    public void MoveBefore(ShortcutItem item, ShortcutItem? target, int emptySlotIndex)
    {
        var oldIndex = _items.IndexOf(item);
        if (oldIndex < 0 || ReferenceEquals(item, target)) return;
        _items.RemoveAt(oldIndex);
        var newIndex = target is not null
            ? _items.IndexOf(target)
            : Math.Clamp(emptySlotIndex - (oldIndex < emptySlotIndex ? 1 : 0), 0, _items.Count);
        if (newIndex < 0) newIndex = _items.Count;
        _items.Insert(newIndex, item);
        ResetUsageCounts();
        SaveAndNotify();
    }

    public void Swap(ShortcutItem first, ShortcutItem second)
    {
        var firstIndex = _items.IndexOf(first);
        var secondIndex = _items.IndexOf(second);
        if (firstIndex < 0 || secondIndex < 0 || firstIndex == secondIndex) return;
        (_items[firstIndex], _items[secondIndex]) = (_items[secondIndex], _items[firstIndex]);
        ResetUsageCounts();
        SaveAndNotify();
    }

    public void CompleteExternalMove(ShortcutItem item)
    {
        if (File.Exists(item.Path) || Directory.Exists(item.Path)) return;
        _items.Remove(item);
        SaveAndNotify();
    }

    public void UpdateShellRename(ShortcutItem item, string newPath, string? displayName = null)
    {
        if (!_items.Contains(item)) return;
        var wasDirectory = !item.IsShellItem && Directory.Exists(newPath);
        item.Path = newPath;
        item.Name = displayName ?? (wasDirectory
            ? System.IO.Path.GetFileName(newPath)
            : System.IO.Path.GetFileNameWithoutExtension(newPath));
        if (item.OriginalPath is not null)
        {
            var originalDirectory = System.IO.Path.GetDirectoryName(item.OriginalPath);
            if (!string.IsNullOrEmpty(originalDirectory))
                item.OriginalPath = System.IO.Path.Combine(originalDirectory, System.IO.Path.GetFileName(newPath));
        }
        SaveAndNotify();
    }

    public void ApplyFileSystemRename(string oldPath, string newPath)
    {
        var item = _items.FirstOrDefault(x => !x.IsShellItem && x.Path.Equals(oldPath, StringComparison.OrdinalIgnoreCase));
        if (item is not null) UpdateShellRename(item, newPath);
    }

    public void ApplyFileSystemRemoval(string path)
    {
        var item = _items.FirstOrDefault(x => !x.IsShellItem && x.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (item is not null) CompleteExternalMove(item);
    }

    public void RecordLaunch(ShortcutItem item, int firstPageCapacity = 8)
    {
        if (firstPageCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(firstPageCapacity));
        item.LaunchCount++;
        item.LastLaunchedAt = DateTime.Now;
        var index = _items.IndexOf(item);
        if (index >= firstPageCapacity && item.LaunchCount >= 3)
        {
            _items.RemoveAt(index);
            _items.Insert(Math.Min(firstPageCapacity - 1, _items.Count), item);
            item.LaunchCount = 0;
            item.LastLaunchedAt = null;
        }
        SaveAndNotify();
    }

    private void ResetUsageCounts()
    {
        foreach (var entry in _items)
        {
            entry.LaunchCount = 0;
            entry.LastLaunchedAt = null;
        }
    }

    private void SaveAndNotify()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        AppStorage.WriteAllTextAtomic(_jsonPath, JsonSerializer.Serialize(_items, options));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private enum DesktopKind { None, UserDesktop, CommonDesktop }

    private static string NormalizeFileSystemPath(string path) =>
        System.IO.Path.TrimEndingDirectorySeparator(path.Trim());

    private static bool IsSupported(string path) =>
        Directory.Exists(path) || !new[] { ".tmp", ".ini", ".db" }.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static DesktopKind GetDesktopKind(string path)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory)) return DesktopKind.None;
        var parent = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(directory));
        var desktop = System.IO.Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        if (!string.IsNullOrEmpty(desktop) && parent.Equals(desktop, StringComparison.OrdinalIgnoreCase))
            return DesktopKind.UserDesktop;
        var common = System.IO.Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
        if (!string.IsNullOrEmpty(common) && parent.Equals(common, StringComparison.OrdinalIgnoreCase))
            return DesktopKind.CommonDesktop;
        return DesktopKind.None;
    }

    private static void MoveDirectory(string source, string destination)
    {
        try
        {
            Directory.Move(source, destination);
        }
        catch (IOException) when (!string.Equals(
            System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(source)),
            System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(destination)),
            StringComparison.OrdinalIgnoreCase))
        {
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("不支持跨卷移动符号链接或联接目录。");
            CopyDirectoryRecursive(source, destination);
            try
            {
                Directory.Delete(source, recursive: true);
            }
            catch
            {
                try { if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true); } catch { }
                throw;
            }
        }
    }

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, System.IO.Path.Combine(destination, System.IO.Path.GetFileName(file)), overwrite: false);
        foreach (var subdirectory in Directory.GetDirectories(source))
        {
            if ((File.GetAttributes(subdirectory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("目录包含无法跨卷复制的符号链接。");
            CopyDirectoryRecursive(subdirectory, System.IO.Path.Combine(destination, System.IO.Path.GetFileName(subdirectory)));
        }
    }

    internal static string UniquePath(string directory, string fileName, bool isDirectory = false)
    {
        var candidate = System.IO.Path.Combine(directory, fileName);
        if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        var stem = isDirectory ? fileName : System.IO.Path.GetFileNameWithoutExtension(fileName);
        var ext = isDirectory ? "" : System.IO.Path.GetExtension(fileName);
        for (var i = 2; ; i++)
        {
            candidate = System.IO.Path.Combine(directory, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }
}
