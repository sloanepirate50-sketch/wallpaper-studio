using Microsoft.Web.WebView2.Core;
using WallpaperStudio.Interop;

namespace WallpaperStudio;

/// <summary>Owns the per-monitor wallpaper windows and decides what they show.</summary>
internal sealed class WallpaperManager : IDisposable
{
    private readonly List<WallpaperWindow> windows = [];
    private readonly SemaphoreSlim gate = new(1, 1);
    private CoreWebView2Environment? env;
    private DesktopHost.Layer? layer;

    public WallpaperItem? Current { get; private set; }

    public async Task ApplyAsync(WallpaperItem item, AppSettings settings)
    {
        await gate.WaitAsync();
        try
        {
            Current = item;
            if (windows.Count == 0)
            {
                try { await CreateWindowsAsync(); }
                catch
                {
                    CloseWindows(restore: false); // don't leave half-built windows behind
                    Current = null;
                    throw;
                }
            }

            var url = BuildUrl(item, settings);
            foreach (var w in windows) w.Navigate(url);
        }
        finally { gate.Release(); }
    }

    /// <summary>Rebuilds the windows (monitor layout changed, or Explorer restarted).</summary>
    public async Task RebuildAsync(AppSettings settings)
    {
        if (Current is not { } item) return;
        CloseWindows(restore: false);
        await ApplyAsync(item, settings);
    }

    public void Stop()
    {
        Current = null;
        CloseWindows(restore: true);
    }

    /// <summary>
    /// Keeps our windows in the right place in the desktop's layers (Explorer reshuffles them when
    /// it repaints its own wallpaper). Called on a timer as a safety net for the event hook.
    /// </summary>
    public async Task MaintainAsync(AppSettings settings)
    {
        if (layer is null || windows.Count == 0 || gate.CurrentCount == 0) return;
        if (DesktopHost.EnsureZOrder(layer, Handles()) == DesktopHost.LayerState.Lost)
            await RebuildAsync(settings);
    }

    private List<IntPtr> Handles() => windows.Select(w => w.Handle).ToList();

    // Fires the moment Explorer reorders the desktop's child windows, so a fix is instant.
    private Native.WinEventProc? reorderCallback;
    private IntPtr reorderHook;

    private void WatchForReorders()
    {
        if (layer is not { Raised: true }) return;
        Native.GetWindowThreadProcessId(layer.Parent, out uint explorerPid);
        reorderCallback = (_, _, hwnd, _, _, _, _) =>
        {
            if (layer is not null && hwnd == layer.Parent && windows.Count > 0)
                DesktopHost.EnsureZOrder(layer, Handles());
        };
        reorderHook = Native.SetWinEventHook(Native.EVENT_OBJECT_REORDER, Native.EVENT_OBJECT_REORDER, IntPtr.Zero,
            reorderCallback, explorerPid, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
    }

    private void StopWatchingForReorders()
    {
        if (reorderHook != IntPtr.Zero) Native.UnhookWinEvent(reorderHook);
        reorderHook = IntPtr.Zero;
        reorderCallback = null;
    }

    /// <summary>Pauses the wallpaper on each monitor for which <paramref name="isPaused"/> returns true.</summary>
    public void SetPaused(Func<Rectangle, bool> isPaused)
    {
        foreach (var w in windows) w.SetPaused(isPaused(w.ScreenBounds));
    }

    private async Task CreateWindowsAsync()
    {
        layer = DesktopHost.Find()
            ?? throw new InvalidOperationException("Couldn't find the Windows desktop layer. Is Explorer running?");

        try
        {
            env ??= await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewData,
                new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
        }
        catch (WebView2RuntimeNotFoundException)
        {
            throw new InvalidOperationException(
                "the Microsoft Edge WebView2 Runtime isn't installed. Get it free from https://go.microsoft.com/fwlink/p/?LinkId=2124703 and try again.");
        }

        foreach (var screen in Screen.AllScreens)
        {
            var w = new WallpaperWindow(screen);
            w.Show();
            DesktopHost.Attach(w.Handle, layer, screen.Bounds);
            windows.Add(w);
            await w.InitAsync(env);
        }
        WatchForReorders();
    }

    private void CloseWindows(bool restore)
    {
        StopWatchingForReorders();
        foreach (var w in windows)
        {
            w.Close();
            w.Dispose();
        }
        windows.Clear();
        if (restore && layer is not null) DesktopHost.Restore(layer);
    }

    private static string BuildUrl(WallpaperItem item, AppSettings s)
    {
        if (item.IsBuiltIn)
            return $"https://{AppPaths.AppHost}/scenes/{item.File}?fps={s.FrameRate}";

        var src = $"https://{AppPaths.MediaHost}/{Uri.EscapeDataString(item.File)}";
        var kind = item.Kind == WallpaperKind.Video ? "video" : "image";
        return $"https://{AppPaths.AppHost}/player.html?kind={kind}&fit={s.Fit}&muted={(s.Muted ? 1 : 0)}&src={Uri.EscapeDataString(src)}";
    }

    public void Dispose()
    {
        Stop();
        gate.Dispose();
    }
}
