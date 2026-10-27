using System.Text;
using Windows.Media.Control;
using WallpaperStudio.Interop;

namespace WallpaperStudio.Media;

/// <summary>
/// Works out whether you're watching something (YouTube, Netflix, a movie in VLC, ...) so effects
/// can get out of the way. It reads the media sessions Windows shows in its volume/media flyout
/// and, for browsers, checks window titles for video sites. Music (e.g. Spotify) doesn't count.
/// </summary>
internal sealed class WatchDetector
{
    private static readonly string[] VideoApps =
    [
        "vlc", "mpc-hc", "mpc-be", "potplayer", "mpv", "kmplayer", "smplayer", "plex", "stremio", "netflix",
        "disney", "amazonvideo", "primevideo", "hulu", "crunchyroll", "zunevideo", "twitch", "kodi", "jellyfin",
    ];

    private static readonly string[] MusicApps =
    [
        "spotify", "applemusic", "itunes", "tidal", "deezer", "foobar", "musicbee", "aimp", "winamp", "amazonmusic",
    ];

    private static readonly string[] VideoSites =
    [
        // Kept specific on purpose: generic words like "Max" or "Plex" match unrelated titles.
        "YouTube", "Netflix", "Prime Video", "Disney+", "Hulu", "Twitch", "Crunchyroll", "HBO Max",
        "Peacock", "Paramount+", "Plex Web", "Vimeo", "Pluto TV", "Apple TV", "Dailymotion",
        "Jellyfin", "Stremio", "VLC media player", "Movies & TV", "Media Player",
    ];

    private GlobalSystemMediaTransportControlsSessionManager? manager;

    public async Task InitAsync()
    {
        try { manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); }
        catch (Exception) { manager = null; } // media sessions unavailable: fall back to "never watching"
    }

    /// <summary>Returns a short description of what's being watched, or null if nothing is.</summary>
    public string? WhatIsPlaying()
    {
        if (manager is null) return null;

        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions;
        try { sessions = manager.GetSessions(); }
        catch (Exception) { return null; }

        foreach (var session in sessions)
        {
            GlobalSystemMediaTransportControlsSessionPlaybackInfo info;
            try { info = session.GetPlaybackInfo(); }
            catch (Exception) { continue; }
            if (info.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) continue;

            var app = session.SourceAppUserModelId ?? "";
            if (Matches(app, MusicApps)) continue;
            if (info.PlaybackType == Windows.Media.MediaPlaybackType.Video || Matches(app, VideoApps))
                return FriendlyApp(app);

            // Browsers (and apps we don't know) report media without saying whether it's video,
            // so look for a visible window showing a video site.
            if (FindVideoWindow() is { } title) return title;
        }
        return null;
    }

    private static bool Matches(string app, string[] names) =>
        names.Any(n => app.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static string FriendlyApp(string aumid)
    {
        var name = aumid.Split('!', '_')[0];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.Length > 0 ? name : "a video";
    }

    private static string? FindVideoWindow()
    {
        string? found = null;
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd) || Native.IsCloaked(hwnd)) return true;
            int len = Native.GetWindowTextLength(hwnd);
            if (len == 0) return true;

            var sb = new StringBuilder(len + 1);
            Native.GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (title.Contains("YouTube Music", StringComparison.OrdinalIgnoreCase)) return true;

            foreach (var site in VideoSites)
            {
                if (title.Contains(site, StringComparison.OrdinalIgnoreCase))
                {
                    found = site.Trim();
                    return false; // stop enumerating
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
