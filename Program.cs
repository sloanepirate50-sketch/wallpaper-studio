namespace WallpaperStudio;

internal static class Program
{
    public const string MutexName = "WallpaperStudio.SingleInstance";
    public const string ShowEventName = "WallpaperStudio.Show";
    public const string ExitEventName = "WallpaperStudio.Exit";

    /// <summary>
    /// Command line:
    ///   --tray       start hidden in the tray (used by "Start with Windows")
    ///   --exit       ask a running copy to close cleanly (used by the installer)
    ///   --uninstall  like --exit, and also remove the "Start with Windows" entry
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        bool uninstall = args.Contains("--uninstall");
        bool exit = uninstall || args.Contains("--exit");
        if (uninstall)
        {
            try { AppSettings.StartWithWindows = false; }
            catch (Exception) { /* nothing to clean up */ }
        }

        using var mutex = new Mutex(true, MutexName, out bool firstInstance);
        if (!firstInstance)
        {
            // Already running: bring that copy to the front (or ask it to quit) instead of starting another.
            // We were just launched by the user, so we may hand our right to take focus to the other copy.
            AllowSetForegroundWindow(-1);
            if (EventWaitHandle.TryOpenExisting(exit ? ExitEventName : ShowEventName, out var signal))
                using (signal) signal.Set();
            if (exit) WaitForOtherInstanceToExit(mutex);
            return;
        }
        if (exit) return; // nothing was running

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => CrashLog.Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Report(e.ExceptionObject as Exception, fatal: true);

        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // dark-mode API may still be flagged experimental
        Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
        Application.Run(new MainForm(startHidden: args.Contains("--tray")));
        mutex.ReleaseMutex();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    private static void WaitForOtherInstanceToExit(Mutex mutex)
    {
        try
        {
            if (mutex.WaitOne(TimeSpan.FromSeconds(8))) mutex.ReleaseMutex();
        }
        catch (AbandonedMutexException) { /* the other copy exited without releasing: that's fine */ }
    }
}

/// <summary>Writes unexpected errors to %AppData%\WallpaperStudio\error.log and tells the user once.</summary>
internal static class CrashLog
{
    public static string FilePath { get; } = Path.Combine(AppPaths.DataDir, "error.log");
    private static bool shown;

    public static void Report(Exception? ex, bool fatal = false)
    {
        try
        {
            File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {(fatal ? "FATAL " : "")}{ex}\n\n");
        }
        catch (IOException) { }

        if (shown) return;
        shown = true;
        MessageBox.Show(
            $"Wallpaper Studio ran into a problem{(fatal ? " and has to close" : "")}.\n\n{ex?.Message}\n\nDetails were saved to:\n{FilePath}",
            "Wallpaper Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
