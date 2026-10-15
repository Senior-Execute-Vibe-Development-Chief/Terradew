using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using Terradew.Core;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>
/// Draws the Terraria dust in the farmer's place from Terraria's own dust sheet, each mote at its own spot in the
/// world. It is drawn in the world pass, so the night and the caves darken it as they darken everything else.
/// </summary>
internal static class DustDraw
{
    public static void Draw(SpriteBatch b, SpriteBook sprites, DustFrame frame, bool sorted)
    {
        Texture2D? sheet = sprites.Load(SpriteNames.Dust);
        if (sheet == null)
            return;
        foreach (DustMote mote in frame.Dusts)
        {
            // A cell outside the sheet cannot be drawn; Terraria never sends one, but a bad frame should not stop the world.
            if (mote.FrameWidth <= 0 || mote.FrameHeight <= 0 || mote.FrameX < 0 || mote.FrameY < 0
                || mote.FrameX + mote.FrameWidth > sheet.Width || mote.FrameY + mote.FrameHeight > sheet.Height)
                continue;
            if (!Utility.isOnScreen(new Vector2(mote.X, mote.Y), 64 * 2))
                continue;
            var source = new Rectangle(mote.FrameX, mote.FrameY, mote.FrameWidth, mote.FrameHeight);
            Vector2 position = Game1.GlobalToLocal(Game1.viewport, new Vector2(mote.X, mote.Y));
            // Terraria draws a dust about its centre, and the sprites are drawn at the body's half size (see SpriteLayout).
            float scale = (mote.Scale <= 0f ? 1f : mote.Scale) * SpriteLayout.DrawScale;
            float depth = sorted ? Math.Clamp((mote.Y + 32f) / 10000f, 0f, 1f) : 0f;
            b.Draw(sheet, position, source, new Color(mote.R, mote.G, mote.B, mote.A), mote.Rotation,
                new Vector2(source.Width / 2f, source.Height / 2f), scale, SpriteEffects.None, depth);
        }
    }
}
