using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.BigCraftables;
using StardewValley.Tools;
using Terradew.Core;
using Terradew.Core.Protocol;
using SObject = StardewValley.Object;

namespace Terradew;

/// <summary>
/// Terraria crafting stations placed on the Stardew map. A station is a Stardew big craftable that remembers the
/// Terraria item type in its mod data and is drawn with that item's sprite. Crafting sees the stations within reach.
/// A station is never used like a Stardew object: it cannot be picked up as one, nor blown up.
/// </summary>
internal sealed class Stations
{
    public const string ObjectId = "Terradew.Station";
    public const string TypeKey = "Terradew/StationType";
    /// <summary>Stations within this many tiles of the farmer count for crafting.</summary>
    public const int Reach = 4;

    private static Stations? instance;

    private readonly SpriteBook sprites;
    private readonly Bridge bridge;
    private readonly Dictionary<int, (string Place, Vector2 Tile, bool Torch)> pending = new();
    private int seq;

    public Stations(SpriteBook sprites, Bridge bridge)
    {
        this.sprites = sprites;
        this.bridge = bridge;
        instance = this;
    }

    public void OnAssetRequested(AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo("Data/BigCraftables"))
            return;
        e.Edit(asset =>
        {
            IDictionary<string, BigCraftableData> data = asset.AsDictionary<string, BigCraftableData>().Data;
            data[ObjectId] = new BigCraftableData
            {
                Name = "Terraria Station",
                DisplayName = "Terraria Station",
                Description = "A Terraria crafting station.",
                Price = 0,
                Texture = null,
                SpriteIndex = 0,
            };
        });
    }

    /// <summary>
    /// The Terraria hand used a station or torch item. It is placed on the cursor's tile when that tile is free and in
    /// reach: one tile for a station, two for a torch. Always returns true: the use is handled here and is never sent to
    /// Terraria as a use.
    /// </summary>
    public bool TryPlace(Bridge bridge, int slot, ItemStack held)
    {
        Farmer player = Game1.player;
        GameLocation? location = player.currentLocation;
        Vector2 tile = Game1.currentCursorTile;
        int reach = held.Torch ? 2 : 1;
        bool ok = location != null
            && Utility.tileWithinRadiusOfPlayer((int)tile.X, (int)tile.Y, reach, player)
            && location.CanItemBePlacedHere(tile)
            && !location.objects.ContainsKey(tile);
        if (ok && location != null)
        {
            int number = ++seq;
            bool sent = held.Torch
                ? bridge.Send(new PlaceTorch { Seq = number, Slot = slot, ItemType = held.ItemType })
                : bridge.Send(new PlaceStation { Seq = number, Slot = slot, ItemType = held.ItemType });
            if (sent)
            {
                pending[number] = (location.NameOrUniqueName, tile, held.Torch);
                return true;
            }
        }
        Game1.playSound("cancel");
        return true;
    }

    /// <summary>Terraria answered a placement. A station that cannot be put down is given back to the farmer.</summary>
    public void OnStationPlaced(StationPlaced msg, Bridge bridge)
    {
        if (!pending.Remove(msg.Seq, out var placed) || !msg.Ok)
            return;
        GameLocation? location = Context.IsWorldReady ? Game1.getLocationFromName(placed.Place) : null;
        if (location == null || location.objects.ContainsKey(placed.Tile))
        {
            bridge.Send(new GiveItem { ItemType = msg.ItemType, Stack = 1 });
            return;
        }
        if (placed.Torch)
        {
            Torches.Place(location, placed.Tile, msg.ItemType);
            return;
        }
        SObject station = ItemRegistry.Create<SObject>("(BC)" + ObjectId);
        station.modData[TypeKey] = msg.ItemType.ToString();
        station.TileLocation = placed.Tile;
        location.objects.Add(placed.Tile, station);
        Game1.playSound("woodyStep");
    }

    /// <summary>The Terraria item types of the stations within reach of the farmer, and whether water is within reach.</summary>
    public StationsNearby Near(GameLocation? location, Vector2 farmerTile)
    {
        var near = new StationsNearby();
        if (location == null)
            return near;
        int fx = (int)MathF.Floor(farmerTile.X);
        int fy = (int)MathF.Floor(farmerTile.Y);
        var types = new HashSet<int>();
        foreach (KeyValuePair<Vector2, SObject> pair in location.objects.Pairs)
        {
            if (Math.Abs((int)pair.Key.X - fx) > Reach || Math.Abs((int)pair.Key.Y - fy) > Reach)
                continue;
            if (TypeOf(pair.Value, out int type))
                types.Add(type);
        }
        near.ItemTypes = types.ToList();
        near.Water = AnyWater(location, fx, fy);
        return near;
    }

    private static bool AnyWater(GameLocation location, int cx, int cy)
    {
        for (int x = cx - Reach; x <= cx + Reach; x++)
            for (int y = cy - Reach; y <= cy + Reach; y++)
                if (location.isWaterTile(x, y))
                    return true;
        return false;
    }

    /// <summary>The Terraria item type a station was placed from. False for any other Stardew object.</summary>
    private static bool TypeOf(SObject obj, out int type)
    {
        type = 0;
        return obj.modData.TryGetValue(TypeKey, out string? text) && int.TryParse(text, out type);
    }

    public void Install(Harmony harmony, IMonitor monitor)
    {
        try
        {
            var draw = AccessTools.Method(typeof(SObject), "draw", new[] { typeof(SpriteBatch), typeof(int), typeof(int), typeof(float) });
            if (draw == null)
                monitor.Log("Terradew could not find Object.draw; stations will draw as the placeholder big craftable.", LogLevel.Warn);
            else
                harmony.Patch(draw, prefix: new HarmonyMethod(typeof(Stations), nameof(DrawPrefix)));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not draw stations with their item sprite. {error.Message}", LogLevel.Warn);
        }
        try
        {
            var tool = AccessTools.Method(typeof(SObject), "performToolAction", new[] { typeof(Tool) });
            if (tool == null)
                monitor.Log("Terradew could not find Object.performToolAction; a station could be picked up as a Stardew item.", LogLevel.Warn);
            else
                harmony.Patch(tool, prefix: new HarmonyMethod(typeof(Stations), nameof(ToolPrefix)));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not guard stations against tools. {error.Message}", LogLevel.Warn);
        }
        try
        {
            var explode = AccessTools.Method(typeof(SObject), "onExplosion", new[] { typeof(Farmer) });
            if (explode == null)
                monitor.Log("Terradew could not find Object.onExplosion; bombs could destroy a station.", LogLevel.Warn);
            else
                harmony.Patch(explode, prefix: new HarmonyMethod(typeof(Stations), nameof(ExplosionPrefix)));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not guard stations against bombs. {error.Message}", LogLevel.Warn);
        }
    }

    private static bool DrawPrefix(SObject __instance, SpriteBatch spriteBatch, int x, int y, float alpha)
    {
        if (instance == null || !TypeOf(__instance, out int type))
            return true;
        Texture2D? icon = instance.sprites.Load(SpriteNames.Item(type));
        // A sprite not loaded yet draws nothing, rather than the placeholder, for the one frame or so it takes to load.
        if (icon == null)
            return false;
        // Bottom-centred on its tile. A Terraria tile and a Stardew tile are both 16 pixels, so the station keeps its
        // Terraria size, capped at two tiles across.
        Vector2 foot = new(x * 64 + 32, y * 64 + 64);
        Vector2 position = Game1.GlobalToLocal(Game1.viewport, foot);
        float scale = 4f * Math.Min(1f, 32f / Math.Max(icon.Width, icon.Height));
        float depth = Math.Max(0f, ((y + 1) * 64 - 24) / 10000f) + x * 1E-05f;
        spriteBatch.Draw(icon, position, null, Color.White * alpha, 0f, new Vector2(icon.Width / 2f, icon.Height), scale, SpriteEffects.None, depth);
        return false;
    }

    private static bool ToolPrefix(SObject __instance, Tool t, ref bool __result)
    {
        if (instance == null || !TypeOf(__instance, out int type))
            return true;
        // Never a Stardew pick-up: the station is only ever taken back by a pickaxe or axe, into Terraria's inventory.
        __result = false;
        if ((t is Pickaxe || t is Axe) && instance.bridge.IsConnected)
        {
            GameLocation? location = t.getLastFarmerToUse().currentLocation;
            if (location != null && instance.Take(location, __instance))
            {
                instance.bridge.Send(new GiveItem { ItemType = type, Stack = 1 });
                Game1.playSound("hammer");
            }
        }
        return false;
    }

    /// <summary>Removes this exact station object from the location. False when it is not there.</summary>
    private bool Take(GameLocation location, SObject station)
    {
        Vector2? key = null;
        foreach (KeyValuePair<Vector2, SObject> pair in location.objects.Pairs)
        {
            if (!ReferenceEquals(pair.Value, station))
                continue;
            key = pair.Key;
            break;
        }
        if (key == null)
            return false;
        location.objects.Remove(key.Value);
        return true;
    }

    private static bool ExplosionPrefix(SObject __instance)
        => instance == null || !TypeOf(__instance, out _);
}
