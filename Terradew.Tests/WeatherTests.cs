using Terradew.Core.Protocol;
using Terradew.Core.World;

/// <summary>Tests for WeatherMap and the WeatherState message.</summary>
internal static class WeatherTests
{
    public static int Run()
    {
        int failed = 0;

        failed += T.Check("weather: clear weather stops rain and leaves wind alone", () =>
        {
            var (raining, strength, wind) = WeatherMap.ToTerraria(WeatherFlags.None);
            T.Assert(!raining && strength == 0f && wind == 0f, "clear is no rain, no wind");
        });

        failed += T.Check("weather: rain and snow both make rain, storms are heavier", () =>
        {
            var (rain, rainStrength, _) = WeatherMap.ToTerraria(WeatherFlags.Rain);
            T.Assert(rain && rainStrength == WeatherMap.RainStrength, "rain");
            var (snow, snowStrength, _) = WeatherMap.ToTerraria(WeatherFlags.Snow);
            T.Assert(snow && snowStrength == WeatherMap.RainStrength, "snow is rain for Terraria");
            var (storm, stormStrength, _) = WeatherMap.ToTerraria(WeatherFlags.Rain | WeatherFlags.Lightning);
            T.Assert(storm && stormStrength == WeatherMap.StormStrength && stormStrength > rainStrength, "storm is heavier rain");
        });

        failed += T.Check("weather: lightning alone is still a storm", () =>
        {
            var (raining, strength, _) = WeatherMap.ToTerraria(WeatherFlags.Lightning);
            T.Assert(raining && strength == WeatherMap.StormStrength, "lightning storms");
        });

        failed += T.Check("weather: windy weather sets the wind and only the wind when clear", () =>
        {
            var (raining, _, wind) = WeatherMap.ToTerraria(WeatherFlags.Debris);
            T.Assert(!raining && wind == WeatherMap.WindSpeed, "windy and clear");
            var (rainy, _, rainyWind) = WeatherMap.ToTerraria(WeatherFlags.Rain | WeatherFlags.Debris);
            T.Assert(rainy && rainyWind == WeatherMap.WindSpeed, "windy and rainy");
        });

        failed += T.Check("weather: state survives a roundtrip", () =>
        {
            var state = (WeatherState)ProtocolTests.Roundtrip(new WeatherState { Flags = WeatherFlags.Rain | WeatherFlags.Lightning | WeatherFlags.Debris });
            T.Assert(state.Flags == (WeatherFlags.Rain | WeatherFlags.Lightning | WeatherFlags.Debris), "flags " + (byte)state.Flags);
            T.Assert(state.Type == MsgType.WeatherState && (int)MsgType.WeatherState == 65, "message type 65");
            var clear = (WeatherState)ProtocolTests.Roundtrip(new WeatherState());
            T.Assert(clear.Flags == WeatherFlags.None, "clear");
        });

        return failed;
    }
}
