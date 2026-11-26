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
            var desired = visible ? 0 : 1;
            var changed = false;
            changed |= SetValueIfNeeded($@"{Root}\NewStartPanel", clsid, desired);
            changed |= SetValueIfNeeded($@"{Root}\ClassicStartMenu", clsid, desired);
            if (!changed) return;
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
            SendMessageTimeout(new IntPtr(0xffff), 0x001A, IntPtr.Zero, "ShellState", 0x0002, 1000, out _);
        }
        catch { /* Some managed desktops block Explorer preference changes. */ }
    }

    internal static string? ExtractClsid(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("::", StringComparison.Ordinal)) return null;
        var candidate = trimmed[2..];
        if (candidate.Contains('\\') || candidate.Contains('/')) return null;
        return Guid.TryParseExact(candidate, "B", out var guid)
            ? guid.ToString("B").ToUpperInvariant()
            : null;
    }

    private static bool SetValueIfNeeded(string keyPath, string name, int value)
    {
        using (var readKey = Registry.CurrentUser.OpenSubKey(keyPath, writable: false))
        {
            if (readKey?.GetValue(name) is int existing && existing == value)
                return false;
        }
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        if (key is null) return false;
        key.SetValue(name, value, RegistryValueKind.DWord);
        return true;
    }

    [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}
