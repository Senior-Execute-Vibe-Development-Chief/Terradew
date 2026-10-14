using System;

namespace Terradew.Core;

/// <summary>
/// Movement in the empty Terraria volume. Flyers and worms already choose both axes.
/// Everyone else walks straight toward a goal, at their own speed, on both axes together.
/// Jump, fall, and gravity never become a step.
/// </summary>
public static class PlaneMotion
{
    public static bool UsesRealAltitude(bool glider, bool worm)
        => glider || worm;

    public static bool IsGrounded(float verticalSpeed)
        => Math.Abs(verticalSpeed) < 0.05f;

    public static float FlyerSouth(float playerSouth, float playerAltitude, float npcAltitude)
        => playerSouth + (npcAltitude - playerAltitude);

    public static float StepToward(float current, float target, float eastSpeed)
    {
        float rate = Math.Abs(eastSpeed);
        if (rate <= 0f)
            return current;
        float delta = target - current;
        if (delta > rate)
            delta = rate;
        else if (delta < -rate)
            delta = -rate;
        return current + delta;
    }

    public const int FeetSize = 12;

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
        if (distance <= arrive || speed <= 0f)
            return new PlaneStep(0f, 0f);
        if (speed >= distance)
            return new PlaneStep(dx, dy);
        float scale = speed / distance;
        return new PlaneStep(dx * scale, dy * scale);
    }

    public static float Spread(int id)
    {
        int band = (id * 97) % 401;
        return band - 200f;
    }
}
