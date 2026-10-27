using System.Text;
using static WallpaperStudio.Interop.Native;

namespace WallpaperStudio.Interop;

/// <summary>
/// Finds the window layer that sits between the desktop icons and the Windows wallpaper,
/// and parents our wallpaper windows into it (the same trick Wallpaper Engine / Lively use).
/// </summary>
internal static class DesktopHost
{
    /// <param name="Parent">Window our wallpaper becomes a child of.</param>
    /// <param name="IconLayer">SHELLDLL_DefView (the icons) when we must z-order below it.</param>
    /// <param name="Raised">True on Windows 11 24H2+, where WorkerW lives inside Progman.</param>
    public sealed record Layer(IntPtr Parent, IntPtr IconLayer, bool Raised);

    public static Layer? Find()
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return null;

        // Ask Explorer to spawn the WorkerW that sits behind the icons.
        SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), SMTO_NORMAL, 1000, out _);
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, SMTO_NORMAL, 1000, out _);

        // Windows 11 24H2+: Progman > [SHELLDLL_DefView, WorkerW]. We slot in between them.
        var defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        var innerWorker = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        if (defView != IntPtr.Zero && innerWorker != IntPtr.Zero)
            return new Layer(progman, defView, Raised: true);

        // Older Windows: the WorkerW right after the one hosting SHELLDLL_DefView is the wallpaper layer.
        var workerw = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                workerw = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            return true;
        }, IntPtr.Zero);

        return workerw != IntPtr.Zero ? new Layer(workerw, IntPtr.Zero, Raised: false) : null;
    }

    public static void Attach(IntPtr hwnd, Layer layer, Rectangle screenBounds)
    {
        if (layer.Raised)
        {
            // Children of Progman on 24H2 must be layered to be composed correctly.
            var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_LAYERED));
            SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
        }

        SetParent(hwnd, layer.Parent);

        var p = new POINT(screenBounds.X, screenBounds.Y);
        ScreenToClient(layer.Parent, ref p);

        uint flags = SWP_NOACTIVATE | SWP_SHOWWINDOW;
        if (!layer.Raised) flags |= SWP_NOZORDER;
        // On 24H2, inserting "after" the icon layer places us directly beneath the icons.
        SetWindowPos(hwnd, layer.Raised ? layer.IconLayer : IntPtr.Zero,
            p.X, p.Y, screenBounds.Width, screenBounds.Height, flags);
    }

    public enum LayerState { Ok, Fixed, Lost }

    /// <summary>
    /// Windows 11 24H2+ layout: Explorer moves its own wallpaper layer (WorkerW) back up under the
    /// icons whenever it repaints the background, e.g. each time a slideshow picture changes, which
    /// buries our windows. This puts them back between the icons and WorkerW.
    /// Returns <see cref="LayerState.Lost"/> if the desktop was rebuilt and we need a full re-attach.
    /// </summary>
    public static LayerState EnsureZOrder(Layer layer, IReadOnlyCollection<IntPtr> ours)
    {
        if (!layer.Raised || ours.Count == 0) return LayerState.Ok;
        if (!IsWindow(layer.Parent) || !IsWindow(layer.IconLayer)) return LayerState.Lost;

        int index = 0, iconIndex = -1, workerIndex = int.MaxValue;
        var positions = new Dictionary<IntPtr, int>();
        for (var h = GetWindow(layer.Parent, GW_CHILD); h != IntPtr.Zero; h = GetWindow(h, GW_HWNDNEXT), index++)
        {
            if (h == layer.IconLayer) iconIndex = index;
            else if (ours.Contains(h)) positions[h] = index;
            else if (workerIndex == int.MaxValue && ClassNameOf(h) == "WorkerW") workerIndex = index;
        }
        if (iconIndex < 0 || positions.Count != ours.Count) return LayerState.Lost;
        if (ours.All(w => positions[w] > iconIndex && positions[w] < workerIndex)) return LayerState.Ok;

        foreach (var w in ours)
            SetWindowPos(w, layer.IconLayer, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        return LayerState.Fixed;
    }

    /// <summary>Repaints the normal Windows wallpaper after our windows are gone.</summary>
    public static void Restore(Layer layer)
    {
        RedrawWindow(layer.Parent, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);
        if (layer.Raised) return;

        // Legacy WorkerW keeps the last frame we drew; re-applying the current wallpaper clears it.
        // fWinIni = 0 so nothing is written to the user's settings.
        var current = new StringBuilder(1024);
        if (SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)current.Capacity, current, 0))
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, current.ToString(), 0);
    }

    /// <summary>
    /// Bounds of every monitor that has a maximized window on it, so the wallpaper behind it can pause.
    /// Only real maximized app windows count; tool windows and invisible overlays are ignored.
    /// </summary>
    public static List<Rectangle> MonitorsWithMaximizedWindows()
    {
        var result = new List<Rectangle>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || !IsZoomed(hwnd) || IsCloaked(hwnd)) return true;
            if ((GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0) return true;

            var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref mi))
            {
                var r = mi.rcMonitor;
                var bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                if (!result.Contains(bounds)) result.Add(bounds);
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>Bounds of the monitor the foreground window is on.</summary>
    public static Rectangle? ForegroundMonitor()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return null;
        var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST), ref mi)) return null;
        return Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
    }

    /// <summary>True when the foreground app covers its whole monitor (games, videos, presentations).</summary>
    public static bool IsForegroundFullscreen()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;

        var cls = ClassNameOf(fg);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (!GetWindowRect(fg, out var r)) return false;

        var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST), ref mi)) return false;

        return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top
            && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
    }
}
