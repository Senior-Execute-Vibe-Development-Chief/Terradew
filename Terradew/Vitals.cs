using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Monsters;
using StardewValley.Tools;
using Terradew.Core;
using Terradew.Core.Economy;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>
/// Terraria owns health. Stardew's health stays full; damage Stardew deals the farmer (bombs, traps)
/// is passed to Terraria, Terraria's hits are shown with Stardew's hurt feedback, food heals the Terraria character, and Stardew monsters and weapons are retired.
/// </summary>
internal sealed class Vitals
{
    private readonly IMonitor monitor;
    private int lastHealth = -1;
    private bool wasEating;
    private int lastLife = -1;
    private Vector2 knock;
    private int knockTicks;

    public Vitals(IMonitor monitor)
    {
        this.monitor = monitor;
    }

    public void Reset()
    {
        lastHealth = -1;
        wasEating = false;
        lastLife = -1;
        knockTicks = 0;
    }

    public void Update(Bridge bridge, ModConfig config)
    {
        Farmer player = Game1.player;
        if (lastHealth >= 0 && player.health < lastHealth && bridge.Linked)
            bridge.Send(new Hurt { Amount = lastHealth - player.health });
        if (bridge.Linked && player.health < player.maxHealth)
            player.health = player.maxHealth;
        lastHealth = player.health;

        FeelTerrariaHits(bridge, player);
        Knockback(player);

        // Energy is gone: the cap stays at the base, so Stardrops and friends do not grow the vanilla bar,
        // and it is refilled every tick so tools never tire you and exhaustion never sets in.
        if (bridge.Linked)
        {
            player.maxStamina.Value = 270;
            player.Stamina = player.MaxStamina;
            player.exhausted.Value = false;
        }

        if (player.isEating && !wasEating && player.itemToEat is StardewValley.Object food)
        {
            int life = Math.Max(0, food.healthRecoveredOnConsumption());
            int mana = Math.Max(0, food.staminaRecoveredOnConsumption() / 4);
            int wellFed = food.Edibility > 0 ? Math.Clamp(food.Edibility * 6, 0, 3600) : 0;
            if (life > 0 || mana > 0 || wellFed > 0)
                bridge.Send(new Heal { Life = life, Mana = mana, WellFedSeconds = wellFed });
        }
        wasEating = player.isEating;

        if (config.RemoveStardewMonsters && bridge.Linked)
            RemoveMonsters(player.currentLocation);
    }

    /// <summary>
    /// Terraria owns health, so its hits never touch Stardew's. When the Terraria life drops, the farmer still gets
    /// Stardew's hurt feedback: the blink, the red number, the "ow", and a shove away from whatever hit them.
    /// </summary>
    private void FeelTerrariaHits(Bridge bridge, Farmer player)
    {
        WorldState? world = bridge.World;
        if (!bridge.Linked || world == null)
        {
            lastLife = -1;
            return;
        }
        int life = world.Life;
        int lost = lastLife < 0 ? 0 : lastLife - life;
        lastLife = life;
        if (lost <= 0 || world.Dead || Game1.eventUp || player.currentLocation == null)
            return;
        try
        {
            // takeDamage shows everything, and is skipped while the farmer still blinks from the last hit.
            // It must never reach zero here, and the health it takes is put straight back, so it is not sent on.
            int shown = Math.Clamp(lost, 1, Math.Max(1, player.health - 1));
            bool blinking = player.temporarilyInvincible;
            player.takeDamage(shown, true, null);
            player.health = player.maxHealth;
            lastHealth = player.health;
            if (!blinking)
                StartKnock(world, player);
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not show a Terraria hit. {error.Message}", LogLevel.Trace);
        }
    }

    /// <summary>Pushes the farmer away from the nearest hostile creature or shot.</summary>
    private void StartKnock(WorldState world, Farmer player)
    {
        Vector2 feet = player.getStandingPosition();
        Vector2? nearest = null;
        float best = 64f * 6;
        void Consider(float x, float y)
        {
            (float mx, float my) = WorldProjection.ToMap(world.RegionX, world.RegionY, x, y);
            var at = new Vector2(mx, my);
            float distance = Vector2.Distance(at, feet);
            if (distance < best)
            {
                best = distance;
                nearest = at;
            }
        }
        foreach (EntityState entity in world.Entities)
        {
            if (entity.Has(EntityFlags.Hostile))
                Consider(entity.X, entity.Y);
        }
        foreach (ProjectileState shot in world.Projectiles)
        {
            if (shot.Hostile)
                Consider(shot.X, shot.Y);
        }
        Vector2 away = nearest is Vector2 from && from != feet ? feet - from : player.FacingDirection switch
        {
            0 => new Vector2(0, 1),
            1 => new Vector2(-1, 0),
            2 => new Vector2(0, -1),
            _ => new Vector2(1, 0),
        };
        away.Normalize();
        knock = away * 8f;
        knockTicks = 8;
    }

    private void Knockback(Farmer player)
    {
        if (knockTicks <= 0 || player.currentLocation == null)
            return;
        knockTicks--;
        Rectangle box = player.GetBoundingBox();
        box.Offset((int)knock.X, (int)knock.Y);
        if (!player.currentLocation.isCollidingPosition(box, Game1.viewport, true, 0, false, player))
            player.Position += knock;
        else
            knockTicks = 0;
        knock *= 0.8f;
    }

    private static void RemoveMonsters(GameLocation? location)
    {
        if (location == null || location is SlimeHutch)
            return;
        for (int i = location.characters.Count - 1; i >= 0; i--)
        {
            if (location.characters[i] is Monster)
                location.characters.RemoveAt(i);
        }
    }

    /// <summary>Stardew weapons melt into coins the moment they reach the inventory. Only Terraria weapons fight.</summary>
    public void OnItemsAdded(IEnumerable<Item> added)
    {
        Farmer player = Game1.player;
        foreach (Item item in added.ToList())
        {
            bool weapon = item is Slingshot || (item is MeleeWeapon melee && !melee.isScythe());
            if (!weapon)
                continue;
            int value = Math.Max(10, item.salePrice());
            player.removeItemFromInventory(item);
            player.Money += value;
            Game1.addHUDMessage(new HUDMessage($"{item.DisplayName} melted into {Coins.Format(Coins.FromStardew(value))}. Only Terraria weapons fight here.", HUDMessage.newQuest_type));
        }
    }

    /// <summary>Terraria killed the farmer: wake up in your own bed.</summary>
    public void OnDied(PlayerDied died, Bridge bridge)
    {
        Game1.addHUDMessage(new HUDMessage(died.Reason, HUDMessage.error_type));
        knockTicks = 0;
        FarmHouse home = Utility.getHomeOfFarmer(Game1.player);
        Point bed = home.GetPlayerBedSpot();
        Game1.warpFarmer(home.NameOrUniqueName, bed.X, bed.Y, 2);
        monitor.Log($"Terraria death: {died.Reason}", LogLevel.Info);
    }
}
