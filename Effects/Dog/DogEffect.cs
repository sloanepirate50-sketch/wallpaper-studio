namespace WallpaperStudio.Effects.Dog;

/// <summary>A border collie catching frisbees in a corner (or the middle) of the screen.</summary>
internal sealed class DogEffect : PlayAreaEffect
{
    public override string Id => "dog";
    public override string Name => "Dog & frisbee";
    public override string Description => "A border collie leaps to catch frisbees thrown from the edge of your screen.";

    protected override SizeF AreaSize => new(DogSim.W, DogSim.H);

    protected override IPlayScene CreateScene() => new DogSim(new Random());
}
