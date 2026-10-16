using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terradew.Core;
using Terradew.Core.Protocol;
using Terraria;
using Terraria.ModLoader;

namespace TerradewBridge;

/// <summary>
/// Draws Terraria's own effects for Stardew: projectiles with the trails and beams Terraria draws in code, dust, gore and
/// particle bursts. Terraria draws them, off screen, over the part of the place Stardew's camera shows, and sends the
/// picture. Stardew draws it in place of its own sprites, so nothing Terraria draws in code is missing.
/// </summary>
/// <remarks>
/// Each picture is drawn twice. The lit picture has every light Terraria reads forced to white, so the sprites show as they
/// are. The self-lit picture has the lights forced to black, so only what Terraria draws without light is left, and that is
/// what glows on its own. Capture lights are kept out of <see cref="WorldLights"/> while <see cref="Capturing"/> is set.
/// </remarks>
internal static class Effects
{
    /// <summary>A picture goes out every this many Terraria ticks while there is anything to draw.</summary>
    private const int SendEvery = 2;

    /// <summary>Room left in one frame for the header, below the largest message the socket takes.</summary>
    private const int MaxPictureBytes = Wire.MaxFrameBytes - 4096;

    /// <summary>Which Lighting reads are overridden. Only while a capture draws.</summary>
    private enum LightMode : byte
    {
        None,
        White,
        Black,
    }

    private static Mod? mod;
    private static ViewRect? view;
    private static LightMode lightMode;
    private static RenderTarget2D? target;
    private static Color[]? litPixels;
    private static Color[]? selfPixels;
    private static byte[]? litRgba;
    private static byte[]? selfRgba;
    private static bool sentSome;
    private static readonly HashSet<string> warned = new();
    private static MethodInfo? drawProjectiles;
    private static MethodInfo? drawDust;
    private static MethodInfo? drawGore;
    private static MethodInfo? drawCachedProjs;
    private static FieldInfo[]? cacheFields;

    /// <summary>True while a capture draws. WorldLights leaves out the lights Terraria adds then, as they are not in the world.</summary>
    public static bool Capturing { get; private set; }

    /// <summary>True once the effects failed for this session. Stardew then draws projectiles and dust the old way.</summary>
    public static bool Failed { get; private set; }

    /// <summary>
    /// True when the effects replace the old drawing this tick: linked, the farmer's place and Stardew's view are known,
    /// and nothing failed. Dust is then left to the effects, so it is not drawn twice.
    /// </summary>
    public static bool Live { get; private set; }

    internal static void Attach(Mod loaded)
    {
        mod = loaded;
        On_Lighting.GetColor_Point += LightPoint;
        On_Lighting.GetColor_Point_Color += LightPointColor;
        On_Lighting.GetColor_int_int += LightTile;
        On_Lighting.GetColor_int_int_Color += LightTileColor;
    }

    internal static void Detach()
    {
        On_Lighting.GetColor_Point -= LightPoint;
        On_Lighting.GetColor_Point_Color -= LightPointColor;
        On_Lighting.GetColor_int_int -= LightTile;
        On_Lighting.GetColor_int_int_Color -= LightTileColor;
        view = null;
    }

    /// <summary>Stardew's camera in the farmer's place, as Stardew last sent it.</summary>
    public static void SetView(ViewRect viewRect) => view = viewRect;

    /// <summary>
    /// Captures and sends this tick's effects. Called every draw-ready tick while linked, with the farmer's place (null
    /// when the farmer is not in a Terraria place). A picture goes out every <see cref="SendEvery"/> ticks.
    /// </summary>
    public static void Publish(Link link, WorldStamp.PlaceState? place, int tick)
    {
        Live = false;
        if (Failed || place == null || view == null || view.PlaceKey != place.Key || Main.instance == null || Main.dedServ)
            return;
        Live = true;
        if (tick % SendEvery != 0)
            return;
        try
        {
            Capture(link, place, view);
        }
        catch (Exception error)
        {
            Fail(link, place.Key, error);
        }
    }

    /// <summary>Turns the effects off for the session and tells Stardew, which then draws the old way.</summary>
    private static void Fail(Link link, string placeKey, Exception error)
    {
        Failed = true;
        Live = false;
        Warn("fail", $"Terradew could not draw Terraria's effects for Stardew, so Stardew draws the projectiles and dust the old way. {error.Message}");
        link.Send(new EffectsFrame { PlaceKey = placeKey, Fallback = true });
    }

