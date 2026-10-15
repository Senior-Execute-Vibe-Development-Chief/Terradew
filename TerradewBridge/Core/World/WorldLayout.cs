using System.Globalization;

namespace Terradew.Core.World;

/// <summary>The Terraria world size and the depth landmarks that bands are cut from.</summary>
public readonly record struct WorldShape(int MaxTilesX, int MaxTilesY, int SurfaceY, int RockY, int UnderworldY);

/// <summary>A rectangle of Terraria tiles given to one Stardew location.</summary>
public readonly record struct Region(string Key, int X, int Y, int Width, int Height, Band Band)
{
    public bool Contains(int tileX, int tileY) => tileX >= X && tileY >= Y && tileX < X + Width && tileY < Y + Height;
}

/// <summary>
/// Places location regions in a Terraria world without overlaps. Deterministic: the same
/// calls in the same order always give the same spots.
/// </summary>
public sealed class WorldLayout
{
    public const int Border = 45;      // keep regions out of Terraria's unplayable world edge
    public const int OceanReach = 380; // tiles from either world edge that Terraria counts as ocean
    public const int GapX = 70;        // horizontal spacing between regions so biome scans don't mix
    public const int GapY = 45;        // vertical spacing
    public const int Step = 8;         // candidate grid step when searching for a spot

    private readonly List<Region> _regions = new List<Region>();

    public WorldLayout(WorldShape shape, IEnumerable<Region>? existing = null)
    {
        Shape = shape;
        if (existing is null)
            return;
        foreach (Region region in existing)
            Upsert(_regions, region);
    }

    public WorldShape Shape { get; }

    public IReadOnlyCollection<Region> Regions => _regions;

    public bool TryGet(string key, out Region region)
    {
        int index = IndexOf(_regions, key);
        if (index >= 0)
        {
            region = _regions[index];
            return true;
        }
        region = default;
        return false;
    }

    /// <summary>
    /// Finds a spot for a region of the given size in the given band. An existing key that is
    /// still big enough in the same band, and not above the band top, keeps its region. Otherwise the key is placed again.
    /// </summary>
    public bool TryPlace(string key, int width, int height, Band band, bool ocean, out Region region)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));
        region = default;
        if (width <= 0 || height <= 0)
            return false;

        int index = IndexOf(_regions, key);
        Region? old = null;
        if (index >= 0)
        {
            Region current = _regions[index];
            if (current.Width >= width && current.Height >= height && current.Band == band && current.Y >= BandRange(band).Top)
            {
                region = current;
                return true;
            }
            old = current;
            _regions.RemoveAt(index);
        }

        if (!FindSpot(width, height, band, ocean, out int x, out int y))
        {
            // Put back what was there so a failed grow does not lose the location.
            if (old is Region previous)
                _regions.Insert(index, previous);
            return false;
        }

        region = new Region(key, x, y, width, height, band);
        _regions.Add(region);
        return true;
    }

    public bool Remove(string key)
    {
        int index = IndexOf(_regions, key);
        if (index < 0)
            return false;
        _regions.RemoveAt(index);
        return true;
    }

    /// <summary>Tile rows a band covers: inclusive top, exclusive bottom.</summary>
    public (int Top, int Bottom) BandRange(Band band)
    {
        WorldShape shape = Shape;
        int top;
        int bottom;
        switch (band)
        {
            case Band.Surface:
                // Terraria counts the top 45% above the surface line as sky, where harpies and wyverns spawn.
                top = Math.Max(Border, shape.SurfaceY / 2);
                bottom = shape.SurfaceY - 5;
                break;
            case Band.Underground:
                top = shape.SurfaceY + 5;
                bottom = shape.RockY - 5;
                break;
            case Band.Cavern:
                top = shape.RockY + 5;
                bottom = shape.UnderworldY - 10;
                break;
            case Band.Underworld:
                top = shape.UnderworldY + 5;
                bottom = shape.MaxTilesY - Border;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(band));
        }
        if (bottom <= top)
            bottom = top + 1;
        return (top, bottom);
    }

    public static string Serialize(IEnumerable<Region> regions)
    {
        if (regions is null)
            throw new ArgumentNullException(nameof(regions));
        var lines = new List<string>();
        foreach (Region region in regions)
        {
            string key = region.Key.Replace('|', '_').Replace('\n', '_').Replace('\r', '_');
            lines.Add(FormattableString.Invariant($"{key}|{region.X}|{region.Y}|{region.Width}|{region.Height}|{(int)region.Band}"));
        }
        return string.Join("\n", lines);
    }

    public static List<Region> Deserialize(string text)
    {
        var result = new List<Region>();
        if (string.IsNullOrEmpty(text))
            return result;

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
                continue;

            string[] parts = line.Split('|');
            if (parts.Length != 6 || parts[0].Length == 0)
                continue;
            if (!TryInt(parts[1], out int x) || !TryInt(parts[2], out int y)
                || !TryInt(parts[3], out int width) || !TryInt(parts[4], out int height)
                || !TryInt(parts[5], out int band))
                continue;
            if (width <= 0 || height <= 0)
                continue;
            if (band < (int)Band.Surface || band > (int)Band.Underworld)
                continue;

            Upsert(result, new Region(parts[0], x, y, width, height, (Band)band));
        }
        return result;
    }

    private bool FindSpot(int width, int height, Band band, bool ocean, out int spotX, out int spotY)
    {
        WorldShape shape = Shape;
        (int top, int bottom) = BandRange(band);
        int worldBottom = shape.MaxTilesY - Border;

        // A region taller than its band gets only the band top and may spill past the band bottom.
        var rows = new List<int>();
        if (top + height > bottom)
            rows.Add(top);
        else
            for (int y = top; y + height <= bottom; y += Step)
                rows.Add(y);
        // Surface places stand on the surface line and only stack upward once a row is full.
        if (band == Band.Surface)
            rows.Reverse();

        // Ocean prefers the left edge and only uses the right edge when the left is full.
        var columns = new List<(int First, int Last)>();
        if (ocean)
        {
            columns.Add((Border, OceanReach - width));
            columns.Add((shape.MaxTilesX - OceanReach, shape.MaxTilesX - Border - width));
        }
        else
        {
            columns.Add((OceanReach, shape.MaxTilesX - OceanReach - width));
        }

        foreach ((int first, int last) in columns)
        {
            foreach (int y in rows)
            {
                if (y + height > worldBottom)
                    continue;
                for (int x = first; x <= last; x += Step)
                {
                    if (!Blocked(x, y, width, height))
                    {
                        spotX = x;
                        spotY = y;
                        return true;
                    }
                }
            }
        }

        spotX = 0;
        spotY = 0;
        return false;
    }

    /// <summary>True when the rectangle, grown by the gaps, touches any existing region.</summary>
    private bool Blocked(int x, int y, int width, int height)
    {
        int left = x - GapX;
        int right = x + width + GapX;
        int top = y - GapY;
        int bottom = y + height + GapY;
        foreach (Region other in _regions)
        {
            if (other.X < right && left < other.X + other.Width
                && other.Y < bottom && top < other.Y + other.Height)
                return true;
        }
        return false;
    }

    private static int IndexOf(List<Region> regions, string key)
    {
        for (int i = 0; i < regions.Count; i++)
        {
            if (string.Equals(regions[i].Key, key, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    /// <summary>Adds a region, or replaces the one with the same key in place (last one wins).</summary>
    private static void Upsert(List<Region> regions, Region region)
    {
        int index = IndexOf(regions, region.Key);
        if (index >= 0)
            regions[index] = region;
        else
            regions.Add(region);
    }

    private static bool TryInt(string text, out int value)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
