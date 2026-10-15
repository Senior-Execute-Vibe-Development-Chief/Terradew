using System;

namespace Terradew.Core.World;

/// <summary>Which Terraria biome a Stardew place is stamped as.</summary>
public enum Biome : byte
{
    Forest = 0,
    Corruption = 1,
    Desert = 2,
    Ocean = 3,
    Jungle = 4,
    Underworld = 5,
    Dungeon = 6,
    Temple = 7,
    Island = 8,
    Cave = 9,
    Hallow = 10,
}

/// <summary>Which Terraria depth layer a Stardew place sits in.</summary>
public enum Band : byte
{
    Surface = 0,
    Underground = 1,
    Cavern = 2,
    Underworld = 3,
}

/// <summary>
/// A Stardew place as Terraria sees it. Mine floors share a few reusable slots,
/// because Stardew builds them fresh on every visit.
/// </summary>
public readonly record struct Place(string Key, Biome Biome, Band Band, bool Spawns, bool IsMineSlot);

public static class Places
{
    public const int QuarryMineLevel = 77377;

    /// <summary>Places that turn Hallow when hardmode starts: the Quarry Mine underground, the Railroad on the surface.</summary>
    public static bool HallowsInHardmode(string key) => key is "mine:quarry" or "Railroad";

    /// <summary>The biome a place has right now. Hardmode spreads the Hallow into its places.</summary>
    public static Biome Current(Place place, bool hardmode)
        => hardmode && HallowsInHardmode(place.Key) ? Biome.Hallow : place.Biome;

    public static Place Classify(string locationName, bool isOutdoors, int mineLevel)
    {
        locationName ??= "";
        if (mineLevel == QuarryMineLevel)
            return new Place("mine:quarry", Biome.Cave, Band.Underground, true, true);
        if (mineLevel >= 1)
            return Mine(mineLevel);

        if (locationName.StartsWith("VolcanoDungeon", StringComparison.Ordinal))
            return new Place(locationName, Biome.Island, Band.Underground, true, false);
        if (locationName.StartsWith("Island", StringComparison.Ordinal)
            || locationName is "Caldera" or "QiNutRoom" or "CaptainRoom")
            return new Place(locationName, Biome.Island, Band.Surface, isOutdoors, false);

        return locationName switch
        {
            "Woods" => new Place(locationName, Biome.Corruption, Band.Surface, true, false),
            "Desert" => new Place(locationName, Biome.Desert, Band.Surface, true, false),
            "SkullCave" => new Place(locationName, Biome.Desert, Band.Surface, false, false),
            "Beach" => new Place(locationName, Biome.Ocean, Band.Surface, true, false),
            "BugLand" => new Place(locationName, Biome.Cave, Band.Underground, true, false),
            "Sewer" => new Place(locationName, Biome.Cave, Band.Underground, false, false),
            "WitchSwamp" or "Mine" or "Summit" => new Place(locationName, Biome.Forest, Band.Surface, false, false),
            _ => new Place(locationName, Biome.Forest, Band.Surface, isOutdoors, false),
        };
    }

    private static Place Mine(int level)
    {
        int slot = level % 2;
        if (level <= 30)
            return new Place($"mine:upper#{slot}", Biome.Cave, Band.Underground, true, true);
        if (level <= 60)
            return new Place($"mine:cavern#{slot}", Biome.Cave, Band.Cavern, true, true);
        if (level <= 90)
            return new Place($"mine:jungle#{slot}", Biome.Jungle, Band.Cavern, true, true);
        if (level <= 120)
            return new Place($"mine:hell#{slot}", Biome.Underworld, Band.Underworld, true, true);
        if (level <= 220)
            return new Place($"skull:dungeon#{slot}", Biome.Dungeon, Band.Cavern, true, true);
        return new Place($"skull:temple#{slot}", Biome.Temple, Band.Cavern, true, true);
    }
}
