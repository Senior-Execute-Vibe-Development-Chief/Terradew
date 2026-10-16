using System;
using Terradew.Core.Protocol;

/// <summary>Checks the run-length coding of the effects pictures, and the view and effects messages.</summary>
internal static class EffectsTests
{
    public static int Run()
    {
        int failed = 0;
        failed += T.Check("an empty picture codes to nothing and decodes blank", () =>
        {
            T.Assert(PictureRle.Encode(Array.Empty<byte>()).Length == 0, "no pixels, no bytes");
            T.Assert(PictureRle.Decode(Array.Empty<byte>(), 0, 0).Length == 0, "zero size");
            T.Assert(PictureRle.Decode(Array.Empty<byte>(), 3, 2).Length == 24, "coded nothing decodes to blank pixels");
        });

        failed += T.Check("an all transparent picture codes to nothing", () =>
        {
            var rgba = new byte[64 * 4];
            for (int i = 0; i < rgba.Length; i += 4)
                rgba[i] = 200;
            T.Assert(PictureRle.Encode(rgba).Length == 0, "transparent pixels are not sent, whatever their colour");
            T.Assert(IsBlank(PictureRle.Decode(PictureRle.Encode(rgba), 8, 8)), "and they decode blank");
        });

        failed += T.Check("mixed pictures round trip, including runs longer than one count", () =>
        {
            const int width = 300;
            const int height = 300;
            var rgba = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                bool visible = i >= PictureRle.MaxRun + 4000 && (i < PictureRle.MaxRun * 2 || i % 3 != 0);
                if (i == width * height - 1)
                    visible = true;
                if (!visible)
                    continue;
                rgba[i * 4] = (byte)i;
                rgba[i * 4 + 1] = (byte)(i >> 8);
                rgba[i * 4 + 2] = 90;
                rgba[i * 4 + 3] = 255;
            }
            byte[] coded = PictureRle.Encode(rgba);
            T.Assert(coded.Length > 0 && coded.Length < rgba.Length, "a sparse picture codes smaller than it is");
            byte[] copy = PictureRle.Decode(coded, width, height);
            T.Assert(copy.Length == rgba.Length, "same size");
            for (int i = 0; i < rgba.Length; i++)
                T.Assert(copy[i] == rgba[i], $"byte {i} survives the round trip");
        });

        failed += T.Check("a fully visible picture round trips", () =>
        {
            var rgba = new byte[5 * 3 * 4];
            for (int i = 0; i < rgba.Length; i++)
                rgba[i] = (byte)(i % 4 == 3 ? 255 : i * 7);
            byte[] copy = PictureRle.Decode(PictureRle.Encode(rgba), 5, 3);
            T.Assert(copy.AsSpan().SequenceEqual(rgba), "every pixel survives");
        });

        failed += T.Check("a coded picture of the wrong size is refused", () =>
        {
            var rgba = new byte[4 * 4 * 4];
            rgba[3] = 255;
            byte[] coded = PictureRle.Encode(rgba);
            T.Assert(Throws(() => PictureRle.Decode(coded, 4, 5)), "too few runs for the size");
            T.Assert(Throws(() => PictureRle.Decode(coded, 2, 2)), "a run past the end");
        });

        failed += T.Check("the view message round trips", () =>
        {
            var view = (ViewRect)ProtocolTests.Roundtrip(new ViewRect { PlaceKey = "Farm", X = -640, Y = 1280, Width = 1920, Height = 1080 });
            T.Assert(view.PlaceKey == "Farm" && view.X == -640 && view.Y == 1280, "origin");
            T.Assert(view.Width == 1920 && view.Height == 1080, "size");
        });

        failed += T.Check("the effects frame round trips", () =>
        {
            var lit = new byte[] { 1, 2, 3, 4 };
            var frame = (EffectsFrame)ProtocolTests.Roundtrip(new EffectsFrame
            {
                PlaceKey = "Mines1", Fallback = false, OriginX = 4096, OriginY = -128, Width = 480, Height = 270,
                Lit = lit, Self = Array.Empty<byte>(),
            });
            T.Assert(frame.PlaceKey == "Mines1" && !frame.Fallback, "place and fallback");
            T.Assert(frame.OriginX == 4096 && frame.OriginY == -128, "origin");
            T.Assert(frame.Width == 480 && frame.Height == 270, "size");
            T.Assert(frame.Lit.AsSpan().SequenceEqual(lit), "lit picture");
            T.Assert(frame.Self.Length == 0, "empty self picture");
        });

        failed += T.Check("an empty fallback frame round trips", () =>
        {
            var frame = (EffectsFrame)ProtocolTests.Roundtrip(new EffectsFrame { Fallback = true });
            T.Assert(frame.Fallback && frame.Lit.Length == 0 && frame.Self.Length == 0, "fallback with no pictures");
        });
        return failed;
    }

    private static bool IsBlank(byte[] rgba)
    {
        foreach (byte b in rgba)
        {
            if (b != 0)
                return false;
        }
        return true;
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }
}
