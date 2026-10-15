using StardewModdingAPI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using Terradew.Core;
using Terradew.Core.Economy;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>Terraria's life, mana, buffs and the shared hotbar, drawn in Stardew's HUD style.</summary>
internal sealed class Hud
{
    private sealed record Line(string Text, Color Color, int Until);

    private const int SlotSize = 64;
    private const int BarPad = 16;

    private readonly Bridge bridge;
    private readonly SpriteBook sprites;
    private readonly Hand hand;
    private readonly Backpack backpack;
    private readonly List<Line> notices = new();
    private readonly StardewModdingAPI.IReflectedField<bool>? showingHealth;

    public Hud(Bridge bridge, SpriteBook sprites, Hand hand, StardewModdingAPI.IModHelper helper, Backpack backpack)
    {
        this.bridge = bridge;
        this.sprites = sprites;
        this.hand = hand;
        this.backpack = backpack;
        showingHealth = helper.Reflection.GetField<bool>(typeof(Game1), "showingHealth", required: false);
    }

    public void Notice(string text, Color color)
    {
        notices.Add(new Line(text, color, (int)Game1.ticks + 60 * 6));
        if (notices.Count > 6)
            notices.RemoveAt(0);
    }

    /// <summary>Stardew's own health bar would show a number that no longer matters.</summary>
    public void BeforeHud()
    {
        if (bridge.Linked)
            showingHealth?.SetValue(false);
    }

    /// <summary>Stardew's own toolbar would draw over the shared hotbar, so it is taken off the screen each tick.</summary>
    public void HideVanillaToolbar()
    {
        if (!Context.IsWorldReady)
            return;
        for (int i = Game1.onScreenMenus.Count - 1; i >= 0; i--)
        {
            if (Game1.onScreenMenus[i] is Toolbar)
                Game1.onScreenMenus.RemoveAt(i);
        }
    }

    /// <summary>A left click on the hotbar selects its slot. Returns true when the click was on the bar and is used up.</summary>
    public bool OnClick(int uiX, int uiY)
    {
        if (!Context.IsWorldReady || !Context.IsPlayerFree)
            return false;
        Rectangle bar = BarArea();
        if (!bar.Contains(uiX, uiY))
            return false;
        for (int i = 0; i < Backpack.Hotbar; i++)
        {
            if (!SlotArea(bar, i).Contains(uiX, uiY))
                continue;
            Game1.player.CurrentToolIndex = i;
            Game1.playSound("smallSelect");
            break;
        }
        return true;
    }

    public void Draw(SpriteBatch b)
    {
        if (Game1.eventUp || Game1.activeClickableMenu != null && Game1.activeClickableMenu is not BobberBar)
            return;
        WorldState? world = bridge.World;
        if (!bridge.Linked || world == null)
        {
            string status = bridge.IsConnected ? "Terraria is loading…" : "Terraria not connected — open the world named Stardew Valley in tModLoader";
            b.DrawString(Game1.smallFont, status, new Vector2(16, 16), Color.White * 0.8f);
            DrawNotices(b, 48);
            DrawHotbar(b);
            return;
        }

        // Life and mana use Stardew's own health and energy bar spots, so the vanilla bars never show.
        Rectangle safe = Utility.getSafeArea();
        int manaX = safe.Right - 48 - 8;
        int lifeX = manaX - 56;
        DrawLife(b, lifeX, safe, world.Life, world.LifeMax);
        DrawMana(b, manaX, safe, world.Mana, world.ManaMax);

        int x = 16;
        int y = 16;
        y = DrawBuffs(b, world, x, y);
        DrawNotices(b, y + 8);
        DrawHotbar(b);
    }

