using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Terradew.Core;
using Terradew.Core.Protocol;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

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

    /// <summary>The last <see cref="InventoryAction.Seq"/> applied, echoed back in every <see cref="InventoryState"/>.</summary>
    private static int lastAckSeq;

    /// <summary>Ticks left to try using an item from the menu, which waits for the hand to be free.</summary>
    public static int PendingUse;

    /// <summary>The hotbar slot holding the item used from the menu.</summary>
    public static int PendingSlot;

    public static void ForceResend()
    {
        lastHash = long.MinValue;
        lastSent = -10_000;
    }

    /// <summary>Stardew said hello: carry on from the last coin change it saw confirmed.</summary>
    public static void ResumeCoins(int seq) => lastCoinSeq = Math.Max(lastCoinSeq, seq);

    /// <summary>Stardew said hello on a new link, so its action counter starts again.</summary>
    public static void ResetAckSeq()
    {
        lastAckSeq = 0;
        ForceResend();
    }

    /// <summary>
    /// Stardew owns the slots its mask names, so its blockers from an earlier link are dropped. Stardew sends the
    /// mask right after hello, and <see cref="ApplyMask"/> puts back the ones it still holds.
    /// </summary>
    public static void ClearBlockers(Player player)
    {
        bool changed = false;
        for (int i = 0; i < SlotMask.Slots && i < player.inventory.Length; i++)
        {
            if (StardewSlot.Is(player.inventory[i]))
            {
                player.inventory[i].TurnToAir();
                changed = true;
            }
        }
        if (changed)
            ForceResend();
    }

    /// <summary>
    /// Puts a blocker in each main slot Stardew holds and takes away blockers from slots it no longer holds. A slot
    /// holding a real Terraria item is left alone: Stardew moves its own item away when it sees the conflict.
    /// </summary>
    public static void ApplyMask(Player player, SlotMask mask)
    {
        bool changed = false;
        for (int i = 0; i < SlotMask.Slots && i < player.inventory.Length; i++)
        {
            if (mask.Has(i) && player.inventory[i].IsAir)
            {
                player.inventory[i] = new Item(ModContent.ItemType<StardewSlot>());
                changed = true;
            }
            else if (!mask.Has(i) && StardewSlot.Is(player.inventory[i]))
            {
                player.inventory[i].TurnToAir();
                changed = true;
            }
        }
        // A blocker on the mouse must not be dropped or placed, so it goes.
        if (StardewSlot.Is(Main.mouseItem))
        {
            Main.mouseItem.TurnToAir();
            changed = true;
        }
        if (changed)
            ForceResend();
    }

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
            AckSeq = lastAckSeq,
            HeadSlot = player.head,
            BodySlot = player.body,
            LegSlot = player.legs,
            Male = player.Male,
            WingsSlot = player.wings,
            ShieldSlot = player.shield,
            BackSlot = player.back,
            FrontSlot = player.front,
            ShoeSlot = player.shoe,
            HeadTint = TintOf(player.dye[0]),
            BodyTint = TintOf(player.dye[1]),
            LegTint = TintOf(player.dye[2]),
            WingsTint = AccessoryTint(player, slot => slot.wingSlot > 0),
            ShieldTint = AccessoryTint(player, slot => slot.shieldSlot > 0),
            BackTint = AccessoryTint(player, slot => slot.backSlot > 0),
            FrontTint = AccessoryTint(player, slot => slot.frontSlot > 0),
            ShoeTint = AccessoryTint(player, slot => slot.shoeSlot > 0),
        };
        for (int i = 0; i < InventoryState.InventorySlots && i < player.inventory.Length; i++)
            state.Items.Add(Describe(player.inventory[i]));
        for (int i = 0; i < InventoryState.EquipmentSlots && i < player.armor.Length; i++)
            state.Equipment.Add(Describe(player.armor[i]));
        state.Trash = Describe(player.trashItem);
        link.Send(state);
        link.Send(new CoinAck { Seq = lastCoinSeq, Total = state.Coins });
        ExportGear(player);
    }

    /// <summary>
    /// The dye for the first accessory (armor[3..9]) whose item fills a sprite slot. Dye slots 3..9 line up with those
    /// accessory slots, so the dye at the same index is the one that colours it.
    /// </summary>
    private static uint AccessoryTint(Player player, Func<Item, bool> fills)
    {
        for (int i = 3; i < 10 && i < player.armor.Length && i < player.dye.Length; i++)
        {
            if (!player.armor[i].IsAir && fills(player.armor[i]))
                return TintOf(player.dye[i]);
        }
        return DyeTint.None;
    }

    private static uint TintOf(Item? dye)
    {
        if (dye == null || dye.IsAir || dye.type <= 0)
            return DyeTint.None;
        return DyeTint.Rgba(ItemID.Search.GetName(dye.type));
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

    public static ItemStack Describe(Item? item) => Describe(item, true);

    public static ItemStack Describe(Item? item, bool tooltip)
    {
        // A blocker is no Terraria item, so Stardew sees the slot as empty.
        if (item == null || item.IsAir || StardewSlot.Is(item))
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
            Station = IsStation(item),
            Pick = (short)item.pick,
            Axe = (short)(item.axe * 5),
            Hammer = (short)item.hammer,
            Blade = Melee.UsesArc(item),
            Usable = item.useStyle != ItemUseStyleID.None,
            Tooltip = tooltip ? Tooltip(item) : "",
        };
    }

    /// <summary>Tooltips by item and prefix. They hold the player's own numbers, such as damage, so they are redone now and then.</summary>
    private static readonly Dictionary<(int Type, int Prefix), string> tooltips = new();
    private static uint tooltipsMade;

    /// <summary>Chat tags: colour tags keep their text; item, glyph and other tags go.</summary>
    private static readonly Regex ColourTag = new(@"\[c/[0-9A-Fa-f]+:([^\]]*)\]", RegexOptions.Compiled);
    private static readonly Regex OtherTag = new(@"\[[a-zA-Z]+(/[^:\]]*)?:[^\]]*\]", RegexOptions.Compiled);

    /// <summary>The lines Terraria shows under the item's name when you hover over it, as Terraria and other mods make them.</summary>
    private static string Tooltip(Item item)
    {
        if (Main.dedServ)
            return "";
        if (Main.GameUpdateCount - tooltipsMade > 600)
        {
            tooltips.Clear();
            tooltipsMade = Main.GameUpdateCount;
        }
        if (tooltips.TryGetValue((item.type, item.prefix), out string? known))
            return known;
        string made;
        try
        {
            const int Most = 60;
            var text = new string[Most];
            var colors = new Color[Most];
            var names = new string[Most];
            text[0] = item.HoverName;
            names[0] = "ItemName";
            int lines = 1;
            int logo = -1;
            Main.MouseText_DrawItemTooltip_GetLinesInfo(item, ref logo, item.knockBack, ref lines, text, colors, names, out int prefixLine);
            ItemLoader.ModifyTooltips(item, ref lines, names, ref text, ref colors, ref logo, prefixLine);
            var tooltip = new StringBuilder();
            for (int i = 0; i < lines; i++)
            {
                if (names[i] == "ItemName" || string.IsNullOrWhiteSpace(text[i]))
                    continue;
                string line = OtherTag.Replace(ColourTag.Replace(text[i], "$1"), "").Trim();
                if (line.Length == 0)
                    continue;
                if (tooltip.Length > 0)
                    tooltip.Append('\n');
                tooltip.Append(line);
            }
            made = tooltip.ToString();
        }
        catch (Exception error)
        {
            BridgeSystem.Instance.Mod.Logger.Debug($"Terradew could not make the tooltip for {item.Name}: {error.Message}");
            made = "";
        }
        tooltips[(item.type, item.prefix)] = made;
        return made;
    }

    private static HashSet<int>? stationTiles;

    /// <summary>
    /// Tiles that some recipe needs a crafting station for, plus the tiles that stand in for them. Built once
    /// the recipes exist; before that it is empty and not kept.
    /// </summary>
    private static HashSet<int> StationTiles()
    {
        if (stationTiles != null)
            return stationTiles;
        if (Recipe.numRecipes <= 0)
            return new HashSet<int>();
        var tiles = new HashSet<int>();
        for (int i = 0; i < Recipe.numRecipes; i++)
        {
            foreach (int tile in Main.recipe[i].requiredTile)
            {
                if (tile >= 0)
                    tiles.Add(tile);
            }
        }
        foreach (int tile in Crafting.Aliases.Keys)
            tiles.Add(tile);
        stationTiles = tiles;
        return tiles;
    }

    /// <summary>Whether the item is a crafting station: a placeable tile that some recipe needs.</summary>
    public static bool IsStation(Item item) => !item.IsAir && item.createTile >= 0 && StationTiles().Contains(item.createTile);

    private static long Hash(Player player)
    {
        long hash = unchecked(17 + lastAckSeq * 7919L);
        foreach (Item item in player.inventory)
            hash = unchecked(hash * 31 + item.type * 7919L + item.stack * 31L + item.prefix);
        foreach (Item item in player.armor)
            hash = unchecked(hash * 31 + item.type * 7919L + item.stack);
        hash = unchecked(hash * 31 + player.trashItem.type * 7919L + player.trashItem.stack);
        hash = unchecked(hash * 31 + player.head * 13 + player.body * 17 + player.legs * 19);
        hash = unchecked(hash * 31 + player.wings * 23 + player.shield * 29 + player.back * 37 + player.front * 41 + player.shoe * 43);
        foreach (Item dye in player.dye)
            hash = unchecked(hash * 31 + dye.type * 7907L);
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
        if (slot == InventoryAction.TrashSlot)
            return ref player.trashItem;
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
        lastAckSeq = Math.Max(lastAckSeq, action.Seq);
        switch (action.Kind)
        {
            case InventoryKind.Swap:
            {
                ref Item a = ref Slot(player, action.A, out bool okA);
                ref Item b = ref Slot(player, action.B, out bool okB);
                // Blockers move only between main slots; anywhere else they would leak into coin, ammo or equipment slots.
                bool mainOnly = IsMain(action.A) && IsMain(action.B);
                if (okA && okB && action.A != action.B && (mainOnly || (!StardewSlot.Is(a) && !StardewSlot.Is(b))))
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
                // Only equipment and the trash slot give their item back to the main slots.
                bool gives = action.A >= InventoryAction.EquipmentBase || action.A == InventoryAction.TrashSlot;
                if (!ok || worn.IsAir || StardewSlot.Is(worn) || !gives)
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
                if (ok && !doomed.favorited && !StardewSlot.Is(doomed))
                {
                    // Terraria keeps the last item thrown away in its trash slot, so it can be taken back out.
                    // Throwing away what is already in the trash slot just deletes it.
                    if (action.A != InventoryAction.TrashSlot)
                        player.trashItem = doomed.Clone();
                    doomed.TurnToAir();
                }
                break;
            }
            case InventoryKind.Use:
                UseFromInventory(player, action.A);
                break;
        }
        ForceResend();
    }

    private static bool IsMain(int slot) => slot >= 0 && slot < SlotMask.Slots;

    public static void Equip(Player player, int slot)
    {
        if (slot < 0 || slot >= 50)
            return;
        Item item = player.inventory[slot];
        if (item.IsAir || StardewSlot.Is(item))
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
            for (int i = 3; i < 10; i++)
            {
                // The sixth and seventh slots need a Demon Heart or master mode.
                if (!player.IsItemSlotUnlockedAndUsable(i))
                    continue;
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

    /// <summary>
    /// Anything Terraria can use, used from the menu: potions, life crystals, boss summons, mount and pet items. It goes
    /// through the hotbar, so Terraria's own rules for it apply, such as the Suspicious Looking Eye needing night.
    /// </summary>
    private static void UseFromInventory(Player player, int slot)
    {
        if (slot < 0 || slot >= 50 || player.inventory[slot].IsAir || StardewSlot.Is(player.inventory[slot]))
            return;
        Item item = player.inventory[slot];
        if (item.useStyle == ItemUseStyleID.None)
            return;
        if (slot >= 10)
        {
            if (StardewSlot.Is(player.inventory[player.selectedItem]))
                return;
            (player.inventory[slot], player.inventory[player.selectedItem]) = (player.inventory[player.selectedItem], player.inventory[slot]);
            PendingSlot = player.selectedItem;
        }
        else
            PendingSlot = slot;
        PendingUse = 20;
    }
}
