using System.Linq;
using Terradew.Core.Town;

internal static class TownHomesTests
{
    public static int Run()
    {
        int failed = 0;

        failed += T.Check("every town npc has a stardew home", () =>
        {
            T.Assert(TownRules.Order.All(n => TownHomes.VillagerOf(n) != null), "missing: " + string.Join(",", TownRules.Order.Where(n => TownHomes.VillagerOf(n) == null)));
            T.Assert(TownHomes.Npcs.SequenceEqual(TownRules.Order), "same order as move-in");
        });

        failed += T.Check("homes map both ways and are not shared", () =>
        {
            T.Assert(TownHomes.VillagerOf("Nurse") == "Harvey" && TownHomes.NpcOf("Harvey") == "Nurse", "nurse in the clinic");
            T.Assert(TownHomes.VillagerOf("Merchant") == "Pierre", "merchant runs the seed shop");
            T.Assert(TownHomes.Npcs.Select(TownHomes.VillagerOf).Distinct().Count() == TownHomes.Npcs.Count, "one villager each");
            T.Assert(TownHomes.VillagerOf("Slime") == null && TownHomes.NpcOf("Slime") == null, "unknown names");
        });

        return failed;
    }
}
