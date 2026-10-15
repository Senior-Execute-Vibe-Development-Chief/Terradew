using System;
using System.Collections.Generic;

// The enum shares its name with OreTable.Rock, so the bodies refer to the enum through this alias.
using RockKind = Terradew.Core.Mining.Rock;

namespace Terradew.Core.Mining;

/// <summary>What a Stardew rock or resource node is.</summary>
public enum Rock : byte
{
    Stone,
    CopperNode,
    IronNode,
    GoldNode,
    IridiumNode,
    GemNode,
    MysticStone,
    Other,
}

/// <summary>
/// Where and how a rock is broken. MineLevel 1-120 is the Mines, 121+ is Skull Cavern and 0 is not a mine.
/// Quarry marks the Mountain quarry, a meteor site outside the mines.
/// </summary>
public readonly record struct MineContext(int MineLevel, bool Quarry, bool Hardmode, bool Crimson, bool DownedEvilBoss, bool DownedAnyMech, bool DownedAllMechs, bool DownedPlantera);

/// <summary>One item stack a rock gives. Terraria internal item name, resolved later with ItemID.Search.</summary>
public readonly record struct Drop(string Item, int Count);

/// <summary>Terraria-style ore drops for Stardew rocks and nodes, keyed on mine floor and progression.</summary>
public static class OreTable
{
    private static readonly string[] Gems = { "Amethyst", "Topaz", "Sapphire", "Emerald", "Ruby", "Diamond", "Amber" };

    /// <summary>Rolls the drops for one rock broken in the given context.</summary>
    public static IReadOnlyList<Drop> Rock(Rock rock, MineContext c, Random rng)
    {
        if (rng is null)
            throw new ArgumentNullException(nameof(rng));

        var drops = new List<Drop>();
        switch (rock)
        {
            case RockKind.Stone:
                RollStone(c, rng, drops);
                break;
            case RockKind.CopperNode:
                drops.Add(new Drop(Pair(rng, "CopperOre", "TinOre"), Between(rng, 2, 4)));
                break;
            case RockKind.IronNode:
                drops.Add(new Drop(Pair(rng, "IronOre", "LeadOre"), Between(rng, 2, 4)));
                break;
            case RockKind.GoldNode:
                drops.Add(GoldNode(c, rng));
                break;
            case RockKind.IridiumNode:
                drops.Add(IridiumNode(c, rng));
                break;
            case RockKind.GemNode:
                drops.Add(new Drop(Gems[rng.Next(Gems.Length)], Between(rng, 1, 2)));
                break;
            case RockKind.MysticStone:
                drops.Add(new Drop("LifeCrystal", 1));
                drops.Add(new Drop(Pair(rng, "GoldOre", "PlatinumOre"), 3));
                break;
            case RockKind.Other:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(rock));
        }

