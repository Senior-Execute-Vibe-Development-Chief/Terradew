using System;
using System.Collections.Generic;
using System.Linq;

namespace Terradew.Core.World;

/// <summary>One light that Terraria added to a tile this tick, as the Lighting engine takes it: 1 is full, above 1 reaches further.</summary>
public readonly record struct LightTile(int X, int Y, float R, float G, float B);

/// <summary>One soft light for Stardew, at a point in Terraria tiles (the centre of the blob, so it may fall between tiles).</summary>
public readonly record struct LightSpot(float X, float Y, float R, float G, float B);

/// <summary>
/// Joins the Terraria lights of one tick into the few soft lights that Stardew draws. Lights on the same tile add up,
/// each channel is capped at <see cref="MaxLevel"/>, and dim tiles are dropped. Tiles that sit close together are
/// joined into one light at their centre, so a torch's glow is one soft blob rather than a ring of spots. Each light is
/// a little less saturated, and only the strongest <c>cap</c> are kept.
/// </summary>
public static class WorldLightMerge
{
    /// <summary>The highest light level a channel can reach. Terraria lights can go past 1, but Stardew shows no more than this.</summary>
    public const float MaxLevel = 1f;

    /// <summary>A tile whose strongest channel is below this is too faint to draw, and is dropped.</summary>
    public const float MinLevel = 0.12f;

    /// <summary>Tiles closer than this many tiles to the strongest tile of a blob are joined into it.</summary>
    public const float JoinTiles = 2.5f;

    /// <summary>How much of each channel's colour is pulled toward grey. The strongest channel keeps its level.</summary>
    public const float Desaturation = 0.25f;

    /// <summary>
    /// Adds the lights on each tile, caps the sums, drops the faint ones, joins the close ones into blobs and keeps the
    /// <paramref name="cap"/> strongest by their strongest channel.
    /// </summary>
    /// <remarks>Ties are ordered by position so the same input always gives the same output.</remarks>
    public static List<LightSpot> Merge(IEnumerable<LightTile> lights, int cap)
    {
        var sums = new Dictionary<(int X, int Y), (float R, float G, float B)>();
        foreach (LightTile light in lights)
        {
            (int X, int Y) key = (light.X, light.Y);
            sums.TryGetValue(key, out (float R, float G, float B) sum);
            sums[key] = (sum.R + light.R, sum.G + light.G, sum.B + light.B);
        }

        var tiles = new List<LightSpot>(sums.Count);
        foreach (KeyValuePair<(int X, int Y), (float R, float G, float B)> pair in sums)
        {
            (float r, float g, float b) = Soften(Clamp(pair.Value.R), Clamp(pair.Value.G), Clamp(pair.Value.B));
            if (Peak(r, g, b) < MinLevel)
                continue;
            tiles.Add(new LightSpot(pair.Key.X, pair.Key.Y, r, g, b));
        }
        tiles = tiles
            .OrderByDescending(light => Peak(light.R, light.G, light.B))
            .ThenBy(light => light.X)
            .ThenBy(light => light.Y)
            .ToList();

        var spots = new List<LightSpot>();
        var joined = new bool[tiles.Count];
        float reach = JoinTiles * JoinTiles;
        for (int seed = 0; seed < tiles.Count; seed++)
        {
            if (joined[seed])
                continue;
            joined[seed] = true;

            // The blob sits at its members' strength-weighted centre and takes each channel's strongest member, so its
            // core keeps the brightest level and hue that Terraria put there.
            float weightX = 0f, weightY = 0f, weightSum = 0f;
            float r = 0f, g = 0f, b = 0f;
            for (int other = seed; other < tiles.Count; other++)
            {
                if (other != seed)
                {
                    if (joined[other])
                        continue;
                    float dx = tiles[other].X - tiles[seed].X;
                    float dy = tiles[other].Y - tiles[seed].Y;
                    if (dx * dx + dy * dy > reach)
                        continue;
                    joined[other] = true;
                }
                LightSpot member = tiles[other];
                float weight = Peak(member.R, member.G, member.B);
                weightX += member.X * weight;
                weightY += member.Y * weight;
                weightSum += weight;
                r = Math.Max(r, member.R);
                g = Math.Max(g, member.G);
                b = Math.Max(b, member.B);
            }
            spots.Add(new LightSpot(weightX / weightSum, weightY / weightSum, r, g, b));
        }

        return spots
            .OrderByDescending(light => Peak(light.R, light.G, light.B))
            .ThenBy(light => light.X)
            .ThenBy(light => light.Y)
            .Take(Math.Max(0, cap))
            .ToList();
    }

    /// <summary>
    /// Pulls a colour part of the way toward grey, so a light is a little less saturated, and scales it back so its
    /// strongest channel is what it was. Brightness is kept; only the colour is softened.
    /// </summary>
    public static (float R, float G, float B) Soften(float r, float g, float b)
    {
        float peak = Peak(r, g, b);
        if (peak <= 0f)
            return (0f, 0f, 0f);
        float grey = 0.3f * r + 0.59f * g + 0.11f * b;
        float softR = r + (grey - r) * Desaturation;
        float softG = g + (grey - g) * Desaturation;
        float softB = b + (grey - b) * Desaturation;
        float scale = peak / Peak(softR, softG, softB);
        return (softR * scale, softG * scale, softB * scale);
    }

    /// <summary>A channel as a byte for the wire: 0 for no light, 255 for <see cref="MaxLevel"/>.</summary>
    public static byte ToByte(float level) => (byte)Math.Round(Clamp(level) / MaxLevel * 255f);

    private static float Peak(float r, float g, float b) => Math.Max(r, Math.Max(g, b));

    private static float Clamp(float level) => Math.Clamp(float.IsFinite(level) ? level : 0f, 0f, MaxLevel);
}
