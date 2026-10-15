using System;
using System.Collections.Generic;
using System.Linq;
using Terradew.Core;
using Terradew.Core.Protocol;
using Terradew.Core.World;

/// <summary>Checks the merge of Terraria lights into the few Stardew draws, and the world light frame.</summary>
internal static class WorldLightTests
{
    public static int Run()
    {
        int failed = 0;
        failed += T.Check("lights on one tile add up, capped at the maximum", () =>
        {
            List<LightTile> merged = WorldLightMerge.Merge(new[]
            {
                new LightTile(3, 4, 0.5f, 0.2f, 0f),
                new LightTile(3, 4, 0.5f, 0.2f, 0.1f),
                new LightTile(3, 4, 0.9f, 0f, 0f),
            }, 64);
            T.Assert(merged.Count == 1, "one tile");
            LightTile tile = merged[0];
            T.Assert(tile.X == 3 && tile.Y == 4, "the tile is kept");
            T.Assert(tile.R == WorldLightMerge.MaxLevel, "red is capped at 1.5");
            T.Assert(Math.Abs(tile.G - 0.4f) < 0.0001f && Math.Abs(tile.B - 0.1f) < 0.0001f, "green and blue are summed");
        });

        failed += T.Check("dark lights are dropped and a bright tile is not", () =>
        {
            List<LightTile> merged = WorldLightMerge.Merge(new[]
            {
                new LightTile(1, 1, 0f, 0f, 0f),
                new LightTile(2, 2, -0.5f, 0f, 0f),
                new LightTile(5, 5, 0.1f, 0f, 0f),
            }, 64);
            T.Assert(merged.Count == 1 && merged[0].X == 5, "only the lit tile remains");
        });

        failed += T.Check("the cap keeps the strongest tiles and orders ties by tile", () =>
        {
            var lights = new List<LightTile>();
            for (int i = 0; i < 70; i++)
                lights.Add(new LightTile(i, 0, 0.01f * (i + 1), 0f, 0f));
            List<LightTile> merged = WorldLightMerge.Merge(lights, WorldLightFrame.MaxLights);
            T.Assert(merged.Count == 64, "64 kept");
            T.Assert(merged.All(light => light.X >= 6), "the six weakest are gone");
            T.Assert(merged[0].X == 69, "the strongest comes first");

            var tied = new[] { new LightTile(9, 0, 0.5f, 0f, 0f), new LightTile(2, 0, 0.5f, 0f, 0f) };
            List<LightTile> order = WorldLightMerge.Merge(tied, 1);
            T.Assert(order.Count == 1 && order[0].X == 2, "ties go to the lower tile");
            T.Assert(WorldLightMerge.Merge(tied, 0).Count == 0, "a cap of zero keeps none");
        });

        failed += T.Check("channels become bytes: 1.5 is 255, 0 is 0", () =>
        {
            T.Assert(WorldLightMerge.ToByte(0f) == 0, "zero");
            T.Assert(WorldLightMerge.ToByte(1.5f) == 255, "the maximum");
            T.Assert(WorldLightMerge.ToByte(3f) == 255, "above the maximum is capped");
            T.Assert(WorldLightMerge.ToByte(-1f) == 0, "below zero is zero");
            T.Assert(WorldLightMerge.ToByte(float.NaN) == 0, "not a number is zero");
        });

        failed += T.Check("a world light frame survives the wire", () =>
        {
            var frame = new WorldLightFrame { PlaceKey = "Farm" };
            frame.Lights.Add(new WorldLight(12.5f, -3f, 255, 128, 0));
            frame.Lights.Add(new WorldLight(0.5f, 4f, 1, 2, 3));
            var copy = (WorldLightFrame)ProtocolTests.Roundtrip(frame);
            T.Assert(copy.PlaceKey == "Farm" && copy.Lights.Count == 2, "frame fields");
            WorldLight first = copy.Lights[0];
            T.Assert(first.X == 12.5f && first.Y == -3f, "position");
            T.Assert(first.R == 255 && first.G == 128 && first.B == 0, "colour");
            T.Assert(copy.Lights[1].X == 0.5f && copy.Lights[1].B == 3, "the second light");

            var full = new WorldLightFrame { PlaceKey = "Farm" };
            for (int i = 0; i < WorldLightFrame.MaxLights; i++)
                full.Lights.Add(new WorldLight(i, i, 1, 1, 1));
            T.Assert(((WorldLightFrame)ProtocolTests.Roundtrip(full)).Lights.Count == WorldLightFrame.MaxLights, "the cap is kept");

            var cleared = (WorldLightFrame)ProtocolTests.Roundtrip(new WorldLightFrame { PlaceKey = "Farm" });
            T.Assert(cleared.Lights.Count == 0, "an empty frame clears");
        });

        return failed;
    }
}
