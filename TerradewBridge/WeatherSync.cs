using Terradew.Core.Protocol;
using Terradew.Core.World;
using Terraria;

namespace TerradewBridge;

/// <summary>
/// Puts Terraria's rain and wind on Stardew's weather while Terradew is linked. Terraria starts and stops rain on its
/// own, so while linked the bridge drops those calls and only its own <see cref="Apply"/> changes the rain.
/// </summary>
internal static class WeatherSync
{
    /// <summary>Set while the bridge itself starts or stops rain, so its own calls get through the hooks.</summary>
    private static bool applying;

    /// <summary>Hooks Main.StartRain and Main.StopRain. Call once from the mod's Load.</summary>
    public static void Load()
    {
        On_Main.StartRain += StartRainHook;
        On_Main.StopRain += StopRainHook;
    }

    /// <summary>Removes the hooks when the mod unloads.</summary>
    public static void Unload()
    {
        On_Main.StartRain -= StartRainHook;
        On_Main.StopRain -= StopRainHook;
    }

    /// <summary>Makes Terraria's rain and wind match Stardew's weather. Called every bridge tick while linked.</summary>
    public static void Apply(WeatherState? state)
    {
        if (state == null)
            return;
        (bool raining, float strength, float wind) = WeatherMap.ToTerraria(state.Flags);
        applying = true;
        try
        {
            if (raining)
            {
                if (!Main.raining)
                    Main.StartRain(instant: true, strengthOverride: strength);
                Main.maxRaining = strength;
            }
            else if (Main.raining)
            {
                Main.StopRain();
            }
            if (wind > 0f)
                Main.windSpeedTarget = wind;
        }
        finally
        {
            applying = false;
        }
    }

    /// <summary>True while the bridge is linked in the Terradew world. Terraria's own rain calls are then dropped.</summary>
    private static bool Holding() => BridgeSystem.Active && BridgeSystem.Instance.Weather != null;

    private static void StartRainHook(On_Main.orig_StartRain orig, bool instant, float? strengthOverride, bool guaranteeCoinRain)
    {
        if (!applying && Holding())
            return;
        orig(instant, strengthOverride, guaranteeCoinRain);
    }

    private static void StopRainHook(On_Main.orig_StopRain orig, bool instant)
    {
        if (!applying && Holding())
            return;
        orig(instant);
    }
}
