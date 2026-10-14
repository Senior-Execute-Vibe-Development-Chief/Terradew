using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Pathfinding;
using Terradew.Core;
using Terradew.Core.Protocol;
using Terradew.Core.Routing;

namespace Terradew;

/// <summary>Asks Stardew's villager path search for a walk to the farmer and sends each ground chaser its next open tile.</summary>
internal sealed class MobRoutes
{
    public const int MapLimit = 200;
    public const int SearchLimit = 10000;

    private readonly RoutePlanner planner = new(searchesPerTick: 2, retryTicks: 30);
    private string place = "";
    private string lastSent = "";
    private int tick;

    public void Reset()
    {
        planner.Reset();
        place = "";
        lastSent = "";
    }

    public void Update(Bridge bridge, Farmer player, string placeKey)
    {
        tick++;
        GameLocation? location = player.currentLocation;
        WorldState? world = bridge.World;
        if (location?.Map == null || world == null || !bridge.WorldIsFor(placeKey))
            return;
        if (placeKey != place)
        {
            place = placeKey;
            planner.Reset();
        }
        int width = location.Map.Layers[0].LayerWidth;
        int height = location.Map.Layers[0].LayerHeight;
        var message = new Routes { PlaceKey = placeKey };
        if (width <= MapLimit && height <= MapLimit)
        {
            Rectangle box = player.GetBoundingBox();
            var goal = new Tile(box.Center.X / 64, (box.Bottom - 1) / 64);
            var chasers = new List<Chaser>();
            foreach (EntityState entity in world.Entities)
            {
                if (!entity.Has(EntityFlags.Chasing) || entity.Has(EntityFlags.Flyer) || entity.Has(EntityFlags.Worm))
                    continue;
                (float x, float y) = WorldProjection.ToMap(world.RegionX, world.RegionY, entity.X, entity.Y);
                var at = new Tile((int)Math.Floor(x / 64f), (int)Math.Floor((y - 1f) / 64f));
                if (at.X < 0 || at.Y < 0 || at.X >= width || at.Y >= height)
                    continue;
                chasers.Add(new Chaser(entity.Id, at));
            }
            foreach (RouteStep step in planner.Plan(tick, goal, chasers, (from, to) => Find(location, from, to)))
                message.Steps.Add(new RouteEntry(step.NpcId, step.TileX, step.TileY));
        }
        string signature = placeKey + ":" + string.Join(";", message.Steps.Select(s => $"{s.NpcId},{s.TileX},{s.TileY}"));
        if (signature == lastSent && tick % 60 != 0)
            return;
        lastSent = signature;
        bridge.Send(message);
    }

    private static IReadOnlyList<Tile>? Find(GameLocation location, Tile from, Tile to)
    {
        try
        {
            Stack<Point>? path = PathFindController.findPathForNPCSchedules(new Point(from.X, from.Y), new Point(to.X, to.Y), location, SearchLimit, null);
            if (path == null || path.Count == 0)
                return null;
            var tiles = new List<Tile>(path.Count);
            while (path.Count > 0)
            {
                Point point = path.Pop();
                if (tiles.Count == 0 && point.X == from.X && point.Y == from.Y)
                    continue;
                tiles.Add(new Tile(point.X, point.Y));
            }
            return tiles.Count == 0 ? null : tiles;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
