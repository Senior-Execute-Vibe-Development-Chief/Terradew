using System;
using System.Collections.Generic;
using System.Linq;
using Terradew.Core.Mining;
using Terradew.Core.Protocol;
using Terradew.Core.World;
using Terraria;
using Terraria.ID;

namespace TerradewBridge;

/// <summary>
/// Breaking Stardew rocks and gathering Stardew wood also gives Terraria materials (see <see cref="OreTable"/>).
/// Breaking Stardew barrels and crates spawns Terraria pots (see <see cref="Pots"/>) and gives their pot loot.
/// </summary>
public static class Gathering
{
    /// <summary>Most pots spawned from one message, so a big stack of crates cannot flood the world.</summary>
    private const int MaxPots = 10;

    public static void Receive(Player player, Gathered message)
    {
        if (message.Kind == GatherKind.Pot)
        {
            Break(player, Math.Clamp(message.Count, 1, 999));
            return;
        }

        var drops = new List<Drop>();
        int count = Math.Clamp(message.Count, 1, 999);
        if (message.Kind is GatherKind.Wood or GatherKind.Hardwood)
        {
            bool hallowed = Main.hardMode && Places.HallowsInHardmode(message.PlaceKey);
            drops.Add(OreTable.Wood(message.PlaceKey, hallowed, WorldGen.crimson, message.Kind == GatherKind.Hardwood, count));
        }
        else
        {
            var context = new MineContext(
                message.MineLevel,
                message.PlaceKey is "Mountain" or "mine:quarry",
                Main.hardMode,
                WorldGen.crimson,
                NPC.downedBoss2,
                NPC.downedMechBossAny,
                NPC.downedMechBoss1 && NPC.downedMechBoss2 && NPC.downedMechBoss3,
                NPC.downedPlantBoss);
            Rock rock = message.Kind switch
            {
                GatherKind.CopperNode => Rock.CopperNode,
                GatherKind.IronNode => Rock.IronNode,
                GatherKind.GoldNode => Rock.GoldNode,
                GatherKind.IridiumNode => Rock.IridiumNode,
                GatherKind.GemNode => Rock.GemNode,
                GatherKind.MysticStone => Rock.MysticStone,
                _ => Rock.Stone,
            };
            var random = new Random(Main.rand.Next());
            for (int i = 0; i < Math.Min(count, 20); i++)
                drops.AddRange(OreTable.Rock(rock, context, random));
        }

        var given = new List<string>();
        foreach (var group in drops.Where(d => d.Count > 0).GroupBy(d => d.Item))
        {
            if (!ItemID.Search.TryGetId(group.Key, out int type))
            {
                BridgeSystem.Instance.Mod.Logger.Warn($"Terradew has no Terraria item called {group.Key}.");
                continue;
            }
            int stack = group.Sum(d => d.Count);
            Inventory.Give(player, type, stack);
            given.Add($"{stack} {Lang.GetItemNameValue(type)}");
        }
        if (given.Count == 0)
            return;
        Inventory.ForceResend();
        BridgeSystem.Instance.Notice("+ " + string.Join(", ", given), 200, 255, 200);
    }

    /// <summary>
    /// Spawns Terraria pot loot at the farmer's tile, in the style of the place they are in. The loot lies on the
    /// ground for the farmer to pick up. If the game refuses to spawn it, the farmer gets coins instead.
    /// </summary>
    private static void Break(Player player, int count)
    {
        Biome biome = PlaceBiome(BridgeSystem.Instance.Stamp.Current);
        int i = (int)(player.Center.X / 16f);
        int j = (int)(player.Center.Y / 16f);
        for (int n = 0; n < Math.Min(count, MaxPots); n++)
        {
            int style = Pots.StyleFor(biome, Main.rand.Next());
            try
            {
                if (!SpawnPotLoot(i, j, style))
                {
                    Inventory.AddCoins(player, Main.rand.Next(20, 80));
                    Inventory.ForceResend();
                }
            }
            catch (Exception error)
            {
                BridgeSystem.Instance.Mod.Logger.Warn($"Terradew could not spawn pot loot in style {style}: {error.Message}");
                Inventory.AddCoins(player, Main.rand.Next(20, 80));
                Inventory.ForceResend();
            }
        }
    }

    private static System.Reflection.MethodInfo? spawnFromPot;
    private static bool spawnFromPotMissing;

    /// <summary>
    /// Terraria's own pot loot. The method is private in tModLoader, so it is found by name once. False when it
    /// cannot be found or called the expected way.
    /// </summary>
    private static bool SpawnPotLoot(int i, int j, int style)
    {
        if (spawnFromPotMissing)
            return false;
        spawnFromPot ??= typeof(WorldGen).GetMethod("SpawnThingsFromPot",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (spawnFromPot == null || spawnFromPot.GetParameters().Length != 5)
        {
            spawnFromPotMissing = true;
            BridgeSystem.Instance.Mod.Logger.Warn("Terradew could not find Terraria's pot loot; pots give coins instead.");
            return false;
        }
        spawnFromPot.Invoke(null, new object[] { i, j, i, j - 1, style });
        return true;
    }

    /// <summary>The biome a Stardew place plays as, with hardmode's Hallow spread applied. Caves when the place is unknown.</summary>
    private static Biome PlaceBiome(WorldStamp.PlaceState? place)
    {
        if (place?.Layout == null)
            return Biome.Cave;
        return Main.hardMode && Places.HallowsInHardmode(place.Key) ? Biome.Hallow : (Biome)place.Layout.Biome;
    }
}
