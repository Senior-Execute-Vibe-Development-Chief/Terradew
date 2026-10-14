using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.Buildings;
using Terradew.Core.Protocol;
using Terradew.Core.World;

namespace Terradew;

/// <summary>
/// Robin builds Terraria Houses on the farm. Each one is a home for one Terraria town NPC; the next morning
/// an NPC whose Terraria conditions are met moves in.
/// </summary>
internal sealed class Houses
{
    public const string BuildingId = "Terradew.TerrariaHouse";

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
                Description = "A home for one Terraria townsperson. Someone moves in the morning after it is built, once their conditions are met.",
                Texture = shed.Texture,
                SourceRect = shed.SourceRect,
                Size = shed.Size,
                CollisionMap = shed.CollisionMap,
                HumanDoor = shed.HumanDoor,
                IndoorMap = shed.IndoorMap,
                IndoorMapType = shed.IndoorMapType,
                Builder = "Robin",
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

    public static IEnumerable<GameLocation> Interiors()
    {
        Farm farm = Game1.getFarm();
        foreach (Building building in farm.buildings)
        {
            if (building.buildingType.Value != BuildingId || building.isUnderConstruction())
                continue;
            GameLocation? inside = building.GetIndoors();
            if (inside != null)
                yield return inside;
        }
    }

    /// <summary>Sends every house, plus each interior's layout, so Terraria can place residents even before you visit.</summary>
    public void Publish(Bridge bridge)
    {
        var homes = new Homes();
        foreach (GameLocation inside in Interiors())
        {
            Place place = Maps.PlaceOf(inside);
            MapLayout? layout = Maps.Build(inside, place);
            if (layout == null)
                continue;
            bridge.Send(layout);
            Point spot = Spot(inside, layout);
            homes.Entries.Add(new HomeEntry(place.Key, spot.X, spot.Y));
        }
        bridge.Send(homes);
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
