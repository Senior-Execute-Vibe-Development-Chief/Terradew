using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using Terradew.Core.Time;

namespace Terradew;

/// <summary>
/// No passing out. Stardew stops its clock at 2:00 AM and makes the farmer faint; Terradew keeps the clock running
/// through the night instead, so Terraria gets its whole night (dusk at 7:30 PM, dawn at 4:30 AM). A farmer who never
/// goes to bed stays up until 6:00 AM: the day changes then (crops grow, shipping is paid, the game saves) and the farmer
/// carries on from where they stood, with no money lost and no letter. Going to bed still ends the day as usual.
/// </summary>
internal sealed class Clock
{
    /// <summary>Stardew's pass-out entry points, most likely first. Only the first one found is patched.</summary>
    private static readonly string[] PassOutMethods = { "passOutFromTired", "startToPassOut" };

    /// <summary>Stardew's flag that makes the farmer faint on the next update, when the game has it.</summary>
    private static readonly FieldInfo? ShouldPassOut = AccessTools.Field(typeof(Game1), "farmerShouldPassOut");

    /// <summary>True from 6:00 AM until the new day starts, so the day is not ended twice.</summary>
    private static bool dayEnding;

    /// <summary>Where the farmer stood when the morning came without sleep, to put them back after the day changes.</summary>
    private static (string Location, Point Tile, int Facing)? stayedUp;

    public void Install(Harmony harmony, IMonitor monitor)
    {
        try
        {
            harmony.Patch(
                AccessTools.Method(typeof(Game1), "performTenMinuteClockUpdate"),
                prefix: new HarmonyMethod(typeof(Clock), nameof(BeforeTenMinutes)));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not patch Game1.performTenMinuteClockUpdate; the clock may stop at 2:00 AM. {error.Message}", LogLevel.Warn);
        }

        foreach (string name in PassOutMethods)
        {
            try
            {
                MethodInfo? target = AccessTools.Method(typeof(Farmer), name);
                if (target == null)
                    continue;
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(Clock), nameof(BeforePassOut)));
                return;
            }
            catch (Exception error)
            {
                monitor.Log($"Terradew could not patch Farmer.{name}; the farmer may still pass out at 2:00 AM. {error.Message}", LogLevel.Warn);
                return;
            }
        }
        monitor.Log("Terradew could not find Stardew's pass-out method; the farmer may still pass out at 2:00 AM.", LogLevel.Warn);
    }

    /// <summary>The farm closed: nothing carries over to the next one loaded.</summary>
    public static void Reset()
    {
        dayEnding = false;
        stayedUp = null;
    }

    /// <summary>Called when a new day starts: a farmer who stayed up goes back to where they were standing.</summary>
    public static void OnDayStarted()
    {
        dayEnding = false;
        if (stayedUp is not { } spot)
            return;
        stayedUp = null;
        if (Game1.getLocationFromName(spot.Location) != null || spot.Location.StartsWith("UndergroundMine"))
            Game1.warpFarmer(spot.Location, spot.Tile.X, spot.Tile.Y, spot.Facing);
    }

    /// <summary>
    /// From 2:00 AM Stardew's own ten-minute update would hold the clock and faint the farmer, so Terradew moves the
    /// clock itself, and at 6:00 AM starts the new day. Returning false skips Stardew's update.
    /// </summary>
    private static bool BeforeTenMinutes()
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer || Game1.timeOfDay < ClockMap.StardewPassOut)
            return true;
        if (dayEnding)
            return false;
        ShouldPassOut?.SetValue(null, false);
        Game1.timeOfDay = ClockMap.StardewNext(Game1.timeOfDay);
        if (Game1.timeOfDay >= ClockMap.StardewMorning && !Game1.eventUp)
            Morning();
        return false;
    }

    /// <summary>Between 2:00 AM and the morning nobody faints. Returning false skips Stardew's pass-out.</summary>
    private static bool BeforePassOut(Farmer __instance)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer || __instance != Game1.player)
            return true;
        if (dayEnding || Game1.timeOfDay >= ClockMap.StardewPassOut)
        {
            ShouldPassOut?.SetValue(null, false);
            return false;
        }
        return true;
    }

    /// <summary>
    /// The farmer stayed up all night: the same day change a bedtime makes, with no money penalty and no letter.
    /// </summary>
    private static void Morning()
    {
        Farmer player = Game1.player;
        try
        {
            dayEnding = true;
            if (player.currentLocation != null)
                stayedUp = (player.currentLocation.NameOrUniqueName, player.TilePoint, player.FacingDirection);
            player.isInBed.Value = true;
            Game1.NewDay(0f);
        }
        catch (Exception)
        {
            dayEnding = false;
            stayedUp = null;
        }
    }
}
