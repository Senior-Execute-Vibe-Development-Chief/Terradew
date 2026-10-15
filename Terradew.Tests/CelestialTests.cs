using Terradew.Core.Events;

internal static class CelestialTests
{
    private static readonly CelestialFlags Start = new(
        GolemDown: true,
        CultistDown: false,
        CultistAlive: false,
        PillarsDown: PillarSet.None,
        PillarsAlive: PillarSet.None,
        MoonLordDown: false,
        MoonLordAlive: false);

    private static CelestialFlags Down(CelestialFlags flags, bool cultist, PillarSet pillars, bool moonLord) => flags with
    {
        CultistDown = cultist,
        CultistAlive = false,
        PillarsDown = pillars,
        MoonLordDown = moonLord,
    };

    private static bool Is(CelestialAction action, CelestialActionKind kind) => action.Kind == kind;

    public static int Run()
    {
        int failed = 0;
        failed += T.Check("each pillar stands in its own place", () =>
        {
            T.Assert(CelestialRules.PillarHome(Pillar.Solar) == "Farm", "solar is on the farm");
            T.Assert(CelestialRules.PillarHome(Pillar.Vortex) == "Town", "vortex is in town");
            T.Assert(CelestialRules.PillarHome(Pillar.Nebula) == "Beach", "nebula is on the beach");
            T.Assert(CelestialRules.PillarHome(Pillar.Stardust) == "Mountain", "stardust is on the mountain");
            T.Assert(CelestialRules.PillarAt("Farm") == Pillar.Solar, "farm holds solar");
            T.Assert(CelestialRules.PillarAt("SkullCave") == null, "skull cavern holds no pillar");
            T.Assert(CelestialRules.PillarAt("Woods") == null, "woods hold no pillar");
        });
        failed += T.Check("the cultist waits for golem and only appears at the skull cavern", () =>
        {
            T.Assert(Is(CelestialRules.Step(Start, "SkullCave"), CelestialActionKind.SpawnCultist), "cultist at skull cavern after golem");
            T.Assert(!Is(CelestialRules.Step(Start with { GolemDown = false }, "SkullCave"), CelestialActionKind.SpawnCultist), "no cultist before golem");
            T.Assert(!Is(CelestialRules.Step(Start, "Farm"), CelestialActionKind.SpawnCultist), "no cultist on the farm");
            T.Assert(!Is(CelestialRules.Step(Start, "mine:cavern#0"), CelestialActionKind.SpawnCultist), "no cultist in the mines");
        });
        failed += T.Check("a living cultist is not summoned again, and a dead one is never summoned again", () =>
        {
            T.Assert(CelestialRules.Step(Start with { CultistAlive = true }, "SkullCave").Kind == CelestialActionKind.None, "alive cultist blocks a second");
            T.Assert(CelestialRules.Step(Down(Start, true, PillarSet.None, false), "SkullCave").Kind == CelestialActionKind.None, "dead cultist does not return");
        });
        failed += T.Check("pillars wait for the cultist, then rise only in their own place", () =>
        {
            T.Assert(CelestialRules.Step(Start, "Farm").Kind == CelestialActionKind.None, "no pillar before the cultist");
            CelestialFlags after = Down(Start, true, PillarSet.None, false);
            CelestialAction farm = CelestialRules.Step(after, "Farm");
            T.Assert(farm.Kind == CelestialActionKind.SpawnPillar && farm.Target == Pillar.Solar, "solar rises on the farm");
            T.Assert(CelestialRules.Step(after, "Town").Target == Pillar.Vortex, "vortex rises in town");
            T.Assert(CelestialRules.Step(after, "Beach").Target == Pillar.Nebula, "nebula rises on the beach");
            T.Assert(CelestialRules.Step(after, "Mountain").Target == Pillar.Stardust, "stardust rises on the mountain");
            T.Assert(CelestialRules.Step(after, "SkullCave").Kind == CelestialActionKind.None, "no pillar in skull cavern");
        });
        failed += T.Check("a pillar is not respawned while it lives or after it falls", () =>
        {
            CelestialFlags after = Down(Start, true, PillarSet.None, false);
            T.Assert(CelestialRules.Step(after with { PillarsAlive = PillarSet.Solar }, "Farm").Kind == CelestialActionKind.None, "living solar blocks respawn");
            T.Assert(CelestialRules.Step(after with { PillarsDown = PillarSet.Solar }, "Farm").Kind == CelestialActionKind.None, "fallen solar does not return");
            T.Assert(CelestialRules.Step(after with { PillarsDown = PillarSet.Solar }, "Town").Target == Pillar.Vortex, "other pillars still rise");
        });
        failed += T.Check("moon lord waits for all four pillars and then comes to the farm only", () =>
        {
            CelestialFlags three = Down(Start, true, PillarSet.Solar | PillarSet.Vortex | PillarSet.Nebula, false);
            T.Assert(CelestialRules.Step(three, "Farm").Kind == CelestialActionKind.None, "no moon lord with a pillar still up");
            T.Assert(CelestialRules.Step(three, "Mountain").Target == Pillar.Stardust, "last pillar still rises");
            CelestialFlags all = Down(Start, true, PillarSet.All, false);
            T.Assert(Is(CelestialRules.Step(all, "Farm"), CelestialActionKind.SpawnMoonLord), "moon lord comes to the farm");
            T.Assert(CelestialRules.Step(all, "Town").Kind == CelestialActionKind.None, "moon lord does not come to town");
            T.Assert(CelestialRules.Step(all, "Mountain").Kind == CelestialActionKind.None, "moon lord does not come to the mountain");
            T.Assert(CelestialRules.Step(all with { MoonLordAlive = true }, "Farm").Kind == CelestialActionKind.None, "living moon lord blocks a second");
            T.Assert(CelestialRules.Step(all with { MoonLordDown = true }, "Farm").Kind == CelestialActionKind.None, "dead moon lord does not return");
        });
        failed += T.Check("full sequence runs in order", () =>
        {
            CelestialFlags flags = Start;
            T.Assert(CelestialRules.Step(flags, "Farm").Kind == CelestialActionKind.None, "nothing on the farm at the start");
            flags = flags with { CultistAlive = true };
            T.Assert(CelestialRules.Step(flags, "SkullCave").Kind == CelestialActionKind.None, "cultist fight is quiet");
            flags = Down(flags, true, PillarSet.None, false);
            T.Assert(CelestialRules.Step(flags, "Farm").Target == Pillar.Solar, "first pillar after the cultist");
            flags = flags with { PillarsDown = PillarSet.Solar };
            T.Assert(CelestialRules.Step(flags, "Farm").Kind == CelestialActionKind.None, "farm is quiet once solar falls");
            flags = flags with { PillarsDown = PillarSet.Solar | PillarSet.Vortex | PillarSet.Nebula | PillarSet.Stardust };
            T.Assert(CelestialRules.Step(flags, "Farm").Kind == CelestialActionKind.SpawnMoonLord, "moon lord after the last pillar");
            flags = flags with { MoonLordAlive = true };
            T.Assert(CelestialRules.Step(flags, "Farm").Kind == CelestialActionKind.None, "moon lord fight is quiet");
        });
        failed += T.Check("announcements name the pillar and its place", () =>
        {
            T.Assert(CelestialRules.Announcement(new CelestialAction(CelestialActionKind.SpawnCultist)) == "The Lunatic Cultist awaits at the Skull Cavern!", "cultist line");
            T.Assert(CelestialRules.Announcement(new CelestialAction(CelestialActionKind.SpawnPillar, Pillar.Solar)) == "The Solar Pillar rises on the Farm!", "solar line");
            T.Assert(CelestialRules.Announcement(new CelestialAction(CelestialActionKind.SpawnPillar, Pillar.Vortex)) == "The Vortex Pillar rises on Pelican Town!", "vortex line");
            T.Assert(CelestialRules.Announcement(new CelestialAction(CelestialActionKind.SpawnPillar, Pillar.Nebula)) == "The Nebula Pillar rises on the Beach!", "nebula line");
            T.Assert(CelestialRules.Announcement(new CelestialAction(CelestialActionKind.SpawnPillar, Pillar.Stardust)) == "The Stardust Pillar rises on the Mountain!", "stardust line");
            T.Assert(CelestialRules.Announcement(new CelestialAction(CelestialActionKind.SpawnMoonLord)) == "Impending doom approaches the Farm...", "moon lord line");
            T.Assert(CelestialRules.Announcement(new CelestialAction(CelestialActionKind.None)) == null, "silent when nothing happens");
        });
        return failed;
    }
}
