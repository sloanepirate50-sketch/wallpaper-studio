using Microsoft.Win32;
using WallpaperStudio.Effects;
using WallpaperStudio.Effects.Butterflies;
using WallpaperStudio.Effects.Cat;
using WallpaperStudio.Interop;
using WallpaperStudio.Media;

namespace WallpaperStudio;

internal sealed class MainForm : Form
{
    private static readonly Color Bg = Color.FromArgb(27, 27, 34);
    private static readonly Color Surface = Color.FromArgb(21, 21, 27);
    private static readonly Color Raised = Color.FromArgb(40, 40, 50);
    private static readonly Color Accent = Color.FromArgb(124, 92, 255);
    private static readonly Color Fg = Color.FromArgb(236, 236, 241);
    private static readonly Color Dim = Color.FromArgb(154, 154, 168);

    private sealed record FitOption(string Value, string Label) { public override string ToString() => Label; }
    private static readonly FitOption[] FitOptions =
    [
        new("cover", "Fill (crop to screen)"),
        new("contain", "Fit (show whole image)"),
        new("stretch", "Stretch"),
        new("center", "Center (original size)"),
    ];

    private readonly AppSettings settings = AppSettings.Load();
    private readonly WallpaperManager wallpaper = new();
    private readonly List<OverlayEffect> effects = EffectRegistry.CreateAll();
    private readonly Dictionary<OverlayEffect, (EffectTile Tile, ToolStripMenuItem TrayItem)> effectControls = [];
    private readonly System.Windows.Forms.Timer previewTimer = new() { Interval = 33 };
    private readonly System.Diagnostics.Stopwatch previewClock = new();
    private readonly WatchDetector watch = new();

    private readonly ListView list = new();
    private readonly ImageList thumbs = new() { ImageSize = new Size(192, 108), ColorDepth = ColorDepth.Depth32Bit };
    private readonly ComboBox fitBox = new();
    private readonly ComboBox fpsBox = new();

    private sealed record FpsOption(int Value, string Label) { public override string ToString() => Label; }
    private static readonly FpsOption[] FpsOptions =
    [
        new(60, "60 fps (smoothest)"),
        new(30, "30 fps (uses about half the power)"),
    ];
    // Per-effect option controls, shown only while that effect is turned on.
    private readonly Dictionary<OverlayEffect, List<Control>> effectOptions = [];
    private readonly Dictionary<OverlayEffect, PlacementPicker> pickers = [];
    private readonly CheckBox muteBox, pauseBox, maximizedBox, batteryBox, watchBox, startupBox;
    private readonly TrackBar countBar = new();
    private readonly Label countLabel = new();
    private readonly Label status = new();
    private readonly NotifyIcon tray = new();
    private readonly ToolTip tips = new();
    private readonly System.Windows.Forms.Timer autoHideTimer = new() { Interval = 1500 };
    private string? hiddenReason;
    private bool sessionLocked, wasOnBattery;

    // Signals from a second launch (Start menu click while running → show; installer → exit).
    private readonly EventWaitHandle showSignal = new(false, EventResetMode.AutoReset, Program.ShowEventName);
    private readonly EventWaitHandle exitSignal = new(false, EventResetMode.AutoReset, Program.ExitEventName);
    private RegisteredWaitHandle? showWait, exitWait;
    private readonly System.Windows.Forms.Timer displayDebounce = new() { Interval = 1500 };
    private readonly uint taskbarCreatedMsg = Native.RegisterWindowMessage("TaskbarCreated");

    private bool startHidden;
    private bool exiting;
    private bool shownTrayHint;
    private bool loadingUi;

    public MainForm(bool startHidden)
    {
        this.startHidden = startHidden;

        // All sizes below are written for 96 DPI; WinForms scales them to the monitor's DPI.
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        SuspendLayout();

        Text = "Wallpaper Studio";
        Icon = ButterflyArt.MakeIcon();
        ClientSize = new Size(1060, 740);
        MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;
        ForeColor = Fg;
        Font = new Font("Segoe UI", 10f);

        muteBox = Check("Mute video sound");
        pauseBox = Check("Pause when an app is fullscreen");
        maximizedBox = Check("Pause behind maximized windows");
        batteryBox = Check("Pause on battery power");
        tips.SetToolTip(maximizedBox, "Saves CPU/GPU: the wallpaper on a monitor pauses while a maximized window covers it.");
        watchBox = Check("Hide effects while I watch videos");
        startupBox = Check("Start with Windows");
        tips.SetToolTip(watchBox, "YouTube, Netflix, Twitch, VLC, movies and more. Effects come back when the video stops.");

        BuildLayout();
        BuildTray();
        WireEvents();
        LoadSettingsIntoUi();
        RefreshList();
        ResumeLayout(performLayout: true);

        HandleCreated += async (_, _) => await StartupAsync();
    }

