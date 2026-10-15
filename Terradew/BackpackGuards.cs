using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using Terradew.Core;
using Terradew.Core.Protocol;
using SObject = StardewValley.Object;

namespace Terradew;

/// <summary>
/// Keeps Stardew's own code away from the placeholders that stand in for Terraria items in the shared backpack.
/// A placeholder is never held, placed, shipped, trashed, gifted or sorted; it is drawn as the Terraria item.
/// </summary>
internal static class BackpackGuards
{
    private static SpriteBook? sprites;

    public static void Install(Harmony harmony, IMonitor monitor, Backpack backpack, SpriteBook sprites)
    {
        BackpackGuards.sprites = sprites;

        // The farmer holds nothing in Stardew: the Terraria hand handles the placeholder.
        Patch(harmony, monitor, "Farmer.ActiveObject",
            () => AccessTools.PropertyGetter(typeof(Farmer), "ActiveObject"),
            postfix: nameof(ActiveObjectPostfix));
        // A placeholder cannot be placed on the map, shipped, trashed or given as a gift.
        Patch(harmony, monitor, "Object.canBePlacedHere",
            () => AccessTools.Method(typeof(SObject), "canBePlacedHere"),
            prefix: nameof(ForbidPrefix));
        Patch(harmony, monitor, "Object.placementAction",
            () => AccessTools.Method(typeof(SObject), "placementAction", new[] { typeof(GameLocation), typeof(int), typeof(int), typeof(Farmer) }),
            prefix: nameof(ForbidPrefix));
        Patch(harmony, monitor, "Object.canBeShipped",
            () => AccessTools.Method(typeof(SObject), "canBeShipped"),
            prefix: nameof(ForbidPrefix));
        Patch(harmony, monitor, "Item.canBeTrashed",
            () => AccessTools.Method(typeof(Item), "canBeTrashed"),
            prefix: nameof(ForbidPrefix));
        Patch(harmony, monitor, "Object.canBeGivenAsGift",
            () => AccessTools.Method(typeof(SObject), "canBeGivenAsGift"),
            prefix: nameof(ForbidPrefix));
        Patch(harmony, monitor, "Object.drawInMenu",
            () => AccessTools.Method(typeof(SObject), "drawInMenu", new[] { typeof(SpriteBatch), typeof(Vector2), typeof(float), typeof(float), typeof(float), typeof(StackDrawType), typeof(Color), typeof(bool) }),
            prefix: nameof(DrawInMenuPrefix));
        // Placeholders move only from Terradew's own menu, never by the vanilla click handlers.
        Patch(harmony, monitor, "InventoryMenu.leftClick",
            () => AccessTools.Method(typeof(InventoryMenu), "leftClick", new[] { typeof(int), typeof(int), typeof(Item), typeof(bool) }),
            prefix: nameof(LeftClickPrefix));
        Patch(harmony, monitor, "InventoryMenu.rightClick",
            () => AccessTools.Method(typeof(InventoryMenu), "rightClick", new[] { typeof(int), typeof(int), typeof(Item), typeof(bool), typeof(bool) }),
            prefix: nameof(RightClickPrefix));
        // A vanilla player-inventory menu keeps its 12x3 layout; Terradew's own menu passes its capacity explicitly.
        Patch(harmony, monitor, "InventoryMenu constructor",
            () => AccessTools.Constructor(typeof(InventoryMenu), new[]
            {
                typeof(int), typeof(int), typeof(bool), typeof(IList<Item>), typeof(InventoryMenu.highlightThisItem),
                typeof(int), typeof(int), typeof(int), typeof(int), typeof(bool),
            }),
            prefix: nameof(InventoryMenuConstructorPrefix));
        // Sorting would scramble the shared backpack.
        Patch(harmony, monitor, "ItemGrabMenu.organizeItemsInList",
            () => AccessTools.Method(typeof(ItemGrabMenu), "organizeItemsInList", new[] { typeof(IList<Item>) }),
            prefix: nameof(OrganizeItemsPrefix));
        // Shifting the toolbar rotates the first twelve backpack slots.
        Patch(harmony, monitor, "Farmer.shiftToolbar",
            () => AccessTools.Method(typeof(Farmer), "shiftToolbar", new[] { typeof(bool) }),
            prefix: nameof(ShiftToolbarPrefix));
    }

