using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace Terradew;

/// <summary>
/// Shows Terraria 1.4.5's detailed portraits at their own resolution. Stardew draws a portrait as a 64 by 64 cell
/// scaled up four times, which would throw away most of the Terraria art. So the portrait sheet Stardew loads is a
/// small stand-in, and when it is drawn the full-size art is drawn in its place, filling the same box on screen.
/// If the draw patch is missing, the stand-in itself shows, a smaller copy of the same face.
/// </summary>
internal static class Portraits
{
    private const int Cell = 64;
    private const int Across = 2;
    private const int Down = 3;

    /// <summary>The full-size art for each stand-in sheet, and how many art pixels make one stand-in pixel.</summary>
    private static readonly ConditionalWeakTable<Texture2D, Detail> details = new();

    /// <summary>Lets the draw patches skip the table lookup until a detailed portrait is in use.</summary>
    private static bool any;

    private sealed record Detail(Texture2D Art, int Scale);

    public static void Install(Harmony harmony, IMonitor monitor)
    {
        var positioned = new[]
        {
            typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color), typeof(float), typeof(Vector2), typeof(float), typeof(SpriteEffects), typeof(float),
        };
        var stretched = new[]
        {
            typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color), typeof(float), typeof(Vector2), typeof(Vector2), typeof(SpriteEffects), typeof(float),
        };
        var boxed = new[]
        {
            typeof(Texture2D), typeof(Rectangle), typeof(Rectangle?), typeof(Color), typeof(float), typeof(Vector2), typeof(SpriteEffects), typeof(float),
        };
        Patch(harmony, monitor, positioned, nameof(DrawScaled));
        Patch(harmony, monitor, stretched, nameof(DrawStretched));
        Patch(harmony, monitor, boxed, nameof(DrawBoxed));
    }

    private static void Patch(Harmony harmony, IMonitor monitor, Type[] parameters, string prefix)
    {
        try
        {
            var method = AccessTools.Method(typeof(SpriteBatch), nameof(SpriteBatch.Draw), parameters);
            if (method == null)
            {
                monitor.Log("Terradew could not find a SpriteBatch.Draw it needs; detailed portraits may show smaller.", LogLevel.Trace);
                return;
            }
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(Portraits), prefix));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not patch portrait drawing; detailed portraits will show smaller. {error.Message}", LogLevel.Warn);
        }
    }

    /// <summary>
    /// Builds a Stardew portrait sheet from a Terraria detailed portrait: the bust is cut to a square a whole
    /// number of cells wide, sitting on the bottom edge as Stardew portraits do, and every face shows it.
    /// </summary>
    public static Texture2D Build(Texture2D portrait)
    {
        var pixels = new Color[portrait.Width * portrait.Height];
        portrait.GetData(pixels);
        int left = portrait.Width, right = -1, top = portrait.Height, bottom = -1;
        for (int y = 0; y < portrait.Height; y++)
        {
            for (int x = 0; x < portrait.Width; x++)
            {
                if (pixels[y * portrait.Width + x].A == 0)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }
        if (right < left)
        {
            left = top = 0;
            right = portrait.Width - 1;
            bottom = portrait.Height - 1;
        }
        int span = Math.Max(right - left + 1, bottom - top + 1);
        // Two art pixels to one stand-in pixel draws the art at its own size in Stardew's box; bigger busts shrink.
        int scale = Math.Clamp((span + Cell - 1) / Cell, 2, 4);
        int side = Cell * scale;
        int fromX = (left + right + 1 - side) / 2;
        int fromY = bottom + 1 - side;
        Color At(int x, int y)
        {
            x += fromX;
            y += fromY;
            return x < 0 || y < 0 || x >= portrait.Width || y >= portrait.Height ? Color.Transparent : pixels[y * portrait.Width + x];
        }

        var art = new Color[side * side];
        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
                art[y * side + x] = At(x, y);
        }
        var detail = new Texture2D(Game1.graphics.GraphicsDevice, side, side);
        detail.SetData(art);

        int width = Cell * Across;
        var sheet = new Color[width * Cell * Down];
        for (int face = 0; face < Across * Down; face++)
        {
            int cellX = face % Across * Cell;
            int cellY = face / Across * Cell;
            for (int y = 0; y < Cell; y++)
            {
                for (int x = 0; x < Cell; x++)
                    sheet[(cellY + y) * width + cellX + x] = At(x * scale + scale / 2, y * scale + scale / 2);
            }
        }
        var standIn = new Texture2D(Game1.graphics.GraphicsDevice, width, Cell * Down);
        standIn.SetData(sheet);
        details.AddOrUpdate(standIn, new Detail(detail, scale));
        any = true;
        return standIn;
    }

    /// <summary>The full-size art and source for a draw from a stand-in sheet, or false for any other texture.</summary>
    private static bool Swap(ref Texture2D texture, ref Rectangle? source, out int scale)
    {
        scale = 1;
        if (!any || texture == null || !details.TryGetValue(texture, out Detail? detail))
            return false;
        Rectangle from = source ?? new Rectangle(0, 0, Cell, Cell);
        scale = detail.Scale;
        // Every face is the same, so the place inside a cell is all that matters.
        source = new Rectangle(from.X % Cell * scale, from.Y % Cell * scale, from.Width * scale, from.Height * scale);
        texture = detail.Art;
        return true;
    }

    // Harmony hands the arguments over by position: texture, position or destination, source, colour, rotation, origin, scale.

    private static void DrawScaled(ref Texture2D __0, ref Rectangle? __2, ref Vector2 __5, ref float __6)
    {
        if (!Swap(ref __0, ref __2, out int factor))
            return;
        __5 *= factor;
        __6 /= factor;
    }

    private static void DrawStretched(ref Texture2D __0, ref Rectangle? __2, ref Vector2 __5, ref Vector2 __6)
    {
        if (!Swap(ref __0, ref __2, out int factor))
            return;
        __5 *= factor;
        __6 /= factor;
    }

    private static void DrawBoxed(ref Texture2D __0, ref Rectangle? __2, ref Vector2 __5)
    {
        if (!Swap(ref __0, ref __2, out int factor))
            return;
        __5 *= factor;
    }
}
