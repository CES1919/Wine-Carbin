using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace WineCabinet;

internal static class ShellDropHelper
{
    public const string ShellIdListFormat = "Shell IDList Array";
    public const string ThisPc = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
    public const string RecycleBin = "::{645FF040-5081-101B-9F08-00AA002F954E}";

    public static IReadOnlyList<(string Name, string ParsingName)> ReadShellItems(IDataObject data)
    {
        var result = new List<(string, string)>();
        if (!data.GetDataPresent(ShellIdListFormat)) return result;
        byte[] bytes;
        var raw = data.GetData(ShellIdListFormat);
        if (raw is MemoryStream stream) bytes = stream.ToArray();
        else if (raw is byte[] array) bytes = array;
        else return result;
        if (!TryReadValidatedCidaOffsets(bytes, out var parentOffset, out var childOffsets))
            return result;

        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var basePtr = handle.AddrOfPinnedObject();
            var parent = IntPtr.Add(basePtr, parentOffset);
            foreach (var childOffset in childOffsets)
            {
                var absolute = ILCombine(parent, IntPtr.Add(basePtr, childOffset));
                if (absolute == IntPtr.Zero) continue;
                try
                {
                    var parsing = GetName(absolute, Sigdn.DesktopAbsoluteParsing);
                    var display = GetName(absolute, Sigdn.NormalDisplay);
                    if (!string.IsNullOrWhiteSpace(parsing))
                        result.Add((string.IsNullOrWhiteSpace(display) ? parsing : display, parsing));
                }
                finally { ILFree(absolute); }
            }
        }
        catch { /* Unsupported shell payload: caller can retry via standard drag-and-drop. */ }
        finally { handle.Free(); }
        return result;
    }

    internal static bool TryReadValidatedCidaOffsets(ReadOnlySpan<byte> bytes, out int parentOffset, out List<int> childOffsets)
    {
        parentOffset = 0;
        childOffsets = new List<int>();
        if (bytes.Length < 12) return false;

        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if (count <= 0 || count > 256) return false;

        var headerSize = 4 + (count + 1) * 4;
        if (bytes.Length < headerSize) return false;

        parentOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(4, 4));
        if (parentOffset < headerSize || !IsValidPidl(bytes, parentOffset))
            return false;

        for (var i = 0; i < count; i++)
        {
            var childOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(8 + i * 4, 4));
            if (childOffset < headerSize || !IsValidPidl(bytes, childOffset))
                continue;
            childOffsets.Add(childOffset);
        }

        return childOffsets.Count > 0;
    }

    internal static bool IsValidPidl(ReadOnlySpan<byte> buffer, int offset)
    {
        if (offset < 0 || offset > buffer.Length - 2) return false;
        while (offset <= buffer.Length - 2)
        {
            var cb = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset, 2));
            if (cb == 0) return true;
            if (cb < 2 || offset > buffer.Length - cb) return false;
            offset += cb;
        }
        return false;
    }

    public static Image? GetIcon(string parsingName)
    {
        if (SHParseDisplayName(parsingName, IntPtr.Zero, out var pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
            return null;
        try
        {
            var info = new ShellFileInfo();
            var ok = SHGetFileInfo(pidl, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(),
                Shgfi.Pidl | Shgfi.Icon | Shgfi.LargeIcon) != IntPtr.Zero;
            if (!ok || info.Icon == IntPtr.Zero) return null;
            try
            {
                using var icon = (Icon)Icon.FromHandle(info.Icon).Clone();
                return icon.ToBitmap();
            }
            finally { DestroyIcon(info.Icon); }
        }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }

    private static string GetName(IntPtr pidl, Sigdn type)
    {
        if (SHGetNameFromIDList(pidl, type, out var value) != 0 || value == IntPtr.Zero) return "";
        try { return Marshal.PtrToStringUni(value) ?? ""; }
        finally { Marshal.FreeCoTaskMem(value); }
    }

    private enum Sigdn : uint
    {
        NormalDisplay = 0,
        DesktopAbsoluteParsing = 0x80028000
    }

    [Flags]
    private enum Shgfi : uint
    {
        Icon = 0x000000100,
        LargeIcon = 0x000000000,
        Pidl = 0x000000008
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll")] private static extern IntPtr ILCombine(IntPtr parent, IntPtr child);
    [DllImport("shell32.dll")] private static extern void ILFree(IntPtr pidl);
    [DllImport("shell32.dll")] private static extern int SHGetNameFromIDList(IntPtr pidl, Sigdn sigdnName, out IntPtr name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint attributesIn, out uint attributesOut);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(IntPtr path, uint attributes, ref ShellFileInfo info, uint size, Shgfi flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
