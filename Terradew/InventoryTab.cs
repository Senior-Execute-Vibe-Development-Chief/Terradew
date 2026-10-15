using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using Terradew.Core.Economy;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>
/// Stardew's inventory tab, rebuilt around the Terraria character. It shows all 50 shared slots with the coin and
/// ammo slots beside them. Armor goes in the hat, shirt and pants slots left of the portrait, and vanity in the ring
/// and boot slots right of it. Accessories sit in the bottom right, and the portrait shows the Terraria character.
/// </summary>
internal sealed class InventoryTab : InventoryPage
{
    private const int Pitch = 56;
    private const int Cell = 52;
    private const int GearPitch = 62;
    private const int GearCell = 58;
    private const int PortraitWidth = 128;

    // Stardew's menu sheet: the empty slot, then the icons it shows in empty equipment slots.
    private const int SlotTile = 10;
    private const int BootsIcon = 40;
    private const int RingIcon = 41;
    private const int HatIcon = 42;
    private const int PantsIcon = 68;
    private const int ShirtIcon = 69;

    private static Bridge? bridge;
    private static Backpack? backpack;
    private static SpriteBook? sprites;

    private readonly BackpackSlots slots;
    private string hover = "";

    public InventoryTab(int x, int y, int width, int height, Bridge bridge, Backpack backpack)
        : base(x, y, width, height)
    {
        slots = new BackpackSlots(bridge, backpack);
    }

    public static void Install(Harmony harmony, IMonitor monitor, Bridge bridge, Backpack backpack, SpriteBook sprites)
    {
        InventoryTab.bridge = bridge;
        InventoryTab.backpack = backpack;
        InventoryTab.sprites = sprites;
        try
        {
            var constructor = AccessTools.Constructor(typeof(GameMenu), new[] { typeof(bool) });
            if (constructor == null)
            {
                monitor.Log("Terradew could not find Stardew's game menu, so its inventory tab stays as it is.", LogLevel.Warn);
                return;
            }
            harmony.Patch(constructor, postfix: new HarmonyMethod(typeof(InventoryTab), nameof(AfterGameMenu)));
            // A resize may rebuild the tabs, which would bring the vanilla page back.
            var resized = AccessTools.DeclaredMethod(typeof(GameMenu), nameof(GameMenu.gameWindowSizeChanged));
            if (resized != null)
                harmony.Patch(resized, postfix: new HarmonyMethod(typeof(InventoryTab), nameof(AfterGameMenu)));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not replace Stardew's inventory tab. {error.Message}", LogLevel.Warn);
        }
    }

    /// <summary>Swaps the menu's vanilla inventory tab for this one, for the main player only.</summary>
    private static void AfterGameMenu(GameMenu __instance)
    {
        if (bridge == null || backpack == null || !Context.IsMainPlayer || Context.ScreenId != 0)
            return;
        if (__instance.pages.Count <= GameMenu.inventoryTab || __instance.pages[GameMenu.inventoryTab] is not InventoryPage old || old is InventoryTab)
            return;
        __instance.pages[GameMenu.inventoryTab] = new InventoryTab(old.xPositionOnScreen, old.yPositionOnScreen, old.width, old.height, bridge, backpack);
    }

    private int Left => xPositionOnScreen + spaceToClearSideBorder + borderWidth;
    private int Top => yPositionOnScreen + spaceToClearTopBorder + borderWidth;
    private int GearTop => Top + Pitch * 5 + 20;
    private Rectangle Portrait => new(Left + GearPitch + 4, GearTop, PortraitWidth, GearPitch * 3 - 4);
    private Rectangle Trash => new(Left + GearPitch * 2 + PortraitWidth + 24, GearTop + GearPitch * 2, GearCell, GearCell);

    /// <summary>
    /// Main slots in rows of ten, then coins and ammo beside them. Then equipment from 100: armor left of the
    /// portrait, vanity right of it, and accessories in the bottom right. The trash slot comes last.
    /// </summary>
    private IEnumerable<(int Slot, Rectangle Area)> Slots()
    {
        for (int i = 0; i < Backpack.Size; i++)
            yield return (i, new Rectangle(Left + i % 10 * Pitch, Top + i / 10 * Pitch, Cell, Cell));
        int extra = Left + Pitch * 10 + 16;
        for (int i = 0; i < 8; i++)
            yield return (Backpack.Size + i, new Rectangle(extra + i / 4 * Pitch, Top + i % 4 * Pitch, Cell, Cell));
        for (int i = 0; i < 3; i++)
            yield return (InventoryAction.EquipmentBase + i, new Rectangle(Left, GearTop + i * GearPitch, GearCell, GearCell));
        int vanity = Portrait.Right + 10;
        for (int i = 0; i < 3; i++)
            yield return (InventoryAction.EquipmentBase + 10 + i, new Rectangle(vanity, GearTop + i * GearPitch, GearCell, GearCell));
        int right = extra + Pitch * 2 - 4;
        for (int i = 0; i < 7; i++)
        {
            int row = i / 4;
            int column = row == 0 ? i : i - 4 + 1;
            yield return (InventoryAction.EquipmentBase + 3 + i, new Rectangle(right - (4 - column) * GearPitch + 4, GearTop + GearPitch + row * GearPitch, GearCell, GearCell));
        }
        // The trash slot holds the item Terraria last threw away, so it can be taken back out.
        yield return (InventoryAction.TrashSlot, Trash);
    }

