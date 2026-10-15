using System;
using System.Collections.Generic;
using Terradew.Core.Economy;
using Terradew.Core.Protocol;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerradewBridge;

/// <summary>Talking to Terraria town NPCs from Stardew: chat, shops, the Nurse, and the Old Man's curse.</summary>
public static class Shops
{
    private static NPC? Townie(int npcId)
    {
        if (npcId < 0 || npcId >= Main.maxNPCs)
            return null;
        NPC npc = Main.npc[npcId];
        return npc.active && (npc.townNPC || npc.type == NPCID.OldMan) ? npc : null;
    }

    public static void Talk(Link link, Player player, int npcId)
    {
        NPC? npc = Townie(npcId);
        if (npc == null)
            return;
        if (npc.type == NPCID.OldMan)
        {
            OldMan(link, npc);
            return;
        }
        string text;
        try
        {
            player.SetTalkNPC(npc.whoAmI);
            text = npc.GetChat();
        }
        catch (Exception)
        {
            text = "...";
        }
        bool hasShop = TryShop(npc, out _);
        bool nurse = npc.type == NPCID.Nurse;
        int head = npc.townNPC ? Terraria.GameContent.TownNPCProfiles.GetHeadIndexSafe(npc) : -1;
        // The portrait must be on disk by the time Stardew opens the chat.
        SpriteDump.NpcHead(head);
        link.Send(new NpcChat
        {
            NpcId = npc.whoAmI,
            NpcType = npc.type,
            Name = npc.GivenOrTypeName,
            Text = text,
            HasShop = hasShop,
            IsNurse = nurse,
            HealCost = nurse ? HealCost(player) : 0,
            HeadIndex = head,
        });
        if (hasShop)
            link.Send(Stock(npc));
    }

    private static void OldMan(Link link, NPC npc)
    {
        string text;
        if (NPC.downedBoss3)
            text = "Thank you for freeing me from my curse.";
        else if (Main.dayTime)
            text = "Come back after dark. The curse only stirs at night.";
        else
        {
            text = "Curse him!";
            NPC.SpawnSkeletron(Main.myPlayer);
        }
        link.Send(new NpcChat { NpcId = npc.whoAmI, NpcType = npc.type, Name = "Old Man", Text = text });
    }

    private static bool TryShop(NPC npc, out AbstractNPCShop? shop)
    {
        shop = null;
        try
        {
            return NPCShopDatabase.TryGetNPCShop(NPCShopDatabase.GetShopName(npc.type), out shop) && shop != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static List<Item> Fill(NPC npc)
    {
        var items = new List<Item>();
        if (!TryShop(npc, out AbstractNPCShop? shop) || shop == null)
            return items;
        var slots = new Item[40];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = new Item();
        try
        {
            shop.FillShop(slots, npc, out _);
        }
        catch (Exception error)
        {
            BridgeSystem.Instance.Mod.Logger.Warn($"Terradew could not fill {npc.GivenOrTypeName}'s shop: {error.Message}");
        }
        foreach (Item item in slots)
        {
            if (item != null && !item.IsAir)
                items.Add(item);
        }
        return items;
    }

    private static long Price(Item item) => item.shopCustomPrice ?? item.GetStoreValue();

    public static ShopStock Stock(NPC npc)
    {
        var stock = new ShopStock { NpcId = npc.whoAmI, Name = npc.GivenOrTypeName };
        foreach (Item item in Fill(npc))
            stock.Entries.Add(new ShopEntry(Inventory.Describe(item), Price(item)));
        return stock;
    }

    public static void Buy(Link link, Player player, Buy buy)
    {
        NPC? npc = Townie(buy.NpcId);
        if (npc == null)
            return;
        List<Item> items = Fill(npc);
        if (buy.Index < 0 || buy.Index >= items.Count)
            return;
        Item item = items[buy.Index];
        long price = Price(item);
        if (Inventory.Coins(player) < price || !player.BuyItem(price))
        {
            BridgeSystem.Instance.Notice($"Not enough coins for {item.Name} ({Coins.Format(price)}).", 255, 200, 80);
            return;
        }
        Inventory.Give(player, item.type, Math.Max(1, item.stack), item.prefix);
        Inventory.ForceResend();
    }

    public static void Sell(Link link, Player player, Sell sell)
    {
        if (sell.Slot < 0 || sell.Slot >= 50)
            return;
        Item item = player.inventory[sell.Slot];
        if (item.IsAir || item.IsACoin || item.favorited)
            return;
        long value = (long)(item.value / 5) * item.stack;
        string name = item.Name;
        if (value <= 0)
        {
            BridgeSystem.Instance.Notice($"{name} can't be sold.", 255, 200, 80);
            return;
        }
        item.TurnToAir();
        if (value > 0)
            Inventory.AddCoins(player, value);
        BridgeSystem.Instance.Notice($"Sold {name} for {Coins.Format(value)}.", 200, 230, 255);
        Inventory.ForceResend();
    }

    public static long HealCost(Player player)
    {
        int missing = Math.Max(0, player.statLifeMax2 - player.statLife);
        int debuffs = 0;
        for (int i = 0; i < player.buffType.Length; i++)
        {
            if (player.buffType[i] > 0 && Main.debuff[player.buffType[i]])
                debuffs++;
        }
        long cost = missing * 20L + debuffs * 100L;
        if (Main.hardMode)
            cost *= 4;
        return cost;
    }

    public static void NurseHeal(Link link, Player player, int npcId)
    {
        NPC? npc = Townie(npcId);
        if (npc == null || npc.type != NPCID.Nurse)
            return;
        long cost = HealCost(player);
        if (cost <= 0)
        {
            BridgeSystem.Instance.Notice("You look healthy already.", 200, 230, 255);
            return;
        }
        if (Inventory.Coins(player) < cost || !player.BuyItem(cost))
        {
            BridgeSystem.Instance.Notice($"The Nurse wants {Coins.Format(cost)}.", 255, 200, 80);
            return;
        }
        player.statLife = player.statLifeMax2;
        for (int i = 0; i < player.buffType.Length; i++)
        {
            if (player.buffType[i] > 0 && Main.debuff[player.buffType[i]])
                player.DelBuff(i--);
        }
        BridgeSystem.Instance.Notice("The Nurse patched you up.", 200, 255, 200);
        Inventory.ForceResend();
    }
}
