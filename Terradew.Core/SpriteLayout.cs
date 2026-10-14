using Terradew.Core.Protocol;

namespace Terradew.Core;

public readonly record struct SpriteBox(float X, float Y, float Width, float Height)
{
    public bool Overlaps(float x, float y, float width, float height)
        => X < x + width && X + Width > x && Y < y + height && Y + Height > y;

    public bool Contains(float x, float y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
}

/// <summary>
/// Where a Terraria creature sits on the Stardew map. Terraria decides every hit with its own
/// hitboxes, so the drawn body uses the same scale as positions: one Terraria pixel is four Stardew pixels.
/// </summary>
public static class SpriteLayout
{
    public const float DrawScale = WorldProjection.Scale;

    /// <summary>The hitbox in Stardew pixels, standing on its feet point.</summary>
    public static SpriteBox Hitbox(float feetX, float feetY, EntityState entity)
    {
        float width = System.Math.Max(4, (int)entity.Width) * DrawScale;
        float height = System.Math.Max(4, (int)entity.Height) * DrawScale;
        return new SpriteBox(feetX - width / 2f, feetY - height, width, height);
    }
}
