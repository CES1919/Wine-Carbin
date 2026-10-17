using System.Text.Json;

namespace WineCabinet;

internal sealed class GridLayoutSettings
{
    private readonly string _path;

    private GridLayoutSettings(string path, int dimension)
    {
        _path = path;
        Dimension = dimension;
    }

    public int Dimension { get; private set; }

    public static GridLayoutSettings Load()
    {
        return LoadFrom(System.IO.Path.Combine(AppStorage.Directory, "settings.json"));
    }

    internal static GridLayoutSettings LoadFrom(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var model = JsonSerializer.Deserialize<SettingsModel>(File.ReadAllText(path));
                if (model?.GridDimension is 3 or 4)
                    return new GridLayoutSettings(path, model.GridDimension);
            }
        }
        catch { /* Invalid or inaccessible settings fall back to the default grid. */ }

        return new GridLayoutSettings(path, 3);
    }

    public bool SetDimension(int dimension)
    {
        if (dimension is not (3 or 4))
            throw new ArgumentOutOfRangeException(nameof(dimension));

        Dimension = dimension;
        try
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_path, JsonSerializer.Serialize(new SettingsModel { GridDimension = dimension }));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class SettingsModel
    {
        public int GridDimension { get; set; } = 3;
    }
}
