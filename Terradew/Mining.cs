using Microsoft.Xna.Framework;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using Terradew.Core.Protocol;
using Terradew.Core.World;
using SObject = StardewValley.Object;

namespace Terradew;

/// <summary>Tells Terraria when the farmer breaks a Stardew rock or gathers Stardew wood, so it can give Terraria ores and wood.</summary>
internal sealed class Mining
{
    // Bombs reach about five tiles; anything farther was not broken by the farmer.
    private const float ReachTiles = 6f;

    public void OnObjectListChanged(ObjectListChangedEventArgs e, Bridge bridge)
    {
        if (!bridge.IsConnected || e.Location != Game1.currentLocation)
            return;
        Vector2 farmer = Game1.player.Tile;
        string placeKey = Maps.PlaceOf(e.Location).Key;
        int mineLevel = e.Location is MineShaft shaft && shaft.mineLevel < Places.QuarryMineLevel ? shaft.mineLevel : 0;
        foreach (var (tile, obj) in e.Removed)
        {
            if (Vector2.Distance(tile, farmer) > ReachTiles || !obj.IsBreakableStone())
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
        if (kind == null)
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
