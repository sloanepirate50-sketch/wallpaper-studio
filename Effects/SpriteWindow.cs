using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using static WallpaperStudio.Interop.Native;

namespace WallpaperStudio.Effects;

/// <summary>
/// An always-on-top, click-through window with per-pixel alpha. Effects draw into
/// <see cref="Graphics"/> with GDI+ and call <see cref="Present"/>, which pushes the pixels to the
/// screen with UpdateLayeredWindow (and moves the window in the same call).
/// </summary>
internal sealed class SpriteWindow : NativeWindow, IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public Graphics Graphics { get; }

    private readonly Bitmap canvas;
    private readonly IntPtr memDc, dib, oldBitmap;
    private bool visible;

    public SpriteWindow(int width, int height)
    {
        Width = width;
        Height = height;
        CreateHandle(new CreateParams
        {
            Caption = "WallpaperStudio effect",
            Style = WS_POPUP,
            ExStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE,
            Width = width,
            Height = height,
        });

        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        var screenDc = GetDC(IntPtr.Zero);
        memDc = CreateCompatibleDC(screenDc);
        dib = CreateDIBSection(screenDc, ref header, 0, out var bits, IntPtr.Zero, 0);
        ReleaseDC(IntPtr.Zero, screenDc);
        oldBitmap = SelectObject(memDc, dib);

        // GDI+ draws straight into the DIB's memory; premultiplied BGRA is what UpdateLayeredWindow wants.
        canvas = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, bits);
        Graphics = Graphics.FromImage(canvas);
        Graphics.SmoothingMode = SmoothingMode.AntiAlias;
    }

    /// <summary>Clears to transparent and resets the transform (origin = top-left).</summary>
    public void Begin()
    {
        Graphics.ResetTransform();
        Graphics.Clear(Color.Transparent);
    }

    /// <summary>Shows the canvas with its top-left corner at a screen position (physical pixels).</summary>
    public void Present(int left, int top)
    {
        Graphics.Flush();
        var dst = new POINT(left, top);
        var sz = new SIZE(Width, Height);
        var src = new POINT(0, 0);
        var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
        UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref sz, memDc, ref src, 0, ref blend, ULW_ALPHA);

        if (!visible)
        {
            ShowWindow(Handle, SW_SHOWNOACTIVATE);
            visible = true;
        }
    }

    public void Hide()
    {
        if (!visible) return;
        ShowWindow(Handle, SW_HIDE);
        visible = false;
    }

    /// <summary>Other topmost windows can cover us; call every so often to stay in front.</summary>
    public void KeepOnTop() =>
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    public void Dispose()
    {
        Graphics.Dispose();
        canvas.Dispose();
        SelectObject(memDc, oldBitmap);
        DeleteObject(dib);
        DeleteDC(memDc);
        DestroyHandle();
    }
}
