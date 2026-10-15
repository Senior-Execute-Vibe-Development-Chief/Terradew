using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terradew.Core.Protocol;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerradewBridge;

/// <summary>
/// Draws the Terraria player with Terraria's own player renderer, off screen and fully lit, and sends the picture to
/// Stardew to draw in place of the farmer. Everything Terraria draws on a player comes along: hair, clothes, armor,
/// vanity, dyes and their shaders, wings, and the item in use. It runs from the bridge tick rather than from drawing,
/// so it keeps going while the Terraria window is in the background.
/// </summary>
internal static class Body
{
    /// <summary>The off-screen canvas, big enough for wings and long swords.</summary>
    private const int Size = 192;

    /// <summary>Where the middle of the player's feet goes on the canvas.</summary>
    private const int FeetX = Size / 2;
    private const int FeetY = 136;

    /// <summary>An unchanged picture is still sent this often, so Stardew knows the bridge is alive.</summary>
    private const int ResendTicks = 30;

    /// <summary>How many accessory and armor slots are checked for glowing wings, as in <see cref="Glow"/>.</summary>
    private const int GlowSlots = 10;

    private static RenderTarget2D? target;
    private static Color[]? pixels;
    private static Color[]? glowPixels;
    private static ulong lastHash;
    private static int lastSent = int.MinValue / 2;
    private static bool failed;

    /// <summary>True while the player is being drawn for Stardew; Puppet.DrawEffects then lights it fully.</summary>
    public static bool Capturing { get; private set; }

    /// <summary>True while only the wings are drawn, for the glowing copy (see <see cref="WingGlowPlayer"/>).</summary>
    public static bool WingsOnly { get; private set; }

    public static void Publish(Link link, Player player, int tick)
    {
        if (failed || Main.dedServ || Main.instance == null || player == null || !player.active)
            return;
        try
        {
            PlayerLook look = Capture(player);
            ulong hash = Hash(look);
            if (hash == lastHash && tick - lastSent < ResendTicks)
                return;
            lastHash = hash;
            lastSent = tick;
            link.Send(look);
        }
        catch (Exception error)
        {
            // Once broken, it stays off for the session: Stardew shows the farmer when no pictures arrive.
            failed = true;
            BridgeSystem.Instance.Mod.Logger.Warn($"Terradew could not draw the Terraria player for Stardew, so Stardew shows the farmer instead: {error}");
        }
    }

    private static PlayerLook Capture(Player player)
    {
        Draw(player, wingsOnly: false);
        target!.GetData(pixels!);
        // The glowing wings are a second picture of the same canvas, so they are cut to the same rectangle.
        bool glowing = GlowingWings(player);
        if (glowing)
        {
            Draw(player, wingsOnly: true);
            target!.GetData(glowPixels!);
        }
        if (glowing)
            KeepShown(pixels!, glowPixels!);
        return Crop(pixels!, glowing ? glowPixels : null);
    }

