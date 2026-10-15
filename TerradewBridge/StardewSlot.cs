using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerradewBridge;

/// <summary>
/// Stands in for a Stardew item in a backpack slot Stardew holds, so Terraria's own pickups and crafting go elsewhere.
/// It can't be used, sold, traded or crafted with, and it is never shown to Stardew.
/// </summary>
public sealed class StardewSlot : ModItem
{
    public override string Texture => $"Terraria/Images/Item_{ItemID.DirtBlock}";

    public override void SetDefaults()
    {
        Item.width = 20;
        Item.height = 20;
        Item.maxStack = 1;
        Item.value = 0;
        Item.rare = ItemRarityID.White;
    }

    /// <summary>Whether the item is a blocker standing in for a Stardew item.</summary>
    public static bool Is(Item item) => !item.IsAir && item.type == ModContent.ItemType<StardewSlot>();
}
