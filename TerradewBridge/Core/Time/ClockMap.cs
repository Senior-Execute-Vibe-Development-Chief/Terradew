using System;

namespace Terradew.Core.Time;

/// <summary>
/// Maps Stardew's clock onto Terraria's day/night cycle.
/// Stardew time is an int hhmm from 600 to 2600, in steps of 10 minutes.
/// Terraria: day starts at 4:30am (time 0) and lasts 54000 ticks;
/// night starts at 7:30pm (time 0) and lasts 32400 ticks.
/// </summary>
public static class ClockMap
{
    public const double DayLength = 54000, NightLength = 32400;

    private const int DayStartMinute = 270;   // 4:30am
    private const int NightStartMinute = 1170; // 7:30pm
    private const int MinutesPerDay = 1440;
    private const double TerrariaTicksPerMinute = 60;

    public static (bool DayTime, double Time) ToTerraria(int stardewTime, float fraction)
    {
        double clamped = Math.Clamp((double)fraction, 0.0, 1.0);
        int hours = stardewTime / 100;
        int minutesOfHour = stardewTime % 100;
        double minutes = hours * 60 + minutesOfHour + clamped * 10;
        double wall = minutes % MinutesPerDay;

        if (wall >= DayStartMinute && wall < NightStartMinute)
            return (true, (wall - DayStartMinute) * TerrariaTicksPerMinute);

        double sinceNight = (wall - NightStartMinute + MinutesPerDay) % MinutesPerDay;
        return (false, sinceNight * TerrariaTicksPerMinute);
    }

    public static double PhaseLength(bool dayTime) => dayTime ? DayLength : NightLength;

    public static (bool DayTime, double Time) Next(bool currentDay, double currentTime, bool targetDay, double targetTime)
    {
        if (currentDay == targetDay)
            return (targetDay, targetTime);

        // Let the game run out the current phase so its dusk/dawn events still fire.
        return (currentDay, PhaseLength(currentDay) - 1);
    }
}
