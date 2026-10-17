using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace WineCabinet;

internal static class ShellDesktopIconManager
{
    private const string Root = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons";

    public static void SetVisible(string parsingName, bool visible)
    {
        var clsid = ExtractClsid(parsingName);
        if (clsid is null) return;
        try
        {
            SetValue($@"{Root}\NewStartPanel", clsid, visible ? 0 : 1);
            SetValue($@"{Root}\ClassicStartMenu", clsid, visible ? 0 : 1);
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
            SendMessageTimeout(new IntPtr(0xffff), 0x001A, IntPtr.Zero, "ShellState", 0x0002, 1000, out _);
        }
        catch { /* Some managed desktops block Explorer preference changes. */ }
    }

    private static string? ExtractClsid(string value)
    {
        var start = value.IndexOf('{');
        var end = value.IndexOf('}', start + 1);
        return start >= 0 && end > start ? value[start..(end + 1)] : null;
    }

    private static void SetValue(string keyPath, string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key?.SetValue(name, value, RegistryValueKind.DWord);
    }

    [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}
