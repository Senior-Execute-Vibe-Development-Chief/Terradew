using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using Terradew.Core;
using Terradew.Core.Economy;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>Shared drawing for the Terraria menus.</summary>
internal static class MenuArt
{
    public const int Slot = 56;

    public static void Box(SpriteBatch b, int x, int y, int width, int height)
        => IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), x, y, width, height, Color.White, 1f, true);

    public static void SlotBox(SpriteBatch b, Rectangle area, bool highlight)
    {
        b.Draw(Game1.staminaRect, area, (highlight ? Color.Gold : Color.SaddleBrown) * 0.35f);
        b.Draw(Game1.staminaRect, new Rectangle(area.X, area.Y, area.Width, 2), Color.Black * 0.4f);
        b.Draw(Game1.staminaRect, new Rectangle(area.X, area.Bottom - 2, area.Width, 2), Color.Black * 0.4f);
    }

    public static void Button(SpriteBatch b, Rectangle area, string text, bool hover)
    {
        IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), area.X, area.Y, area.Width, area.Height, hover ? Color.Wheat : Color.White, 4f, false);
        Vector2 size = Game1.smallFont.MeasureString(text);
        b.DrawString(Game1.smallFont, text, new Vector2(area.Center.X - size.X / 2f, area.Center.Y - size.Y / 2f), Game1.textColor);
    }

    public static string Describe(ItemStack item)
    {
        if (item.IsEmpty)
            return "";
        string text = item.Stack > 1 ? $"{item.Name} ({item.Stack})" : item.Name;
        if (item.Damage > 0)
            text += $"\n{item.Damage} damage";
        return text;
    }
}

/// <summary>The Terraria character's inventory, equipment and crafting, opened with the Terraria menu key.</summary>
internal sealed class TerrariaMenu : IClickableMenu
{
    private const int Width = 1040;
    private const int Height = 660;

    private readonly Bridge bridge;
    private readonly SpriteBook sprites;
    private bool crafting;
    private int selected = -1;
    private int scroll;
    private string hover = "";
    private Rectangle inventoryTab;
    private Rectangle craftingTab;
    private Rectangle trash;

    public TerrariaMenu(Bridge bridge, SpriteBook sprites)
        : base((Game1.uiViewport.Width - Width) / 2, (Game1.uiViewport.Height - Height) / 2, Width, Height, showUpperRightCloseButton: true)
    {
        this.bridge = bridge;
        this.sprites = sprites;
        Layout();
    }

    private void Layout()
    {
        inventoryTab = new Rectangle(xPositionOnScreen + 32, yPositionOnScreen + 24, 200, 52);
        craftingTab = new Rectangle(xPositionOnScreen + 244, yPositionOnScreen + 24, 200, 52);
        trash = new Rectangle(xPositionOnScreen + 32 + MenuArt.Slot * 9, yPositionOnScreen + 96 + MenuArt.Slot * 5 + 24, MenuArt.Slot, MenuArt.Slot);
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        xPositionOnScreen = (Game1.uiViewport.Width - Width) / 2;
        yPositionOnScreen = (Game1.uiViewport.Height - Height) / 2;
        Layout();
    }

    /// <summary>Main inventory, coins and ammo slots, then equipment slots numbered from 100.</summary>
    private IEnumerable<(int Slot, Rectangle Area)> Slots()
    {
        int left = xPositionOnScreen + 32;
        int top = yPositionOnScreen + 96;
        for (int i = 0; i < 50; i++)
            yield return (i, new Rectangle(left + i % 10 * MenuArt.Slot, top + i / 10 * MenuArt.Slot, MenuArt.Slot - 4, MenuArt.Slot - 4));
        int extra = left + MenuArt.Slot * 10 + 16;
        for (int i = 0; i < 8; i++)
            yield return (50 + i, new Rectangle(extra + i / 4 * MenuArt.Slot, top + i % 4 * MenuArt.Slot, MenuArt.Slot - 4, MenuArt.Slot - 4));
        int gear = extra + MenuArt.Slot * 2 + 24;
        for (int i = 0; i < 10; i++)
            yield return (InventoryAction.EquipmentBase + i, new Rectangle(gear, top + i * (MenuArt.Slot - 4), MenuArt.Slot - 8, MenuArt.Slot - 8));
        for (int i = 10; i < 13; i++)
            yield return (InventoryAction.EquipmentBase + i, new Rectangle(gear + MenuArt.Slot, top + (i - 10) * (MenuArt.Slot - 4), MenuArt.Slot - 8, MenuArt.Slot - 8));
    }

