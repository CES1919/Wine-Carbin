namespace WineCabinet;

internal static class AppStorage
{
    internal const string DirectoryName = "WineCabinet";

    private static string? _directory;

    public static string Directory =>
        _directory ??= System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DirectoryName);
}
