using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;

namespace Terradew;

/// <summary>
/// The Terraria go-home items (Recall Potion, Magic Mirror, Ice Mirror) warp the farmer to their bed here. Terraria
/// would teleport its puppet to its own spawn point, which means nothing, so Terraria only reports the use.
/// </summary>
internal static class HomeRecall
{
    /// <summary>Warps the farmer to the bed in their farmhouse, as Stardew's own warp items do.</summary>
    public static void OnRecall(Core.Protocol.Recall recall, IMonitor monitor)
    {
        if (!Context.IsWorldReady)
            return;
        if (Game1.eventUp || Game1.isFestival() || !Context.IsPlayerFree)
        {
            monitor.Log($"Terradew: Terraria item {recall.ItemType} did not recall the farmer, since an event, festival or menu is open.", LogLevel.Info);
            return;
        }
        FarmHouse home = Utility.getHomeOfFarmer(Game1.player);
        Point bed = home.GetPlayerBedSpot();
        // Facing 2 is down, the way the farmer faces when they wake up.
        Game1.warpFarmer(home.NameOrUniqueName, bed.X, bed.Y, 2);
        Game1.playSound("wand");
    }
}
