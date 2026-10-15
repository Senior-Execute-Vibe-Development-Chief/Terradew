using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terradew.Core.Protocol;
using Terraria;

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

    private static RenderTarget2D? target;
    private static Color[]? pixels;
    private static ulong lastHash;
    private static int lastSent = int.MinValue / 2;
    private static bool failed;

    /// <summary>True while the player is being drawn for Stardew; Puppet.DrawEffects then lights it fully.</summary>
    public static bool Capturing { get; private set; }

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
        GraphicsDevice device = Main.instance.GraphicsDevice;
        target ??= new RenderTarget2D(device, Size, Size, false, SurfaceFormat.Color, DepthFormat.None);
        pixels ??= new Color[Size * Size];
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
            Main.PlayerRenderer.DrawPlayer(Main.Camera, player, player.position, player.fullRotation, player.fullRotationOrigin, 0f, 1f);
        }
        finally
        {
            Capturing = false;
            if (begun)
                Main.spriteBatch.End();
            if (previous.Length == 0)
                device.SetRenderTarget(null);
            else
                device.SetRenderTargets(previous);
        }
        target.GetData(pixels);
        return Crop(pixels);
    }

    /// <summary>Cuts the canvas down to what was drawn.</summary>
    private static PlayerLook Crop(Color[] canvas)
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
        return new PlayerLook
        {
            Width = (short)width,
            Height = (short)height,
            FeetX = (short)(FeetX - left),
            FeetY = (short)(FeetY - top),
            Rgba = rgba,
        };
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
        return hash;
    }
}
