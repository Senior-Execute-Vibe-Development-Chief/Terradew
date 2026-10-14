using System;
using System.Collections.Generic;
using Terradew.Core.Protocol;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerradewBridge;

/// <summary>
/// Crafting with stations carried in the inventory: owning a Work Bench, Furnace or Anvil means
/// you can craft at it, since there is nowhere to place one on the Stardew map.
/// </summary>
public static class Crafting
{
    private static readonly Dictionary<int, int[]> Aliases = new()
    {
        { TileID.Hellforge, new[] { TileID.Furnaces } },
        { TileID.AdamantiteForge, new[] { TileID.Furnaces, TileID.Hellforge } },
        { TileID.MythrilAnvil, new[] { TileID.Anvils } },
        { TileID.AlchemyTable, new[] { TileID.Bottles } },
        { TileID.TinkerersWorkbench, new[] { TileID.WorkBenches } },
        { TileID.HeavyWorkBench, new[] { TileID.WorkBenches } },
    };

    private static void Prepare(Player player)
    {
        Array.Clear(player.adjTile);
        foreach (Item station in Inventory.Stations(player))
        {
            Mark(player, station.createTile);
            if (Aliases.TryGetValue(station.createTile, out int[]? more))
            {
                foreach (int tile in more)
                    Mark(player, tile);
            }
        }
        player.adjWater = true;
        Recipe.FindRecipes();
    }

    private static void Mark(Player player, int tile)
    {
        if (tile >= 0 && tile < player.adjTile.Length)
            player.adjTile[tile] = true;
    }

    public static RecipeList List(Player player)
    {
        Prepare(player);
        var list = new RecipeList();
        var stations = new List<string>();
        foreach (Item station in Inventory.Stations(player))
        {
            if (!stations.Contains(station.Name))
                stations.Add(station.Name);
        }
        list.Stations = stations.Count == 0 ? "No crafting stations. Craft a Work Bench from 10 Wood." : "Stations: " + string.Join(", ", stations);
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
        for (int time = 0; time < Math.Clamp(craft.Times, 1, 99); time++)
        {
            Prepare(player);
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
                return;
            Recipe recipe = Main.recipe[craft.RecipeIndex];
            foreach (Item ingredient in recipe.requiredItem)
            {
                if (ingredient == null || ingredient.IsAir)
                    continue;
                Consume(player, recipe, ingredient.type, ingredient.stack);
            }
            Inventory.Give(player, recipe.createItem.type, recipe.createItem.stack, recipe.createItem.prefix);
        }
        Inventory.ForceResend();
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
