using System;
using System.Collections.Generic;

namespace Terradew.Core.Routing;

/// <summary>A tile on the farmer's map.</summary>
public readonly record struct Tile(int X, int Y);

/// <summary>A ground monster that wants a route to the farmer.</summary>
public readonly record struct Chaser(int NpcId, Tile At);

/// <summary>The next tile a chaser should walk to.</summary>
public readonly record struct RouteStep(int NpcId, int TileX, int TileY);

/// <summary>Returns the tiles after <paramref name="from"/>, ending at <paramref name="to"/>. Null or empty means no path.</summary>
public delegate IReadOnlyList<Tile>? FindPath(Tile from, Tile to);

/// <summary>
/// Plans next steps for chasers. Each chaser keeps its own cached path and keeps using it while it
/// stays on that path, even after the goal moves. At most <c>searchesPerTick</c> searches run per call.
/// Chasers without a usable path are searched first; chasers with a stale goal are searched after them.
/// Within each group, searches rotate by NpcId so one slow chaser cannot starve the rest.
/// </summary>
public sealed class RoutePlanner
{
    private sealed class Route
    {
        public Tile Start;                              // where the chaser stood when the search ran (position -1)
        public List<Tile> Points = new List<Tile>();
        public int Index;                               // next point to walk to; Index - 1 is the current position
        public Tile Goal;
        public int RetryAt;                             // no new search before this tick (0 if none)
        public bool Failed;                             // the last search found no path
    }

    private readonly int _searchesPerTick;
    private readonly int _retryTicks;
    private readonly Dictionary<int, Route> _routes = new Dictionary<int, Route>();
    private int _lastServed;
    private bool _hasLastServed;

    public RoutePlanner(int searchesPerTick = 2, int retryTicks = 30)
    {
        if (searchesPerTick < 0)
            throw new ArgumentOutOfRangeException(nameof(searchesPerTick));
        if (retryTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(retryTicks));
        _searchesPerTick = searchesPerTick;
        _retryTicks = retryTicks;
    }

    /// <summary>Number of find() calls made by the last Plan call.</summary>
    public int SearchesLastTick { get; private set; }

    /// <summary>The farmer changed place: forget every route and the rotation cursor.</summary>
    public void Reset()
    {
        _routes.Clear();
        _hasLastServed = false;
        _lastServed = 0;
        SearchesLastTick = 0;
    }

    public IReadOnlyList<RouteStep> Plan(int tick, Tile goal, IReadOnlyList<Chaser> chasers, FindPath find)
    {
        if (chasers == null)
            throw new ArgumentNullException(nameof(chasers));
        if (find == null)
            throw new ArgumentNullException(nameof(find));

        SearchesLastTick = 0;
        DropAbsent(chasers);

        int count = chasers.Count;
        var usable = new bool[count];
        var priority = new int[count]; // 0 = no search needed, 1 = no usable path, 2 = usable but stale goal

        for (int i = 0; i < count; i++)
        {
            Chaser chaser = chasers[i];
            if (!_routes.TryGetValue(chaser.NpcId, out Route? route))
            {
                priority[i] = 1;
                continue;
            }

            usable[i] = IsUsable(route, chaser.At);
            if (!usable[i])
                priority[i] = 1;
            else if (route.Goal != goal)
                priority[i] = 2;
        }

        int budget = _searchesPerTick;
        for (int level = 1; level <= 2; level++)
        {
            var due = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (priority[i] == level)
                    due.Add(i);
            }

            foreach (int i in RotateByNpcId(due, chasers))
            {
                Chaser chaser = chasers[i];
                if (_routes.TryGetValue(chaser.NpcId, out Route? existing) && existing.RetryAt > tick)
                    continue;

                if (chaser.At == goal)
                {
                    // Already there: no path needed and no find() call spent.
                    _routes[chaser.NpcId] = new Route { Start = chaser.At, Goal = goal };
                    usable[i] = true;
                    priority[i] = 0;
                    Served(chaser.NpcId);
                    continue;
                }

                if (budget == 0)
                    continue;

                budget--;
                SearchesLastTick++;
                IReadOnlyList<Tile>? path = find(chaser.At, goal);
                if (path == null || path.Count == 0)
                {
                    _routes[chaser.NpcId] = new Route
                    {
                        Start = chaser.At,
                        Goal = goal,
                        Failed = true,
                        RetryAt = tick + _retryTicks,
                    };
                    usable[i] = false;
                }
                else
                {
                    _routes[chaser.NpcId] = new Route
                    {
                        Start = chaser.At,
                        Points = new List<Tile>(path),
                        Index = 0,
                        Goal = goal,
                    };
                    usable[i] = true;
                }

                priority[i] = 0;
                Served(chaser.NpcId);
            }
        }

        var steps = new List<RouteStep>();
        for (int i = 0; i < count; i++)
        {
            if (!usable[i])
                continue;

            Chaser chaser = chasers[i];
            if (!_routes.TryGetValue(chaser.NpcId, out Route? route))
                continue;
            if (route.Failed || route.Index >= route.Points.Count)
                continue;

            Tile next = route.Points[route.Index];
            steps.Add(new RouteStep(chaser.NpcId, next.X, next.Y));
        }

        return steps;
    }

    private void Served(int npcId)
    {
        _lastServed = npcId;
        _hasLastServed = true;
    }

    /// <summary>Checks whether the chaser is still on the path ahead of it, and advances Index if so.</summary>
    private static bool IsUsable(Route route, Tile at)
    {
        if (route.Failed)
            return false;

        for (int p = route.Index - 1; p < route.Points.Count; p++)
        {
            Tile point = p < 0 ? route.Start : route.Points[p];
            if (point == at)
            {
                route.Index = p + 1;
                return true;
            }
        }

        return false;
    }

    /// <summary>Orders indices by NpcId, starting just after the last served NpcId and wrapping around.</summary>
    private List<int> RotateByNpcId(List<int> due, IReadOnlyList<Chaser> chasers)
    {
        due.Sort((a, b) => chasers[a].NpcId.CompareTo(chasers[b].NpcId));

        int start = 0;
        if (_hasLastServed)
        {
            while (start < due.Count && chasers[due[start]].NpcId <= _lastServed)
                start++;
            if (start == due.Count)
                start = 0;
        }

        var order = new List<int>(due.Count);
        for (int k = 0; k < due.Count; k++)
            order.Add(due[(start + k) % due.Count]);
        return order;
    }

    private void DropAbsent(IReadOnlyList<Chaser> chasers)
    {
        var present = new HashSet<int>();
        foreach (Chaser chaser in chasers)
            present.Add(chaser.NpcId);

        var gone = new List<int>();
        foreach (int npcId in _routes.Keys)
        {
            if (!present.Contains(npcId))
                gone.Add(npcId);
        }

        foreach (int npcId in gone)
            _routes.Remove(npcId);
    }
}