    /// <summary>
    /// Stardew's energy bar, filled with mana. The vanilla frame stays; the mana is drawn over its fill column, from the bottom.
    /// Hover names the value.
    /// </summary>
    private static void DrawMana(SpriteBatch b, int frameX, Rectangle safe, int mana, int manaMax)
    {
        int top = safe.Bottom - 240;
        const int fillHeight = 168;
        Rectangle column = new(frameX + 12, top + 48, 24, fillHeight);
        b.Draw(Game1.staminaRect, column, new Color(60, 40, 40));
        float part = manaMax <= 0 ? 0f : Math.Clamp(mana / (float)manaMax, 0f, 1f);
        int height = (int)(fillHeight * part);
        if (height > 0)
        {
            Rectangle fill = new(column.X, column.Bottom - height, column.Width, height);
            b.Draw(Game1.staminaRect, fill, new Color(60, 110, 240));
            b.Draw(Game1.staminaRect, new Rectangle(fill.X, fill.Y, 4, height), new Color(140, 180, 255));
        }
        Rectangle frame = new(frameX, top, 48, safe.Bottom - 16 - top);
        if (manaMax > 0 && frame.Contains(Game1.getMouseX(), Game1.getMouseY()))
            IClickableMenu.drawHoverText(b, $"Mana {mana}/{manaMax}", Game1.smallFont);
    }

    /// <summary>
    /// Stardew's health bar, drawn the way vanilla draws it (Terraria's life is the same bar, redrawn here since vanilla's is hidden).
    /// The bar grows taller with max life, as vanilla does. Hover names the value.
    /// </summary>
    private static void DrawLife(SpriteBatch b, int x, Rectangle safe, int life, int lifeMax)
    {
        int extra = Math.Clamp((lifeMax - 100) / 4, 0, 120);
        int top = safe.Bottom - 224 - 16 - extra;
        const int full = 168;
        int barFull = full + extra;

        b.Draw(Game1.mouseCursors, new Rectangle(x, top, 48, 64), new Rectangle(268, 408, 12, 16), Color.White);
        b.Draw(Game1.mouseCursors, new Rectangle(x, top + 64, 48, safe.Bottom - 64 - 16 - (top + 64 - 8)), new Rectangle(268, 424, 12, 16), Color.White);
        b.Draw(Game1.mouseCursors, new Rectangle(x, top + 224 + extra - 64, 48, 64), new Rectangle(268, 448, 12, 16), Color.White);

        Rectangle column = new(x + 12, top + 48, 24, barFull);
        b.Draw(Game1.staminaRect, column, new Color(60, 40, 40));
        float part = lifeMax <= 0 ? 0f : Math.Clamp(life / (float)lifeMax, 0f, 1f);
        int height = (int)(barFull * part);
        if (height > 0)
            b.Draw(Game1.staminaRect, new Rectangle(column.X, column.Bottom - height, column.Width, height), new Color(220, 40, 60));

        Rectangle frame = new(x, top, 48, safe.Bottom - 16 - top);
        if (frame.Contains(Game1.getMouseX(), Game1.getMouseY()))
            IClickableMenu.drawHoverText(b, $"Life {life}/{lifeMax}", Game1.smallFont);
    }

    private int DrawBuffs(SpriteBatch b, WorldState world, int x, int y)
    {
        int column = 0;
        foreach (BuffState buff in world.Buffs)
        {
            Texture2D? icon = sprites.Load(SpriteNames.Buff(buff.BuffType));
            int left = x + column * 40;
            if (icon != null)
                b.Draw(icon, new Rectangle(left, y, 32, 32), Color.White);
            else
                b.Draw(Game1.staminaRect, new Rectangle(left, y, 32, 32), Color.Gray * 0.6f);
            int seconds = buff.Time / 60;
            string time = seconds >= 60 ? $"{seconds / 60}m" : $"{seconds}s";
            b.DrawString(Game1.tinyFont, time, new Vector2(left, y + 32), Color.White);
            column++;
            if (column >= 8)
            {
                column = 0;
                y += 52;
            }
        }
        return column == 0 ? y : y + 52;
    }

