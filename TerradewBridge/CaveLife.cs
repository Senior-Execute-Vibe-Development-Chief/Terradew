using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terradew.Core.Protocol;
using Terradew.Core.World;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace TerradewBridge;

/// <summary>
/// Terraria creatures and chests on the Stardew mine floors. Terradew spawns the creatures itself, for the floor's band and
/// biome, rather than leaning on Terraria's natural pools, which a stamped floor does not reliably reach. Every fifth floor
/// is safe. Each new floor gets a chest now and then, which Stardew places and which gives Terraria loot when opened.
/// </summary>
public static class CaveLife
{
    private static int ticksToSpawn = CaveRules.SpawnEvery;
    private static readonly HashSet<string> warned = new();
    /// <summary>The mine floor each place last had chests sent for, so a floor gets its chests once per visit.</summary>
    private static readonly Dictionary<string, int> chestFloor = new();

    /// <summary>Runs once per bridge step while Stardew is linked: sends a new floor's chests, and spawns creatures on the floor.</summary>
    public static void Tick(Link link, WorldStamp stamp, Player player)
    {
        WorldStamp.PlaceState? place = stamp.Current;
        if (place?.Layout == null || !place.Layout.IsMineSlot || player.dead)
            return;
        SendChests(link, stamp, place, player);
        if (CaveRules.IsSafeFloor(place.Layout.Floor))
            return;
        if (--ticksToSpawn > 0)
            return;
        ticksToSpawn = CaveRules.SpawnEvery;
        if (Main.rand.NextDouble() < CaveRules.SpawnChance)
            Spawn(stamp, place);
    }

    /// <summary>Forgets what was sent, when the world is cleared or the bridge restarts.</summary>
    public static void Reset()
    {
        chestFloor.Clear();
        ticksToSpawn = CaveRules.SpawnEvery;
    }

    private static void Spawn(WorldStamp stamp, WorldStamp.PlaceState place)
    {
        Biome biome = BiomeNow(place);
        string[] names = CaveRules.SpawnsFor(biome, (Band)place.Layout!.Band);
        if (names.Length == 0 || CountHostile(place) >= CaveRules.SpawnCap)
            return;
        // The farmer's last-known feet, from the bridge's puppet. The spawn lands on an open tile away from them.
        Player player = Main.LocalPlayer;
        if (!stamp.TryOpenSpot(Puppet.FeetOf(player), 11, 45, out Vector2 feet))
            return;
        string name = names[Main.rand.Next(names.Length)];
        if (!NPCID.Search.TryGetId(name, out int type))
        {
            WarnOnce("creature " + name, $"Terradew has no Terraria creature called {name}.");
            return;
        }
        // A natural spawn source, so Creatures.OnSpawn moves it onto an open tile of the floor, as it does for other spawns.
        NPC.NewNPC(new EntitySource_SpawnNPC(), (int)feet.X, (int)feet.Y, type);
    }

    /// <summary>Live hostile creatures in the place. The spawn cap counts these, however they got there.</summary>
    private static int CountHostile(WorldStamp.PlaceState place)
    {
        int count = 0;
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (npc.active && npc.life > 0 && !npc.friendly && !npc.townNPC && !npc.boss && npc.damage > 0 && place.ContainsWorld(npc.Center))
                count++;
        }
        return count;
    }

    private static void SendChests(Link link, WorldStamp stamp, WorldStamp.PlaceState place, Player player)
    {
        MapLayout map = place.Layout!;
        if (chestFloor.TryGetValue(place.Key, out int sent) && sent == map.Floor)
            return;
        chestFloor[place.Key] = map.Floor;

        var message = new CaveChests { PlaceKey = place.Key, Floor = map.Floor };
        if (Main.rand.NextDouble() < CaveRules.ChestChance(map.Floor))
        {
            Biome biome = BiomeNow(place);
            CaveChestKind kind = CaveRules.KindFor(biome, (Band)map.Band);
            int x = 0, y = 0;
            bool found = false;
            Vector2 farmer = Puppet.FeetOf(player);
            int fx = (int)(farmer.X / 16f) - place.OriginX;
            int fy = (int)(farmer.Y / 16f) - place.OriginY;
            for (int attempt = 0; attempt < 200 && !found; attempt++)
            {
                x = Main.rand.Next(map.Width);
                y = Main.rand.Next(map.Height);
                // Open floor only, and never on top of the farmer's arrival point.
                found = !map.IsBlocked(x, y) && Math.Max(Math.Abs(x - fx), Math.Abs(y - fy)) >= 6;
            }
            // Stardew draws the chest with its Terraria item's sprite, so Terraria exports that sprite.
            if (found && ItemID.Search.TryGetId(CaveRules.ChestItem(kind), out int item))
            {
                SpriteDump.Item(item);
                message.Chests.Add(new CaveChest(x, y, (byte)kind, item));
            }
        }
        if (message.Chests.Count > 0)
            link.Send(message);
    }

    /// <summary>The farmer opened a chest on a mine floor. Terraria rolls its loot and gives it.</summary>
    public static void Open(Player player, OpenCaveChest message)
    {
        if (message.Kind > (byte)CaveChestKind.Dungeon)
            return;
        var kind = (CaveChestKind)message.Kind;
        var random = new Random(Main.rand.Next());
        List<(string Item, int Count)> drops = CaveRules.Roll(kind, random, out int copper);
        Inventory.AddCoins(player, copper);
        var given = new List<string> { $"{copper} copper" };
        foreach ((string item, int count) in drops)
        {
            if (!ItemID.Search.TryGetId(item, out int type))
            {
                WarnOnce("item " + item, $"Terradew has no Terraria item called {item}.");
                continue;
            }
            Inventory.Give(player, type, count);
            given.Add($"{count} {Lang.GetItemNameValue(type)}");
        }
        Inventory.ForceResend();
        BridgeSystem.Instance.Notice("Chest: " + string.Join(", ", given), 255, 230, 120);
    }

    private static Biome BiomeNow(WorldStamp.PlaceState place)
        => Main.hardMode && Places.HallowsInHardmode(place.Key) ? Biome.Hallow : (Biome)place.Layout!.Biome;

    private static void WarnOnce(string key, string text)
    {
        if (warned.Add(key))
            BridgeSystem.Instance.Mod.Logger.Warn(text);
    }
}
