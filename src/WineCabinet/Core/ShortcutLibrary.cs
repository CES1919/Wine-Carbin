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

    public static ShortcutLibrary Load()
    {
        var root = AppStorage.Directory;
        var json = System.IO.Path.Combine(root, "library.json");
        try
        {
            if (File.Exists(json))
            {
                var items = JsonSerializer.Deserialize<List<ShortcutItem>>(File.ReadAllText(json));
                var library = new ShortcutLibrary(root, items?.Where(x =>
                    x.IsShellItem || File.Exists(x.Path) || Directory.Exists(x.Path)).ToList());
                foreach (var shellItem in library.Items.Where(x => x.IsShellItem))
                    ShellDesktopIconManager.SetVisible(shellItem.Path, false);
                return library;
            }
        }
        catch { /* A damaged config should not stop the launcher. */ }
        return new ShortcutLibrary(root);
    }

    public (int added, string? warning) AddFiles(IEnumerable<string> paths)
    {
        var added = 0;
        var skipped = new List<string>();
        foreach (var source in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if ((!File.Exists(source) && !Directory.Exists(source)) || !IsSupported(source))
            {
                skipped.Add(System.IO.Path.GetFileName(source));
                continue;
            }
            if (_items.Any(x => string.Equals(x.Path, source, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(x.OriginalPath, source, StringComparison.OrdinalIgnoreCase)))
                continue;

            var storedPath = source;
            string? originalPath = null;
            if (IsDesktopFile(source))
            {
                var storage = System.IO.Path.Combine(_root, "Items");
                Directory.CreateDirectory(storage);
                storedPath = UniquePath(storage, System.IO.Path.GetFileName(source));
                if (Directory.Exists(source)) Directory.Move(source, storedPath);
                else File.Move(source, storedPath);
                originalPath = source;
            }

            _items.Add(new ShortcutItem
            {
                Name = Directory.Exists(storedPath)
                    ? System.IO.Path.GetFileName(source)
                    : System.IO.Path.GetFileNameWithoutExtension(source),
                Path = storedPath,
                OriginalPath = originalPath
            });
            added++;
        }
        if (added > 0) SaveAndNotify();
        var warning = skipped.Count == 0 ? null : $"部分项目无法收纳，已跳过：{string.Join("、", skipped)}";
        return (added, warning);
    }

    public bool AddShellItem(string name, string parsingName)
    {
        if (string.IsNullOrWhiteSpace(parsingName))
            return false;
        ShellDesktopIconManager.SetVisible(parsingName, false);
        if (_items.Any(x => x.IsShellItem && string.Equals(x.Path, parsingName, StringComparison.OrdinalIgnoreCase)))
            return false;
        _items.Add(new ShortcutItem
        {
            Name = name,
            Path = parsingName,
            IsShellItem = true
        });
        SaveAndNotify();
        return true;
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

        var targetDirectory = System.IO.Path.GetDirectoryName(item.OriginalPath)!;
        Directory.CreateDirectory(targetDirectory);
        var target = UniquePath(targetDirectory, System.IO.Path.GetFileName(item.OriginalPath));
        if (Directory.Exists(item.Path)) Directory.Move(item.Path, target);
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
            item.OriginalPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(item.OriginalPath)!, System.IO.Path.GetFileName(newPath));
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
        File.WriteAllText(_jsonPath, JsonSerializer.Serialize(_items, options));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsSupported(string path) =>
        Directory.Exists(path) || !new[] { ".tmp", ".ini", ".db" }.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static bool IsDesktopFile(string path)
    {
        var parent = System.IO.Path.GetFullPath(System.IO.Path.GetDirectoryName(path) ?? "").TrimEnd('\\');
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory).TrimEnd('\\');
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory).TrimEnd('\\');
        return parent.Equals(desktop, StringComparison.OrdinalIgnoreCase) ||
               parent.Equals(common, StringComparison.OrdinalIgnoreCase);
    }

    private static string UniquePath(string directory, string fileName)
    {
        var candidate = System.IO.Path.Combine(directory, fileName);
        if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        var stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var ext = System.IO.Path.GetExtension(fileName);
        for (var i = 2; ; i++)
        {
            candidate = System.IO.Path.Combine(directory, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }
}
