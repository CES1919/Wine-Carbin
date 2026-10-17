namespace WineCabinet;

internal static class InputRules
{
    public static bool ShouldLaunch(MouseButtons button) => button == MouseButtons.Left;
}
