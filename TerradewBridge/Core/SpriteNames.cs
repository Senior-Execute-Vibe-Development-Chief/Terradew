using System.IO;

namespace Terradew.Core;

/// <summary>File names of sprite sheets Terraria exports for Stardew to draw. Whole sheets, never single frames.</summary>
public static class SpriteNames
{
    public static string Folder => Path.Combine(Paths.Root, "sprites");

    public static string Npc(int type) => $"npc-{type}.png";

    /// <summary>
    /// A town NPC's current look: the texture its Terraria profile picks (default, party, shimmered, a pet's breed),
    /// named after the texture's asset path.
    /// </summary>
    public static string Look(string assetName)
    {
        var name = new System.Text.StringBuilder("look-");
        foreach (char c in assetName)
            name.Append(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' ? char.ToLowerInvariant(c) : '_');
        return name.Append(".png").ToString();
    }

    /// <summary>The sheet to draw an NPC from: its current look when Terraria sent one, else its plain sheet.</summary>
    public static string NpcSheet(int type, string? look)
        => !string.IsNullOrEmpty(look) && look.StartsWith("look-") && Path.GetFileName(look) == look ? look : Npc(type);

    /// <summary>A town NPC's head portrait, as Terraria's NPC_Head sheets number them.</summary>
    public static string NpcHead(int headIndex) => $"npchead-{headIndex}.png";

    /// <summary>A town NPC's detailed portrait, the 200 by 200 character art Terraria 1.4.5 shows while you talk.</summary>
    public static string NpcPortrait(int type) => $"npcportrait-{type}.png";

    public static string Projectile(int type) => $"proj-{type}.png";

    public static string Item(int type) => $"item-{type}.png";

    /// <summary>A Terraria pot, by its Pots tile style (see Pots.StyleFor). The 2 by 2 cells are joined, so the name is new.</summary>
    public static string Pot(int style) => $"pot-2x2-{style}.png";

    public static string Buff(int type) => $"buff-{type}.png";

    public static string Armor(string part, int slot, int variant = 0)
        => variant == 0 ? $"armor-{part}-{slot}.png" : $"armor-{part}-{slot}-{variant}.png";

    /// <summary>An accessory sprite the farmer wears: part is wings, shield, back, front or shoe.</summary>
    public static string Accessory(string part, int slot) => $"acc-{part}-{slot}.png";
}
