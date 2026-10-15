using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>
/// The Wall of Flesh climbs the Stardew mines while the boss fight runs. The farmer can still go down,
/// but the ladder up takes them one floor higher, the elevator is shut, and the top floor's ladder is
/// blocked by the wall itself.
/// </summary>
internal sealed class WallClimb
{
    // The Buildings tile of the ladder up that every mine floor starts at (173 is the ladder down, 174 a shaft).
    private const int LadderUpTile = 115;
    private const int BlockedFloor = 91;
    private const int LastFloor = 120;
    // How close the farmer must be to the ladder tile for the action to count.
    private const float LadderReachTiles = 2f;

    private static WallClimb? instance;

    private readonly Bridge bridge;
    private IMonitor? monitor;
    private int? pendingFloor;
    private int pendingX;

    public WallClimb(Bridge bridge)
    {
        this.bridge = bridge;
        instance = this;
    }

    public void Install(Harmony harmony, IMonitor monitor)
    {
        this.monitor = monitor;
        try
        {
            var target = AccessTools.Method(
                typeof(MineShaft),
                nameof(MineShaft.checkAction),
                new[] { typeof(xTile.Dimensions.Location), typeof(xTile.Dimensions.Rectangle), typeof(Farmer) });
            if (target == null)
            {
                monitor.Log("Terradew could not find MineShaft.checkAction; the wall will not block the mine ladder.", LogLevel.Warn);
                return;
            }
            if (target.DeclaringType != typeof(MineShaft))
            {
                monitor.Log($"Terradew found checkAction on {target.DeclaringType?.Name}, not MineShaft; the wall will not block the mine ladder.", LogLevel.Warn);
                return;
            }
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(WallClimb), nameof(CheckActionPrefix)));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not hook the mine ladder; the wall will not block it. {error.Message}", LogLevel.Warn);
        }
    }

    /// <summary>Forgets a ladder climb that has not landed yet, when the farmer leaves the game.</summary>
    public void Reset()
    {
        pendingFloor = null;
    }

    /// <summary>Stops the elevator while the wall hunts the farmer.</summary>
    public void OnMenuChanged(IClickableMenu? menu)
    {
        WallFight? wall = bridge.Wall;
        if (menu is not MineElevatorMenu || wall == null || !Context.IsWorldReady || Game1.currentLocation == null)
            return;
        // The wall only blocks the floor it is on.
        if (wall.PlaceKey != Maps.PlaceOf(Game1.currentLocation).Key)
            return;
        menu.exitThisMenu(playSound: false);
        if (Game1.activeClickableMenu == menu)
            Game1.activeClickableMenu = null;
        Game1.showRedMessage("Not while the wall hunts you!");
    }

    /// <summary>Puts the farmer on the southernmost safe tile of the floor they just climbed to.</summary>
    public void OnWarped(WarpedEventArgs e)
    {
        if (pendingFloor is not int floor || !e.IsLocalPlayer)
            return;
        if (e.NewLocation is not MineShaft shaft || shaft.mineLevel != floor)
            return;
        pendingFloor = null;
        Vector2? spot = SouthernmostSafeTile(shaft, pendingX);
        if (spot == null)
        {
            monitor?.Log($"Terradew found no safe tile on mine floor {floor}; the farmer keeps the default spot.", LogLevel.Warn);
            return;
        }
        Game1.player.Position = new Vector2(spot.Value.X * Game1.tileSize, spot.Value.Y * Game1.tileSize);
    }

    private static bool CheckActionPrefix(MineShaft __instance, xTile.Dimensions.Location tileLocation, xTile.Dimensions.Rectangle viewport, Farmer who, ref bool __result)
    {
        if (instance == null)
            return true;
        try
        {
            return instance.OnLadderAction(__instance, tileLocation, who, ref __result);
        }
        catch (Exception error)
        {
            instance.monitor?.Log($"Terradew could not handle the mine ladder. {error.Message}", LogLevel.Warn);
            return true;
        }
    }

    /// <summary>Returns false when the ladder was handled here and vanilla should be skipped.</summary>
    private bool OnLadderAction(MineShaft shaft, xTile.Dimensions.Location tileLocation, Farmer who, ref bool result)
    {
        WallFight? wall = bridge.Wall;
        if (who != Game1.player || wall == null || wall.PlaceKey != Maps.PlaceOf(shaft).Key)
            return true;
        if (shaft.mineLevel < BlockedFloor || shaft.mineLevel > LastFloor)
            return true;
        var tile = new Vector2(tileLocation.X, tileLocation.Y);
        if (shaft.getTileIndexAt(new Point(tileLocation.X, tileLocation.Y), "Buildings") != LadderUpTile)
            return true;
        if (Vector2.Distance(tile, who.Tile) > LadderReachTiles)
            return true;

        result = true;
        if (shaft.mineLevel == BlockedFloor)
        {
            Game1.showRedMessage("The wall blocks the way up!");
            return false;
        }
        pendingFloor = shaft.mineLevel - 1;
        pendingX = (int)who.Tile.X;
        Game1.enterMine(shaft.mineLevel - 1);
        return false;
    }

    private static Vector2? SouthernmostSafeTile(GameLocation location, int preferredX)
    {
        var layer = location.Map.Layers[0];
        int width = layer.LayerWidth;
        for (int y = layer.LayerHeight - 1; y >= 0; y--)
        {
            for (int offset = 0; offset < width; offset++)
            {
                foreach (int x in new[] { preferredX + offset, preferredX - offset })
                {
                    if (x < 0 || x >= width)
                        continue;
                    // The same open-floor test Terraria's blocks are built from (see Maps), plus a floor tile under it.
                    if (location.Map.GetLayer("Back")?.Tiles[x, y] != null && !location.isWaterTile(x, y)
                        && !location.IsTileBlockedBy(new Vector2(x, y), Maps.Mask, CollisionMask.None, false))
                        return new Vector2(x, y);
                }
            }
        }
        return null;
    }
}