    private void DrawNotices(SpriteBatch b, int y)
    {
        notices.RemoveAll(n => n.Until < Game1.ticks);
        foreach (Line line in notices)
        {
            b.DrawString(Game1.smallFont, line.Text, new Vector2(18, y + 2), Color.Black * 0.6f);
            b.DrawString(Game1.smallFont, line.Text, new Vector2(16, y), line.Color);
            y += 30;
        }
    }

    /// <summary>The bar at the bottom centre, or at the top when the farmer is low on screen (Stardew's rule, unless the toolbar is pinned).</summary>
    private static Rectangle BarArea()
    {
        int width = Backpack.Hotbar * SlotSize + BarPad * 2;
        int height = SlotSize + BarPad * 2;
        int left = (Game1.uiViewport.Width - width) / 2;
        bool toolbarAtTop = Game1.options.pinToolbarToggle ? false : Game1.player.GetBoundingBox().Center.Y > Game1.viewport.Y + Game1.viewport.Height / 2 + 64;
        int top = toolbarAtTop ? 48 : Game1.uiViewport.Height - height - 16;
        return new Rectangle(left, top, width, height);
    }

    private static Rectangle SlotArea(Rectangle bar, int index) => new(bar.X + BarPad + index * SlotSize, bar.Y + BarPad, SlotSize, SlotSize);

    /// <summary>The bar is the toolbar: it shows whether or not Terraria is connected, since Stardew items still need it.</summary>
    private void DrawHotbar(SpriteBatch b)
    {
        if (!Context.IsWorldReady)
            return;
        Rectangle bar = BarArea();
        IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), bar.X, bar.Y, bar.Width, bar.Height, Color.White, 1f, false);
        int selected = hand.Slot;
        for (int i = 0; i < Backpack.Hotbar; i++)
        {
            Rectangle slot = SlotArea(bar, i);
            var position = new Vector2(slot.X, slot.Y);
            b.Draw(Game1.menuTexture, position, Game1.getSourceRectForStandardTileSheet(Game1.menuTexture, 10), Color.White);
            if (backpack.TerrariaOwns(i))
                DrawItem(b, sprites, backpack.TerrariaItem(i), slot.Center.X, slot.Center.Y, 48);
            else if (backpack.StardewItem(i) is Item item)
                item.drawInMenu(b, position, 1f);
            if (i == selected)
                b.Draw(Game1.menuTexture, position, Game1.getSourceRectForStandardTileSheet(Game1.menuTexture, 56), Color.White);
            b.DrawString(Game1.tinyFont, ((i + 1) % 10).ToString(), new Vector2(slot.X + 4, slot.Y + 2), Color.White);
        }
    }

    /// <summary>
    /// Draws an item's icon centred on a point. Icons are pixel art, so the scale is always whole: 2x when the icon fits
    /// the box, else 1x. Callers that draw their own stack count pass <paramref name="count"/> as false.
    /// </summary>
    public static void DrawItem(SpriteBatch b, SpriteBook sprites, ItemStack item, int centerX, int centerY, int box, float alpha = 1f, bool count = true)
    {
        if (item.IsEmpty)
            return;
        Texture2D? icon = sprites.Load(SpriteNames.Item(item.ItemType));
        if (icon != null)
        {
            int scale = Math.Max(icon.Width, icon.Height) * 2 <= box ? 2 : 1;
            // The centre is already whole pixels, and the origin is in half pixels so odd sizes still centre.
            b.Draw(icon, new Vector2(centerX, centerY), null, Color.White * alpha, 0f, new Vector2(icon.Width / 2f, icon.Height / 2f), scale, SpriteEffects.None, 1f);
        }
        else
        {
            b.DrawString(Game1.tinyFont, item.Name.Length > 4 ? item.Name[..4] : item.Name, new Vector2(centerX - box / 2, centerY - 8), Color.White * alpha);
        }
        if (item.Stack > 1 && count)
            Utility.drawTinyDigits(item.Stack, b, new Vector2(centerX + box / 2 - 12 - (item.Stack >= 100 ? 12 : item.Stack >= 10 ? 6 : 0), centerY + box / 2 - 12), 2f, 1f, Color.White * alpha);
    }
}
