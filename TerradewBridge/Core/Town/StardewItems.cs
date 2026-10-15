using System;
using System.Collections.Generic;

namespace Terradew.Core.Town
{
    /// <summary>Stardew object ids and categories that gift tables may name. Kept to ids that are certain.</summary>
    public static class StardewItems
    {
        public static IReadOnlyDictionary<string, string> Objects { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["16"] = "Wild Horseradish", ["18"] = "Daffodil", ["20"] = "Leek", ["22"] = "Dandelion",
            ["60"] = "Emerald", ["62"] = "Aquamarine", ["64"] = "Ruby", ["66"] = "Amethyst", ["68"] = "Topaz", ["70"] = "Jade",
            ["72"] = "Diamond", ["74"] = "Prismatic Shard", ["78"] = "Cave Carrot", ["80"] = "Quartz", ["82"] = "Fire Quartz",
            ["84"] = "Frozen Tear", ["86"] = "Earth Crystal", ["88"] = "Coconut", ["90"] = "Cactus Fruit", ["92"] = "Sap",
            ["128"] = "Pufferfish", ["143"] = "Catfish", ["149"] = "Octopus", ["151"] = "Squid", ["152"] = "Seaweed",
            ["154"] = "Sea Cucumber", ["155"] = "Super Cucumber", ["162"] = "Lava Eel", ["168"] = "Trash",
            ["196"] = "Salad", ["200"] = "Vegetable Medley", ["201"] = "Complete Breakfast", ["202"] = "Fried Calamari",
            ["206"] = "Pizza", ["210"] = "Hashbrowns", ["211"] = "Pancakes", ["213"] = "Fish Taco", ["214"] = "Crispy Bass",
            ["220"] = "Chocolate Cake", ["221"] = "Pink Cake", ["223"] = "Cookie", ["226"] = "Spicy Eel", ["228"] = "Maki Roll",
            ["241"] = "Survival Burger", ["257"] = "Morel", ["281"] = "Chanterelle", ["286"] = "Cherry Bomb", ["287"] = "Bomb",
            ["288"] = "Mega Bomb", ["296"] = "Salmonberry", ["303"] = "Pale Ale", ["305"] = "Void Egg", ["330"] = "Clay",
            ["334"] = "Copper Bar", ["335"] = "Iron Bar", ["336"] = "Gold Bar", ["337"] = "Iridium Bar", ["340"] = "Honey",
            ["342"] = "Pickles", ["344"] = "Jelly", ["346"] = "Beer", ["348"] = "Wine", ["349"] = "Energy Tonic",
            ["373"] = "Golden Pumpkin", ["376"] = "Poppy", ["378"] = "Copper Ore", ["380"] = "Iron Ore", ["382"] = "Coal",
            ["384"] = "Gold Ore", ["386"] = "Iridium Ore", ["388"] = "Wood", ["390"] = "Stone", ["392"] = "Nautilus Shell",
            ["393"] = "Coral", ["395"] = "Coffee", ["397"] = "Sea Urchin", ["402"] = "Sweet Pea", ["404"] = "Common Mushroom",
            ["410"] = "Blackberry", ["414"] = "Crystal Fruit", ["418"] = "Crocus", ["420"] = "Red Mushroom", ["421"] = "Sunflower",
            ["422"] = "Purple Mushroom", ["424"] = "Cheese", ["426"] = "Goat Cheese", ["430"] = "Truffle", ["432"] = "Truffle Oil",
            ["446"] = "Rabbit's Foot", ["459"] = "Mead", ["591"] = "Tulip", ["593"] = "Summer Spangle", ["595"] = "Fairy Rose",
            ["597"] = "Blue Jazz", ["724"] = "Maple Syrup", ["725"] = "Oak Resin", ["726"] = "Pine Tar", ["766"] = "Slime",
            ["767"] = "Bat Wing", ["768"] = "Solar Essence", ["769"] = "Void Essence", ["773"] = "Life Elixir", ["797"] = "Pearl",
        };

        public static IReadOnlyDictionary<string, string> Categories { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["-2"] = "gems", ["-4"] = "fish", ["-5"] = "eggs", ["-6"] = "milk", ["-7"] = "cooked dishes", ["-8"] = "crafted items",
            ["-12"] = "minerals", ["-15"] = "metal bars and ores", ["-16"] = "building materials", ["-26"] = "artisan goods",
            ["-27"] = "tree syrups", ["-28"] = "monster loot", ["-74"] = "seeds", ["-75"] = "vegetables", ["-79"] = "fruit",
            ["-80"] = "flowers", ["-81"] = "forage",
        };

        public static bool IsKnown(string id) => id != null && (Objects.ContainsKey(id) || Categories.ContainsKey(id));
    }
}