    /// <summary>Installs one patch. A patch that cannot be installed is logged, and the rest still install.</summary>
    private static void Patch(Harmony harmony, IMonitor monitor, string what, Func<MethodBase?> find, string? prefix = null, string? postfix = null)
    {
        try
        {
            MethodBase? target = find();
            if (target == null)
            {
                monitor.Log($"Terradew could not find {what}; placeholders are not guarded there.", LogLevel.Warn);
                return;
            }
            harmony.Patch(
                target,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(BackpackGuards), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(BackpackGuards), postfix));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not patch {what}; placeholders are not guarded there. {error.Message}", LogLevel.Warn);
        }
    }

    private static void ActiveObjectPostfix(ref SObject? __result)
    {
        if (Backpack.IsPlaceholder(__result))
            __result = null;
    }

    /// <summary>A placeholder can never be placed, shipped, trashed or given: the answer is no, and the original is skipped.</summary>
    private static bool ForbidPrefix(Item __instance, ref bool __result)
    {
        if (!Backpack.IsPlaceholder(__instance))
            return true;
        __result = false;
        return false;
    }

    private static bool DrawInMenuPrefix(Item __instance, SpriteBatch spriteBatch, Vector2 location, float scaleSize, float transparency, float layerDepth, Color color)
    {
        if (!Backpack.IsPlaceholder(__instance))
            return true;
        ItemStack stack = Backpack.ReadPlaceholder(__instance);
        float box = 64f * scaleSize;
        Vector2 center = location + new Vector2(32f, 32f);
        Texture2D? icon = sprites?.Load(SpriteNames.Item(stack.ItemType));
        if (icon != null)
        {
            float scale = box / Math.Max(icon.Width, icon.Height);
            spriteBatch.Draw(icon, center, null, Color.White * transparency, 0f, new Vector2(icon.Width / 2f, icon.Height / 2f), scale, SpriteEffects.None, layerDepth);
        }
        else
        {
            // Same fallback as Hud.DrawItem: the first four letters, until the sprite is exported.
            string name = stack.Name ?? "";
            string text = name.Length > 4 ? name[..4] : name;
            spriteBatch.DrawString(Game1.tinyFont, text, new Vector2(center.X - box / 2f, center.Y - 8f), Color.White * transparency);
        }
        if (stack.Stack > 1)
        {
            int adjust = stack.Stack >= 100 ? 12 : stack.Stack >= 10 ? 6 : 0;
            Utility.drawTinyDigits(stack.Stack, spriteBatch, new Vector2(center.X + box / 2f - 12 - adjust, center.Y + box / 2f - 12), 2f * scaleSize, layerDepth, Color.White * transparency);
        }
        return false;
    }

    private static bool LeftClickPrefix(InventoryMenu __instance, int x, int y, Item toPlace, ref Item? __result)
    {
        if (!Blocked(__instance, x, y, toPlace))
            return true;
        __result = toPlace;
        return false;
    }

    private static bool RightClickPrefix(InventoryMenu __instance, int x, int y, Item toAddTo, ref Item? __result)
    {
        if (!Blocked(__instance, x, y, toAddTo))
            return true;
        __result = toAddTo;
        return false;
    }

    /// <summary>A placeholder in hand, or a placeholder in the player's backpack under the click, is never moved by a vanilla menu.</summary>
    private static bool Blocked(InventoryMenu menu, int x, int y, Item? held)
        => Backpack.IsPlaceholder(held) || SlotHoldsPlaceholder(menu, x, y);

    private static bool SlotHoldsPlaceholder(InventoryMenu menu, int x, int y)
    {
        if (!ReferenceEquals(menu.actualInventory, Game1.player.Items))
            return false;
        foreach (ClickableComponent component in menu.inventory)
        {
            if (!component.containsPoint(x, y))
                continue;
            if (!int.TryParse(component.name, out int slot))
                return false;
            return slot >= 0 && slot < Game1.player.Items.Count && Backpack.IsPlaceholder(Game1.player.Items[slot]);
        }
        return false;
    }

    /// <summary>The player's backpack shows all 50 slots by default. Chests, shops and other menus keep their 12x3 view.</summary>
    private static void InventoryMenuConstructorPrefix(IList<Item>? actualInventory, ref int capacity)
    {
        if (capacity != -1)
            return;
        if (actualInventory == null || (Game1.player != null && ReferenceEquals(actualInventory, Game1.player.Items)))
            capacity = 36;
    }

    private static bool OrganizeItemsPrefix(IList<Item> items)
        => Game1.player == null || !ReferenceEquals(items, Game1.player.Items);

    private static bool ShiftToolbarPrefix() => false;
}
