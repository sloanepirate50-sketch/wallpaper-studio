# Wallpaper Studio

A small Wallpaper Engine–style app for Windows 10/11. Set images, GIFs, videos or animated scenes as
your desktop wallpaper, behind your desktop icons, on every monitor, and add playful effects on top.

## Features

- **Your own media**: JPG, PNG, BMP, WEBP, GIF, MP4, WEBM and M4V. Add them with the button or drag them onto the list.
  Files are copied into `%AppData%\WallpaperStudio\Library`.
- **Built-in scenes** (the wallpaper itself)
  - **Digital Rain**: terminal-style characters raining down the screen.
  - **Neon Snake**: a self-playing snake that roams the screen eating food.
  - **Ping Pong**: two AI paddles rallying in neon. The ball speeds up through each rally, and first to 11 wins.
  - **Tetris**: an AI plays Tetris, with a ghost piece, next-piece preview, score, lines and level.
  - **Synthwave Sunset**: a retro 80s striped sun, neon mountains and an endless rolling grid.
  - **Warp Speed**: flying through a starfield with nebulae, jumping to warp every so often.
  - **Game of Life**: Conway's cellular automaton in neon. Cells age from cyan to violet and leave trails, and the board reseeds itself when it goes stale.
  - **Fireworks**: peonies, rings and golden willows bursting over a city skyline.
- **Effects** (float on top of everything; clicks pass straight through)
  - **Butterflies**: colorful butterflies flutter around, flee from your mouse and land on the
    active window or the taskbar. This recreates only the *look* of the early-2000s butterfly screen prank.
  - **Cat with a cotton ball**: a ginger cat stalks, swats, chases and pounces on a cotton ball, sits down
    for naps, and looks at your mouse when it comes near.
  - **Dog & frisbee**: a border collie waits by the screen edge, gallops after frisbees thrown from just
    off-screen, leaps to catch them in mid-air (now and then it misjudges and has to fetch), and brings them back.
  - The cat and the dog each have a **position picker**: click a dot on the little monitor to choose a corner or the center.
    An effect's options only appear while that effect is turned on.
- **Auto-hide**: effects disappear while you watch videos (YouTube, Netflix, Twitch, Prime Video,
  Disney+, VLC, Plex, Stremio, …) and come back when the video stops. Fullscreen apps also pause the wallpaper.
- Fit modes, video mute, start with Windows, tray icon. Closing the window keeps everything running in the tray.

- **Saves power**: the wallpaper pauses behind maximized windows (per monitor), while the PC is locked,
  on battery, and when a fullscreen app or game is in front. Animated scenes can run at 60 or 30 fps
  (30 uses roughly half the CPU/GPU).
- Works with a Windows **slideshow** background: when Windows changes the picture, Explorer reshuffles the
  desktop's layers, and Wallpaper Studio puts itself back on top within a fraction of a second.

## Install

Download the latest `WallpaperStudio-<version>-Setup.msi` from the
[Releases page](https://github.com/sloanepirate50-sketch/wallpaper-studio/releases/latest) and run it
(or build it yourself, below). It installs for your user account (no admin prompt) into
`%LocalAppData%\Programs\Wallpaper Studio` and adds Start menu and desktop shortcuts. Uninstall it from
**Settings → Apps**. Your wallpapers and settings in `%AppData%\WallpaperStudio` are kept.

Needs Windows 10/11 x64 and the WebView2 Runtime (built into Windows 11). The app is self-contained, so .NET is not required.

## Build

Requires the .NET 10 SDK.

```powershell
dotnet run                              # debug run
.\build-installer.ps1                   # → artifacts\WallpaperStudio-1.0.0-Setup.msi
.\build-installer.ps1 -Version 1.1.0    # bump the version for an upgrade
```

To publish a release, push a version tag (`git tag v1.1.0`, then `git push origin v1.1.0`): GitHub Actions
builds the installer and attaches it to a new release automatically.

The installer script installs the WiX Toolset 5 dotnet tool on first use. Installing a newer version upgrades in place,
closing a running copy first.

Command-line switches: `--tray` (start hidden), `--exit` (close a running copy), `--uninstall` (close and remove
"Start with Windows"). Errors are logged to `%AppData%\WallpaperStudio\error.log`.

## Making your own

### A new effect (overlay on top of the screen)

Start from **`Effects/TemplateEffect.cs`**. It's a commented, working example (a glowing firefly).

1. Copy it to `Effects/<Name>/<Name>Effect.cs` and rename the class.
2. Set a unique `Id`, a `Name` (checkbox label) and a `Description` (tooltip).
3. Draw in `OnFrame(dt)` with System.Drawing onto a `SpriteWindow` (transparent, click-through, always on top).
4. Optionally override `CreatePreview()` so its tile in the app shows a live animation (see `ButterflyPreview`, `CatPreview`).
5. Add `new YourEffect()` to `Effects/EffectRegistry.cs`.

For an animal or toy that plays in a corner, derive from `PlayAreaEffect` instead and implement an `IPlayScene`
(see `CatEffect` / `DogEffect`). You get the position picker, mouse-watching and the tile preview for free.

It then appears automatically as a tile in the app's **Effects** row and in the tray menu, is remembered in settings,
and hides itself during fullscreen apps and videos. `Effects/Cat` and `Effects/Butterflies` are fuller examples.

### A new wallpaper scene

Start from **`wwwroot/scenes/_template.html`** (plain HTML canvas), then register it in `Library.Scenes` (`Library.cs`).
Keep the `window.wpPause` function so the app can pause it, and the `fps.js` include so the frame-rate setting applies.
Performance tips: draw anything static once into an offscreen canvas, batch strokes that share a color into one
path, avoid `shadowBlur` in the per-frame loop, and consider rendering at 75% resolution
(`dpr = devicePixelRatio * 0.75`); the GPU upscales it almost invisibly.

## How it works

| Piece | File |
|---|---|
| Finds the layer between the desktop icons and the Windows wallpaper and parents our windows into it (handles both the classic `WorkerW` layout and the Windows 11 24H2+ layout) | `Interop/DesktopHost.cs` |
| One WebView2 browser surface per monitor | `WallpaperWindow.cs`, `WallpaperManager.cs` |
| Image/GIF/video player page; scenes | `wwwroot/player.html`, `wwwroot/scenes/*.html` |
| Effect lifecycle (timer, start/stop, hide) and the click-through per-pixel-alpha window | `Effects/OverlayEffect.cs`, `Effects/SpriteWindow.cs` |
| Video detection: Windows media sessions plus video-site window titles (browsers report video as "music") | `Media/WatchDetector.cs` |
