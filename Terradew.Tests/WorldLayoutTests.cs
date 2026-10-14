using Terradew.Core.World;

internal static class WorldLayoutTests
{
    private static readonly WorldShape Big = new WorldShape(8400, 2400, 700, 950, 2200);
    private static readonly WorldShape Small = new WorldShape(4200, 1200, 350, 480, 1000);

    public static int Run()
    {
        int failed = 0;

        failed += T.Check("band ranges match the spec", () =>
        {
            var layout = new WorldLayout(Big);
            T.Assert(layout.BandRange(Band.Surface) == (280, 695), "big surface");
            T.Assert(layout.BandRange(Band.Underground) == (705, 945), "big underground");
            T.Assert(layout.BandRange(Band.Cavern) == (955, 2190), "big cavern");
            T.Assert(layout.BandRange(Band.Underworld) == (2205, 2355), "big underworld");
            var small = new WorldLayout(Small);
            T.Assert(small.BandRange(Band.Surface) == (140, 345), "small surface");
        });

        failed += T.Check("ocean region lands within reach of the left edge", () =>
        {
            var layout = new WorldLayout(Big);
            T.Assert(layout.TryPlace("sea", 120, 60, Band.Surface, true, out Region sea), "placed");
            T.Assert(sea.X >= WorldLayout.Border, "past border");
            T.Assert(sea.X + sea.Width <= WorldLayout.OceanReach, "within ocean reach");
        });

        failed += T.Check("second ocean region keeps clear of the first", () =>
        {
            var layout = new WorldLayout(Big);
            T.Assert(layout.TryPlace("sea", 120, 60, Band.Surface, true, out Region first), "first");
            T.Assert(layout.TryPlace("bay", 120, 60, Band.Surface, true, out Region second), "second");
            T.Assert(!Overlaps(first, second), "overlap");
            T.Assert(Clear(first, second), "gap");
        });

        failed += T.Check("ocean spills to the right edge once the left is full", () =>
        {
            var layout = new WorldLayout(Big);
            bool usedRight = false;
            for (int i = 0; i < 200; i++)
            {
                if (!layout.TryPlace("sea" + i, 300, 60, Band.Surface, true, out Region sea))
                    break;
                if (sea.X >= Big.MaxTilesX - WorldLayout.OceanReach)
                {
                    usedRight = true;
                    T.Assert(sea.X + sea.Width <= Big.MaxTilesX - WorldLayout.Border, "right edge inside border");
                }
                else
                {
                    T.Assert(sea.X + sea.Width <= WorldLayout.OceanReach, "left ocean within reach");
                    T.Assert(!usedRight, "left placed after right was used");
                }
            }
            T.Assert(usedRight, "right edge never used");
        });

        failed += T.Check("non-ocean regions stay out of the ocean reach", () =>
        {
            var layout = new WorldLayout(Big);
            for (int i = 0; i < 5; i++)
            {
                T.Assert(layout.TryPlace("town" + i, 100, 50, Band.Surface, false, out Region town), "placed");
                T.Assert(town.X >= WorldLayout.OceanReach, "left of reach");
                T.Assert(town.X + town.Width <= Big.MaxTilesX - WorldLayout.OceanReach, "right of reach");
            }
        });

        failed += T.Check("regions in each band lie within that band", () =>
        {
            foreach (Band band in new[] { Band.Surface, Band.Underground, Band.Cavern, Band.Underworld })
            {
                var layout = new WorldLayout(Big);
                (int top, int bottom) = layout.BandRange(band);
                for (int i = 0; i < 5; i++)
                {
                    T.Assert(layout.TryPlace(band + "-" + i, 40, 20, band, false, out Region region), "placed " + band);
                    T.Assert(region.Band == band, "band " + band);
                    T.Assert(region.Y >= top && region.Y + region.Height <= bottom, "rows of " + band);
                }
            }
        });

        failed += T.Check("a region taller than its band takes the band top", () =>
        {
            var layout = new WorldLayout(Big);
            T.Assert(layout.TryPlace("tall", 50, 500, Band.Surface, false, out Region tall), "spills past band");
            T.Assert(tall.Y == 280, "band top");
            var deep = new WorldLayout(Big);
            T.Assert(!deep.TryPlace("deep", 50, 200, Band.Underworld, false, out _), "past world border");
        });

        failed += T.Check("regions keep the gaps between them", () =>
        {
            var layout = new WorldLayout(Big);
            int placed = 0;
            for (int i = 0; i < 40; i++)
            {
                Band band = (Band)(i % 4);
                bool ocean = i % 5 == 0;
                int width = 30 + (i * 37) % 90;
                int height = 20 + (i * 13) % 40;
                if (layout.TryPlace("r" + i, width, height, band, ocean, out _))
                    placed++;
            }
            T.Assert(placed >= 20, "placed " + placed);
            List<Region> all = layout.Regions.ToList();
            for (int i = 0; i < all.Count; i++)
            {
                for (int j = i + 1; j < all.Count; j++)
                    T.Assert(Clear(all[i], all[j]), "gap between " + all[i].Key + " and " + all[j].Key);
            }
        });

        failed += T.Check("same key returns the same region", () =>
        {
            var layout = new WorldLayout(Big);
            T.Assert(layout.TryPlace("farm", 30, 30, Band.Surface, false, out Region first), "first");
            T.Assert(layout.TryPlace("farm", 30, 30, Band.Surface, false, out Region again), "again");
            T.Assert(first == again, "same spot");
            T.Assert(layout.TryPlace("farm", 10, 10, Band.Surface, false, out Region smaller), "smaller");
            T.Assert(smaller == first, "smaller request keeps region");
            T.Assert(layout.Regions.Count == 1, "no duplicate");
        });

        failed += T.Check("growing a key re-places it", () =>
        {
            var layout = new WorldLayout(Big);
            T.Assert(layout.TryPlace("farm", 20, 20, Band.Surface, false, out _), "small");
            T.Assert(layout.TryPlace("barn", 20, 20, Band.Surface, false, out _), "neighbour");
            T.Assert(layout.TryPlace("farm", 200, 200, Band.Surface, false, out Region grown), "grown");
            T.Assert(grown.Width == 200 && grown.Height == 200, "new size");
            T.Assert(layout.TryGet("farm", out Region stored) && stored == grown, "stored");
            T.Assert(layout.Regions.Count == 2, "no duplicate");
            List<Region> all = layout.Regions.ToList();
            T.Assert(Clear(all[0], all[1]), "gap after growing");
        });

        failed += T.Check("a full world refuses more regions", () =>
        {
            var layout = new WorldLayout(Small);
            bool refused = false;
            for (int i = 0; i < 1000 && !refused; i++)
                refused = !layout.TryPlace("r" + i, 300, 100, Band.Surface, false, out _);
            T.Assert(refused, "never full");
            T.Assert(!layout.TryPlace("zero", 0, 10, Band.Surface, false, out _), "zero width");
        });

        failed += T.Check("remove forgets the key", () =>
        {
            var layout = new WorldLayout(Big);
            layout.TryPlace("farm", 20, 20, Band.Surface, false, out _);
            T.Assert(layout.Remove("farm"), "removed");
            T.Assert(!layout.TryGet("farm", out _), "gone");
            T.Assert(!layout.Remove("farm"), "second remove");
        });

        failed += T.Check("serialize roundtrip keeps every region", () =>
        {
            var layout = new WorldLayout(Big);
            layout.TryPlace("sea", 120, 60, Band.Surface, true, out _);
            layout.TryPlace("cave", 80, 40, Band.Cavern, false, out _);
            layout.TryPlace("hell", 60, 30, Band.Underworld, false, out _);
            string text = WorldLayout.Serialize(layout.Regions);
            List<Region> back = WorldLayout.Deserialize(text);
            T.Assert(back.SequenceEqual(layout.Regions), "roundtrip");
            var adopted = new WorldLayout(Big, back);
            T.Assert(adopted.Regions.SequenceEqual(layout.Regions), "adopted");
        });

        failed += T.Check("separators in keys become underscores", () =>
        {
            var one = new List<Region> { new Region("a|b\nc\rd", 1, 2, 3, 4, Band.Cavern) };
            string text = WorldLayout.Serialize(one);
            T.Assert(text == "a_b_c_d|1|2|3|4|2", "format: " + text);
            List<Region> back = WorldLayout.Deserialize(text);
            T.Assert(back.Count == 1 && back[0].Key == "a_b_c_d" && back[0].Band == Band.Cavern, "read back");
        });

        failed += T.Check("deserialize tolerates garbage lines", () =>
        {
            string text = "garbage\n\nfoo|1|2\nsea|10|20|30|40|0\nbad|x|1|2|3|0\nzero|1|1|0|5|1\n"
                + "late|1|1|5|5|9\nsea|99|20|30|40|2\nkeep|1|2|3|4|1\r\n";
            List<Region> parsed = WorldLayout.Deserialize(text);
            T.Assert(parsed.Count == 2, "count " + parsed.Count);
            T.Assert(parsed[0].Key == "sea" && parsed[0].X == 99 && parsed[0].Band == Band.Cavern, "last duplicate wins");
            T.Assert(parsed[1].Key == "keep" && parsed[1].Width == 3, "valid line after garbage");
            T.Assert(WorldLayout.Deserialize("").Count == 0, "empty");
        });

        failed += T.Check("placement is deterministic", () =>
        {
            T.Assert(Build() == Build(), "same calls, same layout");
        });

        return failed;
    }

    private static string Build()
    {
        var layout = new WorldLayout(Big);
        layout.TryPlace("sea", 120, 60, Band.Surface, true, out _);
        layout.TryPlace("bay", 120, 60, Band.Surface, true, out _);
        layout.TryPlace("town", 100, 50, Band.Surface, false, out _);
        layout.TryPlace("cave", 80, 40, Band.Cavern, false, out _);
        layout.TryPlace("town", 150, 50, Band.Surface, false, out _);
        return WorldLayout.Serialize(layout.Regions);
    }

    private static bool Overlaps(Region a, Region b)
        => a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    /// <summary>True when the two regions are separated by at least GapX horizontally or GapY vertically.</summary>
    private static bool Clear(Region a, Region b)
    {
        int dx = Math.Max(b.X - (a.X + a.Width), a.X - (b.X + b.Width));
        int dy = Math.Max(b.Y - (a.Y + a.Height), a.Y - (b.Y + b.Height));
        return dx >= WorldLayout.GapX || dy >= WorldLayout.GapY;
    }
}
