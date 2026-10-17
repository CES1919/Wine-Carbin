using System.Runtime.InteropServices;

namespace WineCabinet;

internal enum ItemMenuAction { None, Opened, Rename, Restore, MoveEarlier, MoveLater }

// Host the actual Shell menu, including namespace items and owner-drawn extensions.
internal sealed class ShellContextMenu : IDisposable
{
    private const uint FirstCommand = 1;
    private const uint LastCommand = 0x6FFF;
    private const uint RestoreCommand = 0x7000;
    private const uint EarlierCommand = 0x7001;
    private const uint LaterCommand = 0x7002;
    private IntPtr _pidl;
    private IntPtr _menu;
    private string _parsingName = string.Empty;
    private IShellFolder? _folder;
    private IContextMenu? _context;

    private ShellContextMenu() { }

    internal static ShellContextMenu Create(string parsingName, IntPtr owner, bool extended = false)
    {
        var session = new ShellContextMenu();
        try
        {
            session._parsingName = parsingName;
            Marshal.ThrowExceptionForHR(SHParseDisplayName(parsingName, IntPtr.Zero, out session._pidl, 0, out _));
            var folderId = typeof(IShellFolder).GUID;
            Marshal.ThrowExceptionForHR(SHBindToParent(session._pidl, ref folderId, out session._folder, out var child));
            var contextId = typeof(IContextMenu).GUID;
            Marshal.ThrowExceptionForHR(session._folder.GetUIObjectOf(owner, 1, new[] { child }, ref contextId, IntPtr.Zero, out var pointer));
            try { session._context = (IContextMenu)Marshal.GetObjectForIUnknown(pointer); }
            finally { Marshal.Release(pointer); }
            session._menu = CreatePopupMenu();
            if (session._menu == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
            // CMF_CANRENAME; Shift exposes the same extended verbs as Explorer.
            Marshal.ThrowExceptionForHR(session._context.QueryContextMenu(session._menu, 0, FirstCommand, LastCommand,
                0x10u | (extended ? 0x100u : 0)));
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    internal ItemMenuAction Show(IntPtr owner, Point point, string restoreLabel)
    {
        AppendMenu(_menu, 0x800, UIntPtr.Zero, null);
        var cabinetMenu = CreatePopupMenu();
        if (cabinetMenu == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        try
        {
            AppendMenu(cabinetMenu, 0, new UIntPtr(RestoreCommand), restoreLabel);
            AppendMenu(cabinetMenu, 0, new UIntPtr(EarlierCommand), "向前移动");
            AppendMenu(cabinetMenu, 0, new UIntPtr(LaterCommand), "向后移动");
            if (!AppendMenu(_menu, 0x10, (UIntPtr)(nuint)cabinetMenu, "Wine Cabinet"))
                throw new System.ComponentModel.Win32Exception();
        }
        catch { DestroyMenu(cabinetMenu); throw; }
        // DestroyMenu on the root also destroys its attached submenu.
        using var messages = new MenuMessageWindow(owner, _context!);
        SetForegroundWindow(owner);
        var command = TrackPopupMenuEx(_menu, 0x100 | 0x2, point.X, point.Y, owner, IntPtr.Zero);
        PostMessage(owner, 0, IntPtr.Zero, IntPtr.Zero);
        if (command == 0) return ItemMenuAction.None;
        if (command == RestoreCommand) return ItemMenuAction.Restore;
        if (command == EarlierCommand) return ItemMenuAction.MoveEarlier;
        if (command == LaterCommand) return ItemMenuAction.MoveLater;
        var verb = GetVerb(command);
        // Rename needs a host edit control; InvokeCommand alone has no Shell view to edit.
        if (verb.Equals("rename", StringComparison.OrdinalIgnoreCase)) return ItemMenuAction.Rename;
        Invoke(command, owner, point);
        return verb.ToLowerInvariant() is "open" or "explore" or "runas"
            ? ItemMenuAction.Opened : ItemMenuAction.None;
    }

    internal IReadOnlyList<string> GetVerbs()
    {
        var verbs = new List<string>();
        CollectVerbs(_menu, verbs);
        return verbs;
    }

    internal string Rename(IntPtr owner, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) throw new InvalidOperationException("名称不能为空。");
        if (trimmed.Contains('\\') || trimmed.Contains('/') || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("名称包含无效字符。");
        // 部分 Windows 版本的 IShellFolder.SetNameOf 会把原扩展名再追加一次（例如 after.txt → after.txt.txt），
        // 因此磁盘上的文件与目录直接使用文件系统 API 重命名，以输入的完整名称作为结果。
        if (File.Exists(_parsingName) || Directory.Exists(_parsingName))
        {
            if (trimmed.EndsWith(' ') || trimmed.EndsWith('.'))
                throw new InvalidOperationException("名称不能以空格或句点结尾。");
            var target = Path.Combine(Path.GetDirectoryName(_parsingName)!, trimmed);
            if (Directory.Exists(_parsingName)) Directory.Move(_parsingName, target);
            else File.Move(_parsingName, target);
            return target;
        }

        var folderId = typeof(IShellFolder).GUID;
        Marshal.ThrowExceptionForHR(SHBindToParent(_pidl, ref folderId, out var folder, out var child));
        IntPtr renamed = IntPtr.Zero;
        IntPtr absolute = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(folder.SetNameOf(owner, child, trimmed, 0, out renamed));
            var parent = ILClone(_pidl);
            if (parent == IntPtr.Zero) throw new OutOfMemoryException();
            try { ILRemoveLastID(parent); absolute = ILCombine(parent, renamed); }
            finally { Marshal.FreeCoTaskMem(parent); }
            Marshal.ThrowExceptionForHR(SHGetNameFromIDList(absolute, 0x80028000, out var text));
            try { return Marshal.PtrToStringUni(text) ?? throw new InvalidOperationException("无法读取重命名后的项目路径。"); }
            finally { Marshal.FreeCoTaskMem(text); }
        }
        finally
        {
            if (absolute != IntPtr.Zero) Marshal.FreeCoTaskMem(absolute);
            if (renamed != IntPtr.Zero) Marshal.FreeCoTaskMem(renamed);
            Marshal.ReleaseComObject(folder);
        }
    }

    private void CollectVerbs(IntPtr menu, List<string> verbs)
    {
        for (var index = 0; index < GetMenuItemCount(menu); index++)
        {
            var id = GetMenuItemID(menu, index);
            if (id >= FirstCommand && id <= LastCommand)
            {
                var verb = GetVerb(id);
                if (!string.IsNullOrEmpty(verb)) verbs.Add(verb);
            }
            var submenu = GetSubMenu(menu, index);
            if (submenu != IntPtr.Zero) CollectVerbs(submenu, verbs);
        }
    }

    private string GetVerb(uint command)
    {
        var buffer = Marshal.AllocHGlobal(1024);
        try
        {
            Marshal.WriteInt16(buffer, 0);
            if (_context!.GetCommandString(new UIntPtr(command - FirstCommand), 4, IntPtr.Zero, buffer, 512) >= 0)
                return Marshal.PtrToStringUni(buffer) ?? "";
            // Some legacy Shell extensions implement only GCS_VERBA.
            Marshal.WriteByte(buffer, 0);
            return _context.GetCommandString(new UIntPtr(command - FirstCommand), 0, IntPtr.Zero, buffer, 512) >= 0
                ? Marshal.PtrToStringAnsi(buffer) ?? "" : "";
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private void Invoke(uint command, IntPtr owner, Point point)
    {
        var info = new InvokeInfo
        {
            Size = Marshal.SizeOf<InvokeInfo>(), Mask = 0x4000 | 0x20000000,
            Window = owner, Verb = new IntPtr(command - FirstCommand), VerbUnicode = new IntPtr(command - FirstCommand),
            Show = 1, Point = point
        };
        if ((Control.ModifierKeys & Keys.Shift) != 0) info.Mask |= 0x10000000;
        if ((Control.ModifierKeys & Keys.Control) != 0) info.Mask |= 0x40000000;
        var result = _context!.InvokeCommand(ref info);
        if (result != unchecked((int)0x800704C7)) Marshal.ThrowExceptionForHR(result); // User cancelled.
    }

    public void Dispose()
    {
        if (_menu != IntPtr.Zero) { DestroyMenu(_menu); _menu = IntPtr.Zero; }
        if (_context is not null) { Marshal.ReleaseComObject(_context); _context = null; }
        if (_folder is not null) { Marshal.ReleaseComObject(_folder); _folder = null; }
        if (_pidl != IntPtr.Zero) { Marshal.FreeCoTaskMem(_pidl); _pidl = IntPtr.Zero; }
    }

    private sealed class MenuMessageWindow : NativeWindow, IDisposable
    {
        private readonly IContextMenu2? _second;
        private readonly IContextMenu3? _third;
        internal MenuMessageWindow(IntPtr owner, IContextMenu context)
        {
            _second = context as IContextMenu2;
            _third = context as IContextMenu3;
            AssignHandle(owner);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg is 0x117 or 0x2B or 0x2C or 0x120)
            {
                if (_third is not null && _third.HandleMenuMsg2((uint)message.Msg, message.WParam, message.LParam, out var result) == 0)
                {
                    message.Result = result;
                    return;
                }
                if (message.Msg != 0x120 && _second is not null && _second.HandleMenuMsg((uint)message.Msg, message.WParam, message.LParam) == 0)
                {
                    message.Result = IntPtr.Zero;
                    return;
                }
            }
            base.WndProc(ref message);
        }

        public void Dispose() => ReleaseHandle();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InvokeInfo
    {
        public int Size;
        public uint Mask;
        public IntPtr Window, Verb, Parameters, Directory;
        public int Show;
        public uint HotKey;
        public IntPtr Icon, Title, VerbUnicode, ParametersUnicode, DirectoryUnicode, TitleUnicode;
        public Point Point;
    }

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr window, IntPtr context, IntPtr name, IntPtr eaten, out IntPtr pidl, IntPtr attributes);
        [PreserveSig] int EnumObjects(IntPtr window, uint flags, out IntPtr enumerator);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr context, ref Guid id, out IntPtr result);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr context, ref Guid id, out IntPtr result);
        [PreserveSig] int CompareIDs(IntPtr parameter, IntPtr first, IntPtr second);
        [PreserveSig] int CreateViewObject(IntPtr window, ref Guid id, out IntPtr result);
        [PreserveSig] int GetAttributesOf(uint count, IntPtr pidls, ref uint attributes);
        [PreserveSig] int GetUIObjectOf(IntPtr window, uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] pidls, ref Guid id, IntPtr reserved, out IntPtr result);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr result);
        [PreserveSig] int SetNameOf(IntPtr window, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr result);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeInfo info);
        [PreserveSig] int GetCommandString(UIntPtr command, uint flags, IntPtr reserved, IntPtr text, uint count);
    }

    // Redeclare base slots explicitly to preserve the native COM vtable layout.
    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeInfo info);
        [PreserveSig] int GetCommandString(UIntPtr command, uint flags, IntPtr reserved, IntPtr text, uint count);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeInfo info);
        [PreserveSig] int GetCommandString(UIntPtr command, uint flags, IntPtr reserved, IntPtr text, uint count);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, IntPtr context, out IntPtr pidl, uint attributes, out uint result);
    [DllImport("shell32.dll")] private static extern int SHBindToParent(IntPtr pidl, ref Guid id, [MarshalAs(UnmanagedType.Interface)] out IShellFolder folder, out IntPtr child);
    [DllImport("shell32.dll")] private static extern IntPtr ILClone(IntPtr pidl);
    [DllImport("shell32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ILRemoveLastID(IntPtr pidl);
    [DllImport("shell32.dll")] private static extern IntPtr ILCombine(IntPtr parent, IntPtr child);
    [DllImport("shell32.dll")] private static extern int SHGetNameFromIDList(IntPtr pidl, uint flags, out IntPtr text);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll")] private static extern uint GetMenuItemID(IntPtr menu, int position);
    [DllImport("user32.dll")] private static extern IntPtr GetSubMenu(IntPtr menu, int position);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