    private ItemStack Item(int slot)
    {
        InventoryState? inventory = bridge.Inventory;
        if (inventory == null)
            return default;
        if (slot >= InventoryAction.EquipmentBase)
        {
            int index = slot - InventoryAction.EquipmentBase;
            return index < inventory.Equipment.Count ? inventory.Equipment[index] : default;
        }
        return slot >= 0 && slot < inventory.Items.Count ? inventory.Items[slot] : default;
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        base.receiveLeftClick(x, y, playSound);
        if (inventoryTab.Contains(x, y))
        {
            crafting = false;
            return;
        }
        if (craftingTab.Contains(x, y))
        {
            crafting = true;
            scroll = 0;
            bridge.Send(new RequestRecipes());
            return;
        }
        if (crafting)
        {
            ClickRecipe(x, y);
            return;
        }
        if (trash.Contains(x, y) && selected >= 0)
        {
            bridge.Send(new InventoryAction { Kind = InventoryKind.Trash, A = selected });
            selected = -1;
            Game1.playSound("trashcan");
            return;
        }
        foreach ((int slot, Rectangle area) in Slots())
        {
            if (!area.Contains(x, y))
                continue;
            if (selected < 0)
            {
                if (!Item(slot).IsEmpty)
                {
                    selected = slot;
                    Game1.playSound("pickUpItem");
                }
            }
            else
            {
                bridge.Send(new InventoryAction { Kind = InventoryKind.Swap, A = selected, B = slot });
                selected = -1;
                Game1.playSound("stoneStep");
            }
            return;
        }
        selected = -1;
    }

    public override void receiveRightClick(int x, int y, bool playSound = true)
    {
        if (crafting)
            return;
        foreach ((int slot, Rectangle area) in Slots())
        {
            if (!area.Contains(x, y))
                continue;
            ItemStack item = Item(slot);
            if (item.IsEmpty)
                return;
            if (slot >= InventoryAction.EquipmentBase)
                bridge.Send(new InventoryAction { Kind = InventoryKind.Unequip, A = slot });
            else if (item.Equippable)
                bridge.Send(new InventoryAction { Kind = InventoryKind.Equip, A = slot });
            else if (item.Consumable)
            {
                bridge.Send(new InventoryAction { Kind = InventoryKind.Use, A = slot });
                exitThisMenu();
            }
            Game1.playSound("dwop");
            return;
        }
    }

    private Rectangle RecipeArea => new(xPositionOnScreen + 32, yPositionOnScreen + 132, Width - 64, Height - 170);

    private const int Row = 52;

    private void ClickRecipe(int x, int y)
    {
        RecipeList? recipes = bridge.Recipes;
        Rectangle area = RecipeArea;
        if (recipes == null || !area.Contains(x, y))
            return;
        int index = scroll + (y - area.Y) / Row;
        if (index < 0 || index >= recipes.Recipes.Count)
            return;
        bridge.Send(new Craft { RecipeIndex = recipes.Recipes[index].RecipeIndex, Times = 1 });
        Game1.playSound("crafting");
    }

    public override void receiveScrollWheelAction(int direction)
    {
        if (!crafting || bridge.Recipes == null)
            return;
        int visible = RecipeArea.Height / Row;
        scroll = Math.Clamp(scroll + (direction > 0 ? -1 : 1), 0, Math.Max(0, bridge.Recipes.Recipes.Count - visible));
    }

