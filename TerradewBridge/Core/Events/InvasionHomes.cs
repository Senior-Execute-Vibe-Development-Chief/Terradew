namespace Terradew.Core.Events;

/// <summary>
/// Each Terraria invasion attacks one Stardew place, announced the Stardew way. The invaders pour in
/// through that place's edges while the farmer is there, and wait while the farmer is elsewhere.
/// </summary>
public static class InvasionHomes
{
    // Terraria's InvasionID values.
    public const int Goblins = 1;
    public const int FrostLegion = 2;
    public const int Pirates = 3;
    public const int Martians = 4;

    public static string? HomeOf(int invasionType) => invasionType switch
    {
        Goblins => "Town",
        FrostLegion => "Mountain",
        Pirates => "Beach",
        Martians => "Farm",
        _ => null,
    };

    public static string PlaceName(string key) => key switch
    {
        "Town" => "Pelican Town",
        "Mountain" => "the Mountain",
        "Beach" => "the Beach",
        "Farm" => "the farm",
        _ => key,
    };

    public static string Name(int invasionType) => invasionType switch
    {
        Goblins => "A goblin army",
        FrostLegion => "The Frost Legion",
        Pirates => "A pirate fleet",
        Martians => "A martian invasion",
        _ => "An invasion",
    };

    public static string Started(int invasionType)
    {
        string? home = HomeOf(invasionType);
        return home == null ? $"{Name(invasionType)} has begun!" : $"{Name(invasionType)} is attacking {PlaceName(home)}!";
    }

    public static string Ended(int invasionType) => $"{Name(invasionType)} has been driven off!";
}
