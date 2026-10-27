using WallpaperStudio.Effects.Butterflies;
using WallpaperStudio.Effects.Cat;
using WallpaperStudio.Effects.Dog;

namespace WallpaperStudio.Effects;

/// <summary>Every effect the app offers. Add yours here (see TemplateEffect.cs).</summary>
internal static class EffectRegistry
{
    public static List<OverlayEffect> CreateAll() =>
    [
        new ButterflyEffect(),
        new CatEffect(),
        new DogEffect(),
    ];
}