    /// <summary>What an empty slot is for.</summary>
    private static string Role(int slot) => slot switch
    {
        >= Backpack.Size and < Backpack.Size + 4 => "Coin slot",
        >= Backpack.Size + 4 and < Backpack.Size + 8 => "Ammo slot",
        InventoryAction.EquipmentBase => "Helmet",
        InventoryAction.EquipmentBase + 1 => "Chestplate",
        InventoryAction.EquipmentBase + 2 => "Leggings",
        >= InventoryAction.EquipmentBase + 3 and < InventoryAction.EquipmentBase + 10 => "Accessory",
        InventoryAction.EquipmentBase + 10 => "Vanity helmet",
        InventoryAction.EquipmentBase + 11 => "Vanity shirt",
        InventoryAction.EquipmentBase + 12 => "Vanity pants",
        InventoryAction.TrashSlot => "Trash",
        _ => "",
    };

    /// <summary>The Stardew icon an empty equipment slot shows, or -1.</summary>
    private static int Icon(int slot) => slot switch
    {
        InventoryAction.EquipmentBase => HatIcon,
        InventoryAction.EquipmentBase + 1 => ShirtIcon,
        InventoryAction.EquipmentBase + 2 => PantsIcon,
        InventoryAction.EquipmentBase + 10 or InventoryAction.EquipmentBase + 11 => RingIcon,
        InventoryAction.EquipmentBase + 12 => BootsIcon,
        _ => -1,
    };

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (Trash.Contains(x, y) && slots.Trash())
            return;
        foreach ((int slot, Rectangle area) in Slots())
        {
            if (area.Contains(x, y))
            {
                slots.Click(slot);
                return;
            }
        }
        slots.Selected = -1;
    }

    public override void receiveRightClick(int x, int y, bool playSound = true)
    {
        foreach ((int slot, Rectangle area) in Slots())
        {
            if (area.Contains(x, y))
            {
                if (slots.Use(slot))
                    Game1.exitActiveMenu();
                return;
            }
        }
    }

    public override void performHoverAction(int x, int y)
    {
        hover = "";
        // With something picked up, the trash slot throws it away rather than showing what it holds.
        if (slots.Selected >= 0 && Trash.Contains(x, y))
        {
            hover = "Throw away";
            return;
        }
        foreach ((int slot, Rectangle area) in Slots())
        {
            if (area.Contains(x, y))
            {
                hover = slots.Filled(slot) ? slots.Describe(slot) : Role(slot);
                return;
            }
        }
    }

    public override void draw(SpriteBatch b)
    {
        foreach ((int slot, Rectangle area) in Slots())
        {
            b.Draw(Game1.menuTexture, area, Game1.getSourceRectForStandardTileSheet(Game1.menuTexture, SlotTile), Color.White);
            if (!slots.Filled(slot) && Icon(slot) >= 0)
                b.Draw(Game1.menuTexture, area, Game1.getSourceRectForStandardTileSheet(Game1.menuTexture, Icon(slot)), Color.White);
            if (!slots.Filled(slot) && slot == InventoryAction.TrashSlot)
                DrawTrashCan(b, area);
            if (sprites != null)
                slots.DrawItem(b, sprites, slot, area);
            if (slot == slots.Selected)
                b.Draw(Game1.staminaRect, area, Color.Gold * 0.35f);
        }
        DrawPortrait(b);
        int infoX = Trash.X;
        b.DrawString(Game1.smallFont, Game1.player.Name, new Vector2(infoX, GearTop), Game1.textColor);
        InventoryState? inventory = bridge?.Inventory;
        string coins = inventory == null ? "Waiting for Terraria…" : Coins.Format(inventory.Coins);
        string coinsText = Game1.parseText(coins, Game1.smallFont, 150);
        b.DrawString(Game1.smallFont, coinsText, new Vector2(infoX, GearTop + 40), Game1.textColor);
        if (bridge?.World is WorldState world)
        {
            float defenseY = GearTop + 40 + Game1.smallFont.MeasureString(coinsText).Y + 4;
            b.DrawString(Game1.smallFont, "Defense " + world.Defense, new Vector2(infoX, defenseY), Game1.textColor);
        }
        if (hover.Length > 0)
            drawHoverText(b, hover, Game1.smallFont);
    }

    /// <summary>Stardew's trash can with its lid, at twice their size and centred in the trash slot.</summary>
    private static void DrawTrashCan(SpriteBatch b, Rectangle area)
    {
        const int Scale = 2;
        int level = Game1.player.trashCanLevel;
        int width = 18 * Scale;
        int lidHeight = 10 * Scale;
        int canHeight = 26 * Scale;
        int x = area.Center.X - width / 2;
        int top = area.Center.Y - (lidHeight + canHeight) / 2;
        b.Draw(Game1.mouseCursors, new Rectangle(x, top, width, lidHeight), new Rectangle(564 + level * 18, 129, 18, 10), Color.White);
        b.Draw(Game1.mouseCursors, new Rectangle(x, top + lidHeight, width, canHeight), new Rectangle(564 + level * 18, 102, 18, 26), Color.White);
    }

    /// <summary>The Terraria character, at the largest whole scale that fits, on Stardew's day or night backdrop.</summary>
    private static void DrawPortrait(SpriteBatch b, Rectangle area, Texture2D? picture)
    {
        b.Draw(Game1.staminaRect, area, (Game1.timeOfDay >= 1900 ? new Color(30, 30, 70) : new Color(150, 200, 230)) * 0.8f);
        if (picture == null || picture.Width <= 0 || picture.Height <= 0)
            return;
        int scale = Math.Max(1, Math.Min(area.Width / picture.Width, area.Height / picture.Height));
        int width = picture.Width * scale;
        int height = picture.Height * scale;
        b.Draw(picture, new Rectangle(area.Center.X - width / 2, area.Bottom - height - 4, width, height), Color.White);
    }

    private void DrawPortrait(SpriteBatch b) => DrawPortrait(b, Portrait, TerrariaBody.Picture);
}
