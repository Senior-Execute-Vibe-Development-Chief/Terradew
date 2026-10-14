using System;
using System.Collections.Generic;
using Terradew.Core.Routing;

/// <summary>Route planner checks: caching, fairness, retries and the search budget.</summary>
internal static class RoutePlannerTests
{
    public static int Run()
    {
        int failed = 0;

        failed += T.Check("a chaser whose search fails does not starve the others", () =>
        {
            var start = new Tile(0, 0);
            int failedCalls = 0;
            FindPath find = (from, to) =>
            {
                if (from == start)
                {
                    failedCalls++;
                    return null;
                }
                return Straight(from, to);
            };
            var planner = new RoutePlanner(2, 30);
            var chasers = new List<Chaser> { new Chaser(1, start), new Chaser(2, new Tile(0, 5)), new Chaser(3, new Tile(0, 9)) };
            var tickSteps = new List<IReadOnlyList<RouteStep>>();
            for (int tick = 1; tick <= 3; tick++)
                tickSteps.Add(planner.Plan(tick, new Tile(4, 4), chasers, find));

            IReadOnlyList<RouteStep> last = tickSteps[2];
            T.Assert(Has(last, 2), "second chaser has a step by tick 3");
            T.Assert(Has(last, 3), "third chaser has a step by tick 3");
            T.Assert(!Has(last, 1), "failed chaser has no step");
            T.Assert(failedCalls == 1, "failed chaser searched only once in 3 ticks");
        });

        failed += T.Check("a moving farmer keeps every chaser stepping and all refresh within five ticks", () =>
        {
            var planner = new RoutePlanner(2, 30);
            var chasers = Line(10);
            var searchedFrom = new Dictionary<Tile, int>();
            FindPath find = (from, to) =>
            {
                searchedFrom[from] = searchedFrom.TryGetValue(from, out int n) ? n + 1 : 1;
                return Straight(from, to);
            };
            for (int tick = 1; tick <= 5; tick++)
                planner.Plan(tick, new Tile(20, 0), chasers, find);

            searchedFrom.Clear();
            for (int tick = 6; tick <= 10; tick++)
            {
                IReadOnlyList<RouteStep> steps = planner.Plan(tick, new Tile(20, tick), chasers, find);
                T.Assert(steps.Count == 10, "every chaser steps on tick " + tick + ", got " + steps.Count);
                T.Assert(planner.SearchesLastTick <= 2, "budget on tick " + tick);
            }

            foreach (Chaser chaser in chasers)
                T.Assert(searchedFrom.ContainsKey(chaser.At), "chaser " + chaser.NpcId + " refreshed within five ticks");
        });

        failed += T.Check("a failed chaser waits out the retry window", () =>
        {
            var start = new Tile(0, 0);
            int calls = 0;
            FindPath find = (from, to) =>
            {
                calls++;
                return null;
            };
            var planner = new RoutePlanner(2, 5);
            var chasers = new List<Chaser> { new Chaser(1, start) };
            planner.Plan(1, new Tile(3, 0), chasers, find);
            T.Assert(calls == 1, "first search");
            for (int tick = 2; tick <= 5; tick++)
            {
                IReadOnlyList<RouteStep> steps = planner.Plan(tick, new Tile(3, 0), chasers, find);
                T.Assert(calls == 1, "no retry on tick " + tick);
                T.Assert(steps.Count == 0, "no step on tick " + tick);
            }
            planner.Plan(6, new Tile(3, 0), chasers, find);
            T.Assert(calls == 2, "retried once the window passes");
        });

        failed += T.Check("a walking chaser advances without new searches when the goal is unchanged", () =>
        {
            var planner = new RoutePlanner(2, 30);
            int calls = 0;
            FindPath find = (from, to) =>
            {
                calls++;
                return Straight(from, to);
            };
            var goal = new Tile(3, 0);
            Tile at = new Tile(0, 0);
            Tile[] expected = { new Tile(1, 0), new Tile(2, 0), new Tile(3, 0) };
            for (int tick = 1; tick <= 3; tick++)
            {
                IReadOnlyList<RouteStep> steps = planner.Plan(tick, goal, new List<Chaser> { new Chaser(7, at) }, find);
                T.Assert(steps.Count == 1 && steps[0].TileX == expected[tick - 1].X && steps[0].TileY == expected[tick - 1].Y, "step on tick " + tick);
                at = expected[tick - 1];
            }
            T.Assert(calls == 1, "only the first tick searched, got " + calls);
            T.Assert(planner.Plan(4, goal, new List<Chaser> { new Chaser(7, at) }, find).Count == 0, "arrived chaser has no step");
            T.Assert(calls == 1, "arrival does not search");
        });

        failed += T.Check("a chaser off its path triggers a new search", () =>
        {
            var planner = new RoutePlanner(2, 30);
            int calls = 0;
            FindPath find = (from, to) =>
            {
                calls++;
                return Straight(from, to);
            };
            var goal = new Tile(3, 0);
            planner.Plan(1, goal, new List<Chaser> { new Chaser(7, new Tile(0, 0)) }, find);
            IReadOnlyList<RouteStep> steps = planner.Plan(2, goal, new List<Chaser> { new Chaser(7, new Tile(0, 7)) }, find);
            T.Assert(calls == 2, "re-searched");
            T.Assert(steps.Count == 1 && steps[0].TileX == 1 && steps[0].TileY == 7, "new path starts from the chaser");
        });

        failed += T.Check("a chaser already at the goal needs no search", () =>
        {
            int calls = 0;
            FindPath find = (from, to) =>
            {
                calls++;
                return Straight(from, to);
            };
            var planner = new RoutePlanner();
            IReadOnlyList<RouteStep> steps = planner.Plan(1, new Tile(5, 5), new List<Chaser> { new Chaser(1, new Tile(5, 5)) }, find);
            T.Assert(steps.Count == 0, "no step");
            T.Assert(calls == 0 && planner.SearchesLastTick == 0, "no find call");
        });

        failed += T.Check("steps follow the order of the chasers", () =>
        {
            var planner = new RoutePlanner(10, 30);
            var chasers = new List<Chaser> { new Chaser(5, new Tile(0, 0)), new Chaser(2, new Tile(0, 4)), new Chaser(9, new Tile(0, 8)) };
            IReadOnlyList<RouteStep> steps = planner.Plan(1, new Tile(3, 3), chasers, Straight);
            T.Assert(steps.Count == 3, "three steps");
            T.Assert(steps[0].NpcId == 5 && steps[1].NpcId == 2 && steps[2].NpcId == 9, "chaser order");
        });

        failed += T.Check("reset forgets every cached route", () =>
        {
            int calls = 0;
            FindPath find = (from, to) =>
            {
                calls++;
                return Straight(from, to);
            };
            var planner = new RoutePlanner(2, 30);
            var chasers = new List<Chaser> { new Chaser(1, new Tile(0, 0)) };
            var goal = new Tile(3, 0);
            planner.Plan(1, goal, chasers, find);
            planner.Plan(2, goal, chasers, find);
            T.Assert(calls == 1, "cached on tick 2");
            planner.Reset();
            IReadOnlyList<RouteStep> steps = planner.Plan(3, goal, chasers, find);
            T.Assert(calls == 2, "searched again after reset");
            T.Assert(planner.SearchesLastTick == 1, "one search after reset");
            T.Assert(steps.Count == 1, "step after reset");
        });

        failed += T.Check("the search budget is never exceeded", () =>
        {
            var planner = new RoutePlanner(2, 30);
            int callsThisTick = 0;
            FindPath find = (from, to) =>
            {
                callsThisTick++;
                return Straight(from, to);
            };
            var chasers = Line(10);
            for (int tick = 1; tick <= 20; tick++)
            {
                callsThisTick = 0;
                planner.Plan(tick, new Tile(20, tick), chasers, find);
                T.Assert(callsThisTick <= 2, "find calls on tick " + tick + ": " + callsThisTick);
                T.Assert(planner.SearchesLastTick == callsThisTick, "SearchesLastTick matches on tick " + tick);
            }
        });

        return failed;
    }

    /// <summary>Walks x first, then y, one tile at a time. Returns the tiles after <paramref name="from"/>.</summary>
    private static IReadOnlyList<Tile>? Straight(Tile from, Tile to)
    {
        var path = new List<Tile>();
        int x = from.X;
        int y = from.Y;
        while (x != to.X)
        {
            x += Math.Sign(to.X - x);
            path.Add(new Tile(x, y));
        }
        while (y != to.Y)
        {
            y += Math.Sign(to.Y - y);
            path.Add(new Tile(x, y));
        }
        return path;
    }

    /// <summary>Chasers 1..count, standing at distinct tiles down the x=0 column.</summary>
    private static List<Chaser> Line(int count)
    {
        var chasers = new List<Chaser>();
        for (int i = 1; i <= count; i++)
            chasers.Add(new Chaser(i, new Tile(0, 3 * i)));
        return chasers;
    }

    private static bool Has(IReadOnlyList<RouteStep> steps, int npcId)
    {
        foreach (RouteStep step in steps)
        {
            if (step.NpcId == npcId)
                return true;
        }
        return false;
    }
}
