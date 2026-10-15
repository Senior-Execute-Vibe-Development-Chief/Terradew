using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Shops;
using StardewValley.Menus;
using Terradew.Core;
using Terradew.Core.Protocol;
using Terradew.Core.Town;

namespace Terradew;

/// <summary>
/// Stardew villagers are gone from view: their homes now belong to Terraria town NPCs. They still walk their
/// schedules, so shops that name them keep working, but they are not drawn, cannot be talked to, and do not
/// block the farmer. Their shop lines are dropped so no villager speaks.
/// </summary>
internal sealed class Villagers
{
    private static Villagers? instance;
    private IMonitor? monitor;

    public void Install(Harmony harmony, IMonitor monitor)
    {
        this.monitor = monitor;
        instance = this;
        try
        {
            var draw = AccessTools.Method(typeof(NPC), "draw", new[] { typeof(SpriteBatch), typeof(float) });
            if (draw == null)
            {
                monitor.Log("Terradew could not find NPC.draw; villagers stay visible.", LogLevel.Warn);
            }
            else
            {
                harmony.Patch(draw, prefix: new HarmonyMethod(typeof(Villagers), nameof(BeforeDraw)));
            }
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not hide villagers when drawn; they stay visible. {error.Message}", LogLevel.Warn);
        }
        try
        {
            // Speech text is drawn in this later pass, so it has to be skipped too.
            var above = AccessTools.Method(typeof(NPC), "drawAboveAlwaysFrontLayer", new[] { typeof(SpriteBatch) });
            if (above != null)
                harmony.Patch(above, prefix: new HarmonyMethod(typeof(Villagers), nameof(BeforeDrawAbove)));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not hide villager speech; it may still show. {error.Message}", LogLevel.Warn);
        }
        try
        {
            var action = AccessTools.Method(typeof(NPC), "checkAction", new[] { typeof(Farmer), typeof(GameLocation) });
            if (action == null)
            {
                monitor.Log("Terradew could not find NPC.checkAction; villagers can still be talked to.", LogLevel.Warn);
            }
            else
            {
                harmony.Patch(action, prefix: new HarmonyMethod(typeof(Villagers), nameof(BeforeCheckAction)));
            }
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not stop talking to villagers. {error.Message}", LogLevel.Warn);
        }
        try
        {
            // The help-wanted board picks a villager each day. Terradew has none, so the board stays empty.
            var refresh = AccessTools.Method(typeof(Game1), "RefreshQuestOfTheDay");
            if (refresh == null)
                monitor.Log("Terradew could not find Game1.RefreshQuestOfTheDay; the board may ask for villagers.", LogLevel.Warn);
            else
                harmony.Patch(refresh, postfix: new HarmonyMethod(typeof(Villagers), nameof(AfterRefreshQuest)));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not clear the help-wanted board. {error.Message}", LogLevel.Warn);
        }
    }

    /// <summary>Shop ids the villager who runs each one. Used to find the Terraria resident who replaced them.</summary>
    private static readonly Dictionary<string, string> ShopOwners = new(StringComparer.Ordinal)
    {
        ["SeedShop"] = "Pierre",
        ["Blacksmith"] = "Clint",
        ["Carpenter"] = "Robin",
        ["AnimalShop"] = "Marnie",
        ["FishShop"] = "Willy",
        ["Saloon"] = "Gus",
        ["AdventureShop"] = "Marlon",
        ["Hospital"] = "Harvey",
        ["Sandy"] = "Sandy",
        ["ShadowShop"] = "Krobus",
    };

