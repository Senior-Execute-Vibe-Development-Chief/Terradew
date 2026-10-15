using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Terradew.Core.World;

namespace Terradew.Core.Fishing;

/// <summary>Terraria-style catch tiers, lowest to highest.</summary>
public enum FishRarity : byte
{
    Junk,
    Common,
    Uncommon,
    Rare,
    VeryRare,
    Legendary,
}

/// <summary>Where and how a cast happens.</summary>
public readonly record struct FishContext(Biome Biome, Band Band, bool Night, bool Hardmode, int Power, int WaterTiles);

/// <summary>One thing the line pulled up. Terraria internal item name, always stack 1 here.</summary>
public readonly record struct FishCatch(string ItemName, int Stack, FishRarity Rarity, bool Crate);

/// <summary>Terraria-style fishing rolls, mapped onto Stardew's rod and skill inputs.</summary>
public static class FishTable
{
    private static readonly string[] JunkNames = { "OldShoe", "FishingSeaweed", "TinCan" };

    private delegate bool Condition(FishContext context);

    private readonly struct Entry
    {
        public Entry(string name, FishRarity rarity, Condition allows)
        {
            Name = name;
            Rarity = rarity;
            Allows = allows;
        }

        public string Name { get; }
        public FishRarity Rarity { get; }
        public Condition Allows { get; }
    }

    private static readonly Entry[] Entries =
    {
        // Common
        new Entry("Bass", FishRarity.Common, c => IsSurface(c) && c.Biome != Biome.Ocean),
        new Entry("Trout", FishRarity.Common, c => IsSurface(c) && c.Biome != Biome.Ocean),
        new Entry("Tuna", FishRarity.Common, c => c.Biome == Biome.Ocean),
        new Entry("RedSnapper", FishRarity.Common, c => c.Biome == Biome.Ocean),
        new Entry("NeonTetra", FishRarity.Common, c => c.Biome == Biome.Jungle),
        new Entry("AtlanticCod", FishRarity.Common, c => (c.Biome == Biome.Cave || IsUndergroundBand(c)) && c.Biome != Biome.Underworld),
        new Entry("SpecularFish", FishRarity.Common, c => c.Band == Band.Cavern || c.Band == Band.Underworld),
        new Entry("Salmon", FishRarity.Common, c => c.Biome == Biome.Forest && IsSurface(c)),

        // Uncommon
        new Entry("ArmoredCavefish", FishRarity.Uncommon, IsUndergroundBand),
        new Entry("Damselfish", FishRarity.Uncommon, c => (c.Biome == Biome.Island || (c.Biome == Biome.Forest && IsSurface(c))) && !c.Night),
        new Entry("Ebonkoi", FishRarity.Uncommon, c => c.Biome == Biome.Corruption),
        new Entry("DoubleCod", FishRarity.Uncommon, c => c.Biome == Biome.Jungle),
        new Entry("Stinkfish", FishRarity.Uncommon, IsUndergroundBand),
        new Entry("PrincessFish", FishRarity.Uncommon, c => c.Hardmode),

        // Rare
        new Entry("VariegatedLardfish", FishRarity.Rare, c => c.Biome == Biome.Jungle),
        new Entry("Obsidifish", FishRarity.Rare, IsUnderworld),
        new Entry("Prismite", FishRarity.Rare, c => c.Hardmode),
        new Entry("Rockfish", FishRarity.Rare, IsUndergroundBand),
        new Entry("ZephyrFish", FishRarity.Rare, IsSurface),
        new Entry("FrogLeg", FishRarity.Rare, IsSurface),
        new Entry("BalloonPufferfish", FishRarity.Rare, c => IsSurface(c) && !c.Night),

        // Very rare
        new Entry("FlarefinKoi", FishRarity.VeryRare, IsUnderworld),
        new Entry("ChaosFish", FishRarity.VeryRare, c => c.Hardmode && c.Band == Band.Cavern),
        new Entry("ReaverShark", FishRarity.VeryRare, c => c.Biome == Biome.Ocean),
        new Entry("SawtoothShark", FishRarity.VeryRare, c => c.Biome == Biome.Ocean),
        new Entry("PurpleClubberfish", FishRarity.VeryRare, c => c.Biome == Biome.Corruption),
        new Entry("GoldenCarp", FishRarity.VeryRare, IsUndergroundBand),

        // Legendary
        new Entry("ObsidianSwordfish", FishRarity.Legendary, c => IsUnderworld(c) && c.Hardmode),
        new Entry("ScalyTruffle", FishRarity.Legendary, c => c.Hardmode && c.Band == Band.Cavern),
        new Entry("CrystalSerpent", FishRarity.Legendary, c => c.Hardmode),
        new Entry("Bladetongue", FishRarity.Legendary, c => c.Hardmode),
    };

    private static readonly IReadOnlyList<string> AllNamesList = BuildAllItemNames();

    /// <summary>Every item name <see cref="Roll"/> can return, distinct.</summary>
    public static IReadOnlyList<string> AllItemNames => AllNamesList;

    /// <summary>
    /// Maps Stardew's rod tier (0 bamboo/training, 1 fiberglass, 2 iridium, 3 advanced iridium),
    /// fishing level and bait/tackle onto a Terraria-style power value.
    /// </summary>
    public static int PowerFromStardew(int rodTier, int fishingLevel, bool hasBait, bool hasTackle)
    {
        int tier = Clamp(rodTier, 0, 3);
        int level = Clamp(fishingLevel, 0, 15);
        int power = 10 + tier * 15 + level * 4 + (hasBait ? 10 : 0) + (hasTackle ? 5 : 0);
        return Clamp(power, 5, 150);
    }

