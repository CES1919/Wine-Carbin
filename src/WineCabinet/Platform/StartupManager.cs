using Microsoft.Win32;

namespace WineCabinet;

internal static class StartupManager
{
    internal const string StartupArgument = "--startup";
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Wine Cabinet";

    public static string? EnsureEnabled(string[] args)
    {
        var installedPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "WineCabinet", "Wine Cabinet.exe");
        var executablePath = SelectExecutablePath(Application.ExecutablePath, installedPath, File.Exists(installedPath));
        return EnsureEnabled(executablePath, args.Contains(StartupArgument, StringComparer.OrdinalIgnoreCase),
            ReadCommand, WriteCommand);
    }

    internal static string SelectExecutablePath(string currentPath, string installedPath, bool installedFileExists) =>
        installedFileExists ? installedPath : currentPath;

    internal static string? EnsureEnabled(string executablePath, bool isStartup,
        Func<string?> readCommand, Action<string> writeCommand)
    {
        // Windows already launched the registered command. No registry access is needed at logon.
        if (isStartup) return null;
        try
        {
            var command = $"\"{executablePath}\" {StartupArgument}";
            if (!string.Equals(readCommand(), command, StringComparison.OrdinalIgnoreCase))
                writeCommand(command);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    private static string? ReadCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
        return key?.GetValue(ValueName) as string;
    }

    private static void WriteCommand(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        if (key is null) throw new IOException("无法打开当前用户的启动项注册表。");
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }
}
