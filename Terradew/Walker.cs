using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Pathfinding;
using Terradew.Core.Protocol;
using Terradew.Core.Routing;

namespace Terradew;

/// <summary>
/// Walks villagers who are off their schedule (running away, chasing an enemy, heading back) with the same A* over
/// the same blocked tiles the creatures use, so they go around rocks, trees, furniture and Terraria blocks instead of
/// through them. Stardew's own villager search ignores most of those. Where the farmer is not, nobody sees the walk,
/// so Stardew's search does it.
/// </summary>
internal sealed class Walker
{
    private sealed class Route
    {
        public Point Goal;
        public int Near;
        public List<Tile> Tiles = new();
        public int Next;
        public int Stuck;
        public int Age;
        public float Speed;
        public int Facing = -1;
    }

    /// <summary>Most tiles one search may look at; a walk that needs more is not taken.</summary>
    private const int SearchLimit = 6000;

    /// <summary>A walk toward a moving goal is planned again this often, in ticks.</summary>
    private const int ReplanTicks = 30;

    /// <summary>Ticks without progress before the walk is planned again from where they stand.</summary>
    private const int StuckTicks = 20;

    private readonly Maps maps;
    private readonly Dictionary<string, Route> routes = new(StringComparer.Ordinal);

    public Walker(Maps maps)
    {
        this.maps = maps;
    }

    public void Reset() => routes.Clear();

    /// <summary>True while they are on a walk this class started, or on Stardew's path to the same end.</summary>
    public bool Busy(NPC v) => routes.ContainsKey(v.Name) || v.controller != null;

    public void Stop(NPC v)
    {
        if (routes.Remove(v.Name))
            v.Halt();
        v.controller = null;
    }

    /// <summary>
    /// Sends them to a tile, stopping once within <paramref name="near"/> tiles of it. Calling again with a goal close
    /// to the last one keeps the walk going. False when there is no way there.
    /// </summary>
    public bool Go(NPC v, Point goal, int near = 0, float speed = 2f, int facing = -1)
    {
        MapLayout? layout = LayoutFor(v);
        if (layout == null)
            return Stardew(v, goal, facing);
        if (routes.TryGetValue(v.Name, out Route? route) && route.Age < ReplanTicks && Chebyshev(route.Goal, goal) <= 1)
        {
            route.Speed = speed;
            route.Near = near;
            return true;
        }
        route = Plan(v, layout, goal, near);
        if (route == null)
        {
            Stop(v);
            return false;
        }
        route.Speed = speed;
        route.Facing = facing;
        v.controller = null;
        routes[v.Name] = route;
        return true;
    }

    /// <summary>True when there is a walk to that tile from where they stand.</summary>
    public bool CanReach(NPC v, Point goal)
    {
        MapLayout? layout = LayoutFor(v);
        if (layout == null)
        {
            var path = new PathFindController(v, v.currentLocation, goal, 2);
            return path.pathToEndPoint is { Count: > 0 };
        }
        return Open(layout, goal) && Plan(v, layout, goal, 0) != null;
    }

