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
/// Floors also get dart traps: a plate Stardew draws, and a dart Terraria fires when the farmer steps on it.
/// </summary>
public static class CaveLife
{
    private static int ticksToSpawn = CaveRules.SpawnEvery;
    private static readonly HashSet<string> warned = new();
    /// <summary>The mine floor each place last had chests sent for, so a floor gets its chests once per visit.</summary>
    private static readonly Dictionary<string, int> chestFloor = new();
    /// <summary>The mine floor each place last had dart traps placed for, so a floor gets its traps once per visit.</summary>
    private static readonly Dictionary<string, int> trapFloor = new();
    /// <summary>The dart traps on the floor the farmer is on, in the place named by trapPlace.</summary>
    private static readonly List<DartTrap> traps = new();
    private static string trapPlace = "";

    /// <summary>A dart trap on a mine floor. Tiles are map tiles, as Stardew names them; Rearm counts the ticks until it is armed.</summary>
    private sealed class DartTrap
    {
        public int X;
        public int Y;
        public int SourceX;
        public int SourceY;
        public int DirX;
        public int DirY;
        public int Rearm;
    }

    /// <summary>Runs once per bridge step while Stardew is linked: sends a new floor's chests, and spawns creatures on the floor.</summary>
    public static void Tick(Link link, WorldStamp stamp, Player player)
    {
        WorldStamp.PlaceState? place = stamp.Current;
        if (place?.Layout == null || !place.Layout.IsMineSlot || player.dead)
            return;
        SendChests(link, stamp, place, player);
        PlaceTraps(link, place, player);
        FireTraps(place, player);
        if (CaveRules.IsSafeFloor(place.Layout.Floor))
            return;
        if (--ticksToSpawn > 0)
            return;
        ticksToSpawn = CaveRules.SpawnEvery;
        if (Main.rand.NextDouble() < CaveRules.SpawnChance)
            Spawn(stamp, place, player);
    }

    /// <summary>Forgets what was sent, when the world is cleared or the bridge restarts.</summary>
    public static void Reset()
    {
        chestFloor.Clear();
        trapFloor.Clear();
        traps.Clear();
        trapPlace = "";
        ticksToSpawn = CaveRules.SpawnEvery;
    }

    private static void Spawn(WorldStamp stamp, WorldStamp.PlaceState place, Player player)
    {
        Biome biome = BiomeNow(place);
        string[] names = CaveRules.SpawnsFor(biome, (Band)place.Layout!.Band);
        if (names.Length == 0 || CountHostile(place) >= CaveRules.SpawnCap)
            return;
        // An open tile of the floor, 11 to 45 tiles from the farmer, so nothing appears on top of them.
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

    /// <summary>
    /// Picks a new floor's dart traps and sends their plates, once per floor visit. A floor with no traps still sends an
    /// empty list, so the plates of an earlier visit to the floor are cleared.
    /// </summary>
    private static void PlaceTraps(Link link, WorldStamp.PlaceState place, Player player)
    {
        MapLayout map = place.Layout!;
        if (trapFloor.TryGetValue(place.Key, out int sent) && sent == map.Floor)
            return;
        trapFloor[place.Key] = map.Floor;
        traps.Clear();
        trapPlace = place.Key;

        var message = new CaveTraps { PlaceKey = place.Key, Floor = map.Floor };
        int wanted = CaveRules.TrapCount(map.Floor, new Random(Main.rand.Next()));
        Vector2 farmer = Puppet.FeetOf(player);
        int fx = (int)(farmer.X / 16f) - place.OriginX;
        int fy = (int)(farmer.Y / 16f) - place.OriginY;
        for (int i = 0; i < wanted; i++)
        {
            if (!TryPickTrap(map, fx, fy, out DartTrap trap))
                continue;
            traps.Add(trap);
            message.Plates.Add(new CaveTrapPlate(trap.X, trap.Y));
        }
        link.Send(message);
    }

    /// <summary>
    /// A plate on an open tile at least TrapMinDistance from the farmer, with an open line of TrapLine tiles from it in one of
    /// the four directions, which ends at the dart source. False when no such spot turns up.
    /// </summary>
    private static bool TryPickTrap(MapLayout map, int farmerX, int farmerY, out DartTrap trap)
    {
        trap = null!;
        for (int attempt = 0; attempt < 200; attempt++)
        {
            int x = Main.rand.Next(map.Width);
            int y = Main.rand.Next(map.Height);
            if (map.IsBlocked(x, y) || Math.Max(Math.Abs(x - farmerX), Math.Abs(y - farmerY)) < CaveRules.TrapMinDistance)
                continue;
            if (traps.Exists(t => t.X == x && t.Y == y))
                continue;
            int first = Main.rand.Next(CaveRules.TrapDirections.Length);
            for (int d = 0; d < CaveRules.TrapDirections.Length; d++)
            {
                (int dx, int dy) = CaveRules.TrapDirections[(first + d) % CaveRules.TrapDirections.Length];
                if (!OpenLine(map, x, y, dx, dy))
                    continue;
                trap = new DartTrap { X = x, Y = y, SourceX = x + dx * CaveRules.TrapLine, SourceY = y + dy * CaveRules.TrapLine, DirX = dx, DirY = dy };
                return true;
            }
        }
        return false;
    }

    /// <summary>Whether the TrapLine tiles from a plate toward its dart source are all open, the source included.</summary>
    private static bool OpenLine(MapLayout map, int x, int y, int dx, int dy)
    {
        for (int step = 1; step <= CaveRules.TrapLine; step++)
        {
            if (map.IsBlocked(x + dx * step, y + dy * step))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Fires each armed dart trap the farmer's Terraria player stands on. A trap that fires waits TrapRearm ticks to arm again.
    /// The player stands on a plate when the tile under its feet is the plate's tile.
    /// </summary>
    private static void FireTraps(WorldStamp.PlaceState place, Player player)
    {
        if (traps.Count == 0 || place.Key != trapPlace)
            return;
        int feetX = (int)(player.Center.X / 16f);
        int feetY = (int)((player.position.Y + player.height - 1) / 16f);
        foreach (DartTrap trap in traps)
        {
            if (trap.Rearm > 0)
            {
                trap.Rearm--;
                continue;
            }
            if (place.OriginX + trap.X != feetX || place.OriginY + trap.Y != feetY)
                continue;
            Fire(place, trap);
            trap.Rearm = CaveRules.TrapRearm;
        }
    }

    /// <summary>Launches a dart from the trap's source along its line, toward the plate. It is hostile and owned by no player, as vanilla traps are.</summary>
    private static void Fire(WorldStamp.PlaceState place, DartTrap trap)
    {
        float sourceX = (place.OriginX + trap.SourceX) * 16f + 8f;
        float sourceY = (place.OriginY + trap.SourceY) * 16f + 8f;
        float speedX = -trap.DirX * CaveRules.DartSpeed;
        float speedY = -trap.DirY * CaveRules.DartSpeed;
        int damage = CaveRules.TrapDamage(place.Layout!.Floor);
        int index = Projectile.NewProjectile(new EntitySource_Misc("Terradew dart trap"), sourceX, sourceY, speedX, speedY,
            ProjectileID.PoisonDartTrap, damage, 2f, Main.maxPlayers);
        Projectile dart = Main.projectile[index];
        dart.hostile = true;
        dart.friendly = false;
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
