using System;
using Terradew.Core.Routing;

/// <summary>Box footing checks: a walker is never pushed deeper into a block, and may walk out of one.</summary>
internal static class FootingTests
{
    public static int Run()
    {
        int failed = 0;
        Func<int, int, bool> wall = (x, y) => x == 5;

        failed += T.Check("a box in open ground touches no blocked tile", () =>
        {
            T.Assert(Footing.Overlap(wall, 1, 1, 2, 2) == 0, "open box");
            T.Assert(Footing.MayStep(0, 0), "clear to clear is a step");
        });

        failed += T.Check("a box that touches a wall counts each blocked tile once", () =>
        {
            T.Assert(Footing.Overlap(wall, 4, 0, 5, 2) == 3, "three tiles of wall under a 2x3 box");
            T.Assert(Footing.Overlap(wall, 5, 0, 5, 0) == 1, "one tile");
        });

        failed += T.Check("a clear walker may not step into a block", () =>
        {
            T.Assert(!Footing.MayStep(0, 1), "entering a wall is refused");
            T.Assert(!Footing.MayStep(0, 2), "entering two walls is refused");
        });

        failed += T.Check("a walker already inside a block may walk out but not deeper", () =>
        {
            T.Assert(Footing.MayStep(2, 1), "walking out of a block is allowed");
            T.Assert(Footing.MayStep(2, 2), "sliding at the same depth is allowed");
            T.Assert(!Footing.MayStep(2, 3), "going deeper is refused");
            T.Assert(Footing.MayStep(2, 0), "clearing the block is allowed");
        });

        return failed;
    }
}
