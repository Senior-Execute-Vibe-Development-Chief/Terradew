namespace Terradew.Core;

/// <summary>
/// One Terraria tile (16px) is one Stardew tile (64px). East stays east, and Terraria's
/// downward axis is Stardew's south. Each Stardew place has a region origin in Terraria tiles.
/// </summary>
public static class WorldProjection
{
    public const float TerrariaTilePixels = 16f;
    public const float StardewTilePixels = 64f;
    public const float Scale = StardewTilePixels / TerrariaTilePixels;

    /// <summary>Terraria world pixels to Stardew map pixels.</summary>
    public static (float X, float Y) ToMap(int regionTileX, int regionTileY, float terrariaX, float terrariaY)
        => (terrariaX * Scale - regionTileX * StardewTilePixels, terrariaY * Scale - regionTileY * StardewTilePixels);

    /// <summary>Stardew map pixels to Terraria world pixels.</summary>
    public static (float X, float Y) ToWorld(int regionTileX, int regionTileY, float stardewX, float stardewY)
        => (regionTileX * TerrariaTilePixels + stardewX / Scale, regionTileY * TerrariaTilePixels + stardewY / Scale);

    /// <summary>Top-left of a body whose feet are the bottom center of that body.</summary>
    public static (float X, float Y) FromFeet(float footX, float footY, float bodyWidth, float bodyHeight)
        => (footX - bodyWidth / 2f, footY - bodyHeight);
}
