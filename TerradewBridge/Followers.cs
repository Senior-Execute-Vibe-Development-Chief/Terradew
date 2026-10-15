using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terradew.Core;
using Terradew.Core.Protocol;
using Terradew.Core.Town;
using Terraria;
using Terraria.ID;

namespace TerradewBridge;

/// <summary>
/// Town NPCs play Stardew villagers. Stardew walks the villager's schedule and sends where each one stands;
/// the Terraria NPC stands there too instead of wandering, and remembers how much the farmer likes it.
/// </summary>
public static class Followers
{
    /// <summary>A spot older than this means Stardew stopped sending it (the villager left the map or the link dropped).</summary>
    private const long StaleMs = 3000;

    private static readonly Dictionary<int, (VillagerSpot Spot, long AtMs)> spots = new();
    private static readonly Dictionary<int, int> friendship = new();

    public static void Clear()
    {
        spots.Clear();
        friendship.Clear();
    }

    public static void Receive(VillagerSpots message)
    {
        long now = Link.NowMs();
        foreach (VillagerSpot spot in message.Spots)
            spots[spot.NpcType] = (spot, now);
    }

    public static void Receive(Friendships message)
    {
        foreach (FriendshipEntry entry in message.Entries)
            friendship[entry.NpcType] = entry.Points;
    }

    /// <summary>The shop price after the friendship discount for this NPC.</summary>
    public static long Discounted(int npcType, long price)
    {
        if (price <= 0 || !friendship.TryGetValue(npcType, out int points))
            return price;
        return Math.Max(1, (long)Math.Round(price * (1.0 - GiftJudge.Discount(points))));
    }

    /// <summary>
    /// Puts a followed town NPC where its villager stands. True when it is followed, so its own AI should not move it.
    /// </summary>
    public static bool Hold(NPC npc)
    {
        if (!npc.townNPC || !spots.TryGetValue(npc.type, out var entry))
            return false;
        if (Link.NowMs() - entry.AtMs > StaleMs)
        {
            spots.Remove(npc.type);
            return false;
        }
        VillagerSpot spot = entry.Spot;
        if (!BridgeSystem.Instance.Stamp.TryGet(spot.PlaceKey, out WorldStamp.PlaceState place))
            return false;
        Vector2 feet = place.ToWorld(spot.TileX * WorldProjection.StardewTilePixels, spot.TileY * WorldProjection.StardewTilePixels);
        (float x, float y) = WorldProjection.FromFeet(feet.X, feet.Y, npc.width, npc.height);
        npc.position = new Vector2(x, y);
        npc.velocity = Vector2.Zero;
        // Stardew facing: 0 up, 1 right, 2 down, 3 left.
        if (spot.Facing == 1 || spot.Facing == 3)
        {
            npc.direction = spot.Facing == 1 ? 1 : -1;
            npc.spriteDirection = npc.direction;
        }
        return true;
    }

