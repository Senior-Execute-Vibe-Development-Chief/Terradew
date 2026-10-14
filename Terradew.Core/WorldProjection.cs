namespace Terradew.Core;

/// <summary>
/// Places a Terraria NPC on the Stardew map the farmer is standing in.
/// One Terraria tile (16px) becomes one Stardew tile (64px). East stays east.
/// South is a chosen ground direction for walkers, and altitude for flyers and worms.
/// Positions are relative to the two players.
/// </summary>
public static class WorldProjection
{
    public const float TerrariaTilePixels = 16f;
    public const float StardewTilePixels = 64f;
    public const float Scale = StardewTilePixels / TerrariaTilePixels;
    public const float TrackRadiusPixels = TerrariaTilePixels * 100f;

    public static (float X, float Y) FollowAnchor(
        float farmerX,
        float farmerY,
        float seenFarmerX,
        float seenFarmerY,
        float terrariaX,
        float terrariaY,
        bool samePlace)
    {
        if (!samePlace)
            return (terrariaX, terrariaY);
        return (
            terrariaX + (farmerX - seenFarmerX) / Scale,
            terrariaY + (farmerY - seenFarmerY) / Scale);
    }

    public static (float X, float Y) ToStardew(
        float farmerX,
        float farmerY,
        float terrariaPlayerX,
        float terrariaPlayerY,
        float entityX,
        float entityY)
    {
        return (
            farmerX + (entityX - terrariaPlayerX) * Scale,
            farmerY + (entityY - terrariaPlayerY) * Scale);
    }

    /// <summary>
    /// Plants an entity on the Stardew map from the region origin alone.
    /// The farmer's live position cancels out, so walking does not drag a stationary creature.
    /// </summary>
    public static (float X, float Y) ToMap(int regionTileX, int regionTileY, float entityX, float entityY)
    {
        return (
            entityX * Scale - regionTileX * StardewTilePixels,
            entityY * Scale - regionTileY * StardewTilePixels);
    }

    public static (float X, float Y) Home(int regionTileX, int regionTileY, float stardewX, float stardewY)
    {
        return (
            regionTileX * TerrariaTilePixels + stardewX / Scale,
            regionTileY * TerrariaTilePixels + stardewY / Scale);
    }

    /// <summary>
    /// Top-left of a body whose feet are the bottom center of that body.
    /// Both games meet at the feet. The head is not the anchor.
    /// </summary>
    public static (float X, float Y) FromFeet(float footX, float footY, float bodyWidth, float bodyHeight)
    {
        return (footX - bodyWidth / 2f, footY - bodyHeight);
    }
}
