namespace WineCabinet;

// Shell operations may finish after InvokeCommand returns (cut/paste, delete, rename).
internal sealed class LibraryFileWatcher : IDisposable
{
    private readonly ShortcutLibrary _library;
    private readonly Control _dispatcher;
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    internal LibraryFileWatcher(ShortcutLibrary library, Control dispatcher)
    {
        _library = library;
        _dispatcher = dispatcher;
        _library.Changed += Refresh;
        Refresh(this, EventArgs.Empty);
    }

    private void Refresh(object? sender, EventArgs args)
    {
        if (_disposed) return;
        var directories = _library.Items.Where(x => !x.IsShellItem)
            .Select(x => Path.GetDirectoryName(x.Path)).Where(x => !string.IsNullOrEmpty(x) && Directory.Exists(x))
            .Select(x => x!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var obsolete in _watchers.Keys.Where(x => !directories.Contains(x)).ToArray())
        {
            _watchers[obsolete].Dispose();
            _watchers.Remove(obsolete);
        }
        foreach (var directory in directories.Where(x => !_watchers.ContainsKey(x)))
        {
            try
            {
                var watcher = new FileSystemWatcher(directory) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
                watcher.Renamed += (_, e) => Dispatch(() => _library.ApplyFileSystemRename(e.OldFullPath, e.FullPath));
                watcher.Deleted += (_, e) => Dispatch(() => _library.ApplyFileSystemRemoval(e.FullPath));
                _watchers.Add(directory, watcher);
                watcher.EnableRaisingEvents = true;
            }
            catch (IOException) { /* The item remains usable if its directory cannot be monitored. */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void Dispatch(Action action)
    {
        if (_disposed || _dispatcher.IsDisposed || !_dispatcher.IsHandleCreated) return;
        try
        {
            _dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_disposed) action();
            }));
        }
        catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        _disposed = true;
        _library.Changed -= Refresh;
        foreach (var watcher in _watchers.Values) watcher.Dispose();
        _watchers.Clear();
    }
}
