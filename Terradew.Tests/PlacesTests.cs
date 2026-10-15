using Terradew.Core.World;

internal static class PlacesTests
{
    public static int Run()
    {
        int failed = 0;
        failed += T.Check("stardew places land in the planned terraria layers", () =>
        {
            T.Assert(Places.Classify("Farm", true, 0) == new Place("Farm", Biome.Forest, Band.Surface, true, false), "farm is surface");
            T.Assert(!Places.Classify("FarmHouse", false, 0).Spawns, "no spawns indoors");
            T.Assert(Places.Classify("Woods", true, 0).Biome == Biome.Corruption, "secret woods are evil");
            T.Assert(Places.Classify("Beach", true, 0).Biome == Biome.Ocean, "beach is the ocean");
            T.Assert(Places.Classify("UndergroundMine5", false, 5).Band == Band.Underground, "upper mines are underground");
            T.Assert(Places.Classify("UndergroundMine45", false, 45).Band == Band.Cavern, "middle mines are caverns");
            T.Assert(Places.Classify("UndergroundMine70", false, 70).Biome == Biome.Jungle, "lower mines are jungle");
            T.Assert(Places.Classify("UndergroundMine100", false, 100).Band == Band.Underworld, "bottom mines are the underworld");
            T.Assert(Places.Classify("UndergroundMine150", false, 150).Biome == Biome.Dungeon, "skull cavern is the dungeon");
            T.Assert(Places.Classify("UndergroundMine300", false, 300).Biome == Biome.Temple, "deep skull cavern is the temple");
            T.Assert(Places.Classify("IslandWest", true, 0).Biome == Biome.Island, "ginger island stays its own place");
            T.Assert(Places.Classify("VolcanoDungeon3", false, 0).Biome == Biome.Island, "the volcano is not hell");
        });
        failed += T.Check("mine floors alternate between two reusable slots", () =>
        {
            Place a = Places.Classify("UndergroundMine11", false, 11);
            Place b = Places.Classify("UndergroundMine12", false, 12);
            Place c = Places.Classify("UndergroundMine13", false, 13);
            T.Assert(a.IsMineSlot && a.Key != b.Key && a.Key == c.Key, "two slots");
        });
        failed += T.Check("mine containers take their biome's pot look", () =>
        {
            T.Assert(Pots.StyleFor(Biome.Underworld, 0) == 13 && Pots.StyleFor(Biome.Underworld, 5) == 15, "underworld pots");
            T.Assert(Pots.StyleFor(Biome.Cave, -1) is >= 0 and <= 3, "negative variants stay in range");
            T.Assert(Pots.StyleFor(Biome.Dungeon, 4) is >= 10 and <= 12, "dungeon pots");
            T.Assert(Pots.StyleFor(Biome.Temple, 0) == 28, "temple pots");
        });
        return failed;
    }
}
