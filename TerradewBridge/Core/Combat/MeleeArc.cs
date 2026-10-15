using System;

namespace Terradew.Core.Combat;

/// <summary>
/// Terraria swings a sword only left or right. On a top-down map the swing has to reach whichever way
/// the farmer aims, so the hit area is a square in front of the body along the aim, in Terraria pixels.
/// </summary>
public static class MeleeArc
{
    public readonly record struct Area(float X, float Y, float Width, float Height)
    {
        public bool Intersects(float x, float y, float width, float height)
            => X < x + width && X + Width > x && Y < y + height && Y + Height > y;
    }

    /// <param name="centerX">Center of the swinging body.</param>
    /// <param name="aimX">Where the farmer aims.</param>
    /// <param name="reach">Weapon reach in pixels, about the item's size times its scale.</param>
    public static Area Swing(float centerX, float centerY, float aimX, float aimY, float reach)
    {
        reach = Math.Max(8f, reach);
        float dx = aimX - centerX;
        float dy = aimY - centerY;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (!float.IsFinite(length) || length < 0.001f)
        {
            dx = 1f;
            dy = 0f;
            length = 1f;
        }
        float ux = dx / length;
        float uy = dy / length;
        float middleX = centerX + ux * reach * 0.6f;
        float middleY = centerY + uy * reach * 0.6f;
        float size = reach * 1.2f;
        return new Area(middleX - size / 2f, middleY - size / 2f, size, size);
    }

    /// <summary>Rotation for drawing the blade: sweeps 90 degrees across the aim over the swing.</summary>
    public static float BladeRotation(float aimAngle, int animation, int animationMax)
    {
        if (animationMax <= 0)
            return aimAngle;
        float progress = 1f - Math.Clamp(animation / (float)animationMax, 0f, 1f);
        return aimAngle - MathF.PI / 4f + progress * MathF.PI / 2f;
    }
}
