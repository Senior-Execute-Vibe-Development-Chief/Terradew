using System;
using System.IO;

namespace Terradew.Core;

public static class SpriteNames
{
    public static string Folder => Path.Combine(BridgeFile.Root, "sprites");

    public static string Npc(int type, int frameX, int frameY, int frameWidth, int frameHeight)
        => $"npc-{type}-{frameX}-{frameY}-{frameWidth}-{frameHeight}.png";

    public static string Item(int type)
        => $"item-{type}.png";

    public static string Armor(string part, int slot, int variant = 0)
        => variant == 0 ? $"armor-{part}-{slot}.png" : $"armor-{part}-{slot}-{variant}.png";
}
