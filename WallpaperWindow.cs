using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using WallpaperStudio.Interop;

namespace WallpaperStudio;

/// <summary>One borderless browser surface per monitor, parented behind the desktop icons.</summary>
internal sealed class WallpaperWindow : Form
{
    private readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
    private bool paused;

    /// <summary>The monitor this window covers (physical pixels).</summary>
    public Rectangle ScreenBounds { get; }

    public WallpaperWindow(Screen screen)
    {
        ScreenBounds = screen.Bounds;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        // Start off-screen so nothing flashes before we move into the desktop layer.
        Location = new Point(-32000, -32000);
        Size = screen.Bounds.Size;
        Controls.Add(web);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public async Task InitAsync(CoreWebView2Environment env)
    {
        await web.EnsureCoreWebView2Async(env);
        var core = web.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping(AppPaths.AppHost, AppPaths.WebRoot, CoreWebView2HostResourceAccessKind.Allow);
        core.SetVirtualHostNameToFolderMapping(AppPaths.MediaHost, AppPaths.Library, CoreWebView2HostResourceAccessKind.Allow);
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
    }

    public void Navigate(string url)
    {
        paused = false; // a freshly loaded page starts running
        web.CoreWebView2?.Navigate(url);
    }

    /// <summary>Pauses or resumes the page's animation/video. Only talks to the page when the state changes.</summary>
    public void SetPaused(bool value)
    {
        if (paused == value || web.CoreWebView2 is null) return;
        paused = value;
        _ = web.CoreWebView2.ExecuteScriptAsync($"window.wpPause && window.wpPause({(value ? "true" : "false")})");
    }
}
