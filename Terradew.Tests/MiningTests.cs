using System;
using System.Collections.Generic;
using Terradew.Core.Mining;

/// <summary>Checks the Terraria-style ore tables and the tree wood mapping.</summary>
internal static class MiningTests
{
    private static readonly string[] HardmodeOres =
    {
        "CobaltOre", "PalladiumOre", "MythrilOre", "OrichalcumOre", "AdamantiteOre", "TitaniumOre", "ChlorophyteOre",
    };

    private static readonly string[] Gems = { "Amethyst", "Topaz", "Sapphire", "Emerald", "Ruby", "Diamond", "Amber" };

    private static MineContext Mine(int level, bool hardmode = false, bool crimson = false, bool evilBoss = false, bool anyMech = false, bool allMechs = false) =>
        new MineContext(level, false, hardmode, crimson, evilBoss, anyMech, allMechs, false);

    private static MineContext Quarry(bool evilBoss) =>
        new MineContext(0, true, false, false, evilBoss, false, false, false);

    // Rolls one rock many times with a fixed seed and returns every item name that came up.
    private static HashSet<string> Seen(Rock rock, MineContext context, int seed = 1, int rolls = 5000)
    {
        var rng = new Random(seed);
        var names = new HashSet<string>();
        for (int i = 0; i < rolls; i++)
        {
            foreach (Drop drop in OreTable.Rock(rock, context, rng))
                names.Add(drop.Item);
        }
        return names;
    }

    private static int Hits(Rock rock, MineContext context, string item, int rolls)
    {
        var rng = new Random(2);
        int hits = 0;
        for (int i = 0; i < rolls; i++)
        {
            foreach (Drop drop in OreTable.Rock(rock, context, rng))
            {
                if (drop.Item == item)
                    hits++;
            }
        }
        return hits;
    }

    private static void AssertSubset(HashSet<string> seen, string[] allowed, string label)
    {
        foreach (string name in seen)
            T.Assert(Array.IndexOf(allowed, name) >= 0, label + " rolled " + name);
    }

    private static void AssertAllSeen(HashSet<string> seen, string[] wanted, string label)
    {
        foreach (string name in wanted)
            T.Assert(seen.Contains(name), label + " never rolled " + name);
    }

    private static void AssertCounts(Rock rock, MineContext context, int low, int high)
    {
        var rng = new Random(11);
        int min = int.MaxValue;
        int max = int.MinValue;
        for (int i = 0; i < 2000; i++)
        {
            foreach (Drop drop in OreTable.Rock(rock, context, rng))
            {
                min = Math.Min(min, drop.Count);
                max = Math.Max(max, drop.Count);
                T.Assert(drop.Count >= low && drop.Count <= high, rock + " count " + drop.Count + " outside " + low + "-" + high);
            }
        }
        T.Assert(min == low && max == high, rock + " never reached both ends of " + low + "-" + high);
    }