    public override void performHoverAction(int x, int y)
    {
        base.performHoverAction(x, y);
        hover = "";
        if (crafting)
            return;
        foreach ((int slot, Rectangle area) in Slots())
        {
            if (area.Contains(x, y))
            {
                hover = MenuArt.Describe(Item(slot));
                return;
            }
        }
    }

    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);
        MenuArt.Box(b, xPositionOnScreen, yPositionOnScreen, width, height);
        MenuArt.Button(b, inventoryTab, "Inventory", !crafting);
        MenuArt.Button(b, craftingTab, "Crafting", crafting);
        InventoryState? inventory = bridge.Inventory;
        if (inventory == null)
        {
            b.DrawString(Game1.dialogueFont, "Waiting for Terraria…", new Vector2(xPositionOnScreen + 40, yPositionOnScreen + 120), Game1.textColor);
        }
        else if (crafting)
        {
            DrawRecipes(b);
        }
        else
        {
            foreach ((int slot, Rectangle area) in Slots())
            {
                MenuArt.SlotBox(b, area, slot == selected);
                Hud.DrawItem(b, sprites, Item(slot), area.Center.X, area.Center.Y, area.Width - 12);
            }
            MenuArt.SlotBox(b, trash, false);
            b.DrawString(Game1.tinyFont, "TRASH", new Vector2(trash.X + 6, trash.Y + 20), Color.White);
            b.DrawString(Game1.smallFont, Coins.Format(inventory.Coins), new Vector2(xPositionOnScreen + 32, trash.Y + 12), Game1.textColor);
            b.DrawString(Game1.smallFont, "Click to move · Right-click to equip, use or take off", new Vector2(xPositionOnScreen + 32, yPositionOnScreen + Height - 52), Game1.textColor * 0.8f);
        }
        base.draw(b);
        if (hover.Length > 0)
            drawHoverText(b, hover, Game1.smallFont);
        drawMouse(b);
    }

    private void DrawRecipes(SpriteBatch b)
    {
        RecipeList? recipes = bridge.Recipes;
        if (recipes == null)
        {
            b.DrawString(Game1.smallFont, "Asking Terraria for recipes…", new Vector2(xPositionOnScreen + 40, yPositionOnScreen + 96), Game1.textColor);
            return;
        }
        b.DrawString(Game1.smallFont, recipes.Stations, new Vector2(xPositionOnScreen + 40, yPositionOnScreen + 92), Game1.textColor);
        Rectangle area = RecipeArea;
        int visible = area.Height / Row;
        int mouseX = Game1.getMouseX();
        int mouseY = Game1.getMouseY();
        for (int i = 0; i < visible && scroll + i < recipes.Recipes.Count; i++)
        {
            RecipeEntry recipe = recipes.Recipes[scroll + i];
            var row = new Rectangle(area.X, area.Y + i * Row, area.Width, Row - 4);
            bool hovered = row.Contains(mouseX, mouseY);
            b.Draw(Game1.staminaRect, row, (hovered ? Color.Gold : Color.SaddleBrown) * 0.2f);
            Hud.DrawItem(b, sprites, recipe.Result, row.X + 26, row.Center.Y, 40);
            string name = recipe.Result.Stack > 1 ? $"{recipe.Result.Name} x{recipe.Result.Stack}" : recipe.Result.Name;
            b.DrawString(Game1.smallFont, name, new Vector2(row.X + 60, row.Y + 4), Game1.textColor);
            string needs = string.Join(", ", (recipe.Ingredients ?? new List<ItemStack>()).Select(n => $"{n.Stack} {n.Name}"));
            b.DrawString(Game1.tinyFont, needs, new Vector2(row.X + 380, row.Y + 14), Game1.textColor * 0.9f);
        }
        if (recipes.Recipes.Count == 0)
            b.DrawString(Game1.smallFont, "Nothing to craft with what you carry.", new Vector2(area.X + 8, area.Y + 8), Game1.textColor);
    }
}

/// <summary>A Terraria townsperson's chat, with their shop and the Nurse's healing.</summary>
internal sealed class ChatMenu : IClickableMenu
{
    private const int Width = 760;
    private const int Height = 320;
    private const int PortraitSize = 160;
    private const int PortraitPad = 12;

    private readonly Bridge bridge;
    private readonly SpriteBook sprites;
    private readonly NpcChat chat;
    private readonly List<(Rectangle Area, string Label, Action Act)> buttons = new();

