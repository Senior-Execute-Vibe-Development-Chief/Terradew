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

/// <summary>
/// Tells Terraria when the farmer breaks a Stardew rock or gathers Stardew wood, so it can give Terraria ores and wood.
/// Those Stardew resource drops are stopped on the ground, so the farmer only ever gets the Terraria version.
/// </summary>
internal sealed class Mining
{
    // Bombs reach about five tiles; anything farther was not broken by the farmer.
    private const float ReachTiles = 6f;
    // A rock breaks on the swing that hits it; a removal this long after a swing was not the farmer's.
    private const int PickWindowTicks = 30;
    // A felled tree drops its wood once it has fallen, a few seconds after the last axe hit.
    private const int ChopWindowTicks = 60 * 10;
    // A hit on an object or a swing drops its loot in the same call, so its window is short.
    private const int HitWindowTicks = 60;

    // The Stardew resource items that are stopped on the ground, by item id.
    // Wood and hardwood give Terraria wood. Rock gives Terraria rock loot, but only when a resource clump dropped it.
    // Quiet items give nothing in their place.
    private enum Drop { Wood, Hardwood, Rock, Quiet }
    private static readonly Dictionary<string, Drop> Drops = new()
    {
        ["388"] = Drop.Wood,
        ["709"] = Drop.Hardwood,
        ["390"] = Drop.Rock, // stone
        ["378"] = Drop.Rock, // copper ore
        ["380"] = Drop.Rock, // iron ore
        ["382"] = Drop.Rock, // coal
        ["384"] = Drop.Rock, // gold ore
        ["386"] = Drop.Rock, // iridium ore
        ["330"] = Drop.Rock, // clay
        ["535"] = Drop.Rock, // geodes
        ["536"] = Drop.Rock,
        ["537"] = Drop.Rock,
        ["749"] = Drop.Rock,
        ["60"] = Drop.Rock, // gems
        ["62"] = Drop.Rock,
        ["64"] = Drop.Rock,
        ["66"] = Drop.Rock,
        ["68"] = Drop.Rock,
        ["70"] = Drop.Rock,
        ["72"] = Drop.Rock,
        ["74"] = Drop.Rock,
        ["80"] = Drop.Rock,
        ["82"] = Drop.Rock,
        ["84"] = Drop.Rock,
        ["86"] = Drop.Rock,
        ["92"] = Drop.Quiet, // sap
        ["292"] = Drop.Quiet, // tree seeds
        ["309"] = Drop.Quiet,
        ["310"] = Drop.Quiet,
        ["311"] = Drop.Quiet,
        ["770"] = Drop.Quiet, // mixed seeds
        ["771"] = Drop.Quiet, // fiber
    };

    private static Mining? instance;
    // Each patch is only trusted once it installs. Without it, gains count as they always did.
    private static bool pickTracked;
    private static bool chopTracked;
    private static int lastPickTick = int.MinValue / 2;
    private static int lastChopTick = int.MinValue / 2;
    // The farmer's last pickaxe swing, a hit on a Stardew object, and a hit on a resource clump (boulder, stump, log).
    private static int lastSwingTick = int.MinValue / 2;
    private static int lastObjectTick = int.MinValue / 2;
    private static int lastClumpTick = int.MinValue / 2;

    private readonly Bridge bridge;
    private IMonitor? monitor;

