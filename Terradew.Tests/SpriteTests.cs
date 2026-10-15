using System;
using System.IO;
using Terradew.Core;
using Terradew.Core.Combat;
using Terradew.Core.Protocol;

internal static class SpriteTests
{
    public static int Run()
    {
        int failed = 0;
        failed += T.Check("back view keeps a darker outline", () =>
        {
            var rgba = new byte[] { 220, 40, 40, 255, 8, 8, 8, 255, 0, 0, 0, 0 };
            SpriteRecolor.ToBackView(rgba);
            T.Assert(rgba[0] > rgba[4], "outline darker than fill");
            T.Assert(rgba[8] == 0 && rgba[11] == 0, "transparent stays empty");
        });

        failed += T.Check("png writer writes a valid one pixel image", () =>
        {
            string path = Path.Combine(Path.GetTempPath(), "terradew-tests", Guid.NewGuid().ToString("N") + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Png.Write(path, 1, 1, new byte[] { 10, 20, 30, 255 });
            byte[] raw = Pixels(File.ReadAllBytes(path), 5);
            T.Assert(raw[0] == 0 && raw[1] == 10 && raw[2] == 20 && raw[3] == 30 && raw[4] == 255, "pixels");
            File.Delete(path);
        });

        failed += T.Check("a sprite appears only after the file is finished, and the first publish stays", () =>
        {
            string folder = Path.Combine(Path.GetTempPath(), "terradew-tests", Guid.NewGuid().ToString("N"));
            SpriteFiles.Publish(folder, "slime.png", 1, 1, new byte[] { 10, 20, 30, 255 });
            string finalPath = Path.Combine(folder, "slime.png");
            T.Assert(File.Exists(finalPath) && !File.Exists(finalPath + ".partial"), "final file only");
            SpriteFiles.Publish(folder, "slime.png", 1, 1, new byte[] { 1, 2, 3, 255 });
            byte[] raw = Pixels(File.ReadAllBytes(finalPath), 5);
            T.Assert(raw[1] == 10 && raw[2] == 20 && raw[3] == 30, "first publish stays");
            Directory.Delete(folder, true);
        });

        failed += T.Check("hitboxes stand on the feet at the map scale", () =>
        {
            var slime = new EntityState { Width = (short)SpriteLayout.Shrink(32), Height = (short)SpriteLayout.Shrink(24) };
            SpriteBox box = SpriteLayout.Hitbox(100f, 200f, slime);
            T.Assert(box.Width == 64f && box.Height == 48f, "shrunk hitbox, four stardew pixels per terraria pixel");
            T.Assert(box.Width == 32 * SpriteLayout.DrawScale && box.Height == 24 * SpriteLayout.DrawScale, "matches the drawn sprite");
            T.Assert(box.X == 68f && box.Y == 152f, "feet at the bottom center");
            T.Assert(box.Contains(100f, 170f) && !box.Contains(100f, 201f), "contains");
        });

        failed += T.Check("a pot is one stardew tile across", () =>
        {
            // A Terraria pot is 2 by 2 tiles, 32 pixels; drawn at the body scale it fills the one tile it is hit on.
            T.Assert(32 * SpriteLayout.DrawScale == WorldProjection.StardewTilePixels, "32 terraria pixels at the body scale is 64 stardew pixels");
        });

        failed += T.Check("map and world coordinates invert", () =>
        {
            var world = WorldProjection.ToWorld(10, 20, 64f, 128f);
            T.Assert(world.X == 176f && world.Y == 352f, "to world");
            var map = WorldProjection.ToMap(10, 20, world.X, world.Y);
            T.Assert(map.X == 64f && map.Y == 128f, "back to map");
            var topLeft = WorldProjection.FromFeet(100f, 200f, 20f, 42f);
            T.Assert(topLeft.X == 90f && topLeft.Y == 158f, "feet stay put");
        });

        failed += T.Check("walkers aim on both axes and never overshoot", () =>
        {
            T.Assert(!PlaneMotion.UsesRealAltitude(false, false) && PlaneMotion.UsesRealAltitude(true, false) && PlaneMotion.UsesRealAltitude(false, true), "who flies");
            PlaneMotion.PlaneStep step = PlaneMotion.Aim(0f, 0f, 30f, 40f, 5f, 2f);
            T.Assert(MathF.Abs(step.X - 3f) < 0.01f && MathF.Abs(step.Y - 4f) < 0.01f, "both axes");
            T.Assert(PlaneMotion.Aim(0f, 0f, 1f, 1f, 5f, 2f) == new PlaneMotion.PlaneStep(0f, 0f), "arrival stands still");
            T.Assert(PlaneMotion.Aim(0f, 0f, 3f, 0f, 5f, 1f) == new PlaneMotion.PlaneStep(3f, 0f), "last step lands exactly");
            T.Assert(PlaneMotion.Aim(0f, 0f, float.NaN, 0f, 5f, 1f) == new PlaneMotion.PlaneStep(0f, 0f), "NaN goal is ignored");
            PlaneMotion.FeetBox feet = PlaneMotion.Feet(0f, 0f, 20f, 42f);
            T.Assert(feet.X == 4f && feet.Y == 30f && feet.Width == 12 && feet.Height == 12, "feet fit a one tile gap");
            for (long id = 0; id < 100_000_000_000L; id += 7_777_777_777L)
            {
                float spread = PlaneMotion.Spread(id);
                T.Assert(spread >= -200f && spread <= 200f, "spread stays in range for huge ids");
            }
        });

        failed += T.Check("a swing reaches whichever way the farmer aims", () =>
        {
            MeleeArc.Area north = MeleeArc.Swing(100f, 100f, 100f, 0f, 40f);
            T.Assert(north.Intersects(95f, 50f, 10f, 10f), "hits north");
            T.Assert(!north.Intersects(95f, 140f, 10f, 10f), "misses behind");
            MeleeArc.Area south = MeleeArc.Swing(100f, 100f, 100f, 500f, 40f);
            T.Assert(south.Intersects(95f, 130f, 10f, 10f), "hits south");
            MeleeArc.Area still = MeleeArc.Swing(100f, 100f, 100f, 100f, 40f);
            T.Assert(still.Intersects(130f, 95f, 10f, 10f), "aiming at yourself swings east");
            float start = MeleeArc.BladeRotation(0f, 10, 10);
            float end = MeleeArc.BladeRotation(0f, 0, 10);
            T.Assert(start < 0f && end > 0f, "blade sweeps across the aim");
        });
        return failed;
    }

    private static byte[] Pixels(byte[] file, int count)
    {
        int offset = 8;
        while (offset + 8 < file.Length)
        {
            int length = (file[offset] << 24) | (file[offset + 1] << 16) | (file[offset + 2] << 8) | file[offset + 3];
            string tag = System.Text.Encoding.ASCII.GetString(file, offset + 4, 4);
            if (tag == "IDAT")
            {
                var zlib = file[(offset + 8)..(offset + 8 + length)];
                T.Assert(zlib[0] == 0x78, "zlib header");
                using var inflater = new System.IO.Compression.DeflateStream(new MemoryStream(zlib, 2, zlib.Length - 6), System.IO.Compression.CompressionMode.Decompress);
                var raw = new byte[count];
                inflater.ReadExactly(raw);
                return raw;
            }
            offset += 12 + length;
        }
        throw new Exception("missing IDAT");
    }
}
