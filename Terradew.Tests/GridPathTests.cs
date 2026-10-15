using System;
using System.Collections.Generic;
using Terradew.Core.Routing;

/// <summary>Grid A* checks: routes around walls, the corner rule, unreachable goals, the node budget and edge tiles.</summary>
public static class GridPathTests
{
    public static int Run()
    {
        int failed = 0;
        Func<int, int, bool> open = (x, y) => false;

        failed += T.Check("straight open path ends at the goal", () =>
        {
            IReadOnlyList<Tile> path = Require(GridPath.Find(10, 10, open, new Tile(0, 0), new Tile(5, 0)));
            T.Assert(path.Count > 0 && path[path.Count - 1] == new Tile(5, 0), "last tile is the goal");
            T.Assert(path.Count == 1, "straight run collapses to its end, got " + path.Count);
        });

        failed += T.Check("open diagonal is taken as one run", () =>
        {
            IReadOnlyList<Tile> path = Require(GridPath.Find(10, 10, open, new Tile(0, 0), new Tile(3, 3)));
            T.Assert(path.Count == 1 && path[0] == new Tile(3, 3), "single diagonal run");
        });

        failed += T.Check("path goes around a wall without entering it", () =>
        {
            Func<int, int, bool> wall = (x, y) => x == 5 && y <= 8;
            var from = new Tile(0, 4);
            var goal = new Tile(9, 4);
            IReadOnlyList<Tile> path = Require(GridPath.Find(10, 10, wall, from, goal));
            T.Assert(path[path.Count - 1] == goal, "ends at the goal");
            List<Tile> walked = Expand(from, path);
            foreach (Tile tile in walked)
                T.Assert(!wall(tile.X, tile.Y), "walks through blocked tile " + tile);
            T.Assert(walked.Exists(t => t.Y == 9), "uses the gap at y=9");
        });

        failed += T.Check("no corner cutting between two diagonal blocks", () =>
        {
            // Blocks at (2,2) and (3,3) sit on both sides of the (2,3)->(3,2) diagonal.
            Func<int, int, bool> pinch = (x, y) => (x == 2 && y == 2) || (x == 3 && y == 3);
            var from = new Tile(2, 3);
            var goal = new Tile(3, 2);
            IReadOnlyList<Tile> path = Require(GridPath.Find(6, 6, pinch, from, goal));
            T.Assert(path[path.Count - 1] == goal, "ends at the goal");
            List<Tile> walked = Expand(from, path);
            T.Assert(walked.Count > 1, "the direct diagonal is refused");
            AssertNoCornerCut(from, walked, pinch);
        });

        failed += T.Check("unreachable goal walled off returns null", () =>
        {
            Func<int, int, bool> ring = (x, y) => Math.Max(Math.Abs(x - 7), Math.Abs(y - 7)) == 1;
            T.Assert(GridPath.Find(10, 10, ring, new Tile(0, 0), new Tile(7, 7)) == null, "no path into the ring");
        });

        failed += T.Check("exceeding maxNodes returns null", () =>
        {
            var from = new Tile(0, 0);
            var goal = new Tile(49, 49);
            T.Assert(GridPath.Find(50, 50, open, from, goal, maxNodes: 5) == null, "budget of 5 is too small");
            T.Assert(GridPath.Find(50, 50, open, from, goal) != null, "default budget finds the path");
        });

        failed += T.Check("blocked start and goal still route", () =>
        {
            Func<int, int, bool> blockedEnds = (x, y) => (x == 0 && y == 0) || (x == 4 && y == 0);
            IReadOnlyList<Tile> path = Require(GridPath.Find(5, 3, blockedEnds, new Tile(0, 0), new Tile(4, 0)));
            T.Assert(path[path.Count - 1] == new Tile(4, 0), "ends at the blocked goal");
        });

        failed += T.Check("out-of-bounds tiles are blocked, not wrapped", () =>
        {
            // A wrapping index would let (3,0) step to (0,1) across the full wall at x=2.
            Func<int, int, bool> column = (x, y) => x == 2;
            T.Assert(GridPath.Find(4, 4, column, new Tile(3, 0), new Tile(0, 0)) == null, "wall column blocks the way");
            T.Assert(GridPath.Find(4, 4, open, new Tile(0, 0), new Tile(4, 0)) == null, "goal outside the grid");
            T.Assert(GridPath.Find(4, 4, open, new Tile(-1, 0), new Tile(2, 0)) == null, "start outside the grid");
        });

        failed += T.Check("equal from and to gives an empty path", () =>
        {
            IReadOnlyList<Tile>? path = GridPath.Find(4, 4, open, new Tile(1, 1), new Tile(1, 1));
            T.Assert(path != null && path.Count == 0, "empty list");
        });

        failed += T.Check("back-to-back calls on different map sizes match fresh results", () =>
        {
            // A 60x60 map with a wall at x=30 and a gap at y=50, then a small 8x8 map, then big again.
            // Stale scratch state from an earlier call would show up as a different path or a wrong null.
            Func<int, int, bool> bigWall = (x, y) => x == 30 && y != 50;
            Func<int, int, bool> smallWall = (x, y) => x == 4 && y <= 5;

            var bigFrom = new Tile(0, 0);
            var bigGoal = new Tile(59, 0);
            IReadOnlyList<Tile> big1 = Require(GridPath.Find(60, 60, bigWall, bigFrom, bigGoal));
            AssertSamePath("big map, first call", big1, Require(FindOnFreshThread(60, 60, bigWall, bigFrom, bigGoal)));
            T.Assert(Expand(bigFrom, big1).Exists(t => t.X == 30 && t.Y == 50), "big route passes the gap at y=50");

            var smallFrom = new Tile(0, 0);
            var smallGoal = new Tile(7, 0);
            IReadOnlyList<Tile>? small = GridPath.Find(8, 8, smallWall, smallFrom, smallGoal);
            AssertSamePath("small map after big", small, FindOnFreshThread(8, 8, smallWall, smallFrom, smallGoal));
            T.Assert(small != null && small.Count > 0 && small[small.Count - 1] == smallGoal, "small route ends at its goal");

            var midFrom = new Tile(5, 5);
            var midGoal = new Tile(55, 20);
            IReadOnlyList<Tile>? mid = GridPath.Find(60, 60, bigWall, midFrom, midGoal);
            AssertSamePath("big map, different query", mid, FindOnFreshThread(60, 60, bigWall, midFrom, midGoal));

            IReadOnlyList<Tile>? big2 = GridPath.Find(60, 60, bigWall, bigFrom, bigGoal);
            AssertSamePath("big map again", big2, FindOnFreshThread(60, 60, bigWall, bigFrom, bigGoal));
            AssertSamePath("big map repeats its first result", big2, big1);
        });

        return failed;
    }

