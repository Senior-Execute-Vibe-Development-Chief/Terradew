using System;
using System.Collections.Generic;
using Terradew.Core.Protocol;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace TerradewBridge;

/// <summary>
/// Crafting at stations placed on the Stardew map. The farmer puts a station down on a tile, Stardew reports
/// which stations are near the farmer (and whether water is near), and Terraria marks those tiles on the
/// player so Recipe.FindRecipes offers the matching recipes. Placing takes the station out of the inventory.
/// </summary>
public static class Crafting
{
    internal static readonly Dictionary<int, int[]> Aliases = new()
    {
        { TileID.Hellforge, new int[] { TileID.Furnaces } },
        { TileID.AdamantiteForge, new int[] { TileID.Furnaces, TileID.Hellforge } },
        { TileID.MythrilAnvil, new int[] { TileID.Anvils } },
        { TileID.AlchemyTable, new int[] { TileID.Bottles } },
        { TileID.TinkerersWorkbench, new int[] { TileID.WorkBenches } },
        { TileID.HeavyWorkBench, new int[] { TileID.WorkBenches } },
    };

    private static void Prepare(Player player, StationsNearby near)
    {
        Array.Clear(player.adjTile, 0, player.adjTile.Length);
        foreach (int type in near.ItemTypes)
        {
            if (!IsValidType(type))
                continue;
            int tile = ContentSamples.ItemsByType[type].createTile;
            Mark(player, tile);
            if (Aliases.TryGetValue(tile, out int[]? more))
            {
                foreach (int alias in more)
                    Mark(player, alias);
            }
        }
        player.adjWater = near.Water;
        Recipe.FindRecipes();
    }

    private static bool IsValidType(int type) => type > 0 && type < ItemLoader.ItemCount;

    private static void Mark(Player player, int tile)
    {
        if (tile >= 0 && tile < player.adjTile.Length)
            player.adjTile[tile] = true;
    }

    public static RecipeList List(Player player, StationsNearby near)
    {
        Prepare(player, near);
        var list = new RecipeList();
        var stations = new List<string>();
        foreach (int type in near.ItemTypes)
        {
            if (!IsValidType(type))
                continue;
            string name = Lang.GetItemNameValue(type);
            if (!stations.Contains(name))
                stations.Add(name);
        }
        list.Stations = stations.Count == 0
            ? "No crafting station nearby. Craft a Work Bench from 10 Wood and place it."
            : "Stations nearby: " + string.Join(", ", stations);
        for (int i = 0; i < Main.numAvailableRecipes && i < Main.availableRecipe.Length; i++)
        {
            int index = Main.availableRecipe[i];
            Recipe recipe = Main.recipe[index];
            if (recipe.createItem == null || recipe.createItem.IsAir)
                continue;
            var entry = new RecipeEntry
            {
                RecipeIndex = index,
                Result = Inventory.Describe(recipe.createItem),
                Ingredients = new List<ItemStack>(),
                CanCraft = true,
            };
            foreach (Item ingredient in recipe.requiredItem)
            {
                if (ingredient != null && !ingredient.IsAir)
                    entry.Ingredients.Add(Inventory.Describe(ingredient));
            }
            list.Recipes.Add(entry);
        }
        return list;
    }

    public static void Make(Player player, Craft craft)
    {
        Prepare(player, craft.Near);
        for (int time = 0; time < Math.Clamp(craft.Times, 1, 99); time++)
        {
            bool available = false;
            for (int i = 0; i < Main.numAvailableRecipes; i++)
            {
                if (Main.availableRecipe[i] == craft.RecipeIndex)
                {
                    available = true;
                    break;
                }
            }
            if (!available || craft.RecipeIndex < 0 || craft.RecipeIndex >= Recipe.numRecipes)
                break;
            Recipe recipe = Main.recipe[craft.RecipeIndex];
            if (!Holds(player, recipe))
                break;
            foreach (Item ingredient in recipe.requiredItem)
            {
                if (ingredient == null || ingredient.IsAir)
                    continue;
                Consume(player, recipe, ingredient.type, ingredient.stack);
            }
            Inventory.Give(player, recipe.createItem.type, recipe.createItem.stack, recipe.createItem.prefix);
        }
        Recipe.FindRecipes();
        Inventory.ForceResend();
    }

    /// <summary>
    /// Takes one station out of the inventory for placing it on the map. Uses the slot Stardew named when it still
    /// holds that station, otherwise the first stack of that type that is a station.
    /// </summary>
    public static bool Take(Player player, PlaceStation place)
    {
        if (place.Slot >= 0 && place.Slot < player.inventory.Length)
        {
            Item named = player.inventory[place.Slot];
            if (named.type == place.ItemType && Inventory.IsStation(named))
            {
                TakeOne(named);
                return true;
            }
        }
        for (int i = 0; i < player.inventory.Length; i++)
        {
            Item item = player.inventory[i];
            if (item.type == place.ItemType && Inventory.IsStation(item))
            {
                TakeOne(item);
                return true;
            }
        }
        return false;
    }

    private static void TakeOne(Item item)
    {
        item.stack--;
        if (item.stack <= 0)
            item.TurnToAir();
    }

    /// <summary>Whether the inventory still holds every ingredient, since the recipe list is not rebuilt between crafts.</summary>
    private static bool Holds(Player player, Recipe recipe)
    {
        foreach (Item ingredient in recipe.requiredItem)
        {
            if (ingredient == null || ingredient.IsAir)
                continue;
            int need = ingredient.stack;
            for (int i = 0; i < 58 && need > 0; i++)
            {
                Item item = player.inventory[i];
                if (!item.IsAir && Accepts(recipe, ingredient.type, item.type))
                    need -= item.stack;
            }
            if (need > 0)
                return false;
        }
        return true;
    }

    private static void Consume(Player player, Recipe recipe, int type, int need)
    {
        for (int i = 0; i < 58 && need > 0; i++)
        {
            Item item = player.inventory[i];
            if (item.IsAir || !Accepts(recipe, type, item.type))
                continue;
            int take = Math.Min(need, item.stack);
            item.stack -= take;
            need -= take;
            if (item.stack <= 0)
                item.TurnToAir();
        }
    }

    private static bool Accepts(Recipe recipe, int wanted, int have)
    {
        if (wanted == have)
            return true;
        foreach (int group in recipe.acceptedGroups)
        {
            if (RecipeGroup.recipeGroups.TryGetValue(group, out RecipeGroup? set) && set.ContainsItem(wanted) && set.ContainsItem(have))
                return true;
        }
        return false;
    }
}
