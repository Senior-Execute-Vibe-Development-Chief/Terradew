using System;
using System.IO;

namespace Terradew.Core.Protocol;

/// <summary>
/// Run-length coding of an RGBA picture (4 bytes a pixel, rows top to bottom). The coded form is a list of runs read in
/// order: a run of fully transparent pixels, then a run of visible pixels, then transparent again, and so on. Each run
/// starts with its pixel count as a little-endian ushort; a visible run is followed by its RGBA pixels. A picture with no
/// visible pixel codes to nothing at all, and nothing decodes to a blank picture.
/// </summary>
public static class PictureRle
{
    /// <summary>The longest run one count can hold. Longer runs are split.</summary>
    public const int MaxRun = ushort.MaxValue;

    /// <summary>Codes an RGBA picture. Its length must be a multiple of 4.</summary>
    public static byte[] Encode(byte[] rgba)
    {
        if (rgba.Length % 4 != 0)
            throw new ArgumentException("A picture is RGBA, four bytes a pixel.", nameof(rgba));
        int pixels = rgba.Length / 4;
        if (!HasVisible(rgba, pixels))
            return Array.Empty<byte>();
        using var body = new MemoryStream();
        using (var writer = new BinaryWriter(body, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            int i = 0;
            bool transparent = true;
            while (i < pixels)
            {
                int start = i;
                if (transparent)
                {
                    while (i < pixels && i - start < MaxRun && rgba[i * 4 + 3] == 0)
                        i++;
                    writer.Write((ushort)(i - start));
                }
                else
                {
                    while (i < pixels && i - start < MaxRun && rgba[i * 4 + 3] != 0)
                        i++;
                    writer.Write((ushort)(i - start));
                    writer.Write(rgba, start * 4, (i - start) * 4);
                }
                transparent = !transparent;
            }
        }
        return body.ToArray();
    }

    /// <summary>Decodes a coded picture of the given size. Empty input decodes to a blank picture.</summary>
    public static byte[] Decode(byte[] coded, int width, int height)
    {
        if (width < 0 || height < 0)
            throw new InvalidDataException("Bad picture size.");
        long pixelCount = (long)width * height;
        if (pixelCount * 4 > int.MaxValue / 2)
            throw new InvalidDataException("Picture too large.");
        int pixels = (int)pixelCount;
        var rgba = new byte[pixels * 4];
        if (coded.Length == 0)
            return rgba;
        int at = 0;
        int i = 0;
        bool transparent = true;
        while (i < pixels)
        {
            if (at + 2 > coded.Length)
                throw new InvalidDataException("Picture ended early.");
            int count = coded[at] | (coded[at + 1] << 8);
            at += 2;
            if (i + count > pixels)
                throw new InvalidDataException("Picture run passes its end.");
            if (!transparent)
            {
                if (at + count * 4 > coded.Length)
                    throw new InvalidDataException("Picture ended inside a run.");
                Buffer.BlockCopy(coded, at, rgba, i * 4, count * 4);
                at += count * 4;
            }
            i += count;
            transparent = !transparent;
        }
        if (at != coded.Length)
            throw new InvalidDataException("Picture has bytes past its last run.");
        return rgba;
    }

    private static bool HasVisible(byte[] rgba, int pixels)
    {
        for (int i = 0; i < pixels; i++)
        {
            if (rgba[i * 4 + 3] != 0)
                return true;
        }
        return false;
    }
}