    private static IReadOnlyList<Tile> Require(IReadOnlyList<Tile>? path) => path ?? throw new Exception("expected a path, got null");

    /// <summary>Runs the search on a new thread, which starts with empty thread-static scratch, so the result is what a fresh call gives.</summary>
    private static IReadOnlyList<Tile>? FindOnFreshThread(int width, int height, Func<int, int, bool> blocked, Tile from, Tile to)
    {
        IReadOnlyList<Tile>? result = null;
        Exception? error = null;
        var worker = new System.Threading.Thread(() =>
        {
            try
            {
                result = GridPath.Find(width, height, blocked, from, to);
            }
            catch (Exception e)
            {
                error = e;
            }
        });
        worker.Start();
        worker.Join();
        if (error != null)
            throw new Exception("fresh-thread search failed", error);
        return result;
    }

    private static void AssertSamePath(string label, IReadOnlyList<Tile>? actual, IReadOnlyList<Tile>? expected)
    {
        if (actual == null || expected == null)
        {
            T.Assert(actual == null && expected == null, label + ": one result is null and the other is not");
            return;
        }
        T.Assert(actual.Count == expected.Count, label + ": path length " + actual.Count + " vs " + expected.Count);
        for (int i = 0; i < actual.Count; i++)
            T.Assert(actual[i] == expected[i], label + ": tile " + i + " is " + actual[i] + " vs " + expected[i]);
    }

    /// <summary>Expands a simplified path into every tile it walks through, checking each segment is a straight 8-direction line.</summary>
    private static List<Tile> Expand(Tile from, IReadOnlyList<Tile> path)
    {
        var walked = new List<Tile>();
        Tile at = from;
        foreach (Tile point in path)
        {
            int dx = point.X - at.X;
            int dy = point.Y - at.Y;
            T.Assert(dx == 0 || dy == 0 || Math.Abs(dx) == Math.Abs(dy), "segment to " + point + " is not a straight line");
            int sx = Math.Sign(dx);
            int sy = Math.Sign(dy);
            while (at != point)
            {
                at = new Tile(at.X + sx, at.Y + sy);
                walked.Add(at);
            }
        }
        return walked;
    }

    private static void AssertNoCornerCut(Tile from, List<Tile> walked, Func<int, int, bool> blocked)
    {
        Tile previous = from;
        foreach (Tile tile in walked)
        {
            if (tile.X != previous.X && tile.Y != previous.Y)
            {
                T.Assert(!blocked(previous.X, tile.Y) && !blocked(tile.X, previous.Y), "diagonal " + previous + " to " + tile + " cuts a corner");
            }
            T.Assert(!blocked(tile.X, tile.Y), "walks through blocked tile " + tile);
            previous = tile;
        }
    }
}
