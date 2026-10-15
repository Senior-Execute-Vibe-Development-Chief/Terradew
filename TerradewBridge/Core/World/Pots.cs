using System;

namespace Terradew.Core.World;

/// <summary>
/// Stardew's breakable mine barrels and crates are drawn as Terraria pots and give Terraria pot loot.
/// Terraria numbers each pot look as a style of its Pots tile; each biome has a run of them.
/// </summary>
public static class Pots
{
    /// <summary>The Terraria pot style for a container in this biome. The variant (any number, such as a tile hash) picks one of the biome's looks.</summary>
    public static int StyleFor(Biome biome, int variant)
    {
        (int first, int count) = biome switch
        {
            Biome.Jungle => (7, 3),
            Biome.Dungeon => (10, 3),
            Biome.Underworld => (13, 3),
            Biome.Corruption => (16, 3),
            Biome.Desert => (25, 3),
            Biome.Temple => (28, 3),
            _ => (0, 4),
        };
        return first + (int)((uint)variant % (uint)count);
    }
}
