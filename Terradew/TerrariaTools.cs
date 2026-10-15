using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Tools;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>
/// A Terraria tool in the hand works on Stardew things too. Each Terraria use of a pickaxe or hammer strikes the
/// tile in front of the farmer with a Stardew pickaxe, an axe strikes it with a Stardew axe, and a blade cuts weeds and
/// grass like the scythe. Better Terraria tools stand for better Stardew upgrades.
/// </summary>
internal sealed class TerrariaTools
{
    private readonly IMonitor monitor;
    private readonly Hand hand;
    private int lastAnimation;
    private Pickaxe? pickaxe;
    private Axe? axe;
    private MeleeWeapon? scythe;

    public TerrariaTools(IMonitor monitor, Hand hand)
    {
        this.monitor = monitor;
        this.hand = hand;
    }

    public void Reset() => lastAnimation = 0;

    public void Update(Bridge bridge)
    {
        WorldState? world = bridge.World;
        InventoryState? inventory = bridge.Inventory;
        Farmer player = Game1.player;
        int animation = world != null && world.Held.ItemType > 0 ? world.Held.Animation : 0;
        // A use starts when the animation jumps up: from rest, or a new swing right after the last one.
        bool started = animation > lastAnimation;
        lastAnimation = animation;
        if (!started || world == null || inventory == null || !hand.Active || !Context.IsPlayerFree || player.currentLocation == null)
            return;
        if (hand.Slot >= inventory.Items.Count)
            return;
        ItemStack held = inventory.Items[hand.Slot];
        if (held.IsEmpty || held.ItemType != world.Held.ItemType)
            return;
        try
        {
            Strike(player, held);
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not use {held.Name} on Stardew. {error.Message}", LogLevel.Trace);
        }
    }

    private void Strike(Farmer player, ItemStack held)
    {
        GameLocation location = player.currentLocation;
        Vector2 spot = player.GetToolLocation();
        int x = (int)spot.X;
        int y = (int)spot.Y;
        int breaking = Math.Max(held.Pick, held.Hammer);
        if (breaking > 0)
        {
            pickaxe ??= new Pickaxe();
            pickaxe.UpgradeLevel = Level(breaking, 55, 100, 150, 200);
            pickaxe.DoFunction(location, x, y, 1, player);
        }
        if (held.Axe > 0)
        {
            axe ??= new Axe();
            axe.UpgradeLevel = Level(held.Axe, 50, 75, 110, 160);
            axe.DoFunction(location, x, y, 1, player);
        }
        if (held.Blade && breaking == 0 && held.Axe == 0)
        {
            // Stardew's own scythe, kept out of the inventory.
            scythe ??= new MeleeWeapon("47");
            scythe.DoDamage(location, x, y, player.FacingDirection, 1, player);
        }
    }

    /// <summary>A Terraria tool power as a Stardew upgrade level: basic, copper, steel, gold, iridium.</summary>
    private static int Level(int power, int copper, int steel, int gold, int iridium)
        => power >= iridium ? 4 : power >= gold ? 3 : power >= steel ? 2 : power >= copper ? 1 : 0;
}
