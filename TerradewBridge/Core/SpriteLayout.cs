using Terradew.Core.Protocol;

namespace Terradew.Core;

public readonly record struct SpriteBox(float X, float Y, float Width, float Height)
{
    public bool Overlaps(float x, float y, float width, float height)
        => X < x + width && X + Width > x && Y < y + height && Y + Height > y;

    public bool Contains(float x, float y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
}

/// <summary>
/// Where a Terraria creature sits on the Stardew map. One Terraria tile is one Stardew tile, which would make
/// Terraria bodies twice the size of Stardew's people. So bodies are drawn at half that scale, and Terraria
/// shrinks every creature and projectile hitbox by <see cref="BodyScale"/> to match the picture.
/// </summary>
public static class SpriteLayout
{
    /// <summary>How much Terraria hitboxes shrink in the Terradew world.</summary>
    public const float BodyScale = 0.5f;

    /// <summary>Stardew pixels per Terraria sprite pixel.</summary>
    public const float DrawScale = WorldProjection.Scale * BodyScale;

    /// <summary>The farmer's body in Terraria pixels: one Stardew tile wide, two tall.</summary>
    public const int FarmerWidth = 16;
    public const int FarmerHeight = 32;

    /// <summary>A Terraria size shrunk to the Terradew body scale.</summary>
    public static int Shrink(int size) => System.Math.Max(2, (int)System.MathF.Round(size * BodyScale));

    /// <summary>The (already shrunk) hitbox in Stardew pixels, standing on its feet point.</summary>
    public static SpriteBox Hitbox(float feetX, float feetY, EntityState entity)
    {
        float width = System.Math.Max(2, (int)entity.Width) * WorldProjection.Scale;
        float height = System.Math.Max(2, (int)entity.Height) * WorldProjection.Scale;
        return new SpriteBox(feetX - width / 2f, feetY - height, width, height);
    }
}
