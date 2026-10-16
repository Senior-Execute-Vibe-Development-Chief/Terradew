using StardewValley;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>
/// Sends Stardew's weather at the farmer's location to Terraria, whose rain and wind then follow it. Sent when the
/// weather changes, and once a second so a Terraria that reconnects gets it too.
/// </summary>
internal sealed class WeatherFeed
{
    /// <summary>Stardew ticks between repeats of the same weather.</summary>
    private const int RepeatTicks = 60;

    private WeatherFlags? last;
    private int ticksSinceSent;

    public void Update(Bridge bridge, GameLocation location)
    {
        if (!bridge.IsConnected)
            return;
        WeatherFlags flags = Read(location);
        ticksSinceSent++;
        if (flags == last && ticksSinceSent < RepeatTicks)
            return;
        if (!bridge.Send(new WeatherState { Flags = flags }))
            return;
        last = flags;
        ticksSinceSent = 0;
    }

    private static WeatherFlags Read(GameLocation location)
    {
        WeatherFlags flags = WeatherFlags.None;
        if (location.IsRainingHere())
            flags |= WeatherFlags.Rain;
        if (location.IsLightningHere())
            flags |= WeatherFlags.Lightning;
        if (location.IsSnowingHere())
            flags |= WeatherFlags.Snow;
        if (location.IsDebrisWeatherHere())
            flags |= WeatherFlags.Debris;
        return flags;
    }
}
