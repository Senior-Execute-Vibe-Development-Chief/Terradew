using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using Terradew.Core.Protocol;
using Terradew.Core.World;

namespace Terradew;

/// <summary>Tells Terraria what the farmer cannot walk through on each map, and which biome the map is.</summary>
internal sealed class Maps
{
    internal const CollisionMask Mask = CollisionMask.Buildings | CollisionMask.Furniture | CollisionMask.Objects | CollisionMask.TerrainFeatures;

    private string lastKey = "";
    private int lastHash;
    private int version;

    public MapLayout? Current { get; private set; }

    public static Place PlaceOf(GameLocation location)
        => Places.Classify(location.NameOrUniqueName, location.IsOutdoors, location is MineShaft shaft ? shaft.mineLevel : 0);

    public void Reset()
    {
        lastKey = "";
        lastHash = 0;
        Current = null;
    }

    /// <summary>Sends the current map when the farmer arrives, and again whenever its obstacles change.</summary>
    public void Update(Bridge bridge, GameLocation location, uint tick, bool force)
    {
        Place place = PlaceOf(location);
        bool arrived = place.Key != lastKey;
        if (!arrived && !force && tick % 30 != 0)
            return;
        MapLayout? layout = Build(location, place);
        if (layout == null)
            return;
        int hash = Hash(layout);
        if (!arrived && !force && hash == lastHash)
            return;
        if (hash != lastHash || arrived)
            version++;
        layout.Version = version;
        lastKey = place.Key;
        lastHash = hash;
        Current = layout;
        bridge.Send(layout);
    }

    public static MapLayout? Build(GameLocation location, Place place)
    {
        if (location.Map == null || location.Map.Layers.Count == 0)
            return null;
        int width = location.Map.Layers[0].LayerWidth;
        int height = location.Map.Layers[0].LayerHeight;
        if (width <= 0 || height <= 0 || width > 400 || height > 400)
            return null;
        var blocked = new bool[width * height];
        int water = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool wet = location.isWaterTile(x, y);
                if (wet)
                    water++;
                blocked[y * width + x] = wet || location.IsTileBlockedBy(new Vector2(x, y), Mask, CollisionMask.None, false);
            }
        }
        return new MapLayout
        {
            PlaceKey = place.Key,
            Width = width,
            Height = height,
            Biome = (byte)place.Biome,
            Band = (byte)place.Band,
            Spawns = place.Spawns,
            IsMineSlot = place.IsMineSlot,
            Floor = location is MineShaft shaft ? shaft.mineLevel : 0,
            WaterTiles = water,
            Blocked = blocked,
        };
    }

    private static int Hash(MapLayout layout)
    {
        int hash = layout.Width * 397 ^ layout.Height ^ layout.Floor * 7919;
        for (int i = 0; i < layout.Blocked.Length; i++)
        {
            if (layout.Blocked[i])
                hash = unchecked(hash * 31 + i);
        }
        return hash;
    }
}
