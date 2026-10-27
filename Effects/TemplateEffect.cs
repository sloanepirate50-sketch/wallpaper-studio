using System.Drawing.Drawing2D;

namespace WallpaperStudio.Effects;

// =====================================================================================
//  EFFECT TEMPLATE: copy this file to start a new effect.
//
//  1. Copy this file into Effects\<YourEffect>\YourEffect.cs and rename the class.
//  2. Give it a unique Id, a Name and a Description.
//  3. Draw your animation in OnFrame (all drawing is plain System.Drawing / GDI+).
//  4. (Optional) override CreatePreview() to animate its tile in the app. Draw your
//     effect on top of PreviewBackdrop (a mini desktop). See ButterflyPreview / CatPreview.
//  5. Register it by adding `new YourEffect()` to EffectRegistry.CreateAll().
//     It then shows up automatically as a tile in the app's EFFECTS row and in the tray
//     menu, is saved in settings, and is hidden during fullscreen apps and videos.
//
//  This template is NOT registered. It's a working example: a glowing firefly that
//  drifts in the top-left corner. Register it to see it run.
// =====================================================================================
internal sealed class TemplateEffect : OverlayEffect
{
    public override string Id => "template";
    public override string Name => "Firefly (template)";
    public override string Description => "Example effect: a glowing firefly drifting in the corner.";

    // Optional: run slower to save CPU (default is 15 ms ≈ 60 fps).
    protected override int FrameIntervalMs => 33;

    // Logical size of the sprite window (at 100% display scale).
    private const int W = 160, H = 120;

    private SpriteWindow? sprite;
    private float time;

    protected override void OnStart()
    {
        // Sprites are sized in physical pixels, so scale the logical size.
        sprite = new SpriteWindow((int)(W * Scale), (int)(H * Scale));
        time = 0;
    }

    protected override void OnFrame(float dt)
    {
        if (sprite is null) return;
        time += dt;

        // --- update: where is the firefly this frame? (logical coordinates inside the sprite)
        float x = W / 2f + MathF.Sin(time * 0.9f) * 50f;
        float y = H / 2f + MathF.Sin(time * 1.7f) * 30f;
        float glow = 0.6f + 0.4f * MathF.Sin(time * 4f);

        // --- draw
        var g = sprite.Graphics;
        sprite.Begin();               // clear to transparent
        g.ScaleTransform(Scale, Scale); // from here on, draw in logical units

        using (var halo = new GraphicsPath())
        {
            halo.AddEllipse(x - 18, y - 18, 36, 36);
            using var brush = new PathGradientBrush(halo)
            {
                CenterColor = Color.FromArgb((int)(160 * glow), 255, 240, 120),
                SurroundColors = [Color.FromArgb(0, 255, 240, 120)],
            };
            g.FillPath(brush, halo);
        }
        using (var core = new SolidBrush(Color.FromArgb(255, 255, 250, 200)))
            g.FillEllipse(core, x - 3, y - 3, 6, 6);

        // --- show it: here, the top-left of the primary screen's work area
        var area = Screen.PrimaryScreen!.WorkingArea;
        sprite.Present(area.Left + 20, area.Top + 20);
    }

    protected override void OnHide() => sprite?.Hide();

    protected override void OnKeepOnTop() => sprite?.KeepOnTop();

    protected override void OnStop()
    {
        sprite?.Dispose();
        sprite = null;
    }
}
