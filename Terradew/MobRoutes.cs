using Microsoft.Xna.Framework;
using StardewValley;
using Terradew.Core;
using Terradew.Core.Protocol;
using Terradew.Core.Routing;

namespace Terradew;

/// <summary>
/// Finds each ground chaser a walk to the farmer with A* over the same blocked tiles Terraria was stamped with
/// (Stardew's villager search walks through rocks, trees and furniture), and sends it the next tile.
/// </summary>
internal sealed class MobRoutes
{
    public const int MapLimit = 256;
    public const int SearchLimit = 8000;

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

    public void Update(Bridge bridge, Maps maps, Farmer player, string placeKey)
    {
        tick++;
        GameLocation? location = player.currentLocation;
        WorldState? world = bridge.World;
        MapLayout? layout = maps.Current;
        if (location?.Map == null || world == null || layout == null || layout.PlaceKey != placeKey || !bridge.WorldIsFor(placeKey))
            return;
        if (placeKey != place)
        {
            place = placeKey;
            planner.Reset();
        }
        int width = layout.Width;
        int height = layout.Height;
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
            foreach (RouteStep step in planner.Plan(tick, goal, chasers, (from, to) => GridPath.Find(width, height, layout.IsBlocked, from, to, SearchLimit, simplify: false)))
                message.Steps.Add(new RouteEntry(step.NpcId, step.TileX, step.TileY));
        }
        string signature = placeKey + ":" + string.Join(";", message.Steps.Select(s => $"{s.NpcId},{s.TileX},{s.TileY}"));
        if (signature == lastSent && tick % 60 != 0)
            return;
        lastSent = signature;
        bridge.Send(message);
    }
}