    private static void Capture(Link link, WorldStamp.PlaceState place, ViewRect seen)
    {
        // The view's top-left in Terraria pixels, and its size, capped at the largest picture.
        (float left, float top) = WorldProjection.ToWorld(place.OriginX, place.OriginY, seen.X, seen.Y);
        int width = Math.Min(EffectsFrame.MaxWidth, (int)MathF.Ceiling(seen.Width / WorldProjection.Scale));
        int height = Math.Min(EffectsFrame.MaxHeight, (int)MathF.Ceiling(seen.Height / WorldProjection.Scale));
        if (width <= 0 || height <= 0)
            return;

        GraphicsDevice device = Main.instance.GraphicsDevice;
        EnsureBuffers(device, width, height);
        RenderTargetBinding[] previous = device.GetRenderTargets();
        Vector2 savedPosition = Main.screenPosition;
        int savedWidth = Main.screenWidth;
        int savedHeight = Main.screenHeight;
        Vector2 savedZoom = Main.GameViewMatrix.Zoom;
        Capturing = true;
        try
        {
            // Terraria culls and places everything by these, so they make its own draw cover exactly the view.
            Main.screenPosition = new Vector2(left, top);
            Main.screenWidth = width;
            Main.screenHeight = height;
            Main.GameViewMatrix.Zoom = Vector2.One;
            lightMode = LightMode.White;
            DrawPass(device, litPixels!);
            lightMode = LightMode.Black;
            DrawPass(device, selfPixels!);
        }
        finally
        {
            lightMode = LightMode.None;
            Capturing = false;
            Main.screenPosition = savedPosition;
            Main.screenWidth = savedWidth;
            Main.screenHeight = savedHeight;
            Main.GameViewMatrix.Zoom = savedZoom;
            if (previous.Length == 0)
                device.SetRenderTarget(null);
            else
                device.SetRenderTargets(previous);
        }

        // Only the self-lit parts are kept: pixels Terraria drew with no light at all.
        for (int i = 0; i < selfPixels!.Length; i++)
        {
            Color c = selfPixels[i];
            if ((c.R | c.G | c.B) == 0)
                selfPixels[i] = Color.Transparent;
        }
        CopyRgba(litPixels!, litRgba!);
        CopyRgba(selfPixels, selfRgba!);
        SendPictures(link, place.Key, seen.X, seen.Y, width, height);
    }

    /// <summary>Draws every effect into the target, then reads it back. Each step is guarded, so one failure keeps the rest.</summary>
    private static void DrawPass(GraphicsDevice device, Color[] pixels)
    {
        device.SetRenderTarget(target);
        device.Clear(Color.Transparent);
        // Terraria's own calls begin and end their sprite batches.
        Run("projectiles", () => (drawProjectiles ??= Method("DrawProjectiles")).Invoke(Main.instance, null));
        Run("dust", () => (drawDust ??= Method("DrawDust")).Invoke(Main.instance, null));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
        try
        {
            Run("gore", () => (drawGore ??= Method("DrawGore")).Invoke(Main.instance, null));
            Run("particles behind players", () =>
            {
                Main.ParticleSystem_World_BehindPlayers.Settings.AnchorPosition = -Main.screenPosition;
                Main.ParticleSystem_World_BehindPlayers.Draw(Main.spriteBatch);
            });
            Run("particles over players", () =>
            {
                Main.ParticleSystem_World_OverPlayers.Settings.AnchorPosition = -Main.screenPosition;
                Main.ParticleSystem_World_OverPlayers.Draw(Main.spriteBatch);
            });
            // Projectiles Terraria holds back to draw later, such as those behind NPCs. Drawn in this batch.
            foreach (FieldInfo field in CacheFields())
            {
                Run("cached projectiles", () => (drawCachedProjs ??= CachedMethod()).Invoke(Main.instance, new object[] { field.GetValue(null)!, false }));
            }
        }
        finally
        {
            Main.spriteBatch.End();
        }
        target!.GetData(pixels);
    }

    private static void Run(string name, Action step)
    {
        try
        {
            step();
        }
        catch (Exception error)
        {
            Exception cause = error is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : error;
            Warn("step " + name, $"Terradew could not draw Terraria's {name} for Stardew; that part is left out. {cause.Message}");
        }
    }

