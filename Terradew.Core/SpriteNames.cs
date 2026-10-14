using System.IO;

namespace Terradew.Core;

/// <summary>File names of sprite sheets Terraria exports for Stardew to draw. Whole sheets, never single frames.</summary>
public static class SpriteNames
{
    public static string Folder => Path.Combine(Paths.Root, "sprites");

    public static string Npc(int type) => $"npc-{type}.png";

    public static string Projectile(int type) => $"proj-{type}.png";

    public static string Item(int type) => $"item-{type}.png";

    public static string Buff(int type) => $"buff-{type}.png";

    public static string Armor(string part, int slot, int variant = 0)
        => variant == 0 ? $"armor-{part}-{slot}.png" : $"armor-{part}-{slot}-{variant}.png";
}