    /// <summary>The first town NPC of a type, for Stardew talking to a villager by who it plays.</summary>
    public static int FindTownie(int npcType)
    {
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (npc.active && npc.townNPC && npc.type == npcType)
                return i;
        }
        return -1;
    }

    /// <summary>Takes one item from a backpack slot as a gift and tells Stardew what it was.</summary>
    public static GiftTaken Gift(Player player, GiftItem gift)
    {
        var reply = new GiftTaken { Seq = gift.Seq, NpcType = gift.NpcType };
        if (gift.Slot < 0 || gift.Slot >= 50)
            return reply;
        Item item = player.inventory[gift.Slot];
        if (item.IsAir || item.IsACoin || StardewSlot.Is(item))
            return reply;
        reply.Ok = true;
        reply.ItemName = ItemID.Search.GetName(item.type);
        reply.DisplayName = item.Name;
        reply.Tags = Tags(item);
        item.stack--;
        if (item.stack <= 0)
            item.TurnToAir();
        Inventory.ForceResend();
        return reply;
    }

    private static readonly HashSet<int> Fish = new()
    {
        ItemID.Bass, ItemID.Trout, ItemID.Salmon, ItemID.AtlanticCod, ItemID.Tuna, ItemID.RedSnapper, ItemID.NeonTetra,
        ItemID.ArmoredCavefish, ItemID.Damselfish, ItemID.CrimsonTigerfish, ItemID.FrostMinnow, ItemID.PrincessFish,
        ItemID.GoldenCarp, ItemID.SpecularFish, ItemID.Prismite, ItemID.VariegatedLardfish, ItemID.FlarefinKoi,
        ItemID.DoubleCod, ItemID.Obsidifish, ItemID.ChaosFish, ItemID.Ebonkoi, ItemID.Hemopiranha, ItemID.Stinkfish,
        ItemID.Shrimp, ItemID.Honeyfin,
    };

    private static readonly HashSet<int> Gems = new()
    {
        ItemID.Amethyst, ItemID.Topaz, ItemID.Sapphire, ItemID.Emerald, ItemID.Ruby, ItemID.Diamond, ItemID.Amber,
    };

    private static readonly HashSet<int> Plants = new()
    {
        ItemID.Daybloom, ItemID.Moonglow, ItemID.Blinkroot, ItemID.Deathweed, ItemID.Waterleaf, ItemID.Fireblossom,
        ItemID.Shiverthorn, ItemID.Mushroom, ItemID.GlowingMushroom, ItemID.VileMushroom, ItemID.ViciousMushroom,
        ItemID.JungleSpores, ItemID.Cactus, ItemID.Pumpkin, ItemID.Hay,
    };

    private static readonly HashSet<int> Junk = new() { ItemID.OldShoe, ItemID.FishingSeaweed, ItemID.TinCan };

    private static readonly HashSet<int> Explosives = new()
    {
        ItemID.Bomb, ItemID.StickyBomb, ItemID.BouncyBomb, ItemID.Dynamite, ItemID.StickyDynamite, ItemID.BouncyDynamite,
        ItemID.Grenade, ItemID.StickyGrenade, ItemID.BouncyGrenade, ItemID.Beenade, ItemID.ScarabBomb, ItemID.DirtBomb,
        ItemID.LandMine, ItemID.Explosives,
    };

    /// <summary>The <see cref="GiftTags"/> that fit an item.</summary>
    public static List<string> Tags(Item item)
    {
        var tags = new List<string>();
        void Add(bool yes, string tag)
        {
            if (yes)
                tags.Add(tag);
        }
        string name = ItemID.Search.GetName(item.type);
        bool equip = item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0;
        bool tool = item.pick > 0 || item.axe > 0 || item.hammer > 0 || item.fishingPole > 0;
        bool weapon = item.damage > 0 && item.ammo == AmmoID.None && !item.accessory && !equip && item.useStyle != ItemUseStyleID.None && !tool;
        Add(weapon, "#weapon");
        Add(weapon && item.DamageType.CountsAsClass(Terraria.ModLoader.DamageClass.Melee), "#melee");
        Add(weapon && item.DamageType.CountsAsClass(Terraria.ModLoader.DamageClass.Ranged), "#ranged");
        Add(weapon && item.DamageType.CountsAsClass(Terraria.ModLoader.DamageClass.Magic), "#magic");
        Add(weapon && item.DamageType.CountsAsClass(Terraria.ModLoader.DamageClass.Summon), "#summon");
        Add(tool, "#tool");
        Add(item.ammo != AmmoID.None && item.consumable, "#ammo");
        Add(Explosives.Contains(item.type), "#explosive");
        Add(ItemID.Sets.IsFood[item.type], "#food");
        Add(!ItemID.Sets.IsFood[item.type] && item.consumable && (item.healLife > 0 || item.healMana > 0 || item.buffType > 0) && item.createTile < 0, "#potion");
        Add(Fish.Contains(item.type) || item.questItem, "#fish");
        Add(item.bait > 0, "#bait");
        Add(item.createTile >= 0 && TileID.Sets.Ore[item.createTile], "#ore");
        Add(item.createTile == TileID.MetalBars, "#bar");
        Add(Gems.Contains(item.type), "#gem");
        Add(name.EndsWith("Seeds", StringComparison.Ordinal), "#seed");
        Add(Plants.Contains(item.type), "#plant");
        Add(item.dye > 0, "#dye");
        Add(item.vanity, "#vanity");
        Add(equip && !item.vanity, "#armor");
        Add(item.accessory, "#accessory");
        bool placed = item.createTile >= 0 && !TileID.Sets.Ore[item.createTile] && item.createTile != TileID.MetalBars;
        Add(placed && Main.tileFrameImportant[item.createTile], "#furniture");
        Add((placed && Main.tileSolid[item.createTile] && !Main.tileFrameImportant[item.createTile]) || item.createWall > 0, "#block");
        Add(ItemID.Sets.SortingPriorityBossSpawns[item.type] >= 0, "#bossSummon");
        Add(item.material, "#material");
        Add(item.rare >= ItemRarityID.LightRed, "#rare");
        Add(Junk.Contains(item.type), "#junk");
        return tags;
    }
}
