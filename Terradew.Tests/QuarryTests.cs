using System;
using Terradew.Core.Town;

internal static class QuarryTests
{
    public static int Run()
    {
        int failed = 0;

        failed += T.Check("a creature picks a town NPC only when it is clearly nearer than the farmer", () =>
        {
            T.Assert(!Quarry.PickTown(5f, 3f), "two tiles nearer is not enough");
            T.Assert(Quarry.PickTown(6f, 3f), "three tiles nearer is enough");
            T.Assert(!Quarry.PickTown(3f, 3f), "a tie goes to the farmer");
            T.Assert(Quarry.PickTown(float.PositiveInfinity, 9f), "a farmer out of aim leaves the town NPC");
        });

        failed += T.Check("a town NPC beyond ten tiles is never picked", () =>
        {
            T.Assert(Quarry.PickTown(float.PositiveInfinity, 10f), "ten tiles is in reach");
            T.Assert(!Quarry.PickTown(float.PositiveInfinity, 10.5f), "past ten tiles is out of reach");
            T.Assert(!Quarry.PickTown(40f, 11f), "out of reach even when the farmer is far");
        });

        failed += T.Check("straight walking stops at six tiles", () =>
        {
            T.Assert(Quarry.Straight(6f), "six tiles walks straight");
            T.Assert(!Quarry.Straight(6.1f), "beyond six tiles follows the route");
        });

        failed += T.Check("a wall blocks sight, a gap does not", () =>
        {
            Func<int, int, bool> open = (x, y) => !(x == 5 && y != 1);
            T.Assert(!Quarry.Sight(16f, 8f, 16f * 10f, 8f, open), "wall across tile row zero blocks sight");
            T.Assert(Quarry.Sight(16f, 24f, 16f * 10f, 24f, open), "the gap on tile row one is open");
            T.Assert(Quarry.Sight(16f, 16f, 16f, 16f, open), "a point sees itself");
        });

        failed += T.Check("heroes and guards engage at Terraria's danger range, capped", () =>
        {
            T.Assert(Math.Abs(TownCourage.Engage(Role.Guard, false, 18.75f) - 18.75f) < 0.001f, "tinkerer range");
            T.Assert(TownCourage.Engage(Role.Guard, false, 3.75f) == TownCourage.Watch(Role.Guard, false), "short danger range keeps the watch range");
            T.Assert(TownCourage.Engage(Role.Hero, false, 56f) == TownCourage.EngageCap, "far danger range is capped");
            T.Assert(TownCourage.Engage(Role.Coward, false, 18.75f) == TownCourage.Watch(Role.Coward, false), "cowards keep their watch range");
        });

        return failed;
    }
}