    /// <summary>The nearest open tile to a spot, within a few tiles, for a goal that may sit on a rock or in water.</summary>
    public Point? OpenNear(NPC v, Point spot, int radius = 3)
    {
        MapLayout? layout = LayoutFor(v);
        if (layout == null)
            return spot;
        for (int r = 0; r <= radius; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                        continue;
                    var tile = new Point(spot.X + dx, spot.Y + dy);
                    if (Open(layout, tile))
                        return tile;
                }
            }
        }
        return null;
    }

    /// <summary>Moves everyone on a walk one tick along it.</summary>
    public void Update()
    {
        if (routes.Count == 0)
            return;
        foreach (string name in routes.Keys.ToList())
        {
            NPC? v = Game1.getCharacterFromName(name);
            Route route = routes[name];
            MapLayout? layout = v == null ? null : LayoutFor(v);
            if (v == null || layout == null)
            {
                routes.Remove(name);
                continue;
            }
            route.Age++;
            if (Step(v, layout, route))
                continue;
            routes.Remove(name);
            v.Halt();
            if (route.Facing >= 0)
                v.faceDirection(route.Facing);
        }
    }

    /// <summary>One tick of walking. False once they have arrived (or the walk cannot go on).</summary>
    private bool Step(NPC v, MapLayout layout, Route route)
    {
        Point here = TileOf(v);
        if (Chebyshev(here, route.Goal) <= route.Near && (route.Near > 0 || Centred(v, here)))
            return false;
        if (route.Next >= route.Tiles.Count)
            return false;
        if (route.Stuck > StuckTicks)
        {
            Route? again = Plan(v, layout, route.Goal, route.Near);
            if (again == null)
                return false;
            route.Tiles = again.Tiles;
            route.Next = 0;
            route.Stuck = 0;
        }
        Tile next = route.Tiles[route.Next];
        Vector2 offset = Offset(v);
        var target = new Vector2(next.X * 64f + 32f, next.Y * 64f + 32f) - offset;
        Vector2 delta = target - v.Position;
        float distance = delta.Length();
        // Blocked tiles under the box now. A walk may leave a block, or stay at the same depth, but never go deeper:
        // a villager standing on a solid tile (after a warp, or a Stardew tile the map marks solid) is not let through it.
        int touching = Overlap(v, layout, v.Position);
        if (distance <= route.Speed && Footing.MayStep(touching, Overlap(v, layout, target)))
        {
            v.Position = target;
            route.Next++;
            route.Stuck = 0;
            return route.Next < route.Tiles.Count;
        }
        Vector2 step = delta / distance * route.Speed;
        bool moved = false;
        foreach (Vector2 part in new[] { step, new Vector2(step.X, 0f), new Vector2(0f, step.Y) })
        {
            if (part.LengthSquared() < 0.01f || !Footing.MayStep(touching, Overlap(v, layout, v.Position + part)))
                continue;
            v.Position += part;
            moved = true;
            break;
        }
        if (!moved)
        {
            route.Stuck++;
            return true;
        }
        Turn(v, step);
        v.animateInFacingDirection(Game1.currentGameTime);
        return true;
    }

    private static Route? Plan(NPC v, MapLayout layout, Point goal, int near)
    {
        Point here = TileOf(v);
        if (!Open(layout, goal) && near == 0)
            return null;
        IReadOnlyList<Tile>? path = GridPath.Find(layout.Width, layout.Height, layout.IsBlocked,
            new Tile(here.X, here.Y), new Tile(goal.X, goal.Y), SearchLimit, simplify: false);
        if (path == null)
            return null;
        var route = new Route { Goal = goal, Near = near, Tiles = new List<Tile>(path) };
        // Start from the middle of their own tile, so the first step never cuts a corner.
        if (!Centred(v, here))
            route.Tiles.Insert(0, new Tile(here.X, here.Y));
        // A goal on a blocked tile (an enemy over water) is only walked toward, never into.
        while (route.Tiles.Count > 0 && layout.IsBlocked(route.Tiles[^1].X, route.Tiles[^1].Y))
            route.Tiles.RemoveAt(route.Tiles.Count - 1);
        return route;
    }

    /// <summary>Stardew's own search, for walks the farmer cannot see.</summary>
    private bool Stardew(NPC v, Point goal, int facing)
    {
        routes.Remove(v.Name);
        var path = new PathFindController(v, v.currentLocation, goal, facing < 0 ? 2 : facing);
        if (path.pathToEndPoint is not { Count: > 0 })
            return false;
        v.controller = path;
        return true;
    }

    private MapLayout? LayoutFor(NPC v)
    {
        MapLayout? layout = maps.Current;
        if (layout == null || v.currentLocation == null || v.currentLocation != Game1.currentLocation)
            return null;
        return layout.PlaceKey == Maps.PlaceOf(v.currentLocation).Key ? layout : null;
    }

    private static bool Open(MapLayout layout, Point tile) => !layout.IsBlocked(tile.X, tile.Y);

    /// <summary>How many blocked tiles their box touches when they stand at that position.</summary>
    private static int Overlap(NPC v, MapLayout layout, Vector2 position)
    {
        Rectangle box = v.GetBoundingBox();
        box.X += (int)MathF.Round(position.X - v.Position.X);
        box.Y += (int)MathF.Round(position.Y - v.Position.Y);
        int left = box.Left / 64, right = (box.Right - 1) / 64, top = box.Top / 64, bottom = (box.Bottom - 1) / 64;
        return Footing.Overlap((x, y) => layout.IsBlocked(x, y), left, top, right, bottom);
    }

    /// <summary>From their position to the middle of their feet.</summary>
    private static Vector2 Offset(NPC v)
    {
        Point center = v.GetBoundingBox().Center;
        return new Vector2(center.X, center.Y) - v.Position;
    }

    private static Point TileOf(NPC v)
    {
        Point center = v.GetBoundingBox().Center;
        return new Point(center.X / 64, center.Y / 64);
    }

    private static bool Centred(NPC v, Point tile)
    {
        Point center = v.GetBoundingBox().Center;
        return Math.Abs(center.X - (tile.X * 64 + 32)) <= 2 && Math.Abs(center.Y - (tile.Y * 64 + 32)) <= 2;
    }

    private static int Chebyshev(Point a, Point b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>Faces the way they walk, but holds the old facing near a diagonal so it does not flicker.</summary>
    private static void Turn(NPC v, Vector2 toward)
    {
        int facing = Math.Abs(toward.X) > Math.Abs(toward.Y) ? (toward.X > 0 ? 1 : 3) : (toward.Y > 0 ? 2 : 0);
        if (facing == v.FacingDirection)
            return;
        float ax = Math.Abs(toward.X), ay = Math.Abs(toward.Y);
        bool clear = Math.Abs(ax - ay) > 0.3f * Math.Max(ax, ay);
        bool sideways = (v.FacingDirection == 1 || v.FacingDirection == 3) != (facing == 1 || facing == 3);
        if (clear || !sideways)
            v.faceDirection(facing);
    }
}
