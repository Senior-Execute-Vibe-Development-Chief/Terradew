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

        failed += T.Check("every town npc has a terraria type id", () =>
        {
            T.Assert(TownHomes.Npcs.All(n => TownHomes.TypeOf(n) != null), "missing: " + string.Join(",", TownHomes.Npcs.Where(n => TownHomes.TypeOf(n) == null)));
            T.Assert(TownHomes.Npcs.Select(TownHomes.TypeOf).Distinct().Count() == TownHomes.Npcs.Count, "type ids are distinct");
            T.Assert(TownHomes.TypeOf("Guide") == 22 && TownHomes.TypeOf("Nurse") == 18 && TownHomes.TypeOf("Angler") == 369, "known ids");
            T.Assert(TownHomes.TypeOf("Slime") == null, "unknown names have no id");
        });

        return failed;
    }
}
