namespace Terradew.Core;

public readonly record struct SpriteBox(float X, float Y, float Width, float Height)
{
    public bool Overlaps(float x, float y, float width, float height)
        => X < x + width && X + Width > x && Y < y + height && Y + Height > y;
}

/// <summary>
/// The rectangle Stardew uses both to draw a creature and to decide that a swing or a touch hit it.
/// A framed sprite stands on the ground point. A glider is raised. A missing frame stays a small square.
/// </summary>
public static class SpriteLayout
{
    public const float GliderLift = 28f;
    public const float DrawScale = 2f;

    public static SpriteBox Bounds(float groundX, float groundY, BridgeEntity entity)
    {
        float anchorY = groundY - (entity.Flags.HasFlag(BridgeFlags.Glider) ? GliderLift : 0f);
        if (entity.FrameW > 0 && entity.FrameH > 0)
        {
            float width = entity.FrameW * DrawScale;
            float height = entity.FrameH * DrawScale;
            return new SpriteBox(groundX - width / 2f, anchorY - height, width, height);
        }

        float size = entity.Flags.HasFlag(BridgeFlags.Boss) ? 28f : 16f;
        return new SpriteBox(groundX - size / 2f, anchorY - size / 2f, size, size);
    }
}
