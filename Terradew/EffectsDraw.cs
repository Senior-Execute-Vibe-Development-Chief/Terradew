using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using Terradew.Core;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>
/// Draws the Terraria effects picture (TerradewBridge's Effects): what Terraria drew of its projectiles, trails, dust, gore
/// and particles in the farmer's view. The lit picture goes over the world where the shots were; the self-lit picture goes
/// over the finished world, as glowing shots did before. Both are placed at the frame's map point and scaled to Stardew.
/// </summary>
internal sealed class EffectsDraw
{
    /// <summary>The lit picture's layer depth in the sorted world pass: over the creatures and the farmer, as the shots are.</summary>
    public const float LitDepth = 0.99f;

    private readonly IMonitor monitor;
    private EffectsFrame? prepared;
    private Texture2D? lit;
    private Texture2D? self;
    private Color[]? litColours;
    private Color[]? selfColours;
    private bool litShown;
    private bool selfShown;
    private bool loggedFailure;

    public EffectsDraw(IMonitor monitor)
    {
        this.monitor = monitor;
    }

    /// <summary>
    /// Whether Terraria's effects stand in for the old drawing in this place. A new frame is decoded here, once. False when
    /// there is no frame, its place is another, or Terraria failed (its frame then says so).
    /// </summary>
    public bool Adopt(EffectsFrame? frame, string placeKey)
    {
        if (frame == null || frame.Fallback || frame.PlaceKey != placeKey)
            return false;
        if (!ReferenceEquals(frame, prepared))
            Prepare(frame);
        return true;
    }

    /// <summary>Draws the lit picture at the frame's map point. Call only after <see cref="Adopt"/> said yes.</summary>
    public void DrawLit(SpriteBatch b, EffectsFrame frame, float depth)
    {
        if (lit == null || !litShown)
            return;
        Draw(b, lit, frame, depth);
    }

    /// <summary>Draws the self-lit picture over the finished world. Call only after <see cref="Adopt"/> said yes.</summary>
    public void DrawSelf(SpriteBatch b, EffectsFrame frame)
    {
        if (self == null || !selfShown)
            return;
        Draw(b, self, frame, 1f);
    }

    private static void Draw(SpriteBatch b, Texture2D picture, EffectsFrame frame, float depth)
    {
        // Terraria pixels are Stardew's map pixels divided by the tile ratio, so each one is WorldProjection.Scale wide here.
        Vector2 position = Game1.GlobalToLocal(Game1.viewport, new Vector2(frame.OriginX, frame.OriginY));
        b.Draw(picture, position, null, Color.White, 0f, Vector2.Zero, WorldProjection.Scale, SpriteEffects.None, depth);
    }

    private void Prepare(EffectsFrame frame)
    {
        prepared = frame;
        try
        {
            litShown = Upload(ref lit, ref litColours, frame.Lit, frame);
            selfShown = Upload(ref self, ref selfColours, frame.Self, frame);
        }
        catch (Exception error)
        {
            // Logged once: a bad picture would otherwise repeat each frame. The world is drawn without it.
            litShown = false;
            selfShown = false;
            if (loggedFailure)
                return;
            loggedFailure = true;
            monitor.Log($"Terradew could not draw the Terraria effects; the projectiles show the old way. {error.Message}", LogLevel.Warn);
        }
    }

    /// <summary>Decodes one picture into its texture, recreating the texture and buffer only when the size changes.</summary>
    private static bool Upload(ref Texture2D? texture, ref Color[]? colours, byte[] coded, EffectsFrame frame)
    {
        if (coded.Length == 0 || frame.Width <= 0 || frame.Height <= 0)
            return false;
        int width = frame.Width;
        int height = frame.Height;
        if (texture == null || texture.Width != width || texture.Height != height || colours == null)
        {
            texture?.Dispose();
            texture = new Texture2D(Game1.graphics.GraphicsDevice, width, height);
            colours = new Color[width * height];
        }
        byte[] rgba = PictureRle.Decode(coded, width, height);
        for (int i = 0; i < colours.Length; i++)
            colours[i] = new Color(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
        texture.SetData(colours);
        return true;
    }
}

/// <summary>
/// Tells Terraria where Stardew's camera is in the farmer's place, in Stardew map pixels, so Terraria draws its effects over
/// that view. Sent when the view or the place changes, when the farmer arrives in a place, and again every
/// <see cref="ResendTicks"/> ticks, since a restarted Terraria forgets the view.
/// </summary>
internal static class ViewReport
{
    private const int ResendTicks = 60;

    private static int tick;
    private static int sentAt = -ResendTicks;
    private static string? sentPlace;
    private static int sentX;
    private static int sentY;
    private static int sentWidth;
    private static int sentHeight;

    public static void Update(Bridge bridge, string placeKey)
    {
        tick++;
        if (!bridge.IsConnected || !Context.IsWorldReady)
            return;
        int x = Game1.viewport.X;
        int y = Game1.viewport.Y;
        int width = Game1.viewport.Width;
        int height = Game1.viewport.Height;
        bool changed = placeKey != sentPlace || x != sentX || y != sentY || width != sentWidth || height != sentHeight;
        if (!changed && tick - sentAt < ResendTicks)
            return;
        if (!bridge.Send(new ViewRect { PlaceKey = placeKey, X = x, Y = y, Width = width, Height = height }))
            return;
        sentPlace = placeKey;
        sentX = x;
        sentY = y;
        sentWidth = width;
        sentHeight = height;
        sentAt = tick;
    }
}
