using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace Terradew;

/// <summary>Breaks the Stardew breakables a bomb Terraria blew up would reach, using Stardew's own bomb blast.</summary>
internal sealed class Explosions
{
    /// <summary>Only in the farmer's place: a blast in another place has nothing in front of the farmer to break.</summary>
    public void OnExplosion(Core.Protocol.Explosion blast, IMonitor? monitor = null)
    {
        GameLocation? location = Game1.currentLocation;
        if (location == null || Game1.player == null)
        {
            monitor?.Log($"Terradew skipped a Terraria blast in {blast.PlaceKey}: no location to blow up.", LogLevel.Trace);
            return;
        }
        string here = Maps.PlaceOf(location).Key;
        if (here != blast.PlaceKey)
        {
            monitor?.Log($"Terradew skipped a Terraria blast in {blast.PlaceKey}: the farmer is in {here}.", LogLevel.Trace);
            return;
        }
        monitor?.Log($"Terradew applied a Terraria blast in {blast.PlaceKey} at {blast.TileX:0.0},{blast.TileY:0.0}, radius {blast.Radius}.", LogLevel.Trace);
        // Terraria already hurt the farmer, so the farmer is not hurt again. Stardew's blast breaks what a Stardew bomb breaks.
        location.explode(new Vector2(blast.TileX, blast.TileY), blast.Radius, Game1.player, damageFarmers: false);
    }
}
