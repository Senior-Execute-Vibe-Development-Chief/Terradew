using System;
using System.Collections.Generic;

namespace Terradew.Core.World;

/// <summary>The kinds of chest that can lie on a mine floor. Each has its own Terraria chest item and loot.</summary>
public enum CaveChestKind : byte
{
    Wooden = 0,
    Gold = 1,
    Jungle = 2,
    Dungeon = 3,
}

/// <summary>One kind of thing a chest can hold: a Terraria item by name, a stack from Min to Max, and how often it comes up.</summary>
public readonly record struct LootEntry(string Item, int Min, int Max, int Weight);

/// <summary>What Terraria puts on the Stardew mine floors: its creatures and its chests.</summary>
public static class CaveRules
{
    /// <summary>Every fifth mine floor is a safe floor, where Terraria creatures never spawn.</summary>
    public const int SafeEvery = 5;
    /// <summary>Most Terraria creatures alive in one floor's place at a time, so a long stay cannot fill it.</summary>
    public const int SpawnCap = 6;
    /// <summary>Terraria ticks between spawn attempts on a mine floor: five seconds.</summary>
    public const int SpawnEvery = 300;
    /// <summary>Chance that an attempt goes ahead when its tick comes round.</summary>
    public const float SpawnChance = 0.5f;

    /// <summary>Whether the floor is a safe floor. The mine entrance (floor 0) is never a mine floor, so it is not counted here.</summary>
    public static bool IsSafeFloor(int floor) => floor > 0 && floor % SafeEvery == 0;

    /// <summary>The Terraria creatures, by name, that wander a mine floor of this biome and band. Empty when none belong there.</summary>
    public static string[] SpawnsFor(Biome biome, Band band) => biome switch
    {
        Biome.Jungle => new[] { "JungleBat", "Hornet", "ManEater", "Derpling", "JungleSlime" },
        Biome.Underworld => new[] { "LavaSlime", "FireImp", "Hellbat", "Demon" },
        Biome.Dungeon => new[] { "BlueArmoredBones", "DarkCaster", "RaggedCaster", "Necromancer" },
        Biome.Temple => new[] { "Lihzahrd", "FlyingSnake" },
        Biome.Hallow => new[] { "Pixie", "RainbowSlime" },
        Biome.Cave when band == Band.Cavern => new[] { "GiantBat", "SkeletonArcher", "UndeadViking", "Skeleton" },
        Biome.Cave => new[] { "CaveBat", "BlueSlime", "Skeleton", "UndeadMiner", "Zombie" },
        _ => Array.Empty<string>(),
    };

    /// <summary>Tiles between a dart trap's plate and its dart source, counting the source: the line that must lie open.</summary>
    public const int TrapLine = 6;
    /// <summary>Fewest tiles from the farmer a dart trap's plate may lie, so one never triggers on arrival.</summary>
    public const int TrapMinDistance = 8;
    /// <summary>Terraria ticks a dart trap waits after firing before it is armed again: two seconds.</summary>
    public const int TrapRearm = 120;
    /// <summary>Pixels per Terraria tick a dart flies.</summary>
    public const float DartSpeed = 8f;
    /// <summary>The four directions a dart trap's line can run, as tile steps.</summary>
    public static readonly (int Dx, int Dy)[] TrapDirections = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>
    /// How many dart-trap plates a fresh mine floor gets. None on the entrance or a safe floor. Deeper floors get more,
    /// up to three, and a floor sometimes gets none.
    /// </summary>
    public static int TrapCount(int floor, Random random)
    {
        if (floor <= 0 || IsSafeFloor(floor))
            return 0;
        if (random.NextDouble() < 0.4)
            return 0;
        int most = floor switch
        {
            < 10 => 1,
            < 30 => 2,
            _ => 3,
        };
        return random.Next(1, most + 1);
    }

    /// <summary>The damage of a dart: Terraria's poison dart trap, a little more on deeper floors.</summary>
    public static int TrapDamage(int floor) => Math.Clamp(20 + floor / 5, 20, 40);

    /// <summary>The chance that a fresh mine floor has a chest: about one in seven at the top, more likely deeper, never certain.</summary>
    public static float ChestChance(int floor) => Math.Clamp(0.15f + floor / 200f, 0.15f, 0.6f);

