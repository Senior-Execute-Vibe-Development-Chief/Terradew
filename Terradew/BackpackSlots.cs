using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>
/// The clicks shared by every menu that shows the Terraria character's slots: main slots 0-49, coins and ammo
/// 50-57, equipment numbered from <see cref="InventoryAction.EquipmentBase"/>, and the trash slot
/// <see cref="InventoryAction.TrashSlot"/>. A first click picks an item up
/// and a second click puts it down.
/// </summary>
internal sealed class BackpackSlots
{
    private readonly Bridge bridge;
    private readonly Backpack backpack;

    public BackpackSlots(Bridge bridge, Backpack backpack)
    {
        this.bridge = bridge;
        this.backpack = backpack;
    }

    /// <summary>The slot picked up by the last click, or -1.</summary>
    public int Selected { get; set; } = -1;

    /// <summary>False until Terraria has sent its slots; nothing can be moved before then.</summary>
    public bool Ready => bridge.Inventory != null;

    public Item? StardewItem(int slot) => slot < Backpack.Size ? backpack.StardewItem(slot) : null;

    /// <summary>The Terraria item in a slot. Main slots show Terraria's view, where a Stardew item is empty.</summary>
    public ItemStack Item(int slot)
    {
        InventoryState? inventory = bridge.Inventory;
        if (inventory == null)
            return default;
        if (slot == InventoryAction.TrashSlot)
            return inventory.Trash;
        if (slot >= InventoryAction.EquipmentBase)
        {
            int index = slot - InventoryAction.EquipmentBase;
            return index < inventory.Equipment.Count ? inventory.Equipment[index] : default;
        }
        if (slot < Backpack.Size)
            return backpack.TerrariaItem(slot);
        return slot >= 0 && slot < inventory.Items.Count ? inventory.Items[slot] : default;
    }

    /// <summary>True when a slot holds anything, a Stardew item or a Terraria one.</summary>
    public bool Filled(int slot) => StardewItem(slot) != null || !Item(slot).IsEmpty;

    /// <summary>Picks up the item in a slot, or puts the picked-up item down there.</summary>
    public void Click(int slot)
    {
        if (!Ready)
            return;
        if (Selected < 0)
        {
            if (Filled(slot))
            {
                Selected = slot;
                Game1.playSound("pickUpItem");
            }
        }
        else if (slot != Selected)
        {
            Move(Selected, slot);
            Selected = -1;
        }
        else
        {
            Selected = -1;
        }
    }

    /// <summary>
    /// Moves an item from one slot to another. Two main slots swap in the shared backpack. Otherwise the swap goes to
    /// Terraria, which cannot take a Stardew item, so one on the main-slot side is refused.
    /// </summary>
    private void Move(int from, int to)
    {
        if (from < Backpack.Size && to < Backpack.Size)
        {
            backpack.Swap(from, to);
            Game1.playSound("stoneStep");
            return;
        }
        if (StardewItem(from) != null || StardewItem(to) != null)
        {
            Game1.playSound("cancel");
            return;
        }
        backpack.Send(new InventoryAction { Kind = InventoryKind.Swap, A = from, B = to });
        Game1.playSound("stoneStep");
    }

    /// <summary>Throws the picked-up item away. False when nothing is picked up.</summary>
    public bool Trash()
    {
        if (!Ready || Selected < 0)
            return false;
        Item? stardew = StardewItem(Selected);
        if (stardew == null)
            backpack.Send(new InventoryAction { Kind = InventoryKind.Trash, A = Selected });
        else if (stardew.canBeTrashed())
            backpack.RemoveStardew(Selected);
        else
        {
            Game1.playSound("cancel");
            return true;
        }
        Selected = -1;
        Game1.playSound("trashcan");
        return true;
    }

    /// <summary>Equips, takes off or uses a Terraria item. True when the item was used, so the menu should close.</summary>
    public bool Use(int slot)
    {
        // A Stardew item has no Terraria use; it is moved or trashed by left click only.
        if (!Ready || StardewItem(slot) != null)
            return false;
        ItemStack item = Item(slot);
        if (item.IsEmpty)
            return false;
        bool used = false;
        if (slot >= InventoryAction.EquipmentBase)
            backpack.Send(new InventoryAction { Kind = InventoryKind.Unequip, A = slot });
        else if (item.Equippable)
            backpack.Send(new InventoryAction { Kind = InventoryKind.Equip, A = slot });
        else if (item.Consumable || item.Usable)
        {
            backpack.Send(new InventoryAction { Kind = InventoryKind.Use, A = slot });
            used = true;
        }
        else
            return false;
        Game1.playSound("dwop");
        return used;
    }

    public string Describe(int slot)
    {
        Item? stardew = StardewItem(slot);
        return stardew != null ? stardew.DisplayName + "\n" + stardew.getDescription() : MenuArt.Describe(Item(slot));
    }

    /// <summary>Draws what a slot holds inside its box.</summary>
    public void DrawItem(SpriteBatch b, SpriteBook sprites, int slot, Rectangle area)
    {
        Item? stardew = StardewItem(slot);
        if (stardew != null)
            stardew.drawInMenu(b, new Vector2(area.X, area.Y), area.Width / 64f);
        else
        {
            ItemStack item = Item(slot);
            Hud.DrawItem(b, sprites, item, area.Center.X, area.Center.Y, area.Width - 12, count: false);
            if (!item.IsEmpty && item.Stack > 1)
            {
                // Like Stardew's own stack count: tiny digits at scale 3, in the bottom-right corner of the slot.
                float width = Utility.getWidthOfTinyDigitString(item.Stack, 3f);
                Utility.drawTinyDigits(item.Stack, b, new Vector2(area.Right - 4 - width, area.Bottom - 17), 3f, 1f, Color.White);
            }
        }
    }
}
