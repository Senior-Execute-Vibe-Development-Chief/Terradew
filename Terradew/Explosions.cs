using Microsoft.Xna.Framework;
using StardewValley;

namespace Terradew;

/// <summary>Breaks the Stardew breakables a bomb Terraria blew up would reach, using Stardew's own bomb blast.</summary>
internal sealed class Explosions
{
    /// <summary>Only in the farmer's place: a blast in another place has nothing in front of the farmer to break.</summary>
    public void OnExplosion(Core.Protocol.Explosion blast)
    {
        GameLocation? location = Game1.currentLocation;
        if (location == null || Game1.player == null || Maps.PlaceOf(location).Key != blast.PlaceKey)
            return;
        // Terraria already hurt the farmer, so the farmer is not hurt again. Stardew's blast breaks what a Stardew bomb breaks.
        location.explode(new Vector2(blast.TileX, blast.TileY), blast.Radius, Game1.player, damageFarmers: false);
    }
}