    /// <summary>Shop menus show the Terraria resident's head in place of the villager's portrait.</summary>
    public void OnMenuChanged(IClickableMenu? menu, Bridge bridge, SpriteBook sprites)
    {
        try
        {
            if (menu is not ShopMenu shop || shop.portraitTexture == null)
                return;
            string? shopId = shop.ShopId;
            if (shopId == null || !ShopOwners.TryGetValue(shopId, out string? villager))
                return;
            string? npc = TownHomes.NpcOf(villager);
            if (npc == null)
                return;
            // With nobody living there yet, no villager face shows.
            shop.portraitTexture = HeadOf(npc, bridge, sprites);
        }
        catch (Exception error)
        {
            monitor?.Log($"Terradew could not show the town resident's portrait. {error.Message}", LogLevel.Warn);
        }
    }

    private static Texture2D? HeadOf(string npc, Bridge bridge, SpriteBook sprites)
    {
        int? type = TownHomes.TypeOf(npc);
        WorldFacts? facts = bridge.Facts;
        if (type == null || facts == null)
            return null;
        foreach (TownResident resident in facts.Residents)
        {
            if (resident.NpcType != type.Value || string.IsNullOrEmpty(resident.HomePlaceKey) || resident.HeadIndex < 0)
                continue;
            return sprites.Load(SpriteNames.NpcHead(resident.HeadIndex));
        }
        return null;
    }

    private static void AfterRefreshQuest()
    {
        try
        {
            Game1.questOfTheDay = null;
        }
        catch (Exception error)
        {
            instance?.monitor?.Log($"Terradew could not clear the help-wanted board. {error.Message}", LogLevel.Warn);
        }
    }

    /// <summary>Marks villagers so the farmer walks through them. Null means every location.</summary>
    public void Refresh(GameLocation? location)
    {
        try
        {
            if (location != null)
            {
                MarkVillagers(location.characters);
                return;
            }
            foreach (GameLocation each in Game1.locations)
                MarkVillagers(each.characters);
        }
        catch (Exception error)
        {
            monitor?.Log($"Terradew could not let the farmer pass villagers. {error.Message}", LogLevel.Warn);
        }
    }

    private static void MarkVillagers(IEnumerable<NPC> npcs)
    {
        foreach (NPC npc in npcs)
        {
            if (npc.IsVillager)
                npc.farmerPassesThrough = true;
        }
    }

    public void OnAssetRequested(AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo("Data/Shops"))
            return;
        e.Edit(asset =>
        {
            try
            {
                var shops = asset.AsDictionary<string, ShopData>().Data;
                foreach (ShopData shop in shops.Values)
                {
                    if (shop.Owners == null)
                        continue;
                    foreach (ShopOwnerData owner in shop.Owners)
                    {
                        // The owner keeps its name, which is how the shop finds the villager. Only the lines go; the portrait is swapped for the resident's when the menu opens.
                        if (TownHomes.NpcOf(owner.Name) == null)
                            continue;
                        // An empty list silences the owner; leaving it unset shows a generic greeting.
                        owner.Dialogues = new();
                    }
                }
            }
            catch (Exception error)
            {
                instance?.monitor?.Log($"Terradew could not quiet villager shop lines. {error.Message}", LogLevel.Warn);
            }
        });
    }

    private static bool BeforeDraw(NPC __instance)
    {
        try
        {
            return !__instance.IsVillager;
        }
        catch (Exception error)
        {
            instance?.monitor?.Log($"Terradew could not check a villager before drawing. {error.Message}", LogLevel.Warn);
            return true;
        }
    }

    private static bool BeforeDrawAbove(NPC __instance)
    {
        try
        {
            return !__instance.IsVillager;
        }
        catch (Exception error)
        {
            instance?.monitor?.Log($"Terradew could not check a villager's speech. {error.Message}", LogLevel.Warn);
            return true;
        }
    }

    private static bool BeforeCheckAction(NPC __instance, ref bool __result)
    {
        try
        {
            if (!__instance.IsVillager)
                return true;
            __result = false;
            return false;
        }
        catch (Exception error)
        {
            instance?.monitor?.Log($"Terradew could not check a villager's action. {error.Message}", LogLevel.Warn);
            return true;
        }
    }
}
