using System;
using Terradew.Core;

/// <summary>Checks the dye name to tint table.</summary>
internal static class DyeTintTests
{
    private static int Brightness(uint tint) => (int)((tint >> 24) & 0xFF) + (int)((tint >> 16) & 0xFF) + (int)((tint >> 8) & 0xFF);

    public static int Run()
    {
        int failed = 0;
        failed += T.Check("a known dye gives its tint", () =>
        {
            T.Assert(DyeTint.Rgba("RedDye") == 0xFF8C8CFF, "red is (255,140,140) at full alpha");
            T.Assert(DyeTint.Rgba("BlackDye") != DyeTint.None, "black is a tint too");
            T.Assert(DyeTint.Rgba("RedAndBlackDye") != DyeTint.None && DyeTint.Rgba("RedAndSilverDye") != DyeTint.None, "and-variants exist");
        });

        failed += T.Check("an unknown or empty dye is white", () =>
        {
            T.Assert(DyeTint.Rgba("FancyDye") == DyeTint.None, "unknown name");
            T.Assert(DyeTint.Rgba("") == DyeTint.None && DyeTint.Rgba(null!) == DyeTint.None, "empty name");
            T.Assert(DyeTint.None == 0xFFFFFFFF, "white is 0xFFFFFFFF");
        });

        failed += T.Check("a bright dye is lighter than its base", () =>
        {
            foreach (string name in new[] { "Red", "Blue", "Teal", "Pink" })
                T.Assert(Brightness(DyeTint.Rgba("Bright" + name + "Dye")) > Brightness(DyeTint.Rgba(name + "Dye")), $"Bright{name} lighter than {name}");
        });

        failed += T.Check("a black-and dye is darker than its base", () =>
        {
            foreach (string name in new[] { "Red", "Blue", "Green" })
                T.Assert(Brightness(DyeTint.Rgba(name + "AndBlackDye")) < Brightness(DyeTint.Rgba(name + "Dye")), $"{name}AndBlack darker than {name}");
        });

        return failed;
    }
}
