using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;
using Terradew.Core.Fishing;
using Terradew.Core.Protocol;
using Terradew.Core.World;

namespace Terradew;

/// <summary>
/// Stardew's fishing, Terraria's catch. When a fish bites, the catch is rolled from Terraria's tables for
/// this place's biome and depth, and it sets how hard the minigame is. Whatever Stardew fish lands in the
/// inventory is swapped for the Terraria catch.
/// </summary>
internal sealed class Fishing
{
    private readonly IModHelper helper;
    private readonly Random random = new();
    private FishCatch? pending;
    private uint pendingUntil;

    public Fishing(IModHelper helper)
    {
        this.helper = helper;
    }

    public void OnMenuChanged(IClickableMenu? menu, Bridge bridge, Maps maps)
    {
        if (menu is not BobberBar bar || Game1.player.CurrentTool is not FishingRod rod)
            return;
        Place place = Maps.PlaceOf(Game1.player.currentLocation);
        int rodTier = Math.Clamp(rod.UpgradeLevel - 1, 0, 3);
        bool bait = rod.attachments.Count > 0 && rod.attachments[0] != null;
        bool tackle = rod.attachments.Count > 1 && rod.attachments[1] != null;
        var context = new FishContext(
            place.Biome,
            place.Band,
            Game1.timeOfDay >= 1930 || Game1.timeOfDay < 430,
            bridge.Facts?.Hardmode ?? false,
            FishTable.PowerFromStardew(rodTier, Game1.player.FishingLevel, bait, tackle),
            Math.Max(maps.Current?.WaterTiles ?? 0, 200));
        FishCatch fish = FishTable.Roll(context, random);
        pending = fish;
        pendingUntil = Game1.ticks + 60 * 60;
        helper.Reflection.GetField<float>(bar, "difficulty", required: false)?.SetValue(FishTable.Difficulty(fish.Rarity));
        helper.Reflection.GetField<int>(bar, "motionType", required: false)?.SetValue(FishTable.Motion(fish.Rarity));
    }

    /// <summary>Swaps the Stardew catch for the Terraria one.</summary>
    public void OnInventoryChanged(IEnumerable<Item> added, IEnumerable<ItemStackSizeChange> changed, Bridge bridge)
    {
        if (pending == null)
            return;
        if (Game1.ticks > pendingUntil)
        {
            pending = null;
            return;
        }
        foreach (Item item in added)
        {
            if (!IsCatch(item))
                continue;
            Game1.player.removeItemFromInventory(item);
            Deliver(bridge);
            return;
        }
        foreach (ItemStackSizeChange change in changed)
        {
            int gained = change.NewSize - change.OldSize;
            if (gained <= 0 || !IsCatch(change.Item))
                continue;
            change.Item.Stack -= gained;
            if (change.Item.Stack <= 0)
                Game1.player.removeItemFromInventory(change.Item);
            Deliver(bridge);
            return;
        }
    }

    private static bool IsCatch(Item item)
        => Game1.player.CurrentTool is FishingRod && (item.Category == StardewValley.Object.FishCategory || item.Category == StardewValley.Object.junkCategory);

    private void Deliver(Bridge bridge)
    {
        FishCatch fish = pending!.Value;
        pending = null;
        bridge.Send(new GiveItem { Name = fish.ItemName, Stack = fish.Stack });
        string what = fish.Crate ? "a crate" : fish.ItemName;
        Game1.addHUDMessage(new HUDMessage($"Caught {Pretty(what)} (Terraria, {fish.Rarity})", HUDMessage.newQuest_type));
    }

    private static string Pretty(string internalName)
    {
        var text = new System.Text.StringBuilder();
        foreach (char c in internalName)
        {
            if (char.IsUpper(c) && text.Length > 0)
                text.Append(' ');
            text.Append(c);
        }
        return text.ToString();
    }
}
