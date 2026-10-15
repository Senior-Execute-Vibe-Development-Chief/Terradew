using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.BigCraftables;
using StardewValley.Locations;
using StardewValley.Tools;
using Terradew.Core;
using Terradew.Core.Protocol;
using SObject = StardewValley.Object;

namespace Terradew;

/// <summary>
/// Terraria chests and dart-trap plates on the Stardew mine floors. Terraria says where they lie on a floor the farmer
/// just reached, and the chests are drawn with their Terraria item's sprite. Right-clicking a chest takes it away and
/// Terraria rolls its loot. A chest is never picked up as a Stardew object, nor blown up. A plate is only drawn; Terraria
/// fires the dart.
/// </summary>
internal sealed class Caves
{
    public const string ObjectId = "Terradew.CaveChest";
    /// <summary>The CaveChestKind number of the chest.</summary>
    public const string KindKey = "Terradew/CaveChestKind";
    /// <summary>The Terraria item type whose sprite draws the chest.</summary>
    public const string ItemKey = "Terradew/CaveChestItem";

    private static Caves? instance;

    private readonly SpriteBook sprites;
    private readonly Bridge bridge;
    /// <summary>The dart-trap plates Terraria put on the floor named by trapPlace and trapFloor.</summary>
    private readonly List<CaveTrapPlate> plates = new();
    private string trapPlace = "";
    private int trapFloor = -1;

    public Caves(SpriteBook sprites, Bridge bridge)
    {
        this.sprites = sprites;
        this.bridge = bridge;
        instance = this;
        bridge.CaveTrapsArrived += OnTraps;
    }

    /// <summary>
    /// Terraria put dart-trap plates on the mine floor the farmer just reached. They are kept as sent and drawn only while the
    /// farmer is on that floor, so a late message for a floor the farmer has not reached yet still shows when they arrive.
    /// </summary>
    public void OnTraps(CaveTraps message)
    {
        plates.Clear();
        plates.AddRange(message.Plates);
        trapPlace = message.PlaceKey;
        trapFloor = message.Floor;
    }

