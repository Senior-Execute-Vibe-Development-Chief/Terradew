using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using Terradew.Core.Protocol;

namespace Terradew;

public sealed class ModConfig
{
    /// <summary>Must match the Terradew Bridge mod in tModLoader.</summary>
    public int Port { get; set; } = Link.DefaultPort;

    /// <summary>Opens the Terraria inventory, equipment and crafting menu.</summary>
    public KeybindList OpenMenu { get; set; } = KeybindList.Parse("I");

    /// <summary>Stardew money changes become Terraria coins. One Stardew gold is one silver coin.</summary>
    public bool MirrorMoney { get; set; } = true;

    /// <summary>Removes Stardew's own monsters so only Terraria creatures fight.</summary>
    public bool RemoveStardewMonsters { get; set; } = true;
}
