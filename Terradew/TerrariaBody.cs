using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using Terradew.Core;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>
/// Shows the Terraria character in place of the farmer. Terraria draws its player each tick and sends the picture
/// (see TerradewBridge.Body); here the farmer is hidden and the picture drawn on the farmer's feet, at the same scale
/// as the Terraria creatures. Stardew-only actions keep the farmer, since Terraria has no animation for them, and so
/// does any moment the pictures stop arriving.
/// </summary>
internal sealed class TerrariaBody
{
    private static TerrariaBody? instance;

    private readonly Bridge bridge;
    private readonly IMonitor monitor;
    private Texture2D? texture;
    private PlayerLook? shown;
    private bool showing;

    public TerrariaBody(Bridge bridge, IMonitor monitor)
    {
        this.bridge = bridge;
        this.monitor = monitor;
        instance = this;
    }

    /// <summary>Whether the Terraria character stands in for the farmer this frame. Armor and held items are then part of it.</summary>
    public static bool Showing => instance?.showing ?? false;

    public void Install(Harmony harmony)
    {
        try
        {
            var draw = AccessTools.Method(typeof(Farmer), nameof(Farmer.draw), new[] { typeof(SpriteBatch) });
            if (draw == null)
            {
                monitor.Log("Terradew could not find Farmer.draw; the farmer shows instead of your Terraria character.", LogLevel.Warn);
                return;
            }
            harmony.Patch(draw, prefix: new HarmonyMethod(typeof(TerrariaBody), nameof(BeforeFarmerDraw)));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not hide the farmer; the farmer shows instead of your Terraria character. {error.Message}", LogLevel.Warn);
        }
    }

    /// <summary>Call once per tick, before drawing.</summary>
    public void Update(ModConfig config)
    {
        PlayerLook? look = bridge.Look;
        Farmer player = Game1.player;
        showing = config.TerrariaBody && look != null && look.Width > 0 && look.Height > 0 && Context.IsWorldReady && FarmerLike(player) == false;
        if (!showing || look == null || ReferenceEquals(look, shown))
            return;
        try
        {
            if (texture == null || texture.Width != look.Width || texture.Height != look.Height)
            {
                texture?.Dispose();
                texture = new Texture2D(Game1.graphics.GraphicsDevice, look.Width, look.Height);
            }
            var colors = new Color[look.Width * look.Height];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = new Color(look.Rgba[i * 4], look.Rgba[i * 4 + 1], look.Rgba[i * 4 + 2], look.Rgba[i * 4 + 3]);
            texture.SetData(colors);
            shown = look;
        }
        catch (Exception error)
        {
            showing = false;
            monitor.Log($"Terradew could not show your Terraria character. {error.Message}", LogLevel.Trace);
        }
    }

    /// <summary>
    /// True while the farmer does something only Stardew can draw: a tool or rod, eating, sitting, riding, swimming,
    /// a one-off animation such as harvesting, or a cutscene.
    /// </summary>
    private static bool FarmerLike(Farmer player)
    {
        if (player.currentLocation == null || Game1.eventUp)
            return true;
        if (player.UsingTool || player.isRidingHorse() || player.IsSitting() || player.isEating)
            return true;
        if (player.swimming.Value || player.bathingClothes.Value)
            return true;
        return player.FarmerSprite != null && player.FarmerSprite.PauseForSingleAnimation;
    }

    private static bool BeforeFarmerDraw(Farmer __instance)
        => !(__instance.IsLocalPlayer && Showing);

    /// <summary>Draws the character on the farmer's feet. Called from the world drawing, in its sort order.</summary>
    public static void DrawOver(SpriteBatch b, float depth)
    {
        TerrariaBody? body = instance;
        if (body == null || !body.showing || body.texture == null || body.shown == null)
            return;
        Farmer player = Game1.player;
        Rectangle box = player.GetBoundingBox();
        Vector2 feet = Game1.GlobalToLocal(Game1.viewport, new Vector2(box.Center.X, box.Bottom));
        feet = new Vector2(MathF.Round(feet.X), MathF.Round(feet.Y));
        b.Draw(Game1.shadowTexture, feet, Game1.shadowTexture.Bounds, Color.White * 0.5f, 0f,
            new Vector2(Game1.shadowTexture.Width / 2f, Game1.shadowTexture.Height / 2f), 3f, SpriteEffects.None, Math.Max(0f, depth - 0.0001f));
        b.Draw(body.texture, feet, null, Color.White, 0f, new Vector2(body.shown.FeetX, body.shown.FeetY), SpriteLayout.DrawScale, SpriteEffects.None, depth);
    }
}