    /// <summary>Draws the dart-trap plates on the mine floor the farmer is on, in the world pass, so anything standing on them is in front.</summary>
    private void DrawTraps(SpriteBatch b, MineShaft shaft)
    {
        // The plates belong to one place: once the farmer is in another, they are forgotten.
        if (Maps.PlaceOf(shaft).Key != trapPlace)
        {
            plates.Clear();
            return;
        }
        if (shaft.mineLevel != trapFloor)
            return;
        foreach (CaveTrapPlate plate in plates)
        {
            // A plate lies on the floor of its tile, a little below the tile's top, so it draws behind anything standing on it.
            Vector2 foot = new(plate.X * 64 + 12, plate.Y * 64 + 48);
            if (!Utility.isOnScreen(foot, 64))
                continue;
            Vector2 position = Game1.GlobalToLocal(Game1.viewport, foot);
            float depth = (plate.Y * 64 + 4) / 10000f;
            b.Draw(Game1.staminaRect, position, null, new Color(70, 70, 78), 0f, Vector2.Zero, new Vector2(40f, 12f), SpriteEffects.None, depth);
            b.Draw(Game1.staminaRect, position, null, new Color(120, 120, 130), 0f, Vector2.Zero, new Vector2(40f, 3f), SpriteEffects.None, depth + 0.00001f);
        }
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
                Name = "Terraria Chest",
                DisplayName = "Terraria Chest",
                Description = "A chest from Terraria.",
                Price = 0,
                Texture = null,
                SpriteIndex = 0,
            };
        });
    }

    /// <summary>Terraria put chests on the floor the farmer is on. Placed on open tiles that are still free; the rest are dropped.</summary>
    public void OnSpawned(CaveChests message)
    {
        if (!Context.IsWorldReady || Game1.currentLocation is not MineShaft shaft || shaft.mineLevel != message.Floor)
            return;
        if (Maps.PlaceOf(shaft).Key != message.PlaceKey)
            return;
        foreach (CaveChest chest in message.Chests)
        {
            Vector2 tile = new(chest.X, chest.Y);
            if (!shaft.CanItemBePlacedHere(tile) || shaft.objects.ContainsKey(tile) || shaft.doesTileHaveProperty(chest.X, chest.Y, "Action", "Buildings") != null)
                continue;
            SObject placed = ItemRegistry.Create<SObject>("(BC)" + ObjectId);
            placed.modData[KindKey] = chest.Kind.ToString();
            placed.modData[ItemKey] = chest.ItemType.ToString();
            placed.TileLocation = tile;
            shaft.objects.Add(tile, placed);
        }
    }

    /// <summary>The chest kind and Terraria item of a placed chest. False for any other Stardew object.</summary>
    private static bool TypeOf(SObject obj, out int kind, out int itemType)
    {
        kind = 0;
        itemType = 0;
        return obj.modData.TryGetValue(KindKey, out string? kindText) && int.TryParse(kindText, out kind)
            && obj.modData.TryGetValue(ItemKey, out string? itemText) && int.TryParse(itemText, out itemType);
    }

    public void Install(Harmony harmony, IMonitor monitor)
    {
        try
        {
            var draw = AccessTools.Method(typeof(SObject), "draw", new[] { typeof(SpriteBatch), typeof(int), typeof(int), typeof(float) });
            if (draw == null)
                monitor.Log("Terradew could not find Object.draw; Terraria chests will draw as the placeholder big craftable.", LogLevel.Warn);
            else
                harmony.Patch(draw, prefix: new HarmonyMethod(typeof(Caves), nameof(DrawPrefix)));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not draw Terraria chests. {error.Message}", LogLevel.Warn);
        }
        try
        {
            var action = AccessTools.Method(typeof(SObject), "checkForAction", new[] { typeof(Farmer), typeof(bool) });
            if (action == null)
                monitor.Log("Terradew could not find Object.checkForAction; Terraria chests could not be opened.", LogLevel.Warn);
            else
                harmony.Patch(action, prefix: new HarmonyMethod(typeof(Caves), nameof(ActionPrefix)));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not open Terraria chests. {error.Message}", LogLevel.Warn);
        }
        try
        {
            var tool = AccessTools.Method(typeof(SObject), "performToolAction", new[] { typeof(Tool) });
            if (tool == null)
                monitor.Log("Terradew could not find Object.performToolAction; a Terraria chest could be picked up as a Stardew item.", LogLevel.Warn);
            else
                harmony.Patch(tool, prefix: new HarmonyMethod(typeof(Caves), nameof(ToolPrefix)));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not guard Terraria chests against tools. {error.Message}", LogLevel.Warn);
        }
        try
        {
            var mineDraw = AccessTools.Method(typeof(MineShaft), "draw", new[] { typeof(SpriteBatch) });
            if (mineDraw == null)
                monitor.Log("Terradew could not find MineShaft.draw; Terraria dart-trap plates will not be drawn.", LogLevel.Warn);
            else
                harmony.Patch(mineDraw, postfix: new HarmonyMethod(typeof(Caves), nameof(DrawTrapsPostfix)));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not draw Terraria dart-trap plates. {error.Message}", LogLevel.Warn);
        }
        try
        {
            var explode = AccessTools.Method(typeof(SObject), "onExplosion", new[] { typeof(Farmer) });
            if (explode == null)
                monitor.Log("Terradew could not find Object.onExplosion; bombs could destroy a Terraria chest.", LogLevel.Warn);
            else
                harmony.Patch(explode, prefix: new HarmonyMethod(typeof(Caves), nameof(ExplosionPrefix)));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not guard Terraria chests against bombs. {error.Message}", LogLevel.Warn);
        }
    }

    private static bool DrawPrefix(SObject __instance, SpriteBatch spriteBatch, int x, int y, float alpha)
    {
        if (instance == null || !TypeOf(__instance, out _, out int itemType))
            return true;
        Texture2D? icon = instance.sprites.Load(SpriteNames.Item(itemType));
        // Nothing until the sprite loads, rather than the placeholder for the moment it takes.
        if (icon == null)
            return false;
        // Bottom-centred on its tile, at Terraria's size, as the stations are drawn.
        Vector2 foot = new(x * 64 + 32, y * 64 + 64);
        Vector2 position = Game1.GlobalToLocal(Game1.viewport, foot);
        float scale = 4f * Math.Min(1f, 32f / Math.Max(icon.Width, icon.Height));
        float depth = Math.Max(0f, ((y + 1) * 64 - 24) / 10000f) + x * 1E-05f;
        spriteBatch.Draw(icon, position, null, Color.White * alpha, 0f, new Vector2(icon.Width / 2f, icon.Height), scale, SpriteEffects.None, depth);
        return false;
    }

    /// <summary>
    /// Right-clicking a chest takes it off the floor at once and sends the open to Terraria, which gives the loot. While
    /// Terraria is not connected the chest stays shut.
    /// </summary>
    private static bool ActionPrefix(SObject __instance, Farmer who, bool justCheckingForActivity, ref bool __result)
    {
        if (instance == null || !TypeOf(__instance, out int kind, out _))
            return true;
        __result = true;
        if (justCheckingForActivity)
            return false;
        GameLocation? location = who.currentLocation;
        if (!instance.bridge.IsConnected || location == null || !instance.Take(location, __instance))
        {
            Game1.playSound("cancel");
            return false;
        }
        instance.bridge.Send(new OpenCaveChest { Kind = (byte)kind });
        Game1.playSound("openChest");
        return false;
    }

    /// <summary>Removes this exact chest from the location. False when it is not there.</summary>
    private bool Take(GameLocation location, SObject chest)
    {
        Vector2? key = null;
        foreach (KeyValuePair<Vector2, SObject> pair in location.objects.Pairs)
        {
            if (!ReferenceEquals(pair.Value, chest))
                continue;
            key = pair.Key;
            break;
        }
        if (key == null)
            return false;
        location.objects.Remove(key.Value);
        return true;
    }

    /// <summary>A tool never picks a Terraria chest up as a Stardew item.</summary>
    private static bool ToolPrefix(SObject __instance, Tool t, ref bool __result)
    {
        if (instance == null || !TypeOf(__instance, out _, out _))
            return true;
        __result = false;
        return false;
    }

    /// <summary>After a mine floor is drawn, its dart-trap plates go over it.</summary>
    private static void DrawTrapsPostfix(GameLocation __instance, SpriteBatch b)
    {
        if (instance == null || instance.plates.Count == 0 || __instance is not MineShaft shaft)
            return;
        instance.DrawTraps(b, shaft);
    }

    private static bool ExplosionPrefix(SObject __instance)
        => instance == null || !TypeOf(__instance, out _, out _);
}
