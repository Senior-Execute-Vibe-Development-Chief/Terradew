using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using Terradew.Core;
using Terradew.Core.Economy;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>Terraria's life, mana, buffs and hotbar, drawn in Stardew's HUD style.</summary>
internal sealed class Hud
{
    private sealed record Line(string Text, Color Color, int Until);

    private readonly Bridge bridge;
    private readonly SpriteBook sprites;
    private readonly Hand hand;
    private readonly List<Line> notices = new();
    private readonly StardewModdingAPI.IReflectedField<bool>? showingHealth;

    public Hud(Bridge bridge, SpriteBook sprites, Hand hand, StardewModdingAPI.IModHelper helper)
    {
        this.bridge = bridge;
        this.sprites = sprites;
        this.hand = hand;
        showingHealth = helper.Reflection.GetField<bool>(typeof(Game1), "showingHealth", required: false);
    }

    public void Notice(string text, Color color)
    {
        notices.Add(new Line(text, color, Game1.ticks + 60 * 6));
        if (notices.Count > 6)
            notices.RemoveAt(0);
    }

    /// <summary>Stardew's own health bar would show a number that no longer matters.</summary>
    public void BeforeHud()
    {
        if (bridge.Linked)
            showingHealth?.SetValue(false);
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
            return;
        }

        // Life and mana sit where Stardew's health and energy bars are, so the vanilla bars never show.
        Rectangle safe = Utility.getSafeArea();
        int barTop = safe.Bottom - 240;
        int manaX = safe.Right - 48 - 8;
        int lifeX = manaX - 56;
        DrawBar(b, lifeX, barTop, world.Life, world.LifeMax, new Color(220, 40, 60), $"Life {world.Life}/{world.LifeMax}");
        DrawBar(b, manaX, barTop, world.Mana, world.ManaMax, new Color(60, 110, 240), world.ManaMax > 0 ? $"Mana {world.Mana}/{world.ManaMax}" : null);

        int x = 16;
        int y = 16;
        b.DrawString(Game1.smallFont, $"Defense {world.Defense}", new Vector2(x, y), Color.White);
        y += 32;
        y = DrawBuffs(b, world, x, y);
        DrawNotices(b, y + 8);
        DrawHotbar(b);
    }

    /// <summary>A Stardew-style vertical bar: an opaque frame over the vanilla bar, filled from the bottom. Hover names the value.</summary>
    private static void DrawBar(SpriteBatch b, int x, int top, int value, int max, Color fill, string? label)
    {
        Rectangle frame = new(x, top, 48, 240);
        float part = max <= 0 ? 0f : Math.Clamp(value / (float)max, 0f, 1f);
        b.Draw(Game1.staminaRect, frame, new Color(30, 24, 28));
        Rectangle inner = new(x + 6, top + 6, 36, 228);
        int height = (int)(inner.Height * part);
        if (height > 0)
            b.Draw(Game1.staminaRect, new Rectangle(inner.X, inner.Bottom - height, inner.Width, height), fill);
        if (label != null && frame.Contains(Game1.getMouseX(), Game1.getMouseY()))
            IClickableMenu.drawHoverText(b, label, Game1.smallFont);
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

    private void DrawHotbar(SpriteBatch b)
    {
        InventoryState? inventory = bridge.Inventory;
        if (inventory == null || inventory.Items.Count < 10)
            return;
        const int size = 56;
        int width = size * 10 + 16;
        int left = (Game1.uiViewport.Width - width) / 2;
        bool toolbarAtTop = Game1.options.pinToolbarToggle ? false : Game1.player.GetBoundingBox().Center.Y > Game1.viewport.Y + Game1.viewport.Height / 2 + 64;
        int top = toolbarAtTop ? 120 : Game1.uiViewport.Height - 180;
        float fade = hand.Active ? 1f : 0.55f;
        IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), left, top, width, size + 16, Color.White * fade, 1f, false);
        for (int i = 0; i < 10; i++)
        {
            int slotX = left + 8 + i * size;
            int slotY = top + 8;
            bool selected = i == hand.Slot;
            b.Draw(Game1.staminaRect, new Rectangle(slotX + 2, slotY + 2, size - 4, size - 4), (selected && hand.Active ? Color.Gold : Color.SaddleBrown) * 0.35f * fade);
            DrawItem(b, sprites, inventory.Items[i], slotX + size / 2, slotY + size / 2, 40, fade);
            b.DrawString(Game1.tinyFont, ((i + 1) % 10).ToString(), new Vector2(slotX + 4, slotY + 2), Color.White * fade);
        }
        string coins = Coins.Format(inventory.Coins);
        string mode = hand.Active ? "Terraria hand" : "Stardew hand";
        b.DrawString(Game1.smallFont, $"{mode} · {coins}", new Vector2(left + 8, top - 30), Color.White * fade);
    }

    public static void DrawItem(SpriteBatch b, SpriteBook sprites, ItemStack item, int centerX, int centerY, int box, float alpha = 1f)
    {
        if (item.IsEmpty)
            return;
        Texture2D? icon = sprites.Load(SpriteNames.Item(item.ItemType));
        if (icon != null)
        {
            float scale = Math.Min(2f, box / (float)Math.Max(icon.Width, icon.Height));
            b.Draw(icon, new Vector2(centerX, centerY), null, Color.White * alpha, 0f, new Vector2(icon.Width / 2f, icon.Height / 2f), scale, SpriteEffects.None, 1f);
        }
        else
        {
            b.DrawString(Game1.tinyFont, item.Name.Length > 4 ? item.Name[..4] : item.Name, new Vector2(centerX - box / 2, centerY - 8), Color.White * alpha);
        }
        if (item.Stack > 1)
            Utility.drawTinyDigits(item.Stack, b, new Vector2(centerX + box / 2 - 12 - (item.Stack >= 100 ? 12 : item.Stack >= 10 ? 6 : 0), centerY + box / 2 - 12), 2f, 1f, Color.White * alpha);
    }
}
