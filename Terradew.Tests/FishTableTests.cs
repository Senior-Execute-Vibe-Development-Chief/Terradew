using System;
using System.Collections.Generic;
using Terradew.Core.Fishing;
using Terradew.Core.World;

/// <summary>Checks the Terraria-style fishing tables and the Stardew power mapping.</summary>
internal static class FishTableTests
{
    private static readonly string[] HardmodeOnly =
    {
        "PrincessFish", "Prismite", "ChaosFish", "ObsidianSwordfish", "ScalyTruffle", "CrystalSerpent", "Bladetongue",
    };

    private static readonly string[] JunkNames = { "OldShoe", "FishingSeaweed", "TinCan" };

    private static FishContext Ctx(Biome biome, Band band, int power = 150, int water = 200, bool night = false, bool hardmode = false) =>
        new FishContext(biome, band, night, hardmode, power, water);

    public static int Run()
    {
        int failed = 0;

        failed += T.Check("tiny pond always rolls junk", () =>
        {
            var rng = new Random(1);
            for (int i = 0; i < 1000; i++)
            {
                FishCatch catchResult = FishTable.Roll(Ctx(Biome.Ocean, Band.Surface, power: 150, water: 10), rng);
                T.Assert(catchResult.Rarity == FishRarity.Junk, "non-junk from tiny pond: " + catchResult.ItemName);
                T.Assert(Array.IndexOf(JunkNames, catchResult.ItemName) >= 0, "unexpected junk name " + catchResult.ItemName);
                T.Assert(!catchResult.Crate, "crate from tiny pond");
            }
        });

        failed += T.Check("ocean crates are OceanCrate", () =>
        {
            var rng = new Random(2);
            int crates = 0;
            for (int i = 0; i < 20000; i++)
            {
                FishCatch catchResult = FishTable.Roll(Ctx(Biome.Ocean, Band.Surface), rng);
                if (!catchResult.Crate)
                    continue;
                crates++;
                T.Assert(catchResult.ItemName == "OceanCrate", "ocean crate was " + catchResult.ItemName);
                T.Assert(catchResult.Stack == 1, "crate stack not 1");
            }
            T.Assert(crates > 0, "no crates rolled in 20000 ocean casts");
        });

        failed += T.Check("biome crates map to their biome name", () =>
        {
            var rng = new Random(3);
            var expected = new Dictionary<Biome, string>
            {
                { Biome.Corruption, "CorruptFishingCrate" },
                { Biome.Jungle, "JungleFishingCrate" },
                { Biome.Dungeon, "DungeonFishingCrate" },
                { Biome.Underworld, "LavaCrate" },
            };
            foreach (KeyValuePair<Biome, string> pair in expected)
            {
                int crates = 0;
                for (int i = 0; i < 5000; i++)
                {
                    FishCatch catchResult = FishTable.Roll(Ctx(pair.Key, Band.Surface), rng);
                    if (!catchResult.Crate)
                        continue;
                    crates++;
                    T.Assert(catchResult.ItemName == pair.Value, pair.Key + " crate was " + catchResult.ItemName);
                }
                T.Assert(crates > 0, "no crates for " + pair.Key);
            }
        });

        failed += T.Check("same seed gives the same catches", () =>
        {
            var a = new Random(42);
            var b = new Random(42);
            FishContext context = Ctx(Biome.Forest, Band.Cavern, power: 80);
            for (int i = 0; i < 500; i++)
            {
                FishCatch first = FishTable.Roll(context, a);
                FishCatch second = FishTable.Roll(context, b);
                T.Assert(first.Equals(second), "diverged at roll " + i);
            }
        });

        failed += T.Check("ocean at power 150 yields Tuna and a VeryRare", () =>
        {
            var rng = new Random(4);
            bool sawTuna = false;
            bool sawVeryRare = false;
            for (int i = 0; i < 20000; i++)
            {
                FishCatch catchResult = FishTable.Roll(Ctx(Biome.Ocean, Band.Surface, power: 150), rng);
                if (catchResult.ItemName == "Tuna")
                    sawTuna = true;
                if (catchResult.Rarity == FishRarity.VeryRare)
                    sawVeryRare = true;
            }
            T.Assert(sawTuna, "no Tuna in 20000 rolls");
            T.Assert(sawVeryRare, "no VeryRare in 20000 rolls");
        });

        failed += T.Check("underworld never returns Bass or Tuna", () =>
        {
            var rng = new Random(5);
            for (int i = 0; i < 20000; i++)
            {
                FishCatch catchResult = FishTable.Roll(Ctx(Biome.Underworld, Band.Underworld, hardmode: i % 2 == 0), rng);
                T.Assert(catchResult.ItemName != "Bass" && catchResult.ItemName != "Tuna", "got " + catchResult.ItemName);
            }
        });

        failed += T.Check("every rolled name is in AllItemNames", () =>
        {
            var rng = new Random(6);
            var known = new HashSet<string>(FishTable.AllItemNames);
            var biomes = (Biome[])Enum.GetValues(typeof(Biome));
            var bands = (Band[])Enum.GetValues(typeof(Band));
            foreach (Biome biome in biomes)
            foreach (Band band in bands)
            foreach (bool night in new[] { false, true })
            foreach (bool hardmode in new[] { false, true })
            {
                for (int i = 0; i < 300; i++)
                {
                    FishContext context = new FishContext(biome, band, night, hardmode, 20 + i % 140, i % 3 == 0 ? 10 : 200);
                    FishCatch catchResult = FishTable.Roll(context, rng);
                    T.Assert(known.Contains(catchResult.ItemName), "unknown name " + catchResult.ItemName);
                }
            }
        });

        failed += T.Check("AllItemNames is distinct", () =>
        {
            var seen = new HashSet<string>();
            foreach (string name in FishTable.AllItemNames)
                T.Assert(seen.Add(name), "duplicate " + name);
        });

        failed += T.Check("hardmode-only fish never appear without hardmode", () =>
        {
            var rng = new Random(7);
            var biomes = (Biome[])Enum.GetValues(typeof(Biome));
            var bands = (Band[])Enum.GetValues(typeof(Band));
            for (int i = 0; i < 10000; i++)
            {
                Biome biome = biomes[i % biomes.Length];
                Band band = bands[(i / biomes.Length) % bands.Length];
                FishCatch catchResult = FishTable.Roll(Ctx(biome, band, power: 150, hardmode: false), rng);
                T.Assert(Array.IndexOf(HardmodeOnly, catchResult.ItemName) < 0, "hardmode fish " + catchResult.ItemName + " without hardmode");
            }
        });

        failed += T.Check("PowerFromStardew follows the formula and clamps inputs", () =>
        {
            T.Assert(FishTable.PowerFromStardew(0, 0, false, false) == 10, "base power");
            T.Assert(FishTable.PowerFromStardew(1, 2, true, false) == 10 + 15 + 8 + 10, "fiberglass with level and bait");
            T.Assert(FishTable.PowerFromStardew(3, 15, true, true) == 130, "max raw power");
            T.Assert(FishTable.PowerFromStardew(99, 99, true, true) == FishTable.PowerFromStardew(3, 15, true, true), "high inputs not clamped");
            T.Assert(FishTable.PowerFromStardew(-5, -3, false, false) == FishTable.PowerFromStardew(0, 0, false, false), "low inputs not clamped");
            T.Assert(FishTable.PowerFromStardew(0, 0, false, false) >= 5, "below floor");
            T.Assert(FishTable.PowerFromStardew(3, 15, true, true) <= 150, "above ceiling");
        });

        failed += T.Check("difficulty and motion match the table", () =>
        {
            int[] difficulty = { 15, 30, 50, 70, 85, 100 };
            int[] motion = { 2, 0, 4, 3, 1, 1 };
            for (int i = 0; i < 6; i++)
            {
                var rarity = (FishRarity)i;
                T.Assert(FishTable.Difficulty(rarity) == difficulty[i], "difficulty " + rarity);
                T.Assert(FishTable.Motion(rarity) == motion[i], "motion " + rarity);
            }
        });

        return failed;
    }
}
