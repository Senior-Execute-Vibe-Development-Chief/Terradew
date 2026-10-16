using System;
using Terradew.Core.Protocol;

namespace Terradew.Core.World;

/// <summary>
/// Maps Stardew's weather onto Terraria's rain and wind. Stardew is the master: while linked, Terraria's rain
/// follows what this says.
/// </summary>
public static class WeatherMap
{
    /// <summary>Terraria rain strength for ordinary rain and snow.</summary>
    public const float RainStrength = 0.5f;
    /// <summary>Terraria rain strength for a storm. Clouds are thicker than in ordinary rain.</summary>
    public const float StormStrength = 0.9f;
    /// <summary>Terraria wind speed target for Stardew's windy weather. Zero leaves Terraria's wind alone.</summary>
    public const float WindSpeed = 0.6f;

    /// <summary>
    /// Raining is true for rain, snow and storms: Terraria shows snow in snow biomes. A storm is always rain.
    /// Wind is set only by the windy weather; Terraria's own wind goes on otherwise.
    /// </summary>
    public static (bool Raining, float Strength, float Wind) ToTerraria(WeatherFlags flags)
    {
        bool storm = Has(flags, WeatherFlags.Lightning);
        bool raining = storm || Has(flags, WeatherFlags.Rain) || Has(flags, WeatherFlags.Snow);
        float strength = !raining ? 0f : storm ? StormStrength : RainStrength;
        float wind = Has(flags, WeatherFlags.Debris) ? WindSpeed : 0f;
        return (raining, strength, wind);
    }

    private static bool Has(WeatherFlags flags, WeatherFlags flag) => (flags & flag) != 0;
}