    public ChatMenu(Bridge bridge, SpriteBook sprites, NpcChat chat)
        : base((Game1.uiViewport.Width - Width) / 2, Game1.uiViewport.Height - Height - 64, Width, Height, showUpperRightCloseButton: true)
    {
        this.bridge = bridge;
        this.sprites = sprites;
        this.chat = chat;
        int x = xPositionOnScreen + 32;
        int y = yPositionOnScreen + Height - 88;
        if (chat.HasShop)
            Add(ref x, y, "Shop", () => Game1.activeClickableMenu = new TerrariaShopMenu(bridge, sprites, chat.NpcId, chat.Name));
        if (chat.IsNurse)
            Add(ref x, y, chat.HealCost > 0 ? $"Heal ({Coins.Format(chat.HealCost)})" : "Heal", () => { bridge.Send(new NurseHeal { NpcId = chat.NpcId }); exitThisMenu(); });
        Add(ref x, y, "Close", () => exitThisMenu());
    }

    private void Add(ref int x, int y, string label, Action act)
    {
        int width = (int)Game1.smallFont.MeasureString(label).X + 48;
        buttons.Add((new Rectangle(x, y, width, 60), label, act));
        x += width + 16;
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        base.receiveLeftClick(x, y, playSound);
        foreach ((Rectangle area, _, Action act) in buttons)
        {
            if (area.Contains(x, y))
            {
                Game1.playSound("smallSelect");
                act();
                return;
            }
        }
    }

    public override void draw(SpriteBatch b)
    {
        MenuArt.Box(b, xPositionOnScreen, yPositionOnScreen, width, height);
        int textX = xPositionOnScreen + 32;
        int textWidth = Width - 72;
        Texture2D? head = chat.HeadIndex >= 0 ? sprites.Load(SpriteNames.NpcHead(chat.HeadIndex)) : null;
        // A portrait needs a real image and must leave the text a usable width.
        if (head != null && head.Width > 0 && head.Height > 0 && Width - 72 - Math.Min(PortraitSize, Math.Max(head.Width, head.Height) * 4) - PortraitPad * 2 - 24 >= 160)
        {
            // The portrait sits in its own frame on the left, like Stardew's, at the largest whole scale up to 4 that keeps it inside the menu.
            int scale = Math.Clamp(PortraitSize / Math.Max(head.Width, head.Height), 1, 4);
            int frameWidth = head.Width * scale + PortraitPad * 2;
            int frameHeight = head.Height * scale + PortraitPad * 2;
            int frameX = xPositionOnScreen + 24;
            int frameY = yPositionOnScreen + 24;
            MenuArt.Box(b, frameX, frameY, frameWidth, frameHeight);
            b.Draw(head, new Rectangle(frameX + PortraitPad, frameY + PortraitPad, head.Width * scale, head.Height * scale), Color.White);
            textX = frameX + frameWidth + 24;
            textWidth = xPositionOnScreen + Width - 32 - textX;
        }
        b.DrawString(Game1.dialogueFont, chat.Name, new Vector2(textX, yPositionOnScreen + 24), Game1.textColor);
        string text = Game1.parseText(chat.Text, Game1.smallFont, textWidth);
        b.DrawString(Game1.smallFont, text, new Vector2(textX, yPositionOnScreen + 84), Game1.textColor);
        int mouseX = Game1.getMouseX();
        int mouseY = Game1.getMouseY();
        foreach ((Rectangle area, string label, _) in buttons)
            MenuArt.Button(b, area, label, area.Contains(mouseX, mouseY));
        base.draw(b);
        drawMouse(b);
    }
}

/// <summary>Buy from a Terraria townsperson on the left; click your own items on the right to sell them.</summary>
internal sealed class TerrariaShopMenu : IClickableMenu
{
    private const int Width = 1100;
    private const int Height = 640;
    private const int Row = 56;

    private readonly Bridge bridge;
    private readonly SpriteBook sprites;
    private readonly int npcId;
    private readonly string name;
    private int scroll;
    private string hover = "";

    public TerrariaShopMenu(Bridge bridge, SpriteBook sprites, int npcId, string name)
        : base((Game1.uiViewport.Width - Width) / 2, (Game1.uiViewport.Height - Height) / 2, Width, Height, showUpperRightCloseButton: true)
    {
        this.bridge = bridge;
        this.sprites = sprites;
        this.npcId = npcId;
        this.name = name;
    }

    private Rectangle StockArea => new(xPositionOnScreen + 32, yPositionOnScreen + 96, 520, Height - 140);

