using System.Drawing;
using System.Text.Json;

namespace WineCabinet;

internal sealed class LauncherPlacement
{
    internal const string FileName = "position.json";
    private const int CoordinateLimit = 100_000;

    private readonly string _path;

    private LauncherPlacement(string path, Point? location)
    {
        _path = path;
        Location = location;
    }

    public Point? Location { get; private set; }

    public static LauncherPlacement Load() =>
        LoadFrom(System.IO.Path.Combine(AppStorage.Directory, FileName));

    internal static LauncherPlacement LoadFrom(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var model = JsonSerializer.Deserialize<PositionModel>(File.ReadAllText(path));
                if (model is { X: >= -CoordinateLimit and <= CoordinateLimit, Y: >= -CoordinateLimit and <= CoordinateLimit })
                    return new LauncherPlacement(path, new Point(model.X, model.Y));
            }
        }
        catch { /* Invalid or inaccessible placement falls back to the default position. */ }

        return new LauncherPlacement(path, null);
    }

    public bool TrySave(Point location)
    {
        Location = location;
        try
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_path, JsonSerializer.Serialize(new PositionModel { X = location.X, Y = location.Y }));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class PositionModel
    {
        public int X { get; set; }
        public int Y { get; set; }
    }
}
