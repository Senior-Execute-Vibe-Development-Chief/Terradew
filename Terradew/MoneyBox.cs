using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using Terradew.Core;

namespace Terradew;

/// <summary>
/// Stardew's money box, the gold counter in the top right of the screen. While Terraria is linked, the counter shows
/// the Terraria coin total in copper (1 silver = 100 copper, 1 gold = 10,000, 1 platinum = 1,000,000), and a copper
/// coin covers the "G" that Stardew prints beside it. The dial only has eight digits, so the total is capped there.
/// </summary>
internal static class MoneyBox
{
    /// <summary>The copper coin's item type in Terraria; its icon is item-71.png.</summary>
    private const int CopperCoinType = 71;

    /// <summary>The most the eight-digit dial can show.</summary>
    private const long MaxDialValue = 99_999_999;

    // Offsets are measured from vanilla's layout, from the box's xPositionOnScreen and yPositionOnScreen:
    // the gold dial is drawn at (68, 196), and the "G" sits just right of its digits at about (200, 196).
    // The patch and coin are 24 pixels square. Adjust these if the box art moves.
    private static readonly Point GPosition = new(200, 196);
    private const int GBox = 24;

    /// <summary>The parchment colour behind the "G", so the coin covers it cleanly.</summary>
    private static readonly Color Parchment = new(254, 214, 136);

    private static Bridge? bridge;
    private static IMonitor? monitor;
    private static SpriteBook? sprites;

    public static void Install(Harmony harmony, IMonitor monitor, Bridge bridge, SpriteBook sprites)
    {
        MoneyBox.bridge = bridge;
        MoneyBox.monitor = monitor;
        MoneyBox.sprites = sprites;
        try
        {
            var dial = AccessTools.Method(typeof(MoneyDial), "draw", new[] { typeof(SpriteBatch), typeof(Vector2), typeof(int) });
            if (dial == null)
                monitor.Log("Terradew could not find Stardew's money dial, so the money box keeps its gold count.", LogLevel.Warn);
            else
                harmony.Patch(dial, prefix: new HarmonyMethod(typeof(MoneyBox), nameof(BeforeDialDraw)));

            var box = AccessTools.Method(typeof(DayTimeMoneyBox), "draw", new[] { typeof(SpriteBatch) });
            if (box == null)
                monitor.Log("Terradew could not find Stardew's money box, so the copper coin is not shown.", LogLevel.Warn);
            else
                harmony.Patch(box, postfix: new HarmonyMethod(typeof(MoneyBox), nameof(AfterBoxDraw)));
        }
        catch (Exception ex)
        {
            monitor.Log($"Terradew could not patch the money box: {ex.Message}", LogLevel.Warn);
        }
    }

    /// <summary>True while Terraria is linked and its coin total is known. Gives the copper total when so.</summary>
    private static bool TryCopper(out long copper)
    {
        copper = 0;
        if (bridge == null || !bridge.Linked || bridge.Inventory == null)
            return false;
        copper = bridge.Inventory.Coins;
        return true;
    }

    /// <summary>Shows the Terraria copper total on the money box's dial instead of the gold count.</summary>
    private static void BeforeDialDraw(MoneyDial __instance, ref int target)
    {
        try
        {
            if (Game1.dayTimeMoneyBox?.moneyDial != __instance)
                return;
            if (!TryCopper(out long copper))
                return;
            target = (int)Math.Clamp(copper, 0, MaxDialValue);
        }
        catch (Exception ex)
        {
            monitor?.Log($"Terradew money dial patch skipped: {ex.Message}", LogLevel.Trace);
        }
    }

    /// <summary>Covers the "G" with a parchment patch and draws the copper coin in its place.</summary>
    private static void AfterBoxDraw(DayTimeMoneyBox __instance, SpriteBatch b)
    {
        try
        {
            if (!TryCopper(out _) || sprites == null)
                return;
            int x = __instance.xPositionOnScreen + GPosition.X;
            int y = __instance.yPositionOnScreen + GPosition.Y;
            b.Draw(Game1.staminaRect, new Rectangle(x, y, GBox, GBox), Parchment);
            Texture2D? coin = sprites.Load(SpriteNames.Item(CopperCoinType));
            if (coin == null)
                return;
            float scale = GBox / (float)Math.Max(coin.Width, coin.Height);
            b.Draw(coin, new Vector2(x, y), null, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
        }
        catch (Exception ex)
        {
            monitor?.Log($"Terradew money box coin skipped: {ex.Message}", LogLevel.Trace);
        }
    }
}
