using Terradew.Core.Protocol;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerradewBridge;

/// <summary>
/// The items that take the character home. Terraria would teleport the character to its spawn point, which means
/// nothing here, because the farmer is in Stardew. The item is still used and consumed once, but Stardew warps the
/// farmer to their bed instead.
/// </summary>
public sealed class RecallItems : GlobalItem
{
    public override bool? UseItem(Item item, Player player)
    {
        if (!BridgeSystem.Active || player.whoAmI != Main.myPlayer || !IsHomeItem(item.type))
            return null;
        // Returning true takes the place of Terraria's own use, which holds the teleport, and consumes the item once.
        BridgeSystem.Instance.Send(new Recall { ItemType = item.type });
        return true;
    }

    /// <summary>Whether the item takes the character home in Terraria: a Recall Potion or a mirror.</summary>
    public static bool IsHomeItem(int type) => type == ItemID.RecallPotion || type == ItemID.MagicMirror || type == ItemID.IceMirror;
}
