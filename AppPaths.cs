namespace WallpaperStudio;

internal static class AppPaths
{
    // Virtual hosts that WebView2 maps to local folders. ".example" is reserved, so it never hits DNS.
    public const string AppHost = "app.example";
    public const string MediaHost = "media.example";

    public static string WebRoot { get; } = Path.Combine(AppContext.BaseDirectory, "wwwroot");
    public static string DataDir { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WallpaperStudio"));
    public static string Library { get; } = Ensure(Path.Combine(DataDir, "Library"));
    public static string SettingsFile { get; } = Path.Combine(DataDir, "settings.json");
    public static string WebViewData { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallpaperStudio", "WebView2"));

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
