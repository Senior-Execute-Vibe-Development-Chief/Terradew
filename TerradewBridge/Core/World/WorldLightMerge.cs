using System;
using System.Collections.Generic;
using System.Linq;

namespace Terradew.Core.World;

/// <summary>One light that Terraria added to a tile this tick, as the Lighting engine takes it: 1 is full, above 1 reaches further.</summary>
public readonly record struct LightTile(int X, int Y, float R, float G, float B);

/// <summary>
/// Joins the Terraria lights of one tick into the few that Stardew draws. Lights on the same tile add up, each
/// channel is capped at <see cref="MaxLevel"/>, and only the strongest <c>cap</c> tiles are kept.
/// </summary>
public static class WorldLightMerge
{
    /// <summary>The highest light level a channel can reach. Terraria lights can go past 1, but Stardew shows no more than this.</summary>
    public const float MaxLevel = 1.5f;

    /// <summary>Adds the lights on each tile, caps the sums, drops the dark ones and keeps the <paramref name="cap"/> strongest by r + g + b.</summary>
    /// <remarks>Ties are ordered by tile so the same input always gives the same output.</remarks>
    public static List<LightTile> Merge(IEnumerable<LightTile> lights, int cap)
    {
        var sums = new Dictionary<(int X, int Y), (float R, float G, float B)>();
        foreach (LightTile light in lights)
        {
            (int X, int Y) key = (light.X, light.Y);
            sums.TryGetValue(key, out (float R, float G, float B) sum);
            sums[key] = (sum.R + light.R, sum.G + light.G, sum.B + light.B);
        }

        var merged = new List<LightTile>(sums.Count);
        foreach (KeyValuePair<(int X, int Y), (float R, float G, float B)> pair in sums)
        {
            float r = Clamp(pair.Value.R);
            float g = Clamp(pair.Value.G);
            float b = Clamp(pair.Value.B);
            if (r + g + b <= 0f)
                continue;
            merged.Add(new LightTile(pair.Key.X, pair.Key.Y, r, g, b));
        }

        return merged
            .OrderByDescending(light => light.R + light.G + light.B)
            .ThenBy(light => light.X)
            .ThenBy(light => light.Y)
            .Take(Math.Max(0, cap))
            .ToList();
    }

    /// <summary>A channel as a byte for the wire: 0 for no light, 255 for <see cref="MaxLevel"/>.</summary>
    public static byte ToByte(float level) => (byte)Math.Round(Clamp(level) / MaxLevel * 255f);

    private static float Clamp(float level) => Math.Clamp(float.IsFinite(level) ? level : 0f, 0f, MaxLevel);
}
