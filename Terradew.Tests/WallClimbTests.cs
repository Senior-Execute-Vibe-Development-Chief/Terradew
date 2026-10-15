using System;
using Terradew.Core.Bosses;

/// <summary>Wall of Flesh climb checks: floor start, speed by life, one-way movement, the map's north edge and contact.</summary>
public static class WallClimbTests
{
    public static int Run()
    {
        int failed = 0;
        const float Tolerance = 0.0001f;

        failed += T.Check("the wall starts at the south edge of the floor", () =>
        {
            T.Assert(Math.Abs(WallClimb.StartY(40) - 40 * 64f) < Tolerance, "40 tiles is 2560 px");
            T.Assert(WallClimb.StartY(0) == 0f, "an empty floor starts at the top");
            T.Assert(WallClimb.StartY(-5) == 0f, "a negative height is clamped");
        });

        failed += T.Check("speed is a tile a second at full life and one and a half at zero", () =>
        {
            float full = WallClimb.Speed(1f);
            float empty = WallClimb.Speed(0f);
            T.Assert(Math.Abs(full - 64f / 60f) < Tolerance, "full life is 1 tile/s, got " + full);
            T.Assert(Math.Abs(empty - 1.5f * 64f / 60f) < Tolerance, "zero life is 1.5 tiles/s, got " + empty);
        });

        failed += T.Check("speed clamps life outside 0 to 1", () =>
        {
            T.Assert(WallClimb.Speed(-1f) == WallClimb.Speed(0f), "below zero is zero");
            T.Assert(WallClimb.Speed(3f) == WallClimb.Speed(1f), "above one is full");
        });

        failed += T.Check("speed rises as life falls", () =>
        {
            float last = -1f;
            for (int i = 10; i >= 0; i--)
            {
                float speed = WallClimb.Speed(i / 10f);
                T.Assert(speed >= last, "speed fell at life " + i / 10f);
                last = speed;
            }
        });

        failed += T.Check("advance moves north by the speed", () =>
        {
            float next = WallClimb.Advance(1000f, 2.5f);
            T.Assert(Math.Abs(next - 997.5f) < Tolerance, "moved to " + next);
        });

        failed += T.Check("advance never moves south", () =>
        {
            T.Assert(WallClimb.Advance(1000f, -4f) == 1000f, "a negative speed holds still");
        });

        failed += T.Check("advance stops at the map's north edge", () =>
        {
            T.Assert(WallClimb.Advance(1f, 5f) == 0f, "clamped to 0");
            T.Assert(WallClimb.Advance(0f, 5f) == 0f, "stays at 0");
        });

        failed += T.Check("a 40-tile floor is crossed in about 40 seconds at full life", () =>
        {
            float y = WallClimb.StartY(40);
            float speed = WallClimb.Speed(1f);
            int ticks = 0;
            while (y > 0f && ticks < 100000)
            {
                y = WallClimb.Advance(y, speed);
                ticks++;
            }
            double seconds = ticks / (double)WallClimb.TicksPerSecond;
            T.Assert(Math.Abs(seconds - 40.0) < 0.5, "took " + seconds + " seconds");
        });

        failed += T.Check("touches when the feet are at or south of the wall", () =>
        {
            T.Assert(WallClimb.Touches(500f, 500f), "feet on the edge touch");
            T.Assert(WallClimb.Touches(500f, 900f), "feet south of the edge touch");
            T.Assert(!WallClimb.Touches(500f, 499.9f), "feet just north of the edge are safe");
            T.Assert(!WallClimb.Touches(500f, 100f), "feet far north are safe");
        });

        return failed;
    }
}