    /// <summary>Rolls one catch for the given context.</summary>
    public static FishCatch Roll(FishContext context, Random rng)
    {
        if (rng is null)
            throw new ArgumentNullException(nameof(rng));

        if (context.WaterTiles < 75)
            return Junk(rng);

        if (context.Power < 50 && rng.Next(10) == 0)
            return Junk(rng);

        if (rng.Next(10) == 0)
            return RollCrate(context, rng);

        int p = Math.Max(1, context.Power);
        bool uncommon = rng.Next(Math.Max(1, 150 / p)) == 0;
        bool rare = rng.Next(Math.Max(1, 300 / p)) == 0;
        bool veryRare = rng.Next(Math.Max(1, 1050 / p)) == 0;
        bool legendary = rng.Next(Math.Max(1, 2250 / p)) == 0;

        if (legendary)
        {
            string? name = PickFish(context, FishRarity.Legendary, rng);
            if (name != null)
                return Fish(name, FishRarity.Legendary);
        }

        if (veryRare)
        {
            string? name = PickFish(context, FishRarity.VeryRare, rng);
            if (name != null)
                return Fish(name, FishRarity.VeryRare);
        }

        if (rare)
        {
            string? name = PickFish(context, FishRarity.Rare, rng);
            if (name != null)
                return Fish(name, FishRarity.Rare);
        }

        if (uncommon)
        {
            string? name = PickFish(context, FishRarity.Uncommon, rng);
            if (name != null)
                return Fish(name, FishRarity.Uncommon);
        }

        string? common = PickFish(context, FishRarity.Common, rng);
        if (common != null)
            return Fish(common, FishRarity.Common);

        // No common candidate exists for this context (e.g. cavern or underworld band outside
        // the named biomes), so the line comes up with junk instead.
        return Junk(rng);
    }

    /// <summary>Difficulty of the Stardew bobber minigame for a rarity.</summary>
    public static int Difficulty(FishRarity rarity)
    {
        switch (rarity)
        {
            case FishRarity.Junk: return 15;
            case FishRarity.Common: return 30;
            case FishRarity.Uncommon: return 50;
            case FishRarity.Rare: return 70;
            case FishRarity.VeryRare: return 85;
            case FishRarity.Legendary: return 100;
            default: throw new ArgumentOutOfRangeException(nameof(rarity));
        }
    }

    /// <summary>Stardew BobberBar motion type: 0 mixed, 1 dart, 2 smooth, 3 sinker, 4 floater.</summary>
    public static int Motion(FishRarity rarity)
    {
        switch (rarity)
        {
            case FishRarity.Junk: return 2;
            case FishRarity.Common: return 0;
            case FishRarity.Uncommon: return 4;
            case FishRarity.Rare: return 3;
            case FishRarity.VeryRare: return 1;
            case FishRarity.Legendary: return 1;
            default: throw new ArgumentOutOfRangeException(nameof(rarity));
        }
    }

    private static bool IsSurface(FishContext context) => context.Band == Band.Surface;

    private static bool IsUndergroundBand(FishContext context) =>
        context.Band == Band.Underground || context.Band == Band.Cavern;

    private static bool IsUnderworld(FishContext context) =>
        context.Band == Band.Underworld || context.Biome == Biome.Underworld;

    private static string? PickFish(FishContext context, FishRarity rarity, Random rng)
    {
        var candidates = new List<string>();
        foreach (Entry entry in Entries)
        {
            if (entry.Rarity == rarity && entry.Allows(context))
                candidates.Add(entry.Name);
        }

        if (candidates.Count == 0)
            return null;

        return candidates[rng.Next(candidates.Count)];
    }

    private static FishCatch Fish(string name, FishRarity rarity) => new FishCatch(name, 1, rarity, false);

    private static FishCatch Junk(Random rng) =>
        new FishCatch(JunkNames[rng.Next(JunkNames.Length)], 1, FishRarity.Junk, false);

    private static FishCatch RollCrate(FishContext context, Random rng)
    {
        switch (context.Biome)
        {
            case Biome.Corruption:
                return new FishCatch("CorruptFishingCrate", 1, FishRarity.Uncommon, true);
            case Biome.Jungle:
                return new FishCatch("JungleFishingCrate", 1, FishRarity.Uncommon, true);
            case Biome.Dungeon:
                return new FishCatch("DungeonFishingCrate", 1, FishRarity.Uncommon, true);
            case Biome.Ocean:
                return new FishCatch("OceanCrate", 1, FishRarity.Uncommon, true);
            case Biome.Underworld:
                return new FishCatch("LavaCrate", 1, FishRarity.Uncommon, true);
        }

        int roll = rng.Next(100);
        if (roll < 70)
            return new FishCatch("WoodenCrate", 1, FishRarity.Common, true);
        if (roll < 95)
            return new FishCatch("IronCrate", 1, FishRarity.Uncommon, true);
        return new FishCatch("GoldenCrate", 1, FishRarity.Rare, true);
    }

    private static IReadOnlyList<string> BuildAllItemNames()
    {
        var names = new List<string>();
        void Add(string name)
        {
            if (!names.Contains(name))
                names.Add(name);
        }

        foreach (string junk in JunkNames)
            Add(junk);

        foreach (string crate in new[]
        {
            "CorruptFishingCrate", "JungleFishingCrate", "DungeonFishingCrate", "OceanCrate", "LavaCrate",
            "WoodenCrate", "IronCrate", "GoldenCrate",
        })
        {
            Add(crate);
        }

        foreach (Entry entry in Entries)
            Add(entry.Name);

        return new ReadOnlyCollection<string>(names);
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
}
