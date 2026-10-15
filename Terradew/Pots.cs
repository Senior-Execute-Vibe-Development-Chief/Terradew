using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using Terradew.Core;
using Terradew.Core.Protocol;
using Terradew.Core.World;
using SObject = StardewValley.Object;

namespace Terradew;

/// <summary>
/// Stardew's breakable mine barrels and crates are drawn as Terraria pots, in the look of their floor's biome, and
/// when the farmer breaks one it also tells Terraria, which gives Terraria pot loot. Stardew's own loot stays.
/// </summary>
internal sealed class PotLook
{
    private const BindingFlags ShakeFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    // A short sideways shake while a container is struck, if Stardew keeps that timer as an int.
    private static readonly FieldInfo? ShakeTimer =
        typeof(BreakableContainer).GetField("shakeTimer", ShakeFlags) ?? typeof(SObject).GetField("shakeTimer", ShakeFlags);

    private static PotLook? instance;

    private readonly SpriteBook sprites;
    private readonly Bridge bridge;
    private readonly Func<bool> leading;
    private IMonitor? monitor;

    public PotLook(SpriteBook sprites, Bridge bridge, Func<bool> leading)
    {
        this.sprites = sprites;
        this.bridge = bridge;
        this.leading = leading;
        instance = this;
    }

    public void Install(Harmony harmony, IMonitor monitor)
    {
        this.monitor = monitor;
        try
        {
            // Resolves to BreakableContainer's own draw when it has one, otherwise to the inherited Object.draw.
            // The prefix checks the type, so it only ever acts on containers.
            var draw = AccessTools.Method(
                typeof(BreakableContainer),
                "draw",
                new[] { typeof(SpriteBatch), typeof(int), typeof(int), typeof(float) });
            if (draw == null)
                monitor.Log("Terradew could not find BreakableContainer.draw; mine barrels will draw as Stardew barrels.", LogLevel.Warn);
            else
                harmony.Patch(draw, prefix: new HarmonyMethod(typeof(PotLook), nameof(DrawPrefix)));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not draw mine barrels as pots. {error.Message}", LogLevel.Warn);
        }
        try
        {
            var release = AccessTools.Method(typeof(BreakableContainer), "releaseContents", new[] { typeof(Farmer) });
            if (release == null)
                monitor.Log("Terradew could not find BreakableContainer.releaseContents; broken barrels will give no Terraria pot loot.", LogLevel.Warn);
            else
                harmony.Patch(release, postfix: new HarmonyMethod(typeof(PotLook), nameof(AfterRelease)));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not hook broken barrels for pot loot. {error.Message}", LogLevel.Warn);
        }
    }

    private static bool DrawPrefix(SObject __instance, SpriteBatch spriteBatch, int x, int y, float alpha)
    {
        if (instance == null || __instance is not BreakableContainer)
            return true;
        return instance.DrawPot(__instance, spriteBatch, x, y, alpha);
    }

    /// <summary>Draws the container as a Terraria pot. False when it was drawn; true to let Stardew draw the barrel.</summary>
    private bool DrawPot(SObject container, SpriteBatch spriteBatch, int x, int y, float alpha)
    {
        // A sprite not loaded yet leaves the Stardew barrel in place, for the one frame or so it takes to load.
        Texture2D? sprite = sprites.Load(SpriteNames.Pot(StyleAt(x, y)));
        if (sprite == null)
            return true;
        // Bottom-centred on its tile, at one Stardew tile across: the breakable is hit on that tile alone, so the
        // picture is no wider than it. A 32 Terraria pixel pot is drawn at the body scale, 2 Stardew pixels each.
        Vector2 foot = new(x * 64 + 32, y * 64 + 64);
        Vector2 position = Game1.GlobalToLocal(Game1.viewport, foot) + new Vector2(Shake(container), 0f);
        float scale = SpriteLayout.DrawScale;
        float depth = Math.Max(0f, ((y + 1) * 64 - 24) / 10000f) + x * 1E-05f;
        spriteBatch.Draw(sprite, position, null, Color.White * alpha, 0f, new Vector2(sprite.Width / 2f, sprite.Height), scale, SpriteEffects.None, depth);
        return false;
    }

    /// <summary>The Terraria pot style for a container on this tile, in the biome of the floor it stands on.</summary>
    private int StyleAt(int x, int y)
    {
        GameLocation? location = Game1.currentLocation;
        Biome biome = location == null
            ? Biome.Forest
            : Places.Current(Maps.PlaceOf(location), bridge.Facts?.Hardmode ?? false);
        return Pots.StyleFor(biome, (x * 73856093) ^ (y * 19349663));
    }

    private static float Shake(SObject container)
    {
        if (ShakeTimer?.GetValue(container) is int timer && timer > 0)
            return timer % 4 < 2 ? -2f : 2f;
        return 0f;
    }

    /// <summary>The farmer broke a container: Terraria gives its pot loot. Stardew's own loot is untouched.</summary>
    private static void AfterRelease(Farmer who)
    {
        try
        {
            if (instance == null || who != Game1.player || !instance.bridge.IsConnected || !instance.leading())
                return;
            GameLocation? location = who.currentLocation;
            if (location == null)
                return;
            int mineLevel = location is MineShaft shaft && shaft.mineLevel < Places.QuarryMineLevel ? shaft.mineLevel : 0;
            instance.bridge.Send(new Gathered
            {
                Kind = GatherKind.Pot,
                PlaceKey = Maps.PlaceOf(location).Key,
                MineLevel = mineLevel,
                Count = 1,
            });
        }
        catch (Exception error)
        {
            instance?.monitor?.Log($"Terradew could not give pot loot for a broken barrel. {error.Message}", LogLevel.Warn);
        }
    }
}
