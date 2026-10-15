using System;
using System.Collections.Generic;

namespace Terradew.Core.Routing;

/// <summary>Grid A* over eight directions. Diagonal moves never cut the corner between two blocked tiles.</summary>
public static class GridPath
{
    private const int StraightCost = 10;
    private const int DiagonalCost = 14;

    /// <summary>
    /// Finds a path from <paramref name="from"/> to <paramref name="to"/>. Returns the tiles after <paramref name="from"/>,
    /// ending at <paramref name="to"/>, with straight runs collapsed to their turning points. Returns an empty list when
    /// the tiles are equal, and null when there is no path or more than <paramref name="maxNodes"/> nodes must be expanded.
    /// With <paramref name="simplify"/> false, every tile of the path is returned, one step apart.
    /// </summary>
    public static IReadOnlyList<Tile>? Find(int width, int height, Func<int, int, bool> blocked, Tile from, Tile to, int maxNodes = 20000, bool simplify = true)
    {
        if (blocked == null)
            throw new ArgumentNullException(nameof(blocked));
        if (from == to)
            return new List<Tile>();
        if (!InBounds(from.X, from.Y) || !InBounds(to.X, to.Y))
            return null;

        int count = width * height;
        var gScore = new int[count];
        var cameFrom = new int[count];
        var closed = new bool[count];
        for (int i = 0; i < count; i++)
        {
            gScore[i] = int.MaxValue;
            cameFrom[i] = -1;
        }

        int start = Index(from);
        int goal = Index(to);
        gScore[start] = 0;

        var open = new PriorityQueue<int, int>();
        open.Enqueue(start, Octile(from.X, from.Y, to));

        int expanded = 0;
        while (open.TryDequeue(out int current, out _))
        {
            if (closed[current])
                continue;
            if (current == goal)
            {
                List<Tile> path = Rebuild(cameFrom, start, goal, width);
                return simplify ? Simplify(from, path) : path;
            }
            if (expanded >= maxNodes)
                return null;

            expanded++;
            closed[current] = true;
            int cx = current % width;
            int cy = current / width;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;

                    int nx = cx + dx;
                    int ny = cy + dy;
                    if (!InBounds(nx, ny))
                        continue;

                    int next = ny * width + nx;
                    if (closed[next] || !IsOpen(nx, ny))
                        continue;

                    bool diagonal = dx != 0 && dy != 0;
                    if (diagonal && (!IsOpen(cx + dx, cy) || !IsOpen(cx, cy + dy)))
                        continue;

                    int tentative = gScore[current] + (diagonal ? DiagonalCost : StraightCost);
                    if (tentative >= gScore[next])
                        continue;

                    gScore[next] = tentative;
                    cameFrom[next] = current;
                    open.Enqueue(next, tentative + Octile(nx, ny, to));
                }
            }
        }

        return null;

        bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;

        // The start and goal are open even when the caller marks them blocked.
        bool IsOpen(int x, int y) =>
            InBounds(x, y) && ((x == from.X && y == from.Y) || (x == to.X && y == to.Y) || !blocked(x, y));

        int Index(Tile tile) => tile.Y * width + tile.X;
    }

    /// <summary>Lower bound on the cost from (x, y) to the goal: 10 per straight step, 14 per diagonal step.</summary>
    private static int Octile(int x, int y, Tile goal)
    {
        int dx = Math.Abs(x - goal.X);
        int dy = Math.Abs(y - goal.Y);
        return StraightCost * Math.Max(dx, dy) + (DiagonalCost - StraightCost) * Math.Min(dx, dy);
    }

    /// <summary>Walks cameFrom back from the goal, returning every tile after the start.</summary>
    private static List<Tile> Rebuild(int[] cameFrom, int start, int goal, int width)
    {
        var tiles = new List<Tile>();
        for (int at = goal; at != start; at = cameFrom[at])
            tiles.Add(new Tile(at % width, at / width));
        tiles.Reverse();
        return tiles;
    }

    /// <summary>Drops tiles that continue the same direction as the step before them. Keeps turns and the final tile.</summary>
    private static List<Tile> Simplify(Tile from, List<Tile> tiles)
    {
        var result = new List<Tile>();
        for (int i = 0; i < tiles.Count; i++)
        {
            Tile previous = i == 0 ? from : tiles[i - 1];
            bool last = i == tiles.Count - 1;
            if (last || Direction(previous, tiles[i]) != Direction(tiles[i], tiles[i + 1]))
                result.Add(tiles[i]);
        }
        return result;
    }

    private static (int X, int Y) Direction(Tile a, Tile b) => (Math.Sign(b.X - a.X), Math.Sign(b.Y - a.Y));
}
