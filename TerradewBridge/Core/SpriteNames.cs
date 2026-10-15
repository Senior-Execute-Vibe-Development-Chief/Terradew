using System.IO;

namespace Terradew.Core;

/// <summary>File names of sprite sheets Terraria exports for Stardew to draw. Whole sheets, never single frames.</summary>
public static class SpriteNames
{
    public static string Folder => Path.Combine(Paths.Root, "sprites");

    public static string Npc(int type) => $"npc-{type}.png";

    /// <summary>A town NPC's head portrait, as Terraria's NPC_Head sheets number them.</summary>
    public static string NpcHead(int headIndex) => $"npchead-{headIndex}.png";

    public static string Projectile(int type) => $"proj-{type}.png";

    public static string Item(int type) => $"item-{type}.png";

    public static string Buff(int type) => $"buff-{type}.png";

    public static string Armor(string part, int slot, int variant = 0)
        => variant == 0 ? $"armor-{part}-{slot}.png" : $"armor-{part}-{slot}-{variant}.png";

    /// <summary>An accessory sprite the farmer wears: part is wings, shield, back, front or shoe.</summary>
    public static string Accessory(string part, int slot) => $"acc-{part}-{slot}.png";
}
