using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Monsters;
using StardewValley.Tools;
using Terradew.Core.Economy;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>
/// Terraria owns health. Stardew's health stays full; damage Stardew deals the farmer (bombs, traps)
/// is passed to Terraria, food heals the Terraria character, and Stardew monsters and weapons are retired.
/// </summary>
internal sealed class Vitals
{
    private readonly IMonitor monitor;
    private int lastHealth = -1;
    private bool wasEating;

    public Vitals(IMonitor monitor)
    {
        this.monitor = monitor;
    }

    public void Reset()
    {
        lastHealth = -1;
        wasEating = false;
    }

    public void Update(Bridge bridge, ModConfig config)
    {
        Farmer player = Game1.player;
        if (lastHealth >= 0 && player.health < lastHealth && bridge.Linked)
            bridge.Send(new Hurt { Amount = lastHealth - player.health });
        if (bridge.Linked && player.health < player.maxHealth)
            player.health = player.maxHealth;
        lastHealth = player.health;

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

    /// <summary>Terraria killed the farmer: wake up with the Nurse if one lives on the farm, otherwise at home.</summary>
    public void OnDied(PlayerDied died, Bridge bridge)
    {
        Game1.addHUDMessage(new HUDMessage(died.Reason, HUDMessage.error_type));
        TownResident nurse = bridge.Facts?.Residents.FirstOrDefault(r => r.Name.Length > 0 && r.NpcType == 18 && r.HomePlaceKey.Length > 0) ?? default;
        GameLocation? clinic = nurse.HomePlaceKey == null ? null : Game1.getLocationFromName(nurse.HomePlaceKey);
        if (clinic != null && clinic.warps.Count > 0)
        {
            Warp door = clinic.warps[0];
            Game1.warpFarmer(clinic.NameOrUniqueName, door.X, Math.Max(1, door.Y - 2), 0);
            return;
        }
        FarmHouse home = Utility.getHomeOfFarmer(Game1.player);
        Point bed = home.GetPlayerBedSpot();
        Game1.warpFarmer(home.NameOrUniqueName, bed.X, bed.Y, 2);
        monitor.Log($"Terraria death: {died.Reason}", LogLevel.Info);
    }
}