    public static int Run()
    {
        int failed = 0;

        failed += T.Check("stone drops stay inside their floor band", () =>
        {
            AssertSubset(Seen(Rock.Stone, Mine(20)), new[] { "CopperOre", "TinOre", "IronOre", "LeadOre", "LifeCrystal" }, "floors 1-30");
            AssertSubset(Seen(Rock.Stone, Mine(45)), new[] { "IronOre", "LeadOre", "SilverOre", "TungstenOre", "DemoniteOre", "LifeCrystal" }, "floors 31-60");
            AssertSubset(Seen(Rock.Stone, Mine(75, anyMech: true)), new[] { "GoldOre", "PlatinumOre", "MudBlock", "LifeFruit", "LifeCrystal" }, "floors 61-90");
            AssertSubset(Seen(Rock.Stone, Mine(100)), new[] { "Hellstone", "Obsidian", "LifeCrystal" }, "floors 91-120");
            AssertSubset(Seen(Rock.Stone, Mine(150)), new[] { "GoldOre", "PlatinumOre", "LifeCrystal" }, "skull cavern pre-hardmode");

            AssertAllSeen(Seen(Rock.Stone, Mine(45)), new[] { "SilverOre", "TungstenOre", "DemoniteOre" }, "floors 31-60");
            AssertAllSeen(Seen(Rock.Stone, Mine(75)), new[] { "GoldOre", "PlatinumOre", "MudBlock" }, "floors 61-90");
            AssertAllSeen(Seen(Rock.Stone, Mine(100)), new[] { "Hellstone", "Obsidian" }, "floors 91-120");
        });

        failed += T.Check("no Hellstone or Obsidian outside floors 91-120", () =>
        {
            foreach (int level in new[] { 1, 30, 31, 60, 61, 90, 121, 170, 171, 220, 221, 300 })
            {
                HashSet<string> names = Seen(Rock.Stone, Mine(level, hardmode: true, anyMech: true, allMechs: true));
                T.Assert(!names.Contains("Hellstone") && !names.Contains("Obsidian"), "underworld ore on floor " + level);
            }
        });

        failed += T.Check("chlorophyte needs hardmode, all mechs and floors 61-90", () =>
        {
            foreach (int level in new[] { 1, 30, 45, 60, 91, 100, 150, 200, 240 })
            {
                T.Assert(!Seen(Rock.Stone, Mine(level, hardmode: true, anyMech: true, allMechs: true)).Contains("ChlorophyteOre"), "chlorophyte on floor " + level);
            }
            T.Assert(!Seen(Rock.Stone, Mine(75, hardmode: true, anyMech: true, allMechs: false)).Contains("ChlorophyteOre"), "chlorophyte before all mechs");
            T.Assert(!Seen(Rock.Stone, Mine(75, hardmode: false, anyMech: true, allMechs: true)).Contains("ChlorophyteOre"), "chlorophyte before hardmode");
            T.Assert(Seen(Rock.Stone, Mine(75, hardmode: true, anyMech: true, allMechs: true)).Contains("ChlorophyteOre"), "no chlorophyte after all mechs");
        });

        failed += T.Check("no hardmode ores before hardmode", () =>
        {
            var rocks = (Rock[])Enum.GetValues(typeof(Rock));
            foreach (Rock rock in rocks)
            {
                for (int level = 0; level <= 300; level += 7)
                {
                    HashSet<string> names = Seen(rock, Mine(level, evilBoss: true, anyMech: true, allMechs: true), rolls: 300);
                    foreach (string ore in HardmodeOres)
                        T.Assert(!names.Contains(ore), rock + " gave " + ore + " before hardmode on floor " + level);
                }
            }
        });

        failed += T.Check("hardmode ores arrive at their floors", () =>
        {
            AssertAllSeen(Seen(Rock.Stone, Mine(20, hardmode: true)), new[] { "CobaltOre", "PalladiumOre" }, "hardmode floors 1-60");
            AssertAllSeen(Seen(Rock.Stone, Mine(75, hardmode: true)), new[] { "MythrilOre", "OrichalcumOre" }, "hardmode floors 61-120");
            AssertSubset(Seen(Rock.Stone, Mine(75, hardmode: true)), new[] { "GoldOre", "PlatinumOre", "MudBlock", "MythrilOre", "OrichalcumOre", "LifeCrystal" }, "hardmode floors 61-90");
            AssertSubset(Seen(Rock.Stone, Mine(150, hardmode: true)), new[] { "CobaltOre", "PalladiumOre", "MythrilOre", "OrichalcumOre", "LifeCrystal" }, "hardmode skull 121-170");
            AssertAllSeen(Seen(Rock.Stone, Mine(175, hardmode: true)), new[] { "AdamantiteOre", "TitaniumOre" }, "skull floors 171+");
            T.Assert(!Seen(Rock.Stone, Mine(150, hardmode: true)).Contains("AdamantiteOre"), "adamantite before floor 171");
        });

        failed += T.Check("evil ore is Crimtane in crimson and Demonite otherwise", () =>
        {
            HashSet<string> crimson = Seen(Rock.Stone, Mine(40, crimson: true));
            T.Assert(crimson.Contains("CrimtaneOre") && !crimson.Contains("DemoniteOre"), "crimson rolled the wrong evil ore");

            HashSet<string> corruption = Seen(Rock.Stone, Mine(40, crimson: false));
            T.Assert(corruption.Contains("DemoniteOre") && !corruption.Contains("CrimtaneOre"), "corruption rolled the wrong evil ore");
        });

        failed += T.Check("life crystals only from floor 10 down", () =>
        {
            T.Assert(Hits(Rock.Stone, Mine(9), "LifeCrystal", 50000) == 0, "life crystal on floor 9");
            T.Assert(Hits(Rock.Stone, Mine(10), "LifeCrystal", 50000) > 0, "no life crystal on floor 10");
        });

        failed += T.Check("life fruit needs a mech kill and floors 61-90", () =>
        {
            T.Assert(Hits(Rock.Stone, Mine(75, anyMech: false), "LifeFruit", 50000) == 0, "life fruit before any mech");
            T.Assert(Hits(Rock.Stone, Mine(75, anyMech: true), "LifeFruit", 50000) > 0, "no life fruit after a mech");
            T.Assert(Hits(Rock.Stone, Mine(45, anyMech: true), "LifeFruit", 50000) == 0, "life fruit on floor 45");
        });

        failed += T.Check("quarry drops follow the evil boss flag", () =>
        {
            HashSet<string> after = Seen(Rock.Stone, Quarry(evilBoss: true));
            T.Assert(after.Count == 1 && after.Contains("Meteorite"), "quarry after evil boss rolled something other than Meteorite");

            HashSet<string> before = Seen(Rock.Stone, Quarry(evilBoss: false));
            AssertSubset(before, new[] { "CopperOre", "TinOre" }, "quarry before evil boss");
            T.Assert(before.Count == 2, "quarry before evil boss never rolled both copper and tin");

            T.Assert(OreTable.Rock(Rock.Stone, Mine(0), new Random(3)).Count == 0, "stone outside mines and quarry gave a drop");
        });

        failed += T.Check("pair picks stay inside their pair", () =>
        {
            AssertSubset(Seen(Rock.CopperNode, Mine(5)), new[] { "CopperOre", "TinOre" }, "copper node");
            AssertAllSeen(Seen(Rock.CopperNode, Mine(5)), new[] { "CopperOre", "TinOre" }, "copper node");
            AssertSubset(Seen(Rock.IronNode, Mine(5)), new[] { "IronOre", "LeadOre" }, "iron node");
            AssertAllSeen(Seen(Rock.IronNode, Mine(5)), new[] { "IronOre", "LeadOre" }, "iron node");
            AssertSubset(Seen(Rock.GoldNode, Mine(10)), new[] { "GoldOre", "PlatinumOre" }, "gold node floor 10");
            AssertSubset(Seen(Rock.GoldNode, Mine(45)), new[] { "GoldOre", "PlatinumOre", "SilverOre", "TungstenOre" }, "gold node floor 45");
            AssertAllSeen(Seen(Rock.GoldNode, Mine(45)), new[] { "SilverOre", "TungstenOre", "GoldOre", "PlatinumOre" }, "gold node floor 45");
            AssertSubset(Seen(Rock.IridiumNode, Mine(150)), new[] { "GoldOre", "PlatinumOre" }, "iridium node pre-hardmode");
            AssertSubset(Seen(Rock.IridiumNode, Mine(150, hardmode: true)), new[] { "MythrilOre", "OrichalcumOre" }, "iridium node without mechs");
            AssertAllSeen(Seen(Rock.IridiumNode, Mine(150, hardmode: true)), new[] { "MythrilOre", "OrichalcumOre" }, "iridium node without mechs");
            AssertSubset(Seen(Rock.IridiumNode, Mine(150, hardmode: true, anyMech: true)), new[] { "AdamantiteOre", "TitaniumOre" }, "iridium node after a mech");
            AssertAllSeen(Seen(Rock.IridiumNode, Mine(150, hardmode: true, anyMech: true)), new[] { "AdamantiteOre", "TitaniumOre" }, "iridium node after a mech");
            AssertSubset(Seen(Rock.GemNode, Mine(5)), Gems, "gem node");
            AssertAllSeen(Seen(Rock.GemNode, Mine(5)), Gems, "gem node");
        });

        failed += T.Check("node counts stay in their ranges", () =>
        {
            AssertCounts(Rock.CopperNode, Mine(5), 2, 4);
            AssertCounts(Rock.IronNode, Mine(5), 2, 4);
            AssertCounts(Rock.GoldNode, Mine(45), 2, 4);
            AssertCounts(Rock.IridiumNode, Mine(150), 3, 5);
            AssertCounts(Rock.IridiumNode, Mine(150, hardmode: true, anyMech: true), 2, 3);
            AssertCounts(Rock.GemNode, Mine(5), 1, 2);
        });

        failed += T.Check("nodes always drop and Other never does", () =>
        {
            var rng = new Random(9);
            var nodes = new[] { Rock.CopperNode, Rock.IronNode, Rock.GoldNode, Rock.IridiumNode, Rock.GemNode, Rock.MysticStone };
            foreach (Rock node in nodes)
            {
                foreach (int level in new[] { 0, 10, 45, 75, 100, 150, 200 })
                {
                    foreach (bool hardmode in new[] { false, true })
                    {
                        MineContext context = Mine(level, hardmode: hardmode, anyMech: hardmode);
                        for (int i = 0; i < 50; i++)
                        {
                            IReadOnlyList<Drop> drops = OreTable.Rock(node, context, rng);
                            T.Assert(drops.Count > 0, node + " gave nothing on floor " + level);
                        }
                    }
                }
            }

            T.Assert(OreTable.Rock(Rock.Other, Mine(50, hardmode: true, anyMech: true), rng).Count == 0, "Other gave a drop");
        });

        failed += T.Check("mystic stone gives a life crystal and three gold or platinum", () =>
        {
            IReadOnlyList<Drop> drops = OreTable.Rock(Rock.MysticStone, Mine(5), new Random(4));
            T.Assert(drops.Count == 2, "mystic stone gave " + drops.Count + " stacks");
            T.Assert(drops[0] == new Drop("LifeCrystal", 1), "first drop was " + drops[0].Item);
            T.Assert(drops[1].Count == 3 && (drops[1].Item == "GoldOre" || drops[1].Item == "PlatinumOre"), "second drop was " + drops[1].Item + " x" + drops[1].Count);
        });

        failed += T.Check("same seed gives the same drops", () =>
        {
            var a = new Random(42);
            var b = new Random(42);
            MineContext context = Mine(70, hardmode: true, anyMech: true, allMechs: true);
            for (int i = 0; i < 500; i++)
            {
                IReadOnlyList<Drop> first = OreTable.Rock(Rock.Stone, context, a);
                IReadOnlyList<Drop> second = OreTable.Rock(Rock.Stone, context, b);
                T.Assert(first.Count == second.Count, "diverged at roll " + i);
                for (int j = 0; j < first.Count; j++)
                    T.Assert(first[j].Equals(second[j]), "diverged at roll " + i);
            }
        });

        failed += T.Check("wood follows the place and biome flags", () =>
        {
            var cases = new (string Key, bool Hallowed, bool Crimson, bool Mahogany, string Expected)[]
            {
                ("Forest", false, false, false, "Wood"),
                ("Mountain", false, false, false, "Wood"),
                (null!, false, false, false, "Wood"),
                ("Forest", true, false, false, "Pearlwood"),
                ("Forest", false, false, true, "RichMahogany"),
                ("Woods", false, false, false, "Ebonwood"),
                ("Woods", false, true, false, "Shadewood"),
                ("Woods", true, true, false, "Pearlwood"),
                ("Woods", true, false, true, "Pearlwood"),
                ("Beach", false, false, false, "PalmWood"),
                ("Desert", false, false, false, "PalmWood"),
                ("IslandNorth", false, false, false, "PalmWood"),
                ("IslandSouth", false, false, false, "PalmWood"),
            };

            foreach (var (key, hallowed, crimson, mahogany, expected) in cases)
            {
                Drop wood = OreTable.Wood(key, hallowed, crimson, mahogany, 5);
                T.Assert(wood.Item == expected, key + " gave " + wood.Item + ", wanted " + expected);
                T.Assert(wood.Count == 5, key + " count was " + wood.Count);
            }
        });

        return failed;
    }
}
