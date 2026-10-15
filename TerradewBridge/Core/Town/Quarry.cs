using System;

namespace Terradew.Core.Town
{
    /// <summary>
    /// Which target a hostile creature goes for when a town NPC is close: the farmer, or a standing town NPC
    /// in reach. The farmer wins ties, so a townsperson only draws a creature away when it is clearly nearer.
    /// </summary>
    public static class Quarry
    {
        /// <summary>How far, in tiles, a creature notices a town NPC.</summary>
        public const float ReachTiles = 10f;

        /// <summary>How many tiles nearer than the farmer a town NPC must be to be picked.</summary>
        public const float FarmerBiasTiles = 3f;

        /// <summary>How close, in tiles, a creature walks straight at a town NPC it can see, instead of the farmer's route.</summary>
        public const float StraightTiles = 6f;

        /// <summary>True when the town NPC is in reach and at least <see cref="FarmerBiasTiles"/> nearer than the farmer.</summary>
        public static bool PickTown(float farmerTiles, float townTiles)
            => townTiles <= ReachTiles && farmerTiles - townTiles >= FarmerBiasTiles;

        /// <summary>True when a town NPC this far away is close enough to walk straight at.</summary>
        public static bool Straight(float tiles) => tiles <= StraightTiles;

        /// <summary>
        /// True when no tile on the straight line between two points in Terraria pixels is closed. The points are
        /// sampled every few pixels, and <paramref name="open"/> takes tile coordinates.
        /// </summary>
        public static bool Sight(float fromX, float fromY, float toX, float toY, Func<int, int, bool> open)
        {
            float length = MathF.Sqrt((toX - fromX) * (toX - fromX) + (toY - fromY) * (toY - fromY));
            int steps = Math.Max(1, (int)MathF.Ceiling(length / SampleSpacing));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                int tileX = (int)MathF.Floor((fromX + (toX - fromX) * t) / TilePixels);
                int tileY = (int)MathF.Floor((fromY + (toY - fromY) * t) / TilePixels);
                if (!open(tileX, tileY))
                    return false;
            }
            return true;
        }

        private const float TilePixels = 16f;
        private const float SampleSpacing = 4f;
    }
}
