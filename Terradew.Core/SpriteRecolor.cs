using System;

namespace Terradew.Core;

public static class SpriteRecolor
{
    /// <summary>
    /// Turns a side-view sprite into a back view. The silhouette stays, the fill becomes the
    /// average color, and the darkest pixels stay a darker shade so the outline does not disappear.
    /// </summary>
    public static void ToBackView(byte[] rgba)
    {
        long red = 0;
        long green = 0;
        long blue = 0;
        int count = 0;
        int darkest = 255;
        for (int i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i + 3] < 16)
                continue;
            red += rgba[i];
            green += rgba[i + 1];
            blue += rgba[i + 2];
            count++;
            darkest = Math.Min(darkest, Luma(rgba[i], rgba[i + 1], rgba[i + 2]));
        }
        if (count == 0)
            return;

        byte averageRed = (byte)(red / count);
        byte averageGreen = (byte)(green / count);
        byte averageBlue = (byte)(blue / count);
        byte outlineRed = (byte)(averageRed / 2);
        byte outlineGreen = (byte)(averageGreen / 2);
        byte outlineBlue = (byte)(averageBlue / 2);
        for (int i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i + 3] < 16)
                continue;
            bool outline = Luma(rgba[i], rgba[i + 1], rgba[i + 2]) <= darkest + 28;
            rgba[i] = outline ? outlineRed : averageRed;
            rgba[i + 1] = outline ? outlineGreen : averageGreen;
            rgba[i + 2] = outline ? outlineBlue : averageBlue;
        }
    }

    private static int Luma(byte red, byte green, byte blue)
        => (red * 3 + green * 6 + blue) / 10;
}
