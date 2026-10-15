using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

namespace Terradew;

/// <summary>
/// No passing out. Stardew makes the farmer pass out at 2:00 AM (clock time 2600): the day ends in a faint,
/// the farmer wakes at home and loses money. Terradew keeps the day ending but as a bedtime instead: no
/// money is lost, there is no letter, and the farmer wakes at home as usual.
/// </summary>
internal sealed class Clock
{
    /// <summary>Stardew's clock reads 2600 at 2:00 AM.</summary>
    public const int PassOutTime = 2600;

    /// <summary>Stardew's pass-out entry points, most likely first. Only the first one found is patched.</summary>
    private static readonly string[] PassOutMethods = { "passOutFromTired", "startToPassOut" };

    /// <summary>True from the bedtime until the new day starts, so a second pass-out in between is skipped.</summary>
    private static bool dayEnding;

    public void Install(Harmony harmony, IMonitor monitor)
    {
        foreach (string name in PassOutMethods)
        {
            try
            {
                MethodInfo? target = AccessTools.Method(typeof(Farmer), name);
                if (target == null)
                    continue;
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(Clock), nameof(BeforePassOut)));
                monitor.Log($"Terradew patched Farmer.{name}: the farmer goes to bed at 2:00 AM instead of passing out.", LogLevel.Trace);
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

    /// <summary>Called when a new day starts. The bedtime is over, so the next pass-out may run again.</summary>
    public static void OnDayStarted() => dayEnding = false;

    /// <summary>
    /// Runs before Stardew's pass-out. Returning false skips it. Only the 2:00 AM case is taken over;
    /// anything earlier (for example running out of energy) keeps Stardew's own behaviour.
    /// </summary>
    private static bool BeforePassOut(Farmer __instance)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer || __instance != Game1.player)
            return true;
        // The day is already ending: NewDay is queued, so skip the pass-out rather than end the day twice.
        if (dayEnding)
            return false;
        if (Game1.timeOfDay < PassOutTime || Game1.eventUp)
            return true;
        try
        {
            // Go to bed where you stand: the same day change a normal bedtime makes, with no money penalty
            // and no pass-out letter, so the farmer wakes at home.
            Game1.player.isInBed.Value = true;
            Game1.NewDay(0f);
            dayEnding = true;
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }
}
