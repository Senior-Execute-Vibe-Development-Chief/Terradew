using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.Buildings;
using StardewValley.GameData.Characters;
using Terradew.Core.Protocol;
using Terradew.Core.Town;
using Terradew.Core.World;

namespace Terradew;

/// <summary>
/// Tells Terraria where each town NPC lives. Every Terraria town NPC takes over the home of one Stardew villager
/// (see <see cref="TownHomes"/>): the villager's first home in Stardew's character data is the house, and the NPC
/// is placed on the nearest open tile there. The Terraria House building is no longer built by Robin; the
/// building data is kept so old saves that already have one still load.
/// </summary>
internal sealed class Houses
{
    public const string BuildingId = "Terradew.TerrariaHouse";

    private readonly IMonitor monitor;

    // Fallback search for a free tile around a home: rings of this many tiles out from the home tile.
    private const int MaxRing = 8;

    public Houses(IMonitor monitor)
    {
        this.monitor = monitor;
    }

    public void OnAssetRequested(AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo("Data/Buildings"))
            return;
        e.Edit(asset =>
        {
            var data = asset.AsDictionary<string, BuildingData>().Data;
            if (!data.TryGetValue("Shed", out BuildingData? shed))
                return;
            data[BuildingId] = new BuildingData
            {
                Name = "Terraria House",
                Description = "No longer used. Terraria town NPCs now live in their Stardew villager's home.",
                Texture = shed.Texture,
                SourceRect = shed.SourceRect,
                Size = shed.Size,
                CollisionMap = shed.CollisionMap,
                HumanDoor = shed.HumanDoor,
                IndoorMap = shed.IndoorMap,
                IndoorMapType = shed.IndoorMapType,
                BuildCost = 8000,
                BuildDays = 1,
                BuildMaterials = new List<BuildingMaterial>
                {
                    new BuildingMaterial { ItemId = "(O)388", Amount = 250 },
                    new BuildingMaterial { ItemId = "(O)390", Amount = 100 },
                },
            };
        });
    }

    /// <summary>
    /// Sends each mapped town NPC's home, plus the layout of every map a home is on, so Terraria can place
    /// residents even before you visit. An NPC whose villager has no usable home is left out.
    /// </summary>
    public void Publish(Bridge bridge)
    {
        var homes = new Homes();
        Dictionary<string, MapLayout> layouts = new();
        Dictionary<string, HashSet<Point>> used = new();
        var sentLayouts = new HashSet<string>();
        IDictionary<string, CharacterData> characters = Game1.characterData;

        foreach (string npc in TownHomes.Npcs)
        {
            try
            {
                string? villager = TownHomes.VillagerOf(npc);
                if (villager == null || !characters.TryGetValue(villager, out CharacterData? data) || data.Home == null || data.Home.Count == 0)
                    continue;
                CharacterHomeData home = data.Home[0];
                GameLocation? loc = Game1.getLocationFromName(home.Location);
                if (loc == null)
                    continue;
                Place place = Maps.PlaceOf(loc);
                if (!layouts.TryGetValue(place.Key, out MapLayout? layout))
                {
                    layout = Maps.Build(loc, place);
                    if (layout == null)
                        continue;
                    layouts[place.Key] = layout;
                }
                if (sentLayouts.Add(place.Key))
                    bridge.Send(layout);

                if (!used.TryGetValue(place.Key, out HashSet<Point>? taken))
                {
                    taken = new HashSet<Point>();
                    used[place.Key] = taken;
                }
                Point tile = home.Tile;
                if (layout.IsBlocked(tile.X, tile.Y) || taken.Contains(tile))
                    tile = FreeTileNear(layout, tile, taken) ?? Spot(loc, layout);
                taken.Add(tile);
                homes.Entries.Add(new HomeEntry(npc, place.Key, tile.X, tile.Y));
            }
            catch (Exception error)
            {
                monitor.Log($"Terradew could not place {npc} in a home. {error.Message}", LogLevel.Warn);
            }
        }
        bridge.Send(homes);
    }

    /// <summary>The nearest tile, searching outward in rings from the home, that is open and not already taken.</summary>
    private static Point? FreeTileNear(MapLayout layout, Point home, HashSet<Point> taken)
    {
        for (int radius = 1; radius <= MaxRing; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    // Only the ring's edge; the inner tiles were checked on earlier rings.
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius)
                        continue;
                    Point tile = new(home.X + dx, home.Y + dy);
                    if (!layout.IsBlocked(tile.X, tile.Y) && !taken.Contains(tile))
                        return tile;
                }
            }
        }
        return null;
    }

    private static Point Spot(GameLocation inside, MapLayout layout)
    {
        if (inside.warps.Count > 0)
        {
            Warp door = inside.warps[0];
            for (int up = 2; up < 6; up++)
            {
                if (!layout.IsBlocked(door.X, door.Y - up))
                    return new Point(door.X, door.Y - up);
            }
        }
        for (int y = layout.Height / 2; y < layout.Height; y++)
        {
            for (int x = 1; x < layout.Width - 1; x++)
            {
                if (!layout.IsBlocked(x, y))
                    return new Point(x, y);
            }
        }
        return new Point(layout.Width / 2, layout.Height / 2);
    }
}
