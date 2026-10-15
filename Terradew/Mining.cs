using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using Terradew.Core.Protocol;
using Terradew.Core.World;
using SObject = StardewValley.Object;

namespace Terradew;

/// <summary>Tells Terraria when the farmer breaks a Stardew rock or gathers Stardew wood, so it can give Terraria ores and wood.</summary>
internal sealed class Mining
{
    // Bombs reach about five tiles; anything farther was not broken by the farmer.
    private const float ReachTiles = 6f;
    // A rock breaks on the swing that hits it; a removal this long after a swing was not the farmer's.
    private const int PickWindowTicks = 30;
    // A felled tree drops its wood a few seconds later, and the farmer picks that up afterwards.
    private const int ChopWindowTicks = 60 * 15;

    private static Mining? instance;
    // Each patch is only trusted once it installs. Without it, gains count as they always did.
    private static bool pickTracked;
    private static bool chopTracked;
    private static int lastPickTick = int.MinValue / 2;
    private static int lastChopTick = int.MinValue / 2;

    private IMonitor? monitor;

    public Mining()
    {
        instance = this;
    }

    public void Install(Harmony harmony, IMonitor monitor)
    {
        this.monitor = monitor;
        try
        {
            var pick = AccessTools.Method(
                typeof(Pickaxe),
                nameof(Pickaxe.DoFunction),
                new[] { typeof(GameLocation), typeof(int), typeof(int), typeof(int), typeof(Farmer) });
            if (pick == null)
            {
                monitor.Log("Terradew could not find Pickaxe.DoFunction; every rock broken counts as the farmer's.", LogLevel.Warn);
            }
            else
            {
                harmony.Patch(pick, postfix: new HarmonyMethod(typeof(Mining), nameof(AfterPickaxe)));
                pickTracked = true;
            }
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not hook the pickaxe; every rock broken counts as the farmer's. {error.Message}", LogLevel.Warn);
        }
        try
        {
            var chop = AccessTools.Method(typeof(Tree), "performToolAction");
            if (chop == null)
            {
                monitor.Log("Terradew could not find Tree.performToolAction; all wood gained counts as the farmer's.", LogLevel.Warn);
            }
            else
            {
                harmony.Patch(chop, postfix: new HarmonyMethod(typeof(Mining), nameof(AfterTreeToolAction)));
                chopTracked = true;
            }
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not hook the axe; all wood gained counts as the farmer's. {error.Message}", LogLevel.Warn);
        }
    }

    /// <summary>Notes the tick of each pickaxe swing the farmer makes.</summary>
    private static void AfterPickaxe(Farmer who)
    {
        try
        {
            if (who == Game1.player)
                lastPickTick = (int)Game1.ticks;
        }
        catch (Exception error)
        {
            instance?.monitor?.Log($"Terradew could not note a pickaxe swing. {error.Message}", LogLevel.Warn);
        }
    }

    /// <summary>Notes the tick of each axe hit the farmer lands on a tree.</summary>
    private static void AfterTreeToolAction(Tool t)
    {
        try
        {
            if (t is Axe && t.getLastFarmerToUse() == Game1.player)
                lastChopTick = (int)Game1.ticks;
        }
        catch (Exception error)
        {
            instance?.monitor?.Log($"Terradew could not note an axe hit. {error.Message}", LogLevel.Warn);
        }
    }

    private static bool RecentPick => !pickTracked || (int)Game1.ticks - lastPickTick <= PickWindowTicks;

    private static bool RecentChop => !chopTracked || (int)Game1.ticks - lastChopTick <= ChopWindowTicks;

    public void OnObjectListChanged(ObjectListChangedEventArgs e, Bridge bridge)
    {
        if (!bridge.IsConnected || e.Location != Game1.currentLocation)
            return;
        Vector2 farmer = Game1.player.Tile;
        string placeKey = Maps.PlaceOf(e.Location).Key;
        int mineLevel = e.Location is MineShaft shaft && shaft.mineLevel < Places.QuarryMineLevel ? shaft.mineLevel : 0;
        foreach (var (tile, obj) in e.Removed)
        {
            if (Vector2.Distance(tile, farmer) > ReachTiles || !obj.IsBreakableStone() || !RecentPick)
                continue;
            bridge.Send(new Gathered { Kind = KindOf(obj), PlaceKey = placeKey, MineLevel = mineLevel, Count = 1 });
        }
    }

    public void OnInventoryChanged(IEnumerable<Item> added, IEnumerable<ItemStackSizeChange> changed, Bridge bridge)
    {
        // Wood bought from a shop or taken from a chest arrives with a menu open; only count what was chopped.
        if (!bridge.IsConnected || Game1.activeClickableMenu != null)
            return;
        foreach (Item item in added)
            Send(item, item.Stack, bridge);
        foreach (ItemStackSizeChange change in changed)
            Send(change.Item, change.NewSize - change.OldSize, bridge);
    }

    private static void Send(Item item, int gained, Bridge bridge)
    {
        if (gained <= 0)
            return;
        GatherKind? kind = item.QualifiedItemId switch
        {
            "(O)388" => GatherKind.Wood,
            "(O)709" => GatherKind.Hardwood,
            _ => null,
        };
        if (kind == null || !RecentChop)
            return;
        bridge.Send(new Gathered { Kind = kind.Value, PlaceKey = Maps.PlaceOf(Game1.currentLocation).Key, Count = gained });
    }

    private static GatherKind KindOf(SObject obj) => obj.ItemId switch
    {
        "751" or "849" => GatherKind.CopperNode,
        "290" or "850" => GatherKind.IronNode,
        "764" => GatherKind.GoldNode,
        "765" => GatherKind.IridiumNode,
        "2" or "4" or "6" or "8" or "10" or "12" or "14" or "44" => GatherKind.GemNode,
        "46" => GatherKind.MysticStone,
        _ => GatherKind.Stone,
    };
}