    /// <summary>Draws the player onto the canvas, fully lit. With <paramref name="wingsOnly"/>, nothing but the wings.</summary>
    private static void Draw(Player player, bool wingsOnly)
    {
        GraphicsDevice device = Main.instance.GraphicsDevice;
        target ??= new RenderTarget2D(device, Size, Size, false, SurfaceFormat.Color, DepthFormat.None);
        pixels ??= new Color[Size * Size];
        glowPixels ??= new Color[Size * Size];
        RenderTargetBinding[] previous = device.GetRenderTargets();
        bool begun = false;
        try
        {
            device.SetRenderTarget(target);
            device.Clear(Color.Transparent);
            // The renderer draws at the player's position less the screen position, as the layers round it.
            float feetX = (int)(player.position.X - Main.screenPosition.X) + player.width / 2f;
            float feetY = (int)(player.position.Y - Main.screenPosition.Y) + player.height;
            Matrix shift = Matrix.CreateTranslation(MathF.Round(FeetX - feetX), MathF.Round(FeetY - feetY), 0f);
            // Immediate, so the dye shaders the renderer applies take effect on each piece.
            Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, shift);
            begun = true;
            Capturing = true;
            WingsOnly = wingsOnly;
            Main.PlayerRenderer.DrawPlayer(Main.Camera, player, player.position, player.fullRotation, player.fullRotationOrigin, 0f, 1f);
            if (!wingsOnly)
                DrawDust(player);
        }
        finally
        {
            Capturing = false;
            WingsOnly = false;
            if (begun)
                Main.spriteBatch.End();
            if (previous.Length == 0)
                device.SetRenderTarget(null);
            else
                device.SetRenderTargets(previous);
        }
    }

    /// <summary>
    /// The wings sit behind the body, but the glowing copy is drawn over everything; only the wing pixels that show in
    /// the whole picture are kept, so the glow never covers the player.
    /// </summary>
    private static void KeepShown(Color[] whole, Color[] wings)
    {
        for (int i = 0; i < wings.Length; i++)
        {
            if (wings[i] != whole[i])
                wings[i] = Color.Transparent;
        }
    }

    /// <summary>
    /// Whether the equipped wings are Fairy Wings or Tattered Fairy Wings. Terraria draws these bright rather than lit,
    /// so Stardew draws their picture again over the finished world. The vanity slots are not checked, as in Glow.
    /// </summary>
    private static bool GlowingWings(Player player)
    {
        for (int i = 0; i < GlowSlots; i++)
        {
            Item item = player.armor[i];
            if ((item.type == ItemID.FairyWings || item.type == ItemID.TatteredFairyWings) && item.wingSlot > 0 && item.wingSlot == player.wings)
                return true;
        }
        return false;
    }

    /// <summary>
    /// The dust around the player, such as the sparks of a fiery swing, fully lit. Stardew draws no Terraria dust of its
    /// own, so this is the only way it shows.
    /// </summary>
    private static void DrawDust(Player player)
    {
        // The renderer may leave a dye shader applied.
        Main.pixelShader.CurrentTechnique.Passes[0].Apply();
        Vector2 middle = player.Center;
        float reach = Size / 2f;
        for (int i = 0; i < Main.maxDust; i++)
        {
            Dust dust = Main.dust[i];
            if (dust == null || !dust.active || dust.scale <= 0f || Vector2.DistanceSquared(dust.position, middle) > reach * reach)
                continue;
            Texture2D texture = (dust.type >= DustID.Count ? ModContent.GetModDust(dust.type)?.Texture2D?.Value : null) ?? TextureAssets.Dust.Value;
            Main.spriteBatch.Draw(texture, dust.position - Main.screenPosition, dust.frame, dust.GetAlpha(Color.White), dust.rotation, new Vector2(4f, 4f), dust.scale, SpriteEffects.None, 0f);
        }
    }

    /// <summary>Cuts the canvas down to what was drawn. The glowing parts, if any, are cut to the same rectangle.</summary>
    private static PlayerLook Crop(Color[] canvas, Color[]? glow)
    {
        int left = Size, right = -1, top = Size, bottom = -1;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (canvas[y * Size + x].A == 0)
                    continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }
        if (right < left)
            return new PlayerLook();
        int width = right - left + 1;
        int height = bottom - top + 1;
        return new PlayerLook
        {
            Width = (short)width,
            Height = (short)height,
            FeetX = (short)(FeetX - left),
            FeetY = (short)(FeetY - top),
            Rgba = CutRgba(canvas, left, top, width, height),
            GlowRgba = glow == null ? Array.Empty<byte>() : CutRgba(glow, left, top, width, height),
        };
    }

    private static byte[] CutRgba(Color[] canvas, int left, int top, int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color c = canvas[(top + y) * Size + left + x];
                int i = (y * width + x) * 4;
                rgba[i] = c.R;
                rgba[i + 1] = c.G;
                rgba[i + 2] = c.B;
                rgba[i + 3] = c.A;
            }
        }
        return rgba;
    }

    /// <summary>FNV-1a over the picture and its placement, to skip sending a picture that has not changed.</summary>
    private static ulong Hash(PlayerLook look)
    {
        ulong hash = 14695981039346656037UL;
        void Mix(int value)
        {
            hash ^= (uint)value;
            hash *= 1099511628211UL;
        }
        Mix(look.Width);
        Mix(look.Height);
        Mix(look.FeetX);
        Mix(look.FeetY);
        foreach (byte b in look.Rgba)
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        Mix(look.GlowRgba.Length);
        foreach (byte b in look.GlowRgba)
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return hash;
    }
}

/// <summary>
/// While Body draws the glowing copy, hides every player draw layer but the wings, so that the picture holds only the
/// wings. Terraria's own layers are the only ones hidden; nothing else about the player changes.
/// </summary>
internal sealed class WingGlowPlayer : ModPlayer
{
    public override void HideDrawLayers(PlayerDrawSet drawInfo)
    {
        if (!Body.WingsOnly)
            return;
        foreach (PlayerDrawLayer layer in PlayerDrawLayerLoader.Layers)
        {
            if (layer != PlayerDrawLayers.Wings)
                layer.Hide();
        }
    }
}
