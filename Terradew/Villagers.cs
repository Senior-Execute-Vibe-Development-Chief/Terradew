using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Shops;
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
                        // The owner keeps its name, which is how the shop finds the villager. Only the lines go.
                        if (TownHomes.NpcOf(owner.Name) == null)
                            continue;
                        owner.Dialogues = null;
                        owner.Portrait = null;
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