        return drops;
    }

    /// <summary>Wood from a chopped Stardew tree. Hallowed, then mahogany, then the place key decide the type.</summary>
    public static Drop Wood(string placeKey, bool hallowed, bool crimson, bool mahogany, int count)
    {
        string item;
        if (hallowed)
            item = "Pearlwood";
        else if (mahogany)
            item = "RichMahogany";
        else if (placeKey == "Woods")
            item = crimson ? "Shadewood" : "Ebonwood";
        else if (placeKey == "Beach" || placeKey == "Desert" || placeKey?.StartsWith("Island", StringComparison.Ordinal) == true)
            item = "PalmWood";
        else
            item = "Wood";

        return new Drop(item, count);
    }

    private static void RollStone(MineContext c, Random rng, List<Drop> drops)
    {
        if (c.MineLevel <= 0)
        {
            if (c.Quarry)
                RollQuarry(c, rng, drops);
            return;
        }

        if (c.MineLevel >= 10 && Chance(rng, 0.004))
            drops.Add(new Drop("LifeCrystal", 1));

        if (c.MineLevel <= 30)
        {
            if (Chance(rng, 0.12))
                drops.Add(new Drop(Pair(rng, "CopperOre", "TinOre"), Between(rng, 1, 2)));
            if (Chance(rng, 0.04))
                drops.Add(new Drop(Pair(rng, "IronOre", "LeadOre"), 1));
        }
        else if (c.MineLevel <= 60)
        {
            if (Chance(rng, 0.10))
                drops.Add(new Drop(Pair(rng, "IronOre", "LeadOre"), Between(rng, 1, 2)));
            if (Chance(rng, 0.06))
                drops.Add(new Drop(Pair(rng, "SilverOre", "TungstenOre"), Between(rng, 1, 2)));
            if (Chance(rng, 0.03))
                drops.Add(new Drop(c.Crimson ? "CrimtaneOre" : "DemoniteOre", 1));
        }
        else if (c.MineLevel <= 90)
        {
            if (Chance(rng, 0.08))
                drops.Add(new Drop(Pair(rng, "GoldOre", "PlatinumOre"), Between(rng, 1, 2)));
            if (Chance(rng, 0.06))
                drops.Add(new Drop("MudBlock", Between(rng, 2, 4)));
            if (c.Hardmode && c.DownedAllMechs && Chance(rng, 0.05))
                drops.Add(new Drop("ChlorophyteOre", Between(rng, 1, 2)));
            if (c.DownedAnyMech && Chance(rng, 0.003))
                drops.Add(new Drop("LifeFruit", 1));
        }
        else if (c.MineLevel <= 120)
        {
            if (Chance(rng, 0.12))
                drops.Add(new Drop("Hellstone", Between(rng, 1, 2)));
            if (Chance(rng, 0.08))
                drops.Add(new Drop("Obsidian", Between(rng, 1, 2)));
        }
        else if (!c.Hardmode)
        {
            // Skull Cavern before hardmode.
            if (Chance(rng, 0.08))
                drops.Add(new Drop(Pair(rng, "GoldOre", "PlatinumOre"), Between(rng, 1, 2)));
        }
        else
        {
            // Skull Cavern in hardmode.
            if (Chance(rng, 0.10))
                drops.Add(new Drop(Pair(rng, "CobaltOre", "PalladiumOre"), Between(rng, 1, 2)));
            if (Chance(rng, 0.06))
                drops.Add(new Drop(Pair(rng, "MythrilOre", "OrichalcumOre"), Between(rng, 1, 2)));
            if (c.MineLevel >= 171 && Chance(rng, 0.04))
                drops.Add(new Drop(Pair(rng, "AdamantiteOre", "TitaniumOre"), Between(rng, 1, 2)));
        }

        // Hardmode ores that the Mines floors add on top of their own tables.
        if (c.Hardmode && c.MineLevel <= 60 && Chance(rng, 0.05))
            drops.Add(new Drop(Pair(rng, "CobaltOre", "PalladiumOre"), 1));
        else if (c.Hardmode && c.MineLevel <= 120 && Chance(rng, 0.04))
            drops.Add(new Drop(Pair(rng, "MythrilOre", "OrichalcumOre"), 1));
    }

    private static void RollQuarry(MineContext c, Random rng, List<Drop> drops)
    {
        if (c.DownedEvilBoss)
        {
            if (Chance(rng, 0.15))
                drops.Add(new Drop("Meteorite", Between(rng, 1, 3)));
        }
        else if (Chance(rng, 0.08))
        {
            drops.Add(new Drop(Pair(rng, "CopperOre", "TinOre"), 1));
        }
    }

    private static Drop GoldNode(MineContext c, Random rng)
    {
        int count = Between(rng, 2, 4);

        // Floors 31-60 sometimes give silver or tungsten instead of gold or platinum.
        if (c.MineLevel >= 31 && c.MineLevel <= 60 && Chance(rng, 0.5))
            return new Drop(Pair(rng, "SilverOre", "TungstenOre"), count);

        return new Drop(Pair(rng, "GoldOre", "PlatinumOre"), count);
    }

    private static Drop IridiumNode(MineContext c, Random rng)
    {
        if (!c.Hardmode)
            return new Drop(Pair(rng, "GoldOre", "PlatinumOre"), Between(rng, 3, 5));

        // Best hardmode tier unlocked so far.
        string tier = c.DownedAnyMech
            ? Pair(rng, "AdamantiteOre", "TitaniumOre")
            : Pair(rng, "MythrilOre", "OrichalcumOre");
        return new Drop(tier, Between(rng, 2, 3));
    }

    private static bool Chance(Random rng, double probability) => rng.NextDouble() < probability;

    private static int Between(Random rng, int min, int max) => rng.Next(min, max + 1);

    private static string Pair(Random rng, string first, string second) => rng.Next(2) == 0 ? first : second;
}