    public Mining(Bridge bridge)
    {
        instance = this;
        this.bridge = bridge;
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
                // A prefix, so the hit is marked before the tree drops its wood in the same call.
                harmony.Patch(chop, prefix: new HarmonyMethod(typeof(Mining), nameof(BeforeTreeToolAction)));
                chopTracked = true;
            }
            // Large stumps and logs are resource clumps, and they give the hardwood.
            var clump = AccessTools.Method(typeof(ResourceClump), "performToolAction");
            if (clump != null)
            {
                harmony.Patch(clump, prefix: new HarmonyMethod(typeof(Mining), nameof(BeforeTreeToolAction)));
                harmony.Patch(clump, prefix: new HarmonyMethod(typeof(Mining), nameof(BeforeClumpTool)));
            }
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not hook the axe; all wood gained counts as the farmer's. {error.Message}", LogLevel.Warn);
        }
        try
        {
            // The farmer's own bombs break rocks too.
            var explode = AccessTools.Method(typeof(GameLocation), "explode");
            if (explode != null)
            {
                harmony.Patch(explode, postfix: new HarmonyMethod(typeof(Mining), nameof(AfterPickaxe)));
                harmony.Patch(explode, prefix: new HarmonyMethod(typeof(Mining), nameof(BeforeExplode)));
            }
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not hook bombs; rocks the farmer blows up give no Terraria ore. {error.Message}", LogLevel.Warn);
        }
        try
        {
            // Marks each pickaxe swing before its drops land. Without it, Stardew stone from a boulder or a rock could reach the farmer.
            var swing = AccessTools.Method(
                typeof(Pickaxe),
                nameof(Pickaxe.DoFunction),
                new[] { typeof(GameLocation), typeof(int), typeof(int), typeof(int), typeof(Farmer) });
            if (swing == null)
                monitor.Log("Terradew could not find Pickaxe.DoFunction; Stardew rock drops are not stopped.", LogLevel.Warn);
            else
                harmony.Patch(swing, prefix: new HarmonyMethod(typeof(Mining), nameof(BeforePickaxe)));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not mark the pickaxe swing; Stardew rock drops are not stopped. {error.Message}", LogLevel.Warn);
        }
        try
        {
            // Twigs are broken with an axe, and they drop Stardew wood.
            var swing = AccessTools.Method(
                typeof(Axe),
                nameof(Axe.DoFunction),
                new[] { typeof(GameLocation), typeof(int), typeof(int), typeof(int), typeof(Farmer) });
            if (swing == null)
                monitor.Log("Terradew could not find Axe.DoFunction; Stardew twig wood is not stopped.", LogLevel.Warn);
            else
                harmony.Patch(swing, prefix: new HarmonyMethod(typeof(Mining), nameof(BeforeAxe)));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not mark the axe swing; Stardew twig wood is not stopped. {error.Message}", LogLevel.Warn);
        }
        try
        {
            // Stones, ore nodes, twigs, weeds and mine barrels are objects, and a tool hit on one drops its loot.
            var hit = AccessTools.Method(typeof(SObject), "performToolAction", new[] { typeof(Tool) });
            if (hit == null)
                monitor.Log("Terradew could not find Object.performToolAction; Stardew object drops are not stopped.", LogLevel.Warn);
            else
                harmony.Patch(hit, prefix: new HarmonyMethod(typeof(Mining), nameof(BeforeObjectTool)));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not mark object hits; Stardew object drops are not stopped. {error.Message}", LogLevel.Warn);
        }
        // Every Game1 method that drops items on the ground is checked once it has run, whichever overload the game calls.
        var makers = typeof(Game1).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(m => DebrisMakers.Contains(m.Name))
            .ToList();
        if (makers.Count == 0)
            monitor.Log("Terradew could not find Game1's debris methods; Stardew resource drops are not stopped.", LogLevel.Warn);
        foreach (var maker in makers)
        {
            try
            {
                harmony.Patch(maker,
                    prefix: new HarmonyMethod(typeof(Mining), nameof(BeforeDebrisMade)),
                    postfix: new HarmonyMethod(typeof(Mining), nameof(AfterDebrisMade)));
            }
            catch (Exception error)
            {
                monitor.Log($"Terradew could not check Game1.{maker.Name}; its Stardew resource drops are not stopped. {error.Message}", LogLevel.Warn);
            }
        }
    }

    private static readonly string[] DebrisMakers =
    {
        "createObjectDebris", "createMultipleObjectDebris", "createItemDebris", "createRadialDebris",
    };

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

    /// <summary>Notes the tick of each pickaxe swing before it lands, so the drops it makes are recognised as they appear.</summary>
    private static void BeforePickaxe(Farmer who)
    {
        try
        {
            if (who == Game1.player)
                lastSwingTick = (int)Game1.ticks;
        }
        catch (Exception error)
        {
            instance?.monitor?.Log($"Terradew could not mark a pickaxe swing. {error.Message}", LogLevel.Warn);
        }
    }

    /// <summary>Notes the tick of each axe swing before it lands. Twigs, which the axe breaks, drop their wood then.</summary>
    private static void BeforeAxe(Farmer who)
    {
        try
        {
            if (who == Game1.player)
                lastObjectTick = (int)Game1.ticks;
        }
        catch (Exception error)
        {
            instance?.monitor?.Log($"Terradew could not mark an axe swing. {error.Message}", LogLevel.Warn);
        }
    }

    /// <summary>Notes the tick of each pickaxe or axe hit the farmer lands on a Stardew object, before its loot drops.</summary>
    private static void BeforeObjectTool(Tool t)
    {
        try
        {
            if ((t is Pickaxe || t is Axe) && t.getLastFarmerToUse() == Game1.player)
                lastObjectTick = (int)Game1.ticks;
        }
        catch (Exception error)
        {
            instance?.monitor?.Log($"Terradew could not mark an object hit. {error.Message}", LogLevel.Warn);
        }
    }

    /// <summary>Notes the tick of each hit the farmer lands on a resource clump, before its loot drops.</summary>
    private static void BeforeClumpTool(Tool t)
    {
        try
        {
            if (t != null && t.getLastFarmerToUse() == Game1.player)
                lastClumpTick = (int)Game1.ticks;
        }
        catch (Exception error)
        {
            instance?.monitor?.Log($"Terradew could not mark a clump hit. {error.Message}", LogLevel.Warn);
        }
    }

    /// <summary>The farmer's bombs break rocks, so their drops count as the farmer's.</summary>
    private static void BeforeExplode(Farmer who)
    {
        try
        {
            if (who == Game1.player)
                lastObjectTick = (int)Game1.ticks;
        }
        catch (Exception error)
        {
            instance?.monitor?.Log($"Terradew could not mark a bomb. {error.Message}", LogLevel.Warn);
        }
    }

    /// <summary>Notes the tick of each axe hit the farmer lands on a tree, before the tree drops its wood.</summary>
    private static void BeforeTreeToolAction(Tool t)
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

    /// <summary>Notes how much debris was already on the ground, so only what this call drops is checked.</summary>
    private static void BeforeDebrisMade(out int __state)
    {
        __state = Game1.currentLocation?.debris.Count ?? 0;
    }

    /// <summary>
    /// Takes the Stardew resource items that a gather has just put on the ground, so the farmer never picks them up.
    /// Terraria gives its own version in their place. Items already on the ground, and Stardew items from anything
    /// else, are left alone.
    /// </summary>
    private static void AfterDebrisMade(int __state)
    {
        try
        {
            Mining? self = instance;
            GameLocation? location = Game1.currentLocation;
            if (self == null || location == null || !self.bridge.IsConnected)
                return;
            // A clump drops its loot on the hit that breaks it.
            bool clump = Recent(lastClumpTick, HitWindowTicks);
            // The clump was hit last, so its stone is not already counted by the rock path.
            bool clumpLast = clump && lastClumpTick >= lastObjectTick && lastClumpTick >= lastSwingTick;
            bool rock = clump || Recent(lastObjectTick, HitWindowTicks) || Recent(lastSwingTick, HitWindowTicks);
            bool wood = clump || Recent(lastChopTick, ChopWindowTicks) || Recent(lastObjectTick, HitWindowTicks);
            if (!rock && !wood)
                return;
            var totals = new Dictionary<GatherKind, int>();
            // A nested call may already have taken some of this call's debris, so start no later than the end.
            int first = Math.Min(Math.Max(0, __state), location.debris.Count);
            foreach (Debris debris in location.debris.Skip(first).ToList())
            {
                Item? item = debris.item;
                if (item == null || !Drops.TryGetValue(item.ItemId, out Drop drop))
                    continue;
                bool gathering = drop switch
                {
                    Drop.Wood => wood,
                    Drop.Hardwood => clump || Recent(lastChopTick, ChopWindowTicks),
                    Drop.Rock => rock,
                    _ => wood || rock,
                };
                if (!gathering)
                    continue;
                location.debris.Remove(debris);
                GatherKind? kind = drop switch
                {
                    Drop.Wood => GatherKind.Wood,
                    Drop.Hardwood => GatherKind.Hardwood,
                    Drop.Rock when clumpLast => RockKindOf(item.ItemId),
                    _ => null,
                };
                if (kind is GatherKind gathered)
                    totals[gathered] = totals.GetValueOrDefault(gathered) + Math.Max(1, item.Stack);
            }
            if (totals.Count == 0)
                return;
            string placeKey = Maps.PlaceOf(location).Key;
            int mineLevel = location is MineShaft shaft && shaft.mineLevel < Places.QuarryMineLevel ? shaft.mineLevel : 0;
            foreach (var (kind, count) in totals)
                self.bridge.Send(new Gathered { Kind = kind, PlaceKey = placeKey, MineLevel = mineLevel, Count = count });
        }
        catch (Exception error)
        {
            instance?.monitor?.Log($"Terradew could not stop a Stardew resource drop. {error.Message}", LogLevel.Warn);
        }
    }

    /// <summary>Notes that a bomb went off in the farmer's place, so the Stardew drops it makes are the farmer's.</summary>
    public static void NoteBlast() => lastObjectTick = (int)Game1.ticks;

    /// <summary>Forgets the last swing and chop, so nothing from the last save counts on this one.</summary>
    public void Reset()
    {
        lastPickTick = int.MinValue / 2;
        lastChopTick = int.MinValue / 2;
        lastSwingTick = int.MinValue / 2;
        lastObjectTick = int.MinValue / 2;
        lastClumpTick = int.MinValue / 2;
    }

    private static bool RecentPick => !pickTracked || (long)Game1.ticks - lastPickTick is >= 0 and <= PickWindowTicks;

    private static bool RecentChop => !chopTracked || (long)Game1.ticks - lastChopTick is >= 0 and <= ChopWindowTicks;

    /// <summary>True when the tick was within the window, counted in game ticks up to now.</summary>
    private static bool Recent(int tick, int window)
    {
        long age = (long)Game1.ticks - tick;
        return age >= 0 && age <= window;
    }

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

    /// <summary>The Terraria rock that gives a Stardew ore or gem dropped by a clump.</summary>
    private static GatherKind RockKindOf(string itemId) => itemId switch
    {
        "378" => GatherKind.CopperNode,
        "380" => GatherKind.IronNode,
        "384" => GatherKind.GoldNode,
        "386" => GatherKind.IridiumNode,
        "60" or "62" or "64" or "66" or "68" or "70" or "72" or "74" or "80" or "82" or "84" or "86" => GatherKind.GemNode,
        _ => GatherKind.Stone,
    };
}