    /// <summary>The chest a floor's biome and band get: jungle chests in the jungle, blue dungeon chests in the dungeon, gold in the caverns, wooden up top.</summary>
    public static CaveChestKind KindFor(Biome biome, Band band)
    {
        if (biome == Biome.Jungle)
            return CaveChestKind.Jungle;
        if (biome == Biome.Dungeon)
            return CaveChestKind.Dungeon;
        return band == Band.Underground ? CaveChestKind.Wooden : CaveChestKind.Gold;
    }

    /// <summary>The Terraria item that stands for a chest kind. Its sprite is how the chest is drawn on the Stardew map.</summary>
    public static string ChestItem(CaveChestKind kind) => kind switch
    {
        CaveChestKind.Gold => "GoldChest",
        CaveChestKind.Jungle => "JungleChest",
        CaveChestKind.Dungeon => "BlueDungeonChest",
        _ => "Chest",
    };

    /// <summary>
    /// Opens a chest: the copper it holds, and the items it rolls. Each roll picks one entry by weight and a stack within its range.
    /// The loot follows vanilla's chest tiers in spirit: copper and bars, potions and torches, and now and then a rare accessory.
    /// </summary>
    public static List<(string Item, int Count)> Roll(CaveChestKind kind, Random random, out int copper)
    {
        (int coinMin, int coinMax, int rolls, LootEntry[] table) = Table(kind);
        copper = random.Next(coinMin, coinMax + 1);
        var items = new List<(string Item, int Count)>();
        int total = 0;
        foreach (LootEntry entry in table)
            total += entry.Weight;
        for (int i = 0; i < rolls; i++)
        {
            int pick = random.Next(total);
            foreach (LootEntry entry in table)
            {
                if (pick >= entry.Weight)
                {
                    pick -= entry.Weight;
                    continue;
                }
                items.Add((entry.Item, random.Next(entry.Min, entry.Max + 1)));
                break;
            }
        }
        return items;
    }

    private static (int CoinMin, int CoinMax, int Rolls, LootEntry[] Table) Table(CaveChestKind kind) => kind switch
    {
        CaveChestKind.Gold => (150, 400, 3, new[]
        {
            new LootEntry("GoldBar", 2, 5, 4),
            new LootEntry("HealingPotion", 2, 4, 2),
            new LootEntry("ManaPotion", 2, 4, 2),
            new LootEntry("Torch", 15, 30, 2),
            new LootEntry("BandofRegeneration", 1, 1, 1),
            new LootEntry("MagicMirror", 1, 1, 1),
            new LootEntry("CloudinaBottle", 1, 1, 1),
            new LootEntry("HermesBoots", 1, 1, 1),
            new LootEntry("EnchantedBoomerang", 1, 1, 1),
            new LootEntry("ShoeSpikes", 1, 1, 1),
            new LootEntry("FlareGun", 1, 1, 1),
        }),
        CaveChestKind.Jungle => (100, 300, 2, new[]
        {
            new LootEntry("BottledHoney", 1, 3, 3),
            new LootEntry("JungleSpores", 2, 5, 3),
            new LootEntry("GoldBar", 2, 4, 2),
            new LootEntry("HealingPotion", 2, 4, 2),
            new LootEntry("Vine", 1, 1, 1),
        }),
        CaveChestKind.Dungeon => (200, 500, 3, new[]
        {
            new LootEntry("GoldBar", 3, 6, 3),
            new LootEntry("HealingPotion", 2, 4, 2),
            new LootEntry("ManaPotion", 2, 4, 2),
            new LootEntry("Torch", 15, 30, 2),
            new LootEntry("MagicMirror", 1, 1, 1),
            new LootEntry("HermesBoots", 1, 1, 1),
            new LootEntry("FlareGun", 1, 1, 1),
        }),
        _ => (50, 150, 2, new[]
        {
            new LootEntry("Torch", 10, 25, 3),
            new LootEntry("HealingPotion", 1, 3, 3),
            new LootEntry("ManaPotion", 1, 3, 2),
            new LootEntry("CopperBar", 2, 6, 2),
            new LootEntry("Rope", 10, 25, 2),
            new LootEntry("IronBar", 2, 4, 1),
            new LootEntry("Bomb", 2, 5, 1),
        }),
    };
}
