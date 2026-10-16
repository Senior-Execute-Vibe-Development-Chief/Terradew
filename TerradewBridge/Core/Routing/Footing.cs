using System;

namespace Terradew.Core.Routing;

/// <summary>
/// How a walker's box sits against blocked tiles. A walker that is already overlapping a block (after a warp, or
/// standing on a tile the map marks solid) may walk out of it, but never deeper into it.
/// </summary>
public static class Footing
{
    /// <summary>How many blocked tiles a box touches, given its inclusive tile range.</summary>
    public static int Overlap(Func<int, int, bool> blocked, int left, int top, int right, int bottom)
    {
        if (blocked == null)
            throw new ArgumentNullException(nameof(blocked));
        int count = 0;
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                if (blocked(x, y))
                    count++;
            }
        }
        return count;
    }

    /// <summary>
    /// True when a step may be taken: it leaves the box clear, or it does not touch more blocked tiles than the box
    /// touches now.
    /// </summary>
    public static bool MayStep(int before, int after) => after == 0 || after <= before;
}
