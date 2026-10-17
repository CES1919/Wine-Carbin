namespace WineCabinet;

internal sealed class EmbeddedImage : IDisposable
{
    internal const string LauncherResourceName = "WineCabinet.Assets.launcher";
    private readonly MemoryStream _stream;

    private EmbeddedImage(MemoryStream stream, Image image)
    {
        _stream = stream;
        Image = image;
    }

    public Image Image { get; }

    public static EmbeddedImage? LoadLauncher()
    {
        using var resource = typeof(EmbeddedImage).Assembly.GetManifestResourceStream(LauncherResourceName);
        if (resource is null) return null;

        var stream = new MemoryStream();
        resource.CopyTo(stream);
        stream.Position = 0;

        try
        {
            return new EmbeddedImage(stream, Image.FromStream(stream));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Image.Dispose();
        _stream.Dispose();
    }
}
