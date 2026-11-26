namespace WineCabinet;

// Shell operations may finish after InvokeCommand returns (cut/paste, delete, rename).
internal sealed class LibraryFileWatcher : IDisposable
{
    private readonly ShortcutLibrary _library;
    private readonly Control _dispatcher;
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _disposed;

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
                var watchedDirectory = directory;
                var watcher = new FileSystemWatcher(watchedDirectory) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
                watcher.Renamed += (_, e) => Dispatch(() => _library.ApplyFileSystemRename(e.OldFullPath, e.FullPath));
                watcher.Deleted += (_, e) => ScheduleRemovalCheck(e.FullPath);
                watcher.Error += (_, _) => Dispatch(() => RecoverWatcher(watchedDirectory));
                _watchers.Add(watchedDirectory, watcher);
                watcher.EnableRaisingEvents = true;
            }
            catch (IOException) { /* The item remains usable if its directory cannot be monitored. */ }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
        }
    }

    private void RecoverWatcher(string directory)
    {
        if (_disposed) return;
        if (_watchers.Remove(directory, out var broken))
            broken.Dispose();
        Refresh(this, EventArgs.Empty);
    }

    private void ScheduleRemovalCheck(string fullPath)
    {
        if (_disposed) return;
        // Brief debounce prevents external editors' delete-then-rename safe-save from dropping in-place items.
        _ = Task.Delay(150).ContinueWith(_ => Dispatch(() => _library.ApplyFileSystemRemoval(fullPath)), TaskScheduler.Default);
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
