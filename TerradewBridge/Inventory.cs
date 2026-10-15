using System;
using System.Collections.Generic;
using Terradew.Core.Protocol;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace TerradewBridge;

/// <summary>
/// The Terraria character's inventory is the farmer's combat inventory. Terraria keeps it, saves it with
/// the character, and stacks coins in it; Stardew shows it and sends actions back.
/// </summary>
public static class Inventory
{
    private static long lastHash = long.MinValue;
    private static int lastSent = -10_000;
    private static int lastCoinSeq;

    /// <summary>Ticks to hold the use button for an item used from the menu.</summary>
    public static int PendingUse;

    public static void ForceResend()
    {
        lastHash = long.MinValue;
        lastSent = -10_000;
    }

    /// <summary>Stardew said hello: carry on from the last coin change it saw confirmed.</summary>
    public static void ResumeCoins(int seq) => lastCoinSeq = Math.Max(lastCoinSeq, seq);

    public static long Coins(Player player)
    {
        long total = Utils.CoinsCount(out _, player.inventory);
        return Math.Max(0, total);
    }

    public static void Publish(Link link, Player player, int tick)
    {
        long hash = Hash(player);
        if (hash == lastHash && tick - lastSent < 300)
            return;
        lastHash = hash;
        lastSent = tick;
        var state = new InventoryState
        {
            Coins = Coins(player),
            HeadSlot = player.head,
            BodySlot = player.body,
            LegSlot = player.legs,
            Male = player.Male,
            WingsSlot = player.wings,
            ShieldSlot = player.shield,
            BackSlot = player.back,
            FrontSlot = player.front,
            ShoeSlot = player.shoe,
        };
        for (int i = 0; i < InventoryState.InventorySlots && i < player.inventory.Length; i++)
            state.Items.Add(Describe(player.inventory[i]));
        for (int i = 0; i < InventoryState.EquipmentSlots && i < player.armor.Length; i++)
            state.Equipment.Add(Describe(player.armor[i]));
        link.Send(state);
        link.Send(new CoinAck { Seq = lastCoinSeq, Total = state.Coins });
        ExportGear(player);
    }

    private static void ExportGear(Player player)
    {
        SpriteDump.Head(player.head);
        SpriteDump.Body(player.body, player.Male);
        SpriteDump.Legs(player.legs);
        SpriteDump.Wings(player.wings);
        SpriteDump.Shield(player.shield);
        SpriteDump.Back(player.back);
        SpriteDump.Front(player.front);
        SpriteDump.Shoe(player.shoe);
    }

    public static ItemStack Describe(Item? item)
    {
        if (item == null || item.IsAir)
            return new ItemStack { Name = "" };
        SpriteDump.Item(item.type);
        return new ItemStack
        {
            ItemType = item.type,
            Stack = item.stack,
            Prefix = item.prefix,
            Name = item.AffixName(),
            Damage = item.damage,
            Equippable = item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0 || item.accessory || item.vanity,
            Consumable = item.consumable,
        };
    }

    private static long Hash(Player player)
    {
        long hash = 17;
        foreach (Item item in player.inventory)
            hash = unchecked(hash * 31 + item.type * 7919L + item.stack * 31L + item.prefix);
        foreach (Item item in player.armor)
            hash = unchecked(hash * 31 + item.type * 7919L + item.stack);
        hash = unchecked(hash * 31 + player.head * 13 + player.body * 17 + player.legs * 19);
        hash = unchecked(hash * 31 + player.wings * 23 + player.shield * 29 + player.back * 37 + player.front * 41 + player.shoe * 43);
        return hash;
    }

    /// <summary>Stardew earned or spent money. Positive adds coins, negative pays coins.</summary>
    public static void AdjustCoins(Link link, Player player, AdjustCoins message)
    {
        if (message.Seq <= lastCoinSeq && message.Seq != 0)
        {
            link.Send(new CoinAck { Seq = lastCoinSeq, Total = Coins(player) });
            return;
        }
        if (message.Delta > 0)
        {
            AddCoins(player, message.Delta);
        }
        else if (message.Delta < 0)
        {
            long have = Coins(player);
            long pay = Math.Min(have, -message.Delta);
            if (pay > 0)
                player.BuyItem(pay);
        }
        lastCoinSeq = message.Seq;
        link.Send(new CoinAck { Seq = lastCoinSeq, Total = Coins(player) });
        ForceResend();
    }

    public static void AddCoins(Player player, long copper)
    {
        int[] split = Utils.CoinsSplit(copper);
        int[] types = { ItemID.CopperCoin, ItemID.SilverCoin, ItemID.GoldCoin, ItemID.PlatinumCoin };
        for (int i = 0; i < split.Length && i < types.Length; i++)
        {
            int left = split[i];
            while (left > 0)
            {
                int stack = Math.Min(left, 999);
                Give(player, types[i], stack);
                left -= stack;
            }
        }
    }