    private IEnumerable<(int Slot, Rectangle Area)> Mine()
    {
        int left = xPositionOnScreen + 600;
        int top = yPositionOnScreen + 96;
        for (int i = 0; i < 50; i++)
            yield return (i, new Rectangle(left + i % 8 * MenuArt.Slot, top + i / 8 * MenuArt.Slot, MenuArt.Slot - 4, MenuArt.Slot - 4));
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        base.receiveLeftClick(x, y, playSound);
        if (bridge.Shops.TryGetValue(npcId, out ShopStock? stock) && StockArea.Contains(x, y))
        {
            int index = scroll + (y - StockArea.Y) / Row;
            if (index >= 0 && index < stock.Entries.Count)
            {
                bridge.Send(new Buy { NpcId = npcId, Index = index });
                Game1.playSound("purchase");
            }
            return;
        }
        foreach ((int slot, Rectangle area) in Mine())
        {
            if (!area.Contains(x, y))
                continue;
            ItemStack item = Item(slot);
            if (!item.IsEmpty)
            {
                bridge.Send(new Sell { NpcId = npcId, Slot = slot });
                Game1.playSound("sell");
            }
            return;
        }
    }

    public override void receiveScrollWheelAction(int direction)
    {
        if (!bridge.Shops.TryGetValue(npcId, out ShopStock? stock))
            return;
        int visible = StockArea.Height / Row;
        scroll = Math.Clamp(scroll + (direction > 0 ? -1 : 1), 0, Math.Max(0, stock.Entries.Count - visible));
    }

    private ItemStack Item(int slot)
    {
        InventoryState? inventory = bridge.Inventory;
        return inventory != null && slot < inventory.Items.Count ? inventory.Items[slot] : default;
    }

    public override void performHoverAction(int x, int y)
    {
        base.performHoverAction(x, y);
        hover = "";
        foreach ((int slot, Rectangle area) in Mine())
        {
            if (area.Contains(x, y) && !Item(slot).IsEmpty)
            {
                hover = MenuArt.Describe(Item(slot)) + "\nClick to sell";
                return;
            }
        }
    }

    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);
        MenuArt.Box(b, xPositionOnScreen, yPositionOnScreen, width, height);
        b.DrawString(Game1.dialogueFont, name, new Vector2(xPositionOnScreen + 32, yPositionOnScreen + 24), Game1.textColor);
        long coins = bridge.Inventory?.Coins ?? 0;
        b.DrawString(Game1.smallFont, Coins.Format(coins), new Vector2(xPositionOnScreen + 600, yPositionOnScreen + 40), Game1.textColor);
        int mouseX = Game1.getMouseX();
        int mouseY = Game1.getMouseY();
        if (bridge.Shops.TryGetValue(npcId, out ShopStock? stock))
        {
            Rectangle area = StockArea;
            int visible = area.Height / Row;
            for (int i = 0; i < visible && scroll + i < stock.Entries.Count; i++)
            {
                ShopEntry entry = stock.Entries[scroll + i];
                var row = new Rectangle(area.X, area.Y + i * Row, area.Width, Row - 4);
                bool affordable = coins >= entry.Price;
                b.Draw(Game1.staminaRect, row, (row.Contains(mouseX, mouseY) ? Color.Gold : Color.SaddleBrown) * 0.2f);
                Hud.DrawItem(b, sprites, entry.Item, row.X + 28, row.Center.Y, 40);
                b.DrawString(Game1.smallFont, entry.Item.Name, new Vector2(row.X + 64, row.Y + 12), affordable ? Game1.textColor : Color.Gray);
                string price = Coins.Format(entry.Price);
                Vector2 size = Game1.smallFont.MeasureString(price);
                b.DrawString(Game1.smallFont, price, new Vector2(row.Right - size.X - 8, row.Y + 12), affordable ? Game1.textColor : Color.Firebrick);
            }
        }
        else
        {
            b.DrawString(Game1.smallFont, "Asking for the stock…", new Vector2(xPositionOnScreen + 40, yPositionOnScreen + 100), Game1.textColor);
        }
        foreach ((int slot, Rectangle area) in Mine())
        {
            MenuArt.SlotBox(b, area, false);
            Hud.DrawItem(b, sprites, Item(slot), area.Center.X, area.Center.Y, area.Width - 12);
        }
        base.draw(b);
        if (hover.Length > 0)
            drawHoverText(b, hover, Game1.smallFont);
        drawMouse(b);
    }
}
