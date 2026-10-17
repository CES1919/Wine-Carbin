namespace WineCabinet;

internal static class Program
{
    private const string StartupLogName = "startup-error.log";

    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            Run(args);
        }
        catch (Exception ex)
        {
            ReportStartupFailure("启动失败", ex);
        }
    }

    private static void Run(string[] args)
    {
        using var singleInstance = new Mutex(true, @"Local\WineCabinet.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance) return;

        // Replaces ApplicationConfiguration.Initialize() so that visual-style activation cannot
        // abort startup: WinForms writes its activation manifest through %TEMP%, and a locked-down
        // temporary directory otherwise ends the process before the launcher window is created.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        try
        {
            Application.EnableVisualStyles();
        }
        catch (Exception ex)
        {
            WriteLog("无法启用视觉样式，继续以无主题外观启动", ex);
        }
        Application.SetCompatibleTextRenderingDefault(false);

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.Message, "Wine Cabinet", MessageBoxButtons.OK, MessageBoxIcon.Error);

        var startupError = StartupManager.EnsureEnabled(args);
        if (startupError is not null)
            MessageBox.Show($"无法登记开机启动：{startupError}\n\n本次仍可使用 Wine Cabinet。", "Wine Cabinet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        var library = ShortcutLibrary.Load();
        Application.Run(new LauncherForm(library));
        GC.KeepAlive(singleInstance);
    }

    private static void ReportStartupFailure(string context, Exception exception)
    {
        var logPath = WriteLog(context, exception);
        var detail = logPath is null ? context : $"{context}\n\n详细信息：{logPath}";
        try
        {
            MessageBox.Show($"{detail}\n\n{exception.Message}", "Wine Cabinet", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { /* Reporting must never throw. */ }
    }

    private static string? WriteLog(string context, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppStorage.Directory);
            var path = Path.Combine(AppStorage.Directory, StartupLogName);
            File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] {context}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            return path;
        }
        catch
        {
            return null;
        }
    }
}
