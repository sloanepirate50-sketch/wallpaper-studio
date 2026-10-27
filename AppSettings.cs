using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace WallpaperStudio;

internal sealed class AppSettings
{
    public string? CurrentId { get; set; }
    public string Fit { get; set; } = "cover";
    public bool Muted { get; set; } = true;
    public bool PauseOnFullscreen { get; set; } = true;
    public bool PauseBehindMaximized { get; set; } = true;
    /// <summary>Frame rate for animated scenes: 60 (smoothest) or 30 (about half the CPU/GPU).</summary>
    public int FrameRate { get; set; } = 60;
    public bool PauseOnBattery { get; set; } = true;
    public bool HideEffectsWhileWatching { get; set; } = true;
    public List<string> Effects { get; set; } = [];
    public int ButterflyCount { get; set; } = 8;
    /// <summary>Effect id → screen placement name (for effects like the cat and dog).</summary>
    public Dictionary<string, string> Placements { get; set; } = [];

    /// <summary>Older single cat setting; migrated into <see cref="Placements"/> on load.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CatPlacement { get; set; }

    /// <summary>Pre-effects-list setting; migrated into <see cref="Effects"/> on load.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Butterflies { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        var s = new AppSettings();
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile)) ?? s;
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }

        if (s.Butterflies == true && !s.Effects.Contains("butterflies")) s.Effects.Add("butterflies");
        s.Butterflies = null;
        if (s.CatPlacement is { } cat) s.Placements.TryAdd("cat", cat);
        s.CatPlacement = null;
        return s;
    }

    public void Save()
    {
        try { File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, Json)); }
        catch (IOException) { }
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "WallpaperStudio";

    public static bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunName) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(RunName, $"\"{Environment.ProcessPath}\" --tray");
            else key.DeleteValue(RunName, throwOnMissingValue: false);
        }
    }
}