    private static MethodInfo Method(string name)
        => typeof(Main).GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
           ?? throw new MissingMethodException(typeof(Main).FullName, name);

    private static MethodInfo CachedMethod()
        => typeof(Main).GetMethod("DrawCachedProjs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, new[] { typeof(List<int>), typeof(bool) })
           ?? throw new MissingMethodException(typeof(Main).FullName, "DrawCachedProjs");

    /// <summary>The Terraria lists of projectiles held back for a later draw, found by name. Looked up once.</summary>
    private static IEnumerable<FieldInfo> CacheFields()
    {
        cacheFields ??= typeof(Main)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f.Name.StartsWith("DrawCacheProjs", StringComparison.Ordinal) && f.FieldType == typeof(List<int>))
            .ToArray();
        return cacheFields;
    }

    private static void EnsureBuffers(GraphicsDevice device, int width, int height)
    {
        if (target == null || target.Width != width || target.Height != height)
        {
            target?.Dispose();
            target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None);
        }
        int count = width * height;
        if (litPixels == null || litPixels.Length != count)
        {
            litPixels = new Color[count];
            selfPixels = new Color[count];
            litRgba = new byte[count * 4];
            selfRgba = new byte[count * 4];
        }
    }

    private static void CopyRgba(Color[] pixels, byte[] rgba)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            Color c = pixels[i];
            rgba[i * 4] = c.R;
            rgba[i * 4 + 1] = c.G;
            rgba[i * 4 + 2] = c.B;
            rgba[i * 4 + 3] = c.A;
        }
    }

    private static void SendPictures(Link link, string placeKey, int originX, int originY, int width, int height)
    {
        byte[] lit = PictureRle.Encode(litRgba!);
        byte[] self = PictureRle.Encode(selfRgba!);
        var frame = new EffectsFrame
        {
            PlaceKey = placeKey,
            OriginX = originX,
            OriginY = originY,
            Width = width,
            Height = height,
            Lit = lit,
            Self = self,
        };
        // Nothing to draw goes out once, when the last of it ends; after that, nothing until something appears.
        if (lit.Length == 0 && self.Length == 0)
        {
            if (!sentSome)
                return;
            sentSome = false;
            link.Send(frame);
            return;
        }
        if (lit.Length + self.Length > MaxPictureBytes)
        {
            Warn("size", "Terradew skipped an effects picture too large to send in one frame; the old drawing shows in its place until a smaller one.");
            return;
        }
        sentSome = true;
        link.Send(frame);
    }

    /// <summary>Logs a problem once per key, so a fault that repeats each tick does not flood the log.</summary>
    private static void Warn(string key, string text)
    {
        if (!warned.Add(key))
            return;
        mod?.Logger.Warn(text);
    }

    // Lighting reads. White keeps each sprite as it is; the overloads that take a colour multiply by it, so white keeps
    // that colour. Black makes every read dark. Overridden only while a capture draws.
    private static Color LightPoint(On_Lighting.orig_GetColor_Point orig, Point tileCoords) => lightMode switch
    {
        LightMode.White => Color.White,
        LightMode.Black => Color.Black,
        _ => orig(tileCoords),
    };

    private static Color LightPointColor(On_Lighting.orig_GetColor_Point_Color orig, Point tileCoords, Color originalColor) => lightMode switch
    {
        LightMode.White => originalColor,
        LightMode.Black => Color.Black,
        _ => orig(tileCoords, originalColor),
    };

    private static Color LightTile(On_Lighting.orig_GetColor_int_int orig, int x, int y) => lightMode switch
    {
        LightMode.White => Color.White,
        LightMode.Black => Color.Black,
        _ => orig(x, y),
    };

    private static Color LightTileColor(On_Lighting.orig_GetColor_int_int_Color orig, int x, int y, Color oldColor) => lightMode switch
    {
        LightMode.White => oldColor,
        LightMode.Black => Color.Black,
        _ => orig(x, y, oldColor),
    };
}

/// <summary>Loads <see cref="Effects"/> with the mod, so its lighting reads are hooked for the session.</summary>
public sealed class EffectsSystem : ModSystem
{
    public override void Load() => Effects.Attach(Mod);

    public override void Unload() => Effects.Detach();
}