    private readonly ColumnStyle sideColumn = new(SizeType.Absolute, 312);

    // TableLayoutPanel's absolute column widths aren't DPI-scaled for us.
    private void ScaleSideColumn() => sideColumn.Width = LogicalToDeviceUnits(312);

    protected override void OnLoad(EventArgs e)
    {
        ScaleSideColumn();
        base.OnLoad(e);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ScaleSideColumn();
    }

    // ---------- Layout ----------

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(16) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(sideColumn);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "Wallpaper Studio",
            Font = new Font("Segoe UI Semibold", 18f),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 2),
        };
        var subtitle = new Label
        {
            Text = "Double-click a wallpaper to put it on your desktop. Drop your own images, GIFs or videos on the list to add them.",
            ForeColor = Dim,
            AutoSize = true,
            Margin = new Padding(2, 0, 0, 12),
        };
        var header = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        root.Controls.Add(header, 0, 0);
        root.SetColumnSpan(header, 2);

        list.Dock = DockStyle.Fill;
        list.View = View.LargeIcon;
        list.LargeImageList = thumbs;
        list.BackColor = Surface;
        list.ForeColor = Fg;
        list.BorderStyle = BorderStyle.None;
        list.MultiSelect = false;
        list.HideSelection = false;
        list.AllowDrop = true;

        // Left column: wallpapers on top, effect tiles (with live previews) underneath.
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        list.Margin = Padding.Empty;
        left.Controls.Add(list, 0, 0);
        var effectsHeader = Section("Effects: click to turn on or off");
        effectsHeader.Margin = new Padding(0, 14, 0, 8);
        left.Controls.Add(effectsHeader, 0, 1);
        var tiles = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        foreach (var effect in effects)
        {
            var tile = new EffectTile(effect);
            tips.SetToolTip(tile, effect.Description);
            effectControls[effect] = (tile, new ToolStripMenuItem(effect.Name));
            tiles.Controls.Add(tile);
        }
        left.Controls.Add(tiles, 0, 2);
        root.Controls.Add(left, 0, 1);

        var side = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(16, 0, 0, 0),
        };
        side.Controls.Add(Button("Set as wallpaper", accent: true, ApplySelected));
        side.Controls.Add(Button("Add images, GIFs or videos…", accent: false, AddFiles));
        side.Controls.Add(Button("Remove from library", accent: false, RemoveSelected));
        side.Controls.Add(Button("Stop wallpaper", accent: false, StopWallpaper));

        side.Controls.Add(Section("Display"));
        side.Controls.Add(new Label { Text = "Image / video fit", ForeColor = Dim, AutoSize = true, Margin = new Padding(0, 0, 0, 4) });
        fitBox.DropDownStyle = ComboBoxStyle.DropDownList;
        fitBox.FlatStyle = FlatStyle.Flat;
        fitBox.BackColor = Raised;
        fitBox.ForeColor = Fg;
        fitBox.Width = 270;
        fitBox.Margin = new Padding(0, 0, 0, 10);
        fitBox.Items.AddRange(FitOptions);
        side.Controls.Add(fitBox);
        side.Controls.Add(new Label { Text = "Animated scene frame rate", ForeColor = Dim, AutoSize = true, Margin = new Padding(0, 0, 0, 4) });
        fpsBox.DropDownStyle = ComboBoxStyle.DropDownList;
        fpsBox.FlatStyle = FlatStyle.Flat;
        fpsBox.BackColor = Raised;
        fpsBox.ForeColor = Fg;
        fpsBox.Width = 270;
        fpsBox.Margin = new Padding(0, 0, 0, 10);
        fpsBox.Items.AddRange(FpsOptions);
        side.Controls.Add(fpsBox);
        side.Controls.Add(muteBox);
        side.Controls.Add(pauseBox);
        side.Controls.Add(maximizedBox);
        side.Controls.Add(batteryBox);

        side.Controls.Add(Section("Effect options"));
        foreach (var effect in effects)
        {
            var controls = new List<Control>();
            if (effect is ButterflyEffect)
            {
                countLabel.AutoSize = true;
                countLabel.ForeColor = Dim;
                countBar.Minimum = 1;
                countBar.Maximum = 30;
                countBar.TickStyle = TickStyle.None;
                countBar.Width = 270;
                countBar.Margin = new Padding(0, 0, 0, 6);
                countBar.BackColor = Bg;
                controls.AddRange([countLabel, countBar]);
            }
            if (effect is IPlaceable)
            {
                var label = new Label
                {
                    Text = $"{effect.Name}: where on screen",
                    UseMnemonic = false, // keep the "&" visible
                    ForeColor = Dim,
                    AutoSize = true,
                    Margin = new Padding(0, 2, 0, 4),
                };
                var picker = new PlacementPicker { Margin = new Padding(0, 0, 0, 10) };
                tips.SetToolTip(picker, "Click a dot to choose a corner or the center.");
                pickers[effect] = picker;
                controls.AddRange([label, picker]);
            }
            foreach (var c in controls) side.Controls.Add(c);
            effectOptions[effect] = controls;
        }
        side.Controls.Add(watchBox);

        side.Controls.Add(Section("App"));
        side.Controls.Add(startupBox);
        root.Controls.Add(side, 1, 1);

        status.AutoSize = true;
        status.ForeColor = Dim;
        status.Margin = new Padding(2, 10, 0, 0);
        root.Controls.Add(status, 0, 2);
        root.SetColumnSpan(status, 2);

        Controls.Add(root);
    }

    private static Button Button(string text, bool accent, Action onClick)
    {
        var b = new Button
        {
            Text = text,
            Width = 270,
            Height = 40,
            FlatStyle = FlatStyle.Flat,
            BackColor = accent ? Accent : Raised,
            ForeColor = Color.White,
            Margin = new Padding(0, 0, 0, 8),
            Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = accent ? Color.FromArgb(145, 118, 255) : Color.FromArgb(55, 55, 68);
        b.Click += (_, _) => onClick();
        return b;
    }

    private static Label Section(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        Font = new Font("Segoe UI Semibold", 8.5f),
        ForeColor = Color.FromArgb(160, 140, 255),
        AutoSize = true,
        Margin = new Padding(0, 18, 0, 8),
    };

    private static CheckBox Check(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Fg,
        Margin = new Padding(0, 0, 0, 6),
        Cursor = Cursors.Hand,
    };

    private void BuildTray()
    {
        tray.Icon = Icon;
        tray.Text = "Wallpaper Studio";
        tray.Visible = true;
        tray.DoubleClick += (_, _) => ShowFromTray();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Wallpaper Studio", null, (_, _) => ShowFromTray());
        foreach (var (tile, item) in effectControls.Values)
        {
            item.Click += (_, _) => tile.Checked = !tile.Checked;
            menu.Items.Add(item);
        }
        menu.Items.Add("Stop wallpaper", null, (_, _) => StopWallpaper());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        tray.ContextMenuStrip = menu;
    }

    // ---------- Behavior ----------

    private void WireEvents()
    {
        list.ItemActivate += (_, _) => ApplySelected();
        list.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        };
        list.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) Import(files);
        };

        fitBox.SelectedIndexChanged += async (_, _) =>
        {
            if (loadingUi || fitBox.SelectedItem is not FitOption fit) return;
            settings.Fit = fit.Value;
            settings.Save();
            await ReapplyIfMedia();
        };
        fpsBox.SelectedIndexChanged += async (_, _) =>
        {
            if (loadingUi || fpsBox.SelectedItem is not FpsOption fps) return;
            settings.FrameRate = fps.Value;
            settings.Save();
            if (wallpaper.Current is { IsBuiltIn: true } scene) await Apply(scene);
        };
        muteBox.CheckedChanged += async (_, _) =>
        {
            if (loadingUi) return;
            settings.Muted = muteBox.Checked;
            settings.Save();
            if (wallpaper.Current?.Kind == WallpaperKind.Video) await ReapplyIfMedia();
        };
        pauseBox.CheckedChanged += (_, _) =>
        {
            if (loadingUi) return;
            settings.PauseOnFullscreen = pauseBox.Checked;
            settings.Save();
            UpdateAutoHide();
        };
        maximizedBox.CheckedChanged += (_, _) =>
        {
            if (loadingUi) return;
            settings.PauseBehindMaximized = maximizedBox.Checked;
            settings.Save();
            UpdateAutoHide();
        };
        batteryBox.CheckedChanged += (_, _) =>
        {
            if (loadingUi) return;
            settings.PauseOnBattery = batteryBox.Checked;
            settings.Save();
            UpdateAutoHide();
        };
        watchBox.CheckedChanged += (_, _) =>
        {
            if (loadingUi) return;
            settings.HideEffectsWhileWatching = watchBox.Checked;
            settings.Save();
            UpdateAutoHide();
        };
        foreach (var (effect, (tile, trayItem)) in effectControls)
        {
            tile.CheckedChanged += (_, _) =>
            {
                trayItem.Checked = tile.Checked;
                UpdateEffectOptionsVisibility();
                if (loadingUi) return;
                settings.Effects.Remove(effect.Id);
                if (tile.Checked) settings.Effects.Add(effect.Id);
                settings.Save();
                if (tile.Checked) effect.Start(); else effect.Stop();
                UpdateAutoHide();
            };
        }

        // Animate the effect previews only while the window is actually on screen.
        previewTimer.Tick += (_, _) =>
        {
            float dt = (float)Math.Min(0.1, previewClock.Elapsed.TotalSeconds);
            previewClock.Restart();
            if (!Visible || WindowState == FormWindowState.Minimized) return;
            foreach (var (tile, _) in effectControls.Values) tile.Advance(dt);
        };
        previewClock.Start();
        previewTimer.Start();

        countBar.ValueChanged += (_, _) =>
        {
            countLabel.Text = $"Butterflies: {countBar.Value}";
            if (loadingUi) return;
            settings.ButterflyCount = countBar.Value;
            settings.Save();
            foreach (var b in effects.OfType<ButterflyEffect>()) b.Count = countBar.Value;
        };
        foreach (var (effect, picker) in pickers)
        {
            picker.ValueChanged += (_, _) =>
            {
                ((IPlaceable)effect).Placement = picker.Value;
                if (loadingUi) return;
                settings.Placements[effect.Id] = picker.Value.ToString();
                settings.Save();
            };
        }
        startupBox.CheckedChanged += (_, _) =>
        {
            if (loadingUi) return;
            try { AppSettings.StartWithWindows = startupBox.Checked; }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                SetStatus("Couldn't change the startup setting: " + ex.Message);
            }
        };

        autoHideTimer.Tick += async (_, _) =>
        {
            UpdateAutoHide();
            try { await wallpaper.MaintainAsync(settings); }
            catch (Exception ex) { SetStatus("Couldn't restore the wallpaper: " + ex.Message); }
        };
        displayDebounce.Tick += async (_, _) =>
        {
            displayDebounce.Stop();
            await RebuildWallpaper();
        };
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        HandleCreated += (_, _) =>
        {
            showWait = ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => BeginInvoke(ShowFromTray), null, -1, false);
            exitWait = ThreadPool.RegisterWaitForSingleObject(exitSignal, (_, _) => BeginInvoke(ExitApp), null, -1, false);
        };
    }

    private void LoadSettingsIntoUi()
    {
        loadingUi = true;
        fitBox.SelectedItem = FitOptions.FirstOrDefault(f => f.Value == settings.Fit) ?? FitOptions[0];
        fpsBox.SelectedItem = FpsOptions.FirstOrDefault(f => f.Value == settings.FrameRate) ?? FpsOptions[0];
        muteBox.Checked = settings.Muted;
        pauseBox.Checked = settings.PauseOnFullscreen;
        maximizedBox.Checked = settings.PauseBehindMaximized;
        batteryBox.Checked = settings.PauseOnBattery;
        watchBox.Checked = settings.HideEffectsWhileWatching;
        foreach (var (effect, (tile, trayItem)) in effectControls)
            tile.Checked = trayItem.Checked = settings.Effects.Contains(effect.Id);
        countBar.Value = Math.Clamp(settings.ButterflyCount, countBar.Minimum, countBar.Maximum);
        countLabel.Text = $"Butterflies: {countBar.Value}";
        foreach (var (effect, picker) in pickers)
        {
            var placement = settings.Placements.TryGetValue(effect.Id, out var name) && Enum.TryParse<ScreenPlacement>(name, out var parsed)
                ? parsed : ScreenPlacement.BottomRight;
            picker.Value = placement;
            ((IPlaceable)effect).Placement = placement;
        }
        UpdateEffectOptionsVisibility();
        startupBox.Checked = AppSettings.StartWithWindows;
        loadingUi = false;
    }

    /// <summary>Each effect's options (butterfly count, position picker) only show while it's on.</summary>
    private void UpdateEffectOptionsVisibility()
    {
        foreach (var (effect, controls) in effectOptions)
        {
            bool on = effectControls[effect].Tile.Checked;
            foreach (var c in controls) c.Visible = on;
        }
    }

    private async Task StartupAsync()
    {
        foreach (var b in effects.OfType<ButterflyEffect>()) b.Count = settings.ButterflyCount;
        foreach (var effect in effects.Where(e => settings.Effects.Contains(e.Id)))
            effect.Start();

        await watch.InitAsync();
        autoHideTimer.Start();
        UpdateAutoHide();

        if (Library.Find(settings.CurrentId) is { } item)
            await Apply(item);
        else
            SetStatus("Pick a wallpaper to get started.");
    }

    private void RefreshList(string? selectId = null)
    {
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var img in thumbs.Images.Cast<Image>().ToList()) img.Dispose();
        thumbs.Images.Clear();

        foreach (var item in Library.Scenes.Concat(Library.UserItems()))
        {
            thumbs.Images.Add(item.Id, Thumbnails.For(item, thumbs.ImageSize));
            var lvi = new ListViewItem(item.Name, item.Id) { Tag = item };
            list.Items.Add(lvi);
            if (item.Id == (selectId ?? settings.CurrentId)) lvi.Selected = true;
        }
        list.EndUpdate();
        list.SelectedItems.Cast<ListViewItem>().FirstOrDefault()?.EnsureVisible();
    }

    private WallpaperItem? Selected => list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as WallpaperItem : null;

    private async void ApplySelected()
    {
        if (Selected is { } item) await Apply(item);
        else SetStatus("Select a wallpaper first.");
    }

    private async Task Apply(WallpaperItem item)
    {
        try
        {
            SetStatus($"Applying “{item.Name}”…");
            await wallpaper.ApplyAsync(item, settings);
            settings.CurrentId = item.Id;
            settings.Save();
            SetStatus($"Now showing “{item.Name}”.");
        }
        catch (Exception ex)
        {
            SetStatus("Couldn't set the wallpaper: " + ex.Message);
        }
    }

    private async Task ReapplyIfMedia()
    {
        if (wallpaper.Current is { IsBuiltIn: false } item) await Apply(item);
    }

    private async Task RebuildWallpaper()
    {
        try { await wallpaper.RebuildAsync(settings); }
        catch (Exception ex) { SetStatus("Couldn't restore the wallpaper: " + ex.Message); }
    }

    private void StopWallpaper()
    {
        wallpaper.Stop();
        settings.CurrentId = null;
        settings.Save();
        SetStatus("Wallpaper stopped. Your normal Windows wallpaper is back.");
    }

    private void AddFiles()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Add wallpapers",
            Filter = Library.FileFilter,
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) Import(dlg.FileNames);
    }

    private void Import(IEnumerable<string> paths)
    {
        WallpaperItem? lastAdded = null;
        int skipped = 0;
        foreach (var path in paths)
        {
            try
            {
                if (Library.Import(path) is { } added) lastAdded = added;
                else skipped++;
            }
            catch (IOException) { skipped++; }
        }
        RefreshList(lastAdded?.Id);
        SetStatus(lastAdded is null
            ? "Nothing added. Supported: JPG, PNG, BMP, WEBP, GIF, MP4, WEBM, M4V."
            : skipped > 0 ? $"Added. Skipped {skipped} unsupported file(s)." : "Added to your library. Double-click to use it.");
    }

    private void RemoveSelected()
    {
        if (Selected is not { } item) { SetStatus("Select a wallpaper first."); return; }
        if (item.IsBuiltIn) { SetStatus("Built-in scenes can't be removed."); return; }

        var answer = MessageBox.Show(this, $"Remove “{item.Name}” from your library?\nThe original file you added is not touched.",
            "Remove wallpaper", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        if (wallpaper.Current?.Id == item.Id) StopWallpaper();
        try
        {
            Library.Delete(item);
            RefreshList();
            SetStatus($"Removed “{item.Name}”.");
        }
        catch (IOException ex) { SetStatus("Couldn't remove it: " + ex.Message); }
    }

    /// <summary>
    /// Saves CPU/GPU/battery and keeps effects out of the way:
    /// - locked, on battery, or a fullscreen app → pause the wallpaper everywhere;
    /// - a maximized window → pause the wallpaper on just that monitor (you can't see it anyway);
    /// - locked, fullscreen, or watching a video → hide effects.
    /// </summary>
    private void UpdateAutoHide()
    {
        bool onBattery = settings.PauseOnBattery && SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
        bool fullscreen = settings.PauseOnFullscreen && DesktopHost.IsForegroundFullscreen();
        var maximized = settings.PauseBehindMaximized ? DesktopHost.MonitorsWithMaximizedWindows() : [];
        bool pauseAll = sessionLocked || onBattery || fullscreen;
        wallpaper.SetPaused(monitor => pauseAll || maximized.Contains(monitor));

        if (onBattery != wasOnBattery && wallpaper.Current is not null)
            SetStatus(onBattery ? "On battery power: wallpaper paused to save energy." : "Plugged in: wallpaper running again.");
        wasOnBattery = onBattery;

        string? reason = sessionLocked ? "the screen is locked" : fullscreen ? "a fullscreen app" : null;
        if (reason is null && settings.HideEffectsWhileWatching && effects.Any(e => e.Running))
            reason = watch.WhatIsPlaying() is { } what ? $"you watch {what}" : null;

        foreach (var effect in effects) effect.SetHidden(reason is not null);

        if (reason == hiddenReason) return;
        if (reason is not null && !sessionLocked && effects.Any(e => e.Running))
            SetStatus($"Effects hidden while {reason}. They'll come back when it stops.");
        else if (hiddenReason is not null && reason is null && effects.Any(e => e.Running))
            SetStatus("Effects are back.");
        hiddenReason = reason;
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        sessionLocked = e.Reason switch
        {
            SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect => true,
            SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect => false,
            _ => sessionLocked,
        };
        BeginInvoke(UpdateAutoHide);
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        // After sleep, Explorer sometimes rebuilds the desktop layer: re-attach to be safe.
        if (e.Mode == PowerModes.Resume) BeginInvoke(() => { displayDebounce.Stop(); displayDebounce.Start(); });
        else if (e.Mode == PowerModes.StatusChange) BeginInvoke(UpdateAutoHide);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // Fires on a background thread; restart the debounce on the UI thread.
        BeginInvoke(() => { displayDebounce.Stop(); displayDebounce.Start(); });
    }

    private void SetStatus(string text) => status.Text = text;

    // ---------- Window / tray lifetime ----------

    protected override void SetVisibleCore(bool value)
    {
        if (startHidden)
        {
            startHidden = false;
            if (!IsHandleCreated) CreateHandle();
            value = false;
        }
        base.SetVisibleCore(value);
    }

    protected override void WndProc(ref Message m)
    {
        // Explorer restarted: the desktop layer we were attached to is gone.
        if (m.Msg == (int)taskbarCreatedMsg) BeginInvoke(async () => await RebuildWallpaper());
        base.WndProc(ref m);
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    private void ExitApp()
    {
        exiting = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            // Closing the window keeps the wallpaper running from the tray.
            e.Cancel = true;
            Hide();
            if (!shownTrayHint)
            {
                shownTrayHint = true;
                tray.ShowBalloonTip(3000, "Still running", "Wallpaper Studio is in the tray. Right-click the butterfly to exit.", ToolTipIcon.Info);
            }
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        showWait?.Unregister(null);
        exitWait?.Unregister(null);
        showSignal.Dispose();
        exitSignal.Dispose();
        autoHideTimer.Dispose();
        previewTimer.Dispose();
        displayDebounce.Dispose();
        foreach (var effect in effects) effect.Dispose();
        wallpaper.Dispose();
        tray.Visible = false;
        tray.Dispose();
        base.OnFormClosed(e);
    }
}
