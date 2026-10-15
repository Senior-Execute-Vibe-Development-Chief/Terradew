using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terradew.Core.Protocol;
using Terradew.Core.World;

internal static class CaveTests
{
    public static int Run()
    {
        int failed = 0;
        failed += T.Check("every fifth mine floor is safe, and the entrance is not a mine floor", () =>
        {
            T.Assert(CaveRules.IsSafeFloor(5) && CaveRules.IsSafeFloor(10) && CaveRules.IsSafeFloor(120), "fives are safe");
            T.Assert(!CaveRules.IsSafeFloor(0) && !CaveRules.IsSafeFloor(4) && !CaveRules.IsSafeFloor(11), "other floors are not");
        });
        failed += T.Check("creatures follow the floor's biome and band", () =>
        {
            T.Assert(CaveRules.SpawnsFor(Biome.Cave, Band.Underground).Contains("CaveBat"), "upper mines have cave bats");
            T.Assert(CaveRules.SpawnsFor(Biome.Cave, Band.Cavern).Contains("GiantBat"), "caverns have giant bats");
            T.Assert(CaveRules.SpawnsFor(Biome.Jungle, Band.Cavern).Contains("Hornet"), "jungle floors have hornets");
            T.Assert(CaveRules.SpawnsFor(Biome.Underworld, Band.Underworld).Contains("LavaSlime"), "underworld has lava slimes");
            T.Assert(CaveRules.SpawnsFor(Biome.Dungeon, Band.Cavern).Contains("DarkCaster"), "skull cavern has dark casters");
            T.Assert(CaveRules.SpawnsFor(Biome.Forest, Band.Surface).Length == 0, "the surface gets nothing from the caves");
        });
        failed += T.Check("chests take their biome's kind and get deeper more often", () =>
        {
            T.Assert(CaveRules.KindFor(Biome.Cave, Band.Underground) == CaveChestKind.Wooden, "upper mines are wooden");
            T.Assert(CaveRules.KindFor(Biome.Cave, Band.Cavern) == CaveChestKind.Gold, "caverns are gold");
            T.Assert(CaveRules.KindFor(Biome.Jungle, Band.Cavern) == CaveChestKind.Jungle, "jungle is jungle");
            T.Assert(CaveRules.KindFor(Biome.Dungeon, Band.Cavern) == CaveChestKind.Dungeon, "dungeon is dungeon");
            T.Assert(CaveRules.ChestChance(100) > CaveRules.ChestChance(1), "deeper floors are likelier");
            T.Assert(CaveRules.ChestChance(1000) <= 0.6f && CaveRules.ChestChance(1) >= 0.15f, "chests stay between rare and common");
        });
        failed += T.Check("a chest rolls copper and items in its range", () =>
        {
            foreach (CaveChestKind kind in Enum.GetValues(typeof(CaveChestKind)))
            {
                var random = new Random(7);
                for (int i = 0; i < 200; i++)
                {
                    List<(string Item, int Count)> items = CaveRules.Roll(kind, random, out int copper);
                    T.Assert(copper > 0, $"{kind} holds copper");
                    T.Assert(items.Count >= 2 && items.All(x => x.Count > 0), $"{kind} rolls stacks");
                }
            }
        });
        failed += T.Check("every chest item and loot name is a Terraria item", () =>
        {
            var names = new HashSet<string>(File.ReadAllLines(TerrariaItemsPath()).Where(l => l.Length > 0), StringComparer.Ordinal);
            foreach (CaveChestKind kind in Enum.GetValues(typeof(CaveChestKind)))
            {
                T.Assert(names.Contains(CaveRules.ChestItem(kind)), $"{CaveRules.ChestItem(kind)} is a Terraria item");
                var random = new Random(3);
                foreach ((string item, int _) in CaveRules.Roll(kind, random, out _))
                    T.Assert(names.Contains(item), $"{item} is a Terraria item");
            }
            foreach (Biome biome in Enum.GetValues(typeof(Biome)))
                foreach (string creature in CaveRules.SpawnsFor(biome, Band.Cavern))
                    T.Assert(creature.Length > 0, "creature named");
        });
        failed += T.Check("cave chests and their opening round-trip", () =>
        {
            var chests = new CaveChests { PlaceKey = "mine:upper#1", Floor = 7, Chests = { new CaveChest(12, 30, 1, 1234), new CaveChest(3, 4, 0, 99) } };
            var copy = (CaveChests)ProtocolTests.Roundtrip(chests);
            T.Assert(copy.PlaceKey == "mine:upper#1" && copy.Floor == 7, "floor fields");
            T.Assert(copy.Chests.Count == 2 && copy.Chests[0] == new CaveChest(12, 30, 1, 1234) && copy.Chests[1].ItemType == 99, "chests");
            var open = (OpenCaveChest)ProtocolTests.Roundtrip(new OpenCaveChest { Kind = 3 });
            T.Assert(open.Kind == 3, "kind");
        });
        failed += T.Check("dart traps stay off the entrance and safe floors, and never exceed three", () =>
        {
            var random = new Random(11);
            for (int i = 0; i < 300; i++)
            {
                T.Assert(CaveRules.TrapCount(0, random) == 0, "the entrance has no traps");
                T.Assert(CaveRules.TrapCount(5, random) == 0 && CaveRules.TrapCount(10, random) == 0, "safe floors have no traps");
                for (int floor = 1; floor <= 60; floor++)
                {
                    int count = CaveRules.TrapCount(floor, random);
                    T.Assert(count >= 0 && count <= 3, $"floor {floor} has 0..3 traps");
                    T.Assert(!CaveRules.IsSafeFloor(floor) || count == 0, $"safe floor {floor} has none");
                }
            }
        });
        failed += T.Check("deeper floors get more dart traps", () =>
        {
            var random = new Random(5);
            int shallow = 0, deep = 0;
            for (int i = 0; i < 2000; i++)
            {
                int few = CaveRules.TrapCount(3, random);
                T.Assert(few <= 1, "the top floors have at most one trap");
                shallow += few;
                deep += CaveRules.TrapCount(41, random);
            }
            T.Assert(deep > shallow, "floor 41 has more traps than floor 3");
            T.Assert(CaveRules.TrapDamage(1) >= 20 && CaveRules.TrapDamage(500) <= 40, "dart damage stays in range");
        });
        failed += T.Check("dart trap plates round-trip", () =>
        {
            var plates = new CaveTraps { PlaceKey = "mine:upper#1", Floor = 7, Plates = { new CaveTrapPlate(12, 30), new CaveTrapPlate(3, 4) } };
            var copy = (CaveTraps)ProtocolTests.Roundtrip(plates);
            T.Assert(copy.PlaceKey == "mine:upper#1" && copy.Floor == 7, "floor fields");
            T.Assert(copy.Plates.Count == 2 && copy.Plates[0] == new CaveTrapPlate(12, 30) && copy.Plates[1] == new CaveTrapPlate(3, 4), "plates");
            var empty = (CaveTraps)ProtocolTests.Roundtrip(new CaveTraps { PlaceKey = "mine:upper#1", Floor = 8 });
            T.Assert(empty.Plates.Count == 0 && empty.Floor == 8, "an empty list clears a floor");
        });
        return failed;
    }

    private static string TerrariaItemsPath()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            string candidate = Path.Combine(dir, "Terradew.Tests", "Data", "TerrariaItems.txt");
            if (File.Exists(candidate))
                return candidate;
            string local = Path.Combine(dir, "Data", "TerrariaItems.txt");
            if (File.Exists(local))
                return local;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("TerrariaItems.txt");
    }
}
