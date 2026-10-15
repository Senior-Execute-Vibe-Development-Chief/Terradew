using System;

namespace Terradew.Core;

/// <summary>
/// Movement in the flat Terraria volume. Flyers and worms already choose both axes.
/// Everyone else walks straight toward a goal on both axes together. Jump, fall and gravity never become a step.
/// </summary>
public static class PlaneMotion
{
    public const int FeetSize = 12;

    /// <summary>Ticks a walker may spend without reaching its wander point before it picks another.</summary>
    public const int WanderPatience = 180;

    public static bool UsesRealAltitude(bool flyer, bool worm) => flyer || worm;

    public readonly record struct FeetBox(float X, float Y, int Width, int Height);

    /// <summary>
    /// Movement box at the bottom center of a body. It is smaller than one tile,
    /// so a one-tile gap that Stardew allows does not stop the creature.
    /// </summary>
    public static FeetBox Feet(float bodyX, float bodyY, float bodyWidth, float bodyHeight)
    {
        int size = FeetSize;
        float x = bodyX + (bodyWidth - size) / 2f;
        float y = bodyY + bodyHeight - size;
        return new FeetBox(x, y, size, size);
    }

    public readonly record struct PlaneStep(float X, float Y);

    public static PlaneStep Aim(float fromX, float fromY, float toX, float toY, float speed, float arrive)
    {
        float dx = toX - fromX;
        float dy = toY - fromY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (!float.IsFinite(distance) || distance <= arrive || speed <= 0f)
            return new PlaneStep(0f, 0f);
        if (speed >= distance)
            return new PlaneStep(dx, dy);
        float scale = speed / distance;
        return new PlaneStep(dx * scale, dy * scale);
    }

    /// <summary>A repeatable offset in [-200, 200] pixels for wander points.</summary>
    public static float Spread(long id)
    {
        long band = ((id * 97) % 401 + 401) % 401;
        return band - 200f;
    }
}
