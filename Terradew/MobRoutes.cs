using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Pathfinding;
using Terradew.Core;

namespace Terradew;

/// <summary>
/// Asks Stardew for a walk to the farmer and publishes the next open tile for each ground mob.
/// </summary>
internal static class MobRoutes
{
    public const int MapLimit = 127;
    public const int SearchLimit = 10000;

    private sealed class Cached
    {
        public int GoalX;
        public int GoalY;
        public Point[] Points = Array.Empty<Point>();
        public int Index;
    }

    private static readonly Dictionary<int, Cached> Cache = new();
    private static string place = "";

    public static void Publish(Farmer player, BridgeSnapshot snapshot, string placeName, int placeHash)
    {
        GameLocation? location = player.currentLocation;
        if (location?.Map == null || location.Map.Layers.Count == 0 || snapshot.RegionX == 0 && snapshot.RegionY == 0)
            return;
        if (placeName != place)
        {
            place = placeName;
            Cache.Clear();
        }
        int width = location.Map.Layers[0].LayerWidth;
        int height = location.Map.Layers[0].LayerHeight;
        if (width <= 0 || height <= 0 || width > MapLimit || height > MapLimit)
        {
            Cache.Clear();
            RouteMap.Publish(placeHash, Array.Empty<RouteMap.Step>());
            return;
        }

        Rectangle box = player.GetBoundingBox();
        var farmer = new Point(box.Center.X / 64, box.Center.Y / 64);
        var steps = new List<RouteMap.Step>();
        var seen = new HashSet<int>();
        bool searched = false;
        foreach (BridgeEntity entity in snapshot.Entities)
        {
            if (!IsGroundChaser(entity))
                continue;
            (float sx, float sy) = WorldProjection.ToMap(snapshot.RegionX, snapshot.RegionY, entity.X, entity.Y);
            var mob = new Point((int)Math.Floor(sx / 64f), (int)Math.Floor(sy / 64f));
            if (mob.X < 0 || mob.Y < 0 || mob.X >= width || mob.Y >= height)
                continue;
            seen.Add(entity.NpcId);
            Cache.TryGetValue(entity.NpcId, out Cached? cache);
            bool stale = cache == null || cache.GoalX != farmer.X || cache.GoalY != farmer.Y || !OnPath(cache, mob);
            if (stale)
            {
                if (searched)
                    continue;
                searched = true;
                cache = Search(location, mob, farmer);
                Cache[entity.NpcId] = cache;
            }
            Point? next = Next(cache!, mob);
            if (next != null)
                steps.Add(new RouteMap.Step(entity.NpcId, next.Value.X, next.Value.Y));
        }
        foreach (int id in Cache.Keys.ToArray())
        {
            if (!seen.Contains(id))
                Cache.Remove(id);
        }
        RouteMap.Publish(placeHash, steps);
    }

    private static bool IsGroundChaser(BridgeEntity entity)
    {
        if (entity.ContactDamage <= 0)
            return false;
        BridgeFlags flags = entity.Flags;
        return !flags.HasFlag(BridgeFlags.Town)
            && !flags.HasFlag(BridgeFlags.Glider)
            && !flags.HasFlag(BridgeFlags.Worm);
    }

    private static Cached Search(GameLocation location, Point mob, Point farmer)
    {
        var cache = new Cached { GoalX = farmer.X, GoalY = farmer.Y };
        if (mob == farmer)
            return cache;
        try
        {
            Stack<Point>? path = PathFindController.findPathForNPCSchedules(mob, farmer, location, SearchLimit, null);
            if (path == null || path.Count == 0)
                return cache;
            var points = new List<Point>(path.Count);
            while (path.Count > 0)
            {
                Point point = path.Pop();
                if (points.Count == 0 && point == mob)
                    continue;
                points.Add(point);
            }
            cache.Points = points.ToArray();
        }
        catch (Exception)
        {
            cache.Points = Array.Empty<Point>();
        }
        return cache;
    }

    private static bool OnPath(Cached cache, Point mob)
    {
        if (cache.Index < cache.Points.Length && cache.Points[cache.Index] == mob)
            return true;
        if (cache.Index > 0 && cache.Points[cache.Index - 1] == mob)
            return true;
        return cache.Points.Length == 0 && mob.X == cache.GoalX && mob.Y == cache.GoalY;
    }

    private static Point? Next(Cached cache, Point mob)
    {
        if (cache.Index < cache.Points.Length && cache.Points[cache.Index] == mob)
            cache.Index++;
        if (cache.Index >= cache.Points.Length)
            return null;
        return cache.Points[cache.Index];
    }
}