    /// <summary>Puts an item straight into the inventory; whatever does not fit drops at the farmer's feet.</summary>
    public static void Give(Player player, int type, int stack, int prefix = 0)
    {
        if (type <= 0 || stack <= 0)
            return;
        var item = new Item();
        item.SetDefaults(type);
        item.stack = Math.Min(stack, Math.Max(1, item.maxStack));
        if (prefix > 0)
            item.Prefix(prefix);
        Item left = player.GetItem(player.whoAmI, item, GetItemSettings.InventoryEntityToPlayerInventorySettings);
        if (left != null && !left.IsAir)
            player.QuickSpawnItem(new EntitySource_Gift(player), left, left.stack);
        if (stack > item.maxStack)
            Give(player, type, stack - item.maxStack, prefix);
    }

    public static void Give(Player player, GiveItem message)
    {
        int type = message.ItemType;
        if (message.Name.Length > 0 && ItemID.Search.TryGetId(message.Name, out int named))
            type = named;
        if (type <= 0)
        {
            BridgeSystem.Instance.Mod.Logger.Warn($"Terradew has no Terraria item called {message.Name}.");
            return;
        }
        Give(player, type, message.Stack);
        ForceResend();
    }

    private static ref Item Slot(Player player, int slot, out bool valid)
    {
        valid = true;
        if (slot >= InventoryAction.EquipmentBase)
        {
            int index = slot - InventoryAction.EquipmentBase;
            if (index >= 0 && index < player.armor.Length)
                return ref player.armor[index];
        }
        else if (slot >= 0 && slot < player.inventory.Length)
        {
            return ref player.inventory[slot];
        }
        valid = false;
        return ref player.inventory[0];
    }

    public static void Act(Player player, InventoryAction action)
    {
        switch (action.Kind)
        {
            case InventoryKind.Swap:
            {
                ref Item a = ref Slot(player, action.A, out bool okA);
                ref Item b = ref Slot(player, action.B, out bool okB);
                if (okA && okB && action.A != action.B)
                {
                    if (!a.IsAir && !b.IsAir && a.type == b.type && b.stack < b.maxStack)
                    {
                        int moved = Math.Min(a.stack, b.maxStack - b.stack);
                        b.stack += moved;
                        a.stack -= moved;
                        if (a.stack <= 0)
                            a.TurnToAir();
                    }
                    else
                    {
                        (a, b) = (b, a);
                    }
                }
                break;
            }
            case InventoryKind.Equip:
                Equip(player, action.A);
                break;
            case InventoryKind.Unequip:
            {
                ref Item worn = ref Slot(player, action.A, out bool ok);
                if (!ok || worn.IsAir || action.A < InventoryAction.EquipmentBase)
                    break;
                for (int i = 0; i < 50; i++)
                {
                    if (player.inventory[i].IsAir)
                    {
                        (player.inventory[i], worn) = (worn, player.inventory[i]);
                        break;
                    }
                }
                break;
            }
            case InventoryKind.Trash:
            {
                ref Item doomed = ref Slot(player, action.A, out bool ok);
                if (ok && !doomed.favorited)
                    doomed.TurnToAir();
                break;
            }
            case InventoryKind.Use:
                UseFromInventory(player, action.A);
                break;
        }
        ForceResend();
    }

    public static void Equip(Player player, int slot)
    {
        if (slot < 0 || slot >= 50)
            return;
        Item item = player.inventory[slot];
        if (item.IsAir)
            return;
        int target = -1;
        bool vanity = item.vanity && !item.accessory;
        if (item.headSlot >= 0)
            target = vanity ? 10 : 0;
        else if (item.bodySlot >= 0)
            target = vanity ? 11 : 1;
        else if (item.legSlot >= 0)
            target = vanity ? 12 : 2;
        else if (item.accessory)
        {
            int last = 3 + 5 + player.extraAccessorySlots;
            for (int i = 3; i < last && i < 10; i++)
            {
                if (player.armor[i].IsAir)
                {
                    target = i;
                    break;
                }
                if (player.armor[i].type == item.type)
                    return;
            }
            if (target < 0)
                target = 3;
        }
        if (target < 0)
            return;
        (player.inventory[slot], player.armor[target]) = (player.armor[target], player.inventory[slot]);
    }

    /// <summary>Potions, life crystals and other consumables used from the menu go through the hotbar.</summary>
    private static void UseFromInventory(Player player, int slot)
    {
        if (slot < 0 || slot >= 50 || player.inventory[slot].IsAir)
            return;
        Item item = player.inventory[slot];
        if (item.healLife > 0 || item.healMana > 0 || item.buffType > 0 || item.type == ItemID.LifeCrystal || item.type == ItemID.ManaCrystal || item.type == ItemID.LifeFruit)
        {
            if (slot >= 10)
                (player.inventory[slot], player.inventory[player.selectedItem]) = (player.inventory[player.selectedItem], player.inventory[slot]);
            else
                player.selectedItem = slot;
            PendingUse = 3;
        }
    }

    public static IEnumerable<Item> Stations(Player player)
    {
        foreach (Item item in player.inventory)
        {
            if (!item.IsAir && item.createTile >= 0)
                yield return item;
        }
    }
}
