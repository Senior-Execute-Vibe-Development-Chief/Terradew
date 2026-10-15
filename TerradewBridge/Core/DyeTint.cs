namespace Terradew.Core;

/// <summary>
/// Approximates a Terraria dye as one flat colour that multiplies over the sprite. Terraria dyes with shaders, so this
/// only gets close. Tints are packed as 0xRRGGBBAA and kept light so the armor stays readable. 0xFFFFFFFF means no tint.
/// </summary>
public static class DyeTint
{
    public const uint None = 0xFFFFFFFF;

    // Plain dyes, keyed by the colour name Terraria uses inside "<Name>Dye". Black and silver have no bright or and-variants.
    private static readonly (string Name, byte R, byte G, byte B)[] Plain =
    {
        ("Red", 255, 140, 140),
        ("Orange", 255, 190, 130),
        ("Yellow", 255, 250, 150),
        ("Lime", 200, 255, 130),
        ("Green", 150, 240, 150),
        ("Teal", 140, 240, 230),
        ("Cyan", 150, 240, 255),
        ("SkyBlue", 150, 200, 255),
        ("Blue", 150, 160, 255),
        ("Purple", 200, 150, 255),
        ("Violet", 225, 150, 255),
        ("Pink", 255, 160, 215),
        ("Brown", 200, 150, 120),
        ("Black", 110, 110, 115),
        ("Silver", 205, 210, 220),
    };

    private static readonly Dictionary<string, uint> Tints = Build();

    /// <summary>The packed tint for a Terraria dye item name ("RedDye", "BrightRedDye", "RedAndBlackDye", ...), or None.</summary>
    public static uint Rgba(string itemName)
    {
        if (string.IsNullOrEmpty(itemName))
            return None;
        return Tints.TryGetValue(itemName, out uint tint) ? tint : None;
    }

    private static Dictionary<string, uint> Build()
    {
        var table = new Dictionary<string, uint>();
        foreach (var (name, r, g, b) in Plain)
        {
            table[name + "Dye"] = Pack(r, g, b);
            if (name == "Black" || name == "Silver")
                continue;
            table["Bright" + name + "Dye"] = Pack(Lerp(r, 255, 0.35f), Lerp(g, 255, 0.35f), Lerp(b, 255, 0.35f));
            table[name + "AndBlackDye"] = Pack(r * 55 / 100, g * 55 / 100, b * 55 / 100);
            table[name + "AndSilverDye"] = Silvered(r, g, b);
        }
        return table;
    }

    // Light and desaturated: halfway to grey, then lifted toward white.
    private static uint Silvered(int r, int g, int b)
    {
        int grey = (r + g + b) / 3;
        int MuteAndLift(int c) => Lerp(Lerp(c, grey, 0.5f), 255, 0.2f);
        return Pack(MuteAndLift(r), MuteAndLift(g), MuteAndLift(b));
    }

    private static int Lerp(int from, int to, float amount) => (int)(from + (to - from) * amount);

    private static uint Pack(int r, int g, int b) =>
        ((uint)Math.Clamp(r, 0, 255) << 24) | ((uint)Math.Clamp(g, 0, 255) << 16) | ((uint)Math.Clamp(b, 0, 255) << 8) | 0xFFu;
}
