using System;
using System.Collections.Generic;
using System.Linq;
using Terradew.Core.Town;

internal static class TownRulesTests
{
    public static int Run()
    {
        int failed = 0;

        failed += T.Check("fresh game starts with the guide", () =>
        {
            TownFacts facts = Fresh();
            T.Assert(TownRules.NextArrival(facts, 1) == "Guide", "guide first");
            T.Assert(TownRules.Waiting(facts).Count == 1 && TownRules.Waiting(facts)[0] == "Guide", "only guide waits");
        });

        failed += T.Check("merchant needs fifty silver", () =>
        {
            T.Assert(!TownRules.Eligible("Merchant", Fresh() with { Coins = 4999 }), "4999 copper is not enough");
            T.Assert(TownRules.Eligible("Merchant", Fresh() with { Coins = 5000 }), "5000 copper is fifty silver");
        });

        failed += T.Check("nurse needs merchant and life above one hundred", () =>
        {
            TownFacts withMerchant = Fresh() with { Present = new[] { "Merchant" } };
            T.Assert(TownRules.Eligible("Nurse", withMerchant with { MaxLife = 101 }), "life 101 with merchant");
            T.Assert(!TownRules.Eligible("Nurse", withMerchant with { MaxLife = 100 }), "life 100 is not above");
            T.Assert(!TownRules.Eligible("Nurse", Fresh() with { MaxLife = 400 }), "no merchant, no nurse");
        });

        failed += T.Check("no arrival without free homes", () =>
        {
            TownFacts facts = Fresh() with { Coins = 10000, HasGun = true };
            T.Assert(TownRules.NextArrival(facts, 0) == null, "zero homes");
            T.Assert(TownRules.NextArrival(facts, -2) == null, "negative homes");
            T.Assert(TownRules.NextArrival(facts, 1) == "Guide", "one home");
        });

        failed += T.Check("present npcs never return", () =>
        {
            TownFacts facts = Fresh() with { Present = new[] { "Guide" }, DownedSkeletron = true };
            T.Assert(!TownRules.Eligible("Guide", facts), "guide already home");
            T.Assert(!TownRules.Waiting(facts).Contains("Guide"), "guide not waiting");
            T.Assert(TownRules.NextArrival(facts, 3) != "Guide", "guide not next");
        });

        failed += T.Check("painter arrives at eight present", () =>
        {
            TownFacts seven = Fresh() with { Present = Names(7) };
            TownFacts eight = Fresh() with { Present = Names(8) };
            T.Assert(!TownRules.Eligible("Painter", seven), "seven is not enough");
            T.Assert(TownRules.Eligible("Painter", eight), "eight is enough");
        });

        failed += T.Check("waiting follows the move-in order", () =>
        {
            TownFacts facts = Fresh() with
            {
                MaxLife = 200, Coins = 10000, HasGun = true, HasExplosive = true, DownedEye = true,
                DownedSkeletron = true, VisitedOcean = true, DownedGoblins = true,
                Present = new[] { "Merchant" },
            };
            string[] expected =
            {
                "Guide", "Nurse", "Demolitionist", "ArmsDealer", "Dryad", "Angler",
                "Clothier", "GoblinTinkerer", "Mechanic",
            };
            T.Assert(TownRules.Waiting(facts).SequenceEqual(expected), "waiting: " + string.Join(",", TownRules.Waiting(facts)));
            T.Assert(TownRules.NextArrival(facts, 1) == "Guide", "guide leads");
            T.Assert(TownRules.Order.SequenceEqual(new[]
            {
                "Guide", "Merchant", "Nurse", "Demolitionist", "ArmsDealer", "Dryad",
                "Angler", "Painter", "Clothier", "GoblinTinkerer", "Mechanic", "PartyGirl",
            }), "order constant");
        });

        failed += T.Check("unknown and miscased names are not eligible", () =>
        {
            TownFacts everything = Fresh() with
            {
                MaxLife = 400, Coins = 100000, HasGun = true, HasExplosive = true,
                DownedEye = true, DownedEvilBoss = true, DownedSkeletron = true, DownedQueenBee = true,
                Hardmode = true, DownedGoblins = true, VisitedOcean = true,
                Present = Names(20),
            };
            T.Assert(!TownRules.Eligible("Slime", everything), "unknown name");
            T.Assert(!TownRules.Eligible("", everything), "empty name");
            T.Assert(!TownRules.Eligible("guide", Fresh()), "case sensitive");
            T.Assert(!TownRules.Eligible("merchant", Fresh() with { Coins = 99999 }), "case sensitive merchant");
        });

        return failed;
    }

    private static TownFacts Fresh()
    {
        return new TownFacts(
            MaxLife: 100, Coins: 0, HasGun: false, HasExplosive: false,
            DownedEye: false, DownedEvilBoss: false, DownedSkeletron: false, DownedQueenBee: false,
            Hardmode: false, DownedGoblins: false, VisitedOcean: false,
            Present: Array.Empty<string>());
    }

    private static string[] Names(int count)
    {
        return Enumerable.Range(0, count).Select(i => "Villager" + i).ToArray();
    }
}
