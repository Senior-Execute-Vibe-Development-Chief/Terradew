using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace Terradew;

/// <summary>Breaks the Stardew breakables a bomb Terraria blew up would reach, using Stardew's own bomb blast.</summary>
internal sealed class Explosions
{
    /// <summary>Damage a blast deals to a grass tile, a small tree or a resource clump. Enough to break each in one hit.</summary>
    private const int BlastDamage = 50;

    /// <summary>Only in the farmer's place: a blast in another place has nothing in front of the farmer to break.</summary>
    public void OnExplosion(Core.Protocol.Explosion blast, IMonitor? monitor = null)
    {
        GameLocation? location = Game1.currentLocation;
        if (location == null || Game1.player == null)
        {
            monitor?.Log($"Terradew skipped a Terraria blast in {blast.PlaceKey}: no location to blow up.", LogLevel.Trace);
            return;
        }
        string here = Maps.PlaceOf(location).Key;
        if (here != blast.PlaceKey)
        {
            monitor?.Log($"Terradew skipped a Terraria blast in {blast.PlaceKey}: the farmer is in {here}.", LogLevel.Trace);
            return;
        }
        monitor?.Log($"Terradew applied a Terraria blast in {blast.PlaceKey} at {blast.TileX:0.0},{blast.TileY:0.0}, radius {blast.Radius}.", LogLevel.Trace);
        Vector2 centre = new(blast.TileX, blast.TileY);
        // Stardew drops from this blast are the farmer's, so Mining's Terraria drop replacement must see it, even when the explode patch did not run.
        Mining.NoteBlast();
        // Noted before the blast, so the sweep knows what was in reach and which of it the blast left standing.
        var objects = new List<(Vector2 Tile, SObject Obj)>();
        foreach (var pair in location.objects.Pairs)
        {
            if (InReach(pair.Key, centre, blast.Radius) && Breakable(pair.Value))
                objects.Add((pair.Key, pair.Value));
        }
        var features = new List<(Vector2 Tile, TerrainFeature Feature)>();
        foreach (var pair in location.terrainFeatures.Pairs)
        {
            // Grass and small trees only; fruit trees and crops are not what a Stardew bomb fells.
            // A grown tree is left standing: removing it here would give no wood.
            bool wanted = pair.Value is Grass || pair.Value is Tree { growthStage.Value: < Tree.treeStage };
            if (wanted && InReach(pair.Key, centre, blast.Radius))
                features.Add((pair.Key, pair.Value));
        }
        var clumps = new List<ResourceClump>();
        foreach (ResourceClump clump in location.resourceClumps)
        {
            if (InReach(clump.Tile, centre, blast.Radius))
                clumps.Add(clump);
        }
        try
        {
            // Terraria already hurt the farmer, so the farmer is not hurt again. Stardew's blast breaks what a Stardew bomb breaks.
            location.explode(centre, blast.Radius, Game1.player, damageFarmers: false);
        }
        catch (Exception error)
        {
            monitor?.Log($"Stardew's blast threw in {blast.PlaceKey}; the sweep still breaks what is in reach. {error.Message}", LogLevel.Warn);
        }
        Sweep(location, objects, features, clumps, blast, monitor);
    }

    /// <summary>
    /// Breaks what Stardew's own blast left standing, the way a Stardew bomb would: stones, weeds, twigs and forage come
    /// off the floor, grass and small trees are felled, and resource clumps take the blast's damage.
    /// </summary>
    private static void Sweep(
        GameLocation location,
        List<(Vector2 Tile, SObject Obj)> objects,
        List<(Vector2 Tile, TerrainFeature Feature)> features,
        List<ResourceClump> clumps,
        Core.Protocol.Explosion blast,
        IMonitor? monitor)
    {
        var broken = new List<string>();
        int objectsLeft = 0;
        foreach (var (tile, obj) in objects)
        {
            if (!location.objects.Pairs.Any(pair => pair.Key == tile && ReferenceEquals(pair.Value, obj)))
                continue;
            objectsLeft++;
            try
            {
                if (obj.IsBreakableStone())
                    location.OnStoneDestroyed(obj.ItemId, (int)tile.X, (int)tile.Y, Game1.player);
                location.objects.Remove(tile);
                broken.Add($"{obj.Name} at {tile.X:0},{tile.Y:0}");
            }
            catch (Exception error)
            {
                monitor?.Log($"Terradew could not break {obj.Name} at {tile.X:0},{tile.Y:0}. {error.Message}", LogLevel.Warn);
            }
        }
        int featuresLeft = 0;
        foreach (var (tile, feature) in features)
        {
            if (!location.terrainFeatures.Pairs.Any(pair => pair.Key == tile && ReferenceEquals(pair.Value, feature)))
                continue;
            featuresLeft++;
            try
            {
                feature.performToolAction(null, BlastDamage, tile);
                if (location.terrainFeatures.Pairs.Any(pair => pair.Key == tile && ReferenceEquals(pair.Value, feature)))
                    location.terrainFeatures.Remove(tile);
                broken.Add($"{feature.GetType().Name} at {tile.X:0},{tile.Y:0}");
            }
            catch (Exception error)
            {
                monitor?.Log($"Terradew could not break a {feature.GetType().Name} at {tile.X:0},{tile.Y:0}. {error.Message}", LogLevel.Warn);
            }
        }
        int clumpsLeft = 0;
        foreach (ResourceClump clump in clumps)
        {
            if (!location.resourceClumps.Contains(clump))
                continue;
            clumpsLeft++;
            try
            {
                clump.performToolAction(null, BlastDamage, clump.Tile);
                broken.Add($"resource clump at {clump.Tile.X:0},{clump.Tile.Y:0}");
            }
            catch (Exception error)
            {
                monitor?.Log($"Terradew could not damage a resource clump at {clump.Tile.X:0},{clump.Tile.Y:0}. {error.Message}", LogLevel.Warn);
            }
        }
        if (objects.Count + features.Count + clumps.Count == 0)
            return;
        monitor?.Log(
            $"Terradew's blast in {blast.PlaceKey} reached {objects.Count} breakable objects, {features.Count} terrain features and {clumps.Count} clumps; "
            + $"Stardew's blast left {objectsLeft}, {featuresLeft} and {clumpsLeft}, and Terradew broke: {(broken.Count == 0 ? "nothing" : string.Join(", ", broken))}.",
            LogLevel.Trace);
    }

    /// <summary>
    /// The Stardew breakables a bomb breaks: breakable stones, weeds, twigs and spawned forage. Terraria chests and stations,
    /// furniture, machines holding an item, and big objects are never broken here.
    /// </summary>
    private static bool Breakable(SObject obj)
    {
        if (obj is Chest or Furniture || obj.bigCraftable.Value || obj.heldObject.Value != null)
            return false;
        if (obj.modData.TryGetValue(Caves.KindKey, out _) || obj.modData.TryGetValue(Stations.TypeKey, out _) || obj.modData.TryGetValue(Torches.TypeKey, out _))
            return false;
        return obj.IsBreakableStone() || obj.Name.Contains("Weeds") || obj.Name.Contains("Twig") || obj.IsSpawnedObject;
    }

    /// <summary>True when the tile's centre is within the blast's circle. Tiles are whole numbers, so the centre is half a tile in.</summary>
    private static bool InReach(Vector2 tile, Vector2 centre, int radius)
        => Vector2.Distance(tile + new Vector2(0.5f, 0.5f), centre) <= radius;
}
