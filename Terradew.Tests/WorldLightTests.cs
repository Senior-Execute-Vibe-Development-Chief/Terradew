using System;
using System.Collections.Generic;
using System.Linq;
using Terradew.Core;
using Terradew.Core.Protocol;
using Terradew.Core.World;

/// <summary>Checks the merge of Terraria lights into the few soft lights Stardew draws, and the world light frame.</summary>
internal static class WorldLightTests
{
    public static int Run()
    {
        int failed = 0;
        failed += T.Check("lights on one tile add up, capped at the maximum", () =>
        {
            List<LightSpot> merged = WorldLightMerge.Merge(new[]
            {
                new LightTile(3, 4, 0.5f, 0.2f, 0f),
                new LightTile(3, 4, 0.5f, 0.2f, 0.1f),
                new LightTile(3, 4, 0.9f, 0f, 0f),
            }, 64);
            T.Assert(merged.Count == 1, "one tile");
            LightSpot spot = merged[0];
            T.Assert(spot.X == 3f && spot.Y == 4f, "the tile is kept");
            T.Assert(Math.Abs(spot.R - WorldLightMerge.MaxLevel) < 0.0001f, "red is capped at the maximum");
            T.Assert(spot.G > 0f && spot.B > 0f && spot.B < spot.G, "green and blue are summed and kept in order");
        });

        failed += T.Check("the colour is softened without losing the brightest channel", () =>
        {
            (float r, float g, float b) = WorldLightMerge.Soften(1f, 0.4f, 0.1f);
            T.Assert(Math.Abs(Math.Max(r, Math.Max(g, b)) - 1f) < 0.0001f, "the peak is kept");
            T.Assert(r - g < 1f - 0.4f && g - b < 0.4f - 0.1f, "the colours are less saturated");
            T.Assert(r > g && g > b, "the order of the channels is kept");
            T.Assert(WorldLightMerge.Soften(0f, 0f, 0f) == (0f, 0f, 0f), "no light stays no light");
        });

        failed += T.Check("dim tiles are dropped and a bright tile is not", () =>
        {
            List<LightSpot> merged = WorldLightMerge.Merge(new[]
            {
                new LightTile(1, 1, 0f, 0f, 0f),
                new LightTile(2, 2, -0.5f, 0f, 0f),
                new LightTile(5, 5, WorldLightMerge.MinLevel / 2f, 0f, 0f),
                new LightTile(7, 7, 0.5f, 0f, 0f),
            }, 64);
            T.Assert(merged.Count == 1 && merged[0].X == 7f, "only the lit tile remains");
        });

        failed += T.Check("tiles close together join into one blob at their centre", () =>
        {
            List<LightSpot> merged = WorldLightMerge.Merge(new[]
            {
                new LightTile(10, 10, 0.5f, 0f, 0f),
                new LightTile(11, 10, 0.5f, 0f, 0f),
                new LightTile(10, 11, 0.5f, 0f, 0f),
                new LightTile(30, 30, 0.9f, 0f, 0f),
            }, 64);
            T.Assert(merged.Count == 2, "the three close tiles are one blob, the far tile is another");
            LightSpot far = merged[0];
            T.Assert(far.X == 30f && far.Y == 30f, "the strongest blob comes first");
            LightSpot blob = merged[1];
            T.Assert(Math.Abs(blob.X - 10.333f) < 0.01f && Math.Abs(blob.Y - 10.333f) < 0.01f, "the blob sits at the members' centre");
            T.Assert(Math.Abs(blob.R - 0.5f) < 0.0001f, "the blob keeps its core level, not a sum of its members");
        });

        failed += T.Check("tiles far apart stay separate lights", () =>
        {
            var lights = new List<LightTile>();
            for (int i = 0; i < 70; i++)
                lights.Add(new LightTile(i * 4, 0, 0.2f + 0.01f * i, 0f, 0f));
            List<LightSpot> merged = WorldLightMerge.Merge(lights, WorldLightFrame.MaxLights);
            T.Assert(merged.Count == 64, "64 kept");
            T.Assert(merged.All(light => light.X >= 6 * 4), "the six weakest are gone");
            T.Assert(merged[0].X == 69 * 4, "the strongest comes first");

            var tied = new[] { new LightTile(9, 0, 0.5f, 0f, 0f), new LightTile(2, 0, 0.5f, 0f, 0f) };
            List<LightSpot> order = WorldLightMerge.Merge(tied, 1);
            T.Assert(order.Count == 1 && order[0].X == 2f, "ties go to the lower tile");
            T.Assert(WorldLightMerge.Merge(tied, 0).Count == 0, "a cap of zero keeps none");
        });

        failed += T.Check("channels become bytes: the maximum is 255, 0 is 0", () =>
        {
            T.Assert(WorldLightMerge.ToByte(0f) == 0, "zero");
            T.Assert(WorldLightMerge.ToByte(WorldLightMerge.MaxLevel) == 255, "the maximum");
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
