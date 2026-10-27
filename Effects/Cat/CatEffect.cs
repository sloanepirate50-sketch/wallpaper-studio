namespace WallpaperStudio.Effects.Cat;

/// <summary>A ginger cat playing with a cotton ball in a corner (or the middle) of the screen.</summary>
internal sealed class CatEffect : PlayAreaEffect
{
    public override string Id => "cat";
    public override string Name => "Cat & cotton ball";
    public override string Description => "A cat bats a cotton ball around in a corner of your screen.";

    protected override SizeF AreaSize => new(CatSim.W, CatSim.H);

    protected override IPlayScene CreateScene() => new CatSim(new Random());
}
