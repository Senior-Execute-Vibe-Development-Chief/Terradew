using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using StardewValley;

namespace Terradew;

/// <summary>
/// Which tiles of a map the farmer can ever walk to. Stardew maps hold floor behind their walls and cliffs that no
/// path reaches; those tiles are sealed, so Terraria creatures never spawn there and the farmer cannot fly there.
/// A flood fill starts at every way in (warps, doors and the farmer's own tile) and crosses any tile whose map
/// tiles let the farmer through. Things the farmer can break or move (stones, trees, furniture) and water do not
/// stop the fill, so they never seal what lies behind them.
/// </summary>
internal static class Reach
{
    /// <summary>A seed tile that is itself walled (a warp just off the edge, a door in a wall) looks this far around.</summary>
    private const int SeedSearch = 2;

    private sealed class Fill
    {
        public string Name = "";
        public int Width;
        public int Height;
        public bool[] Reached = System.Array.Empty<bool>();
        /// <summary>Tiles the map itself lets the farmer through, reached or not.</summary>
        public bool[] Open = System.Array.Empty<bool>();
        public readonly HashSet<Point> Seeds = new();
    }

    // One fill per loaded map, dropped with the map; the name guards against a map reused for another place.
    private static readonly ConditionalWeakTable<xTile.Map, Fill> Fills = new();

    /// <summary>True when the tile is on the map but no walkable path from a way in reaches it.</summary>
    public static bool Sealed(GameLocation location, int x, int y)
    {
        Fill? fill = Of(location);
        if (fill == null || x < 0 || y < 0 || x >= fill.Width || y >= fill.Height)
            return false;
        return !fill.Reached[y * fill.Width + x];
    }

    /// <summary>
    /// True for open floor that no path reaches: the hidden space behind walls. Walls and cliffs themselves are not
    /// hidden, so the farmer can still fly over them; only coming down into the space behind is refused.
    /// </summary>
    public static bool Hidden(GameLocation location, int x, int y)
    {
        Fill? fill = Of(location);
        if (fill == null || x < 0 || y < 0 || x >= fill.Width || y >= fill.Height)
            return false;
        int i = y * fill.Width + x;
        return fill.Open[i] && !fill.Reached[i];
    }

    /// <summary>
    /// The farmer stands on the ground on this tile, so it is reachable. When the fill missed it (a way in it did not
    /// know about, such as an event drop-off), it fills again from here too.
    /// </summary>
    public static void Stood(GameLocation location, Point tile)
    {
        Fill? fill = Of(location);
        if (fill == null || tile.X < 0 || tile.Y < 0 || tile.X >= fill.Width || tile.Y >= fill.Height)
            return;
        if (fill.Reached[tile.Y * fill.Width + tile.X])
            return;
        fill.Seeds.Add(tile);
        Flood(location, fill, tile);
    }

    private static Fill? Of(GameLocation location)
    {
        xTile.Map? map = location.Map;
        if (map == null || map.Layers.Count == 0)
            return null;
        if (Fills.TryGetValue(map, out Fill? known) && known.Name == location.NameOrUniqueName)
            return known;
        int width = map.Layers[0].LayerWidth;
        int height = map.Layers[0].LayerHeight;
        if (width <= 0 || height <= 0 || width > 400 || height > 400)
            return null;
        var fill = new Fill { Name = location.NameOrUniqueName, Width = width, Height = height, Reached = new bool[width * height], Open = new bool[width * height] };
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                fill.Open[y * width + x] = Passable(location, fill, x, y);
        }
        foreach (Warp warp in location.warps)
            fill.Seeds.Add(new Point(warp.X, warp.Y));
        foreach (Point door in location.doors.Keys)
            fill.Seeds.Add(door);
        if (Game1.player?.currentLocation == location)
            fill.Seeds.Add(Game1.player.TilePoint);
        foreach (Point seed in fill.Seeds)
            Flood(location, fill, seed);
        // A map no way in reaches (a cutscene stage, say) is left whole rather than sealed outright.
        if (System.Array.IndexOf(fill.Reached, true) < 0)
            System.Array.Fill(fill.Reached, true);
        Fills.AddOrUpdate(map, fill);
        return fill;
    }

    private static void Flood(GameLocation location, Fill fill, Point seed)
    {
        var open = new Queue<Point>();
        for (int r = 0; r <= SeedSearch && open.Count == 0; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = System.Math.Clamp(seed.X, 0, fill.Width - 1) + dx;
                    int y = System.Math.Clamp(seed.Y, 0, fill.Height - 1) + dy;
                    if (OpenAt(fill, x, y) && !fill.Reached[y * fill.Width + x])
                    {
                        fill.Reached[y * fill.Width + x] = true;
                        open.Enqueue(new Point(x, y));
                    }
                }
            }
        }
        while (open.Count > 0)
        {
            Point at = open.Dequeue();
            Visit(location, fill, open, at.X + 1, at.Y);
            Visit(location, fill, open, at.X - 1, at.Y);
            Visit(location, fill, open, at.X, at.Y + 1);
            Visit(location, fill, open, at.X, at.Y - 1);
        }
    }

    private static void Visit(GameLocation location, Fill fill, Queue<Point> open, int x, int y)
    {
        if (!OpenAt(fill, x, y) || fill.Reached[y * fill.Width + x])
            return;
        fill.Reached[y * fill.Width + x] = true;
        open.Enqueue(new Point(x, y));
    }

    private static bool OpenAt(Fill fill, int x, int y)
        => x >= 0 && y >= 0 && x < fill.Width && y < fill.Height && fill.Open[y * fill.Width + x];

    /// <summary>A tile with floor under it that the map's own tiles let the farmer through. Water counts as open.</summary>
    private static bool Passable(GameLocation location, Fill fill, int x, int y)
    {
        if (x < 0 || y < 0 || x >= fill.Width || y >= fill.Height)
            return false;
        if (location.Map.GetLayer("Back")?.Tiles[x, y] == null)
            return false;
        return location.isWaterTile(x, y) || location.isTilePassable(new xTile.Dimensions.Location(x, y), Game1.viewport);
    }
}
