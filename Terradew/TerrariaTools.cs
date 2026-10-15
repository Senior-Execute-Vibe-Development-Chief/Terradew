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
    /// <summary>Ticks the use button has been held with no Terraria swing seen, and when that was last logged.</summary>
    private int unansweredTicks;
    private int lastUnansweredLog = -100000;
    private int ticks;
    private Pickaxe? pickaxe;
    private Axe? axe;
    private MeleeWeapon? scythe;
    /// <summary>Ticks since the last strike of a channelled tool that is still held down.</summary>
    private int channelTicks;
    // Terraria's ItemUseStyleID.Shoot: drills, chainsaws and jackhammers use it, and their held projectile keeps the animation pinned.
    private const int ChannelStyle = 5;
    // Most drills and chainsaws use the item about every seven to thirteen ticks in Terraria; a strike lands this often while held.
    private const int ChannelTicks = 9;

    public TerrariaTools(IMonitor monitor, Hand hand)
    {
        this.monitor = monitor;
        this.hand = hand;
    }

    public void Reset()
    {
        lastAnimation = 0;
        channelTicks = 0;
    }

    public void Update(Bridge bridge)
    {
        WorldState? world = bridge.World;
        InventoryState? inventory = bridge.Inventory;
        Farmer player = Game1.player;
        int animation = world != null && world.Held.ItemType > 0 ? world.Held.Animation : 0;
        // A use starts when the animation jumps up: from rest, or a new swing right after the last one.
        bool started = animation > lastAnimation;
        lastAnimation = animation;
        // A channelled tool holds its animation at a fixed value while the button is down, so it never jumps again. It strikes on a timer instead.
        bool channelling = world != null && world.Held.UseStyle == ChannelStyle && hand.UseHeld && animation > 0;
        if (!channelling)
            channelTicks = 0;
        WatchForNoSwing(bridge, world, inventory, animation);
        if ((!started && !channelling) || world == null || inventory == null || !hand.Active || !Context.IsPlayerFree || player.currentLocation == null)
            return;
        if (hand.Slot >= inventory.Items.Count)
            return;
        ItemStack held = inventory.Items[hand.Slot];
        if (held.IsEmpty || held.ItemType != world.Held.ItemType)
        {
            if (started)
                monitor.Log($"Terradew: Terraria swung item {world.Held.ItemType} but Stardew's slot {hand.Slot + 1} holds {(held.IsEmpty ? "nothing" : held.Name)}; nothing happens in Stardew.", LogLevel.Info);
            return;
        }
        if (held.Pick == 0 && held.Hammer == 0 && held.Axe == 0 && !held.Blade)
            return;
        if (channelling && ++channelTicks >= ChannelTicks)
            channelTicks = 0;
        else if (channelling)
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

    /// <summary>
    /// The use button is held but no swing comes back from Terraria: logs what Stardew knows, at most every ten seconds.
    /// Terraria reports its own side of it when it can (see TerradewBridge.Puppet).
    /// </summary>
    private void WatchForNoSwing(Bridge bridge, WorldState? world, InventoryState? inventory, int animation)
    {
        ticks++;
        if (!hand.UseHeld || animation > 0)
        {
            unansweredTicks = 0;
            return;
        }
        if (++unansweredTicks != 60 || ticks - lastUnansweredLog < 600)
            return;
        lastUnansweredLog = ticks;
        string place = Game1.player.currentLocation == null ? "nowhere" : Maps.PlaceOf(Game1.player.currentLocation).Key;
        string why;
        if (!bridge.IsConnected)
            why = "Terraria is not connected";
        else if (!bridge.Linked || world == null)
            why = "Terraria is connected but sends no world (is the world named Stardew Valley open, with both mods rebuilt?)";
        else if (world.PlaceKey != place)
            why = $"Terraria is in place {world.PlaceKey}, the farmer in {place}";
        else if (inventory == null || hand.Slot >= inventory.Items.Count || inventory.Items[hand.Slot].IsEmpty)
            why = $"Terraria's inventory has nothing in slot {hand.Slot + 1}";
        else
            why = $"Terraria holds item {world.Held.ItemType} and does not swing {inventory.Items[hand.Slot].Name}";
        monitor.Log($"Terradew: the use button is held but Terraria does not swing: {why}.", LogLevel.Info);
        Game1.addHUDMessage(new HUDMessage($"No Terraria swing: {why}", HUDMessage.error_type));
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
