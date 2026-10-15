using System;
using System.Collections.Generic;
using System.Linq;

namespace Terradew.Core.Town
{
    /// <summary>Snapshot of the game state that decides which Terraria town NPCs may move in.</summary>
    public sealed record TownFacts(
        int MaxLife, long Coins, bool HasGun, bool HasExplosive,
        bool DownedEye, bool DownedEvilBoss, bool DownedSkeletron, bool DownedQueenBee,
        bool Hardmode, bool DownedGoblins, bool VisitedOcean,
        IReadOnlyCollection<string> Present,
        bool DownedMechAny = false, bool DownedGolem = false, bool DownedPlantera = false, bool DownedPirates = false,
        bool SavedBartender = false, bool SavedTaxCollector = false, bool SavedStylist = false,
        bool SavedGolfer = false, bool SavedWizard = false);

    /// <summary>Move-in rules for town NPCs. Names are Terraria internal NPC names.</summary>
    public static class TownRules
    {
        private static readonly string[] OrderNames =
        {
            "Guide",
            "Merchant",
            "Nurse",
            "Demolitionist",
            "ArmsDealer",
            "Dryad",
            "Angler",
            "Painter",
            "Clothier",
            "GoblinTinkerer",
            "Mechanic",
            "PartyGirl",
            "DD2Bartender",
            "TaxCollector",
            "Stylist",
            "Steampunker",
            "WitchDoctor",
            "Cyborg",
            "Pirate",
            "BestiaryGirl",
            "Golfer",
            "Princess",
            "DyeTrader",
            "Wizard",
        };

        private const long MerchantCoins = 5000; // 50 silver; coins are copper.
        private const int NurseMinLife = 100;    // Nurse needs MaxLife strictly above this.
        private const int PainterMinPresent = 8;
        private const int PartyGirlMinPresent = 14;

        /// <summary>Move-in priority order.</summary>
        public static IReadOnlyList<string> Order { get; } = Array.AsReadOnly(OrderNames);

        /// <summary>True when the named NPC may move in and is not already present.</summary>
        public static bool Eligible(string name, TownFacts facts)
        {
            if (facts.Present.Contains(name))
                return false;

            switch (name)
            {
                case "Guide":
                    return true;
                case "Merchant":
                    return facts.Coins >= MerchantCoins;
                case "Nurse":
                    return facts.Present.Contains("Merchant") && facts.MaxLife > NurseMinLife;
                case "Demolitionist":
                    return facts.Present.Contains("Merchant") && facts.HasExplosive;
                case "ArmsDealer":
                    return facts.HasGun;
                case "Dryad":
                    return facts.DownedEye || facts.DownedEvilBoss || facts.DownedSkeletron || facts.DownedQueenBee;
                case "Angler":
                    return facts.VisitedOcean;
                case "Painter":
                    return facts.Present.Count >= PainterMinPresent;
                case "Clothier":
                    return facts.DownedSkeletron;
                case "GoblinTinkerer":
                    return facts.DownedGoblins;
                case "Mechanic":
                    return facts.DownedSkeletron;
                case "PartyGirl":
                    return facts.Present.Count >= PartyGirlMinPresent;
                // Bound residents are rarely found on stamped places, so like the Goblin and the Mechanic they
                // also come at the point in progression where vanilla would first leave them to be found.
                case "DD2Bartender":
                    return facts.SavedBartender || facts.DownedEvilBoss;
                case "TaxCollector":
                    return facts.SavedTaxCollector || facts.Hardmode;
                case "Stylist":
                    return facts.SavedStylist || facts.DownedEye;
                case "Steampunker":
                    return facts.DownedMechAny;
                case "WitchDoctor":
                    return facts.DownedPlantera;
                case "Cyborg":
                    return facts.DownedPlantera;
                case "Pirate":
                    return facts.DownedPirates;
                case "BestiaryGirl":
                    return facts.Hardmode;
                case "Golfer":
                    return facts.SavedGolfer || facts.DownedEye;
                case "Princess":
                    return facts.DownedGolem;
                case "DyeTrader":
                    return facts.Hardmode;
                case "Wizard":
                    return facts.SavedWizard || facts.Hardmode;
                default:
                    return false;
            }
        }

        /// <summary>The next NPC to move in, or null when no home is free or nobody is eligible.</summary>
        public static string? NextArrival(TownFacts facts, int freeHomes)
        {
            if (freeHomes <= 0)
                return null;

            IReadOnlyList<string> waiting = Waiting(facts);
            return waiting.Count > 0 ? waiting[0] : null;
        }

        /// <summary>Eligible NPCs that are not yet present, in <see cref="Order"/>.</summary>
        public static IReadOnlyList<string> Waiting(TownFacts facts)
        {
            return OrderNames.Where(name => Eligible(name, facts)).ToList();
        }
    }
}
