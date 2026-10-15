using System;
using System.Collections.Generic;
using System.Linq;
using Terradew.Core.Mining;
using Terradew.Core.Protocol;
using Terradew.Core.World;
using Terraria;
using Terraria.ID;

namespace TerradewBridge;

/// <summary>Breaking Stardew rocks and gathering Stardew wood also gives Terraria materials (see <see cref="OreTable"/>).</summary>
public static class Gathering
{
    public static void Receive(Player player, Gathered message)
    {
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
}
