namespace Terradew.Core.Events;

/// <summary>The four Lunar Pillars. Each one stands in one Stardew place.</summary>
public enum Pillar : byte
{
    Solar = 0,
    Vortex = 1,
    Nebula = 2,
    Stardust = 3,
}

/// <summary>A set of pillars, one bit each.</summary>
[Flags]
public enum PillarSet : byte
{
    None = 0,
    Solar = 1 << 0,
    Vortex = 1 << 1,
    Nebula = 1 << 2,
    Stardust = 1 << 3,
    All = Solar | Vortex | Nebula | Stardust,
}

public enum CelestialActionKind : byte
{
    None,
    SpawnCultist,
    SpawnPillar,
    SpawnMoonLord,
}

/// <summary>What the bridge should do this tick. <see cref="Target"/> is set only for <see cref="CelestialActionKind.SpawnPillar"/>.</summary>
public readonly record struct CelestialAction(CelestialActionKind Kind, Pillar? Target = null);

/// <summary>What the Terraria world currently says about the late-game sequence.</summary>
public readonly record struct CelestialFlags(
    bool GolemDown,
    bool CultistDown,
    bool CultistAlive,
    PillarSet PillarsDown,
    PillarSet PillarsAlive,
    bool MoonLordDown,
    bool MoonLordAlive);

/// <summary>
/// The late-game order: Golem, then the Lunatic Cultist at the Skull Cavern entrance, then the four Lunar
/// Pillars each in its own place, then Moon Lord on the Farm once every pillar is down.
/// </summary>
public static class CelestialRules
{
    public const string CultistPlace = "SkullCave";
    public const string MoonLordPlace = "Farm";

    /// <summary>The Stardew place a pillar stands in.</summary>
    public static string PillarHome(Pillar pillar) => pillar switch
    {
        Pillar.Solar => "Farm",
        Pillar.Vortex => "Town",
        Pillar.Nebula => "Beach",
        Pillar.Stardust => "Mountain",
        _ => throw new ArgumentOutOfRangeException(nameof(pillar)),
    };

    /// <summary>The pillar standing in a place, or null when no pillar does.</summary>
    public static Pillar? PillarAt(string placeKey) => placeKey switch
    {
        "Farm" => Pillar.Solar,
        "Town" => Pillar.Vortex,
        "Beach" => Pillar.Nebula,
        "Mountain" => Pillar.Stardust,
        _ => null,
    };

    public static PillarSet Bit(Pillar pillar) => (PillarSet)(1 << (int)pillar);

    /// <summary>
    /// The one thing to do in this place right now, or None. At most one rule can apply per place: SkullCave
    /// holds only the cultist, each pillar's place holds only its own pillar, and Moon Lord needs every pillar
    /// down, so the Farm never offers both its pillar and Moon Lord.
    /// </summary>
    public static CelestialAction Step(CelestialFlags flags, string placeKey)
    {
        placeKey ??= "";

        if (flags.GolemDown && !flags.CultistDown && !flags.CultistAlive && placeKey == CultistPlace)
            return new CelestialAction(CelestialActionKind.SpawnCultist);

        Pillar? pillar = PillarAt(placeKey);
        if (flags.CultistDown && pillar is Pillar here)
        {
            PillarSet bit = Bit(here);
            if ((flags.PillarsDown & bit) == 0 && (flags.PillarsAlive & bit) == 0)
                return new CelestialAction(CelestialActionKind.SpawnPillar, here);
        }

        if (placeKey == MoonLordPlace && flags.PillarsDown == PillarSet.All && !flags.MoonLordDown && !flags.MoonLordAlive)
            return new CelestialAction(CelestialActionKind.SpawnMoonLord);

        return new CelestialAction(CelestialActionKind.None);
    }

    /// <summary>The line announced when an action happens, or null when it is silent.</summary>
    public static string? Announcement(CelestialAction action)
    {
        switch (action.Kind)
        {
            case CelestialActionKind.SpawnCultist:
                return "The Lunatic Cultist awaits at the Skull Cavern!";
            case CelestialActionKind.SpawnPillar:
                if (action.Target is Pillar pillar)
                    return $"The {PillarName(pillar)} Pillar rises on {PlaceName(PillarHome(pillar))}!";
                return null;
            case CelestialActionKind.SpawnMoonLord:
                return "Impending doom approaches the Farm...";
            default:
                return null;
        }
    }

    private static string PillarName(Pillar pillar) => pillar switch
    {
        Pillar.Solar => "Solar",
        Pillar.Vortex => "Vortex",
        Pillar.Nebula => "Nebula",
        Pillar.Stardust => "Stardust",
        _ => "",
    };

    private static string PlaceName(string key) => key switch
    {
        "Farm" => "the Farm",
        "Town" => "Pelican Town",
        "Beach" => "the Beach",
        "Mountain" => "the Mountain",
        _ => key,
    };
}
