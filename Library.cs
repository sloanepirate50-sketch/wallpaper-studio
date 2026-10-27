namespace WallpaperStudio;

internal enum WallpaperKind { Scene, Image, Gif, Video }

internal sealed record WallpaperItem(string Id, string Name, WallpaperKind Kind, string File)
{
    public bool IsBuiltIn => Kind == WallpaperKind.Scene;
}

/// <summary>
/// Built-in scenes plus the user's library. Imported media is copied into
/// %AppData%\WallpaperStudio\Library so wallpapers survive the originals being moved.
/// </summary>
internal static class Library
{
    private static readonly string[] ImageExt = [".jpg", ".jpeg", ".png", ".bmp", ".webp"];
    private static readonly string[] GifExt = [".gif"];
    private static readonly string[] VideoExt = [".mp4", ".webm", ".m4v"];

    public const string FileFilter =
        "Images, GIFs and videos|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif;*.mp4;*.webm;*.m4v|All files|*.*";

    public static IReadOnlyList<WallpaperItem> Scenes { get; } =
    [
        new("scene:matrix", "Digital Rain", WallpaperKind.Scene, "matrix.html"),
        new("scene:snake", "Neon Snake", WallpaperKind.Scene, "snake.html"),
        new("scene:pong", "Ping Pong", WallpaperKind.Scene, "pong.html"),
        new("scene:tetris", "Tetris", WallpaperKind.Scene, "tetris.html"),
        new("scene:synthwave", "Synthwave Sunset", WallpaperKind.Scene, "synthwave.html"),
        new("scene:warp", "Warp Speed", WallpaperKind.Scene, "warp.html"),
        new("scene:life", "Game of Life", WallpaperKind.Scene, "life.html"),
        new("scene:fireworks", "Fireworks", WallpaperKind.Scene, "fireworks.html"),
    ];

    public static WallpaperKind? KindOf(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ImageExt.Contains(ext)) return WallpaperKind.Image;
        if (GifExt.Contains(ext)) return WallpaperKind.Gif;
        if (VideoExt.Contains(ext)) return WallpaperKind.Video;
        return null;
    }

    public static List<WallpaperItem> UserItems()
    {
        var items = new List<WallpaperItem>();
        foreach (var path in Directory.EnumerateFiles(AppPaths.Library).OrderBy(File.GetCreationTimeUtc))
        {
            if (KindOf(path) is { } kind)
            {
                var name = Path.GetFileName(path);
                items.Add(new("file:" + name, Path.GetFileNameWithoutExtension(path), kind, name));
            }
        }
        return items;
    }

    public static WallpaperItem? Find(string? id) =>
        id is null ? null : Scenes.Concat(UserItems()).FirstOrDefault(i => i.Id == id);

    public static string FullPath(WallpaperItem item) =>
        item.IsBuiltIn ? Path.Combine(AppPaths.WebRoot, "scenes", item.File) : Path.Combine(AppPaths.Library, item.File);

    /// <summary>Copies a file into the library. Returns null for unsupported types.</summary>
    public static WallpaperItem? Import(string source)
    {
        if (KindOf(source) is not { } kind) return null;

        var baseName = Path.GetFileNameWithoutExtension(source);
        var ext = Path.GetExtension(source).ToLowerInvariant();
        var dest = Path.Combine(AppPaths.Library, baseName + ext);
        for (int n = 2; File.Exists(dest); n++)
            dest = Path.Combine(AppPaths.Library, $"{baseName} ({n}){ext}");

        File.Copy(source, dest);
        var name = Path.GetFileName(dest);
        return new("file:" + name, Path.GetFileNameWithoutExtension(dest), kind, name);
    }

    public static void Delete(WallpaperItem item)
    {
        if (!item.IsBuiltIn) File.Delete(FullPath(item));
    }
}
