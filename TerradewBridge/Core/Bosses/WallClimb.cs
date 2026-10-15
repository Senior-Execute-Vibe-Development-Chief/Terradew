using System;

namespace Terradew.Core.Bosses;

/// <summary>
/// The Wall of Flesh's climb up one Stardew mine floor. Every position is in Stardew map pixels, 64 to a tile,
/// measured from the map's top-left corner, so north is the smaller Y. The wall's north edge is the value
/// here, and everything south of it is flesh. Speeds are Stardew pixels per tick (60 ticks a second).
/// </summary>
public static class WallClimb
{
    public const float PixelsPerTile = 64f;
    public const float TicksPerSecond = 60f;

    /// <summary>Climb speed with full life, in tiles per second. A 40-tile floor takes 40 seconds at the start.</summary>
    public const float FullLifeTilesPerSecond = 1f;

    /// <summary>Extra speed as life runs out, in tiles per second. The wall is fastest at zero life.</summary>
    public const float LostLifeTilesPerSecond = 0.5f;

    /// <summary>The wall's north edge when a floor starts: the map's south edge, in Stardew pixels.</summary>
    public static float StartY(int mapHeightTiles) => Math.Max(0, mapHeightTiles) * PixelsPerTile;

    /// <summary>How far north the wall moves each tick, in Stardew pixels. Life outside 0 to 1 is clamped.</summary>
    public static float Speed(float lifeFraction)
    {
        float lost = 1f - Clamp01(lifeFraction);
        float tilesPerSecond = FullLifeTilesPerSecond + LostLifeTilesPerSecond * lost;
        return tilesPerSecond * PixelsPerTile / TicksPerSecond;
    }

    /// <summary>The wall's north edge after one tick. It never moves south and stops at the map's north edge.</summary>
    public static float Advance(float wallY, float speed) => Math.Max(0f, wallY - Math.Max(0f, speed));

    /// <summary>True once the farmer's feet are at or south of the wall's north edge.</summary>
    public static bool Touches(float wallY, float farmerFeetY) => farmerFeetY >= wallY;

    private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
}
