using System.Runtime.InteropServices;

namespace WineCabinet;

internal static class ShellActions
{
    public static void ShowProperties(string path, IntPtr owner)
    {
        var info = new ShellExecuteInfo
        {
            Size = Marshal.SizeOf<ShellExecuteInfo>(),
            Mask = 0x0000000C,
            Window = owner,
            Verb = "properties",
            File = path,
            Show = 1
        };
        if (!ShellExecuteEx(ref info))
            MessageBox.Show("无法打开此项目的属性。", "Wine Cabinet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int Size;
        public uint Mask;
        public IntPtr Window;
        [MarshalAs(UnmanagedType.LPWStr)] public string Verb;
        [MarshalAs(UnmanagedType.LPWStr)] public string File;
        public IntPtr Parameters;
        public IntPtr Directory;
        public int Show;
        public IntPtr Instance;
        public IntPtr IdList;
        public IntPtr Class;
        public IntPtr KeyClass;
        public uint HotKey;
        public IntPtr IconOrMonitor;
        public IntPtr Process;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);
}
