using System;
using System.Collections.Generic;
using System.Linq;

namespace Terradew.Core.Town
{
    /// <summary>
    /// Which Stardew villager's home each Terraria town NPC takes over. The villagers themselves are gone;
    /// their shops stay open under the Terraria NPC who now lives there.
    /// </summary>
    public static class TownHomes
    {
        private static readonly (string Npc, string Villager)[] Pairs =
        {
            ("Guide", "Gunther"),
            ("Merchant", "Pierre"),
            ("Nurse", "Harvey"),
            ("Demolitionist", "Kent"),
            ("ArmsDealer", "Marlon"),
            ("Dryad", "Linus"),
            ("Angler", "Willy"),
            ("Painter", "Leah"),
            ("Clothier", "Emily"),
            ("GoblinTinkerer", "Clint"),
            ("Mechanic", "Robin"),
            ("PartyGirl", "Pam"),
            ("DD2Bartender", "Gus"),
            ("TaxCollector", "Lewis"),
            ("Stylist", "Haley"),
            ("Steampunker", "Demetrius"),
            ("WitchDoctor", "Leo"),
            ("Cyborg", "Krobus"),
            ("Pirate", "Elliott"),
            ("BestiaryGirl", "Marnie"),
            ("Golfer", "Alex"),
            ("Princess", "Evelyn"),
            ("DyeTrader", "Sandy"),
            ("Wizard", "Wizard"),
            // The basement under Robin's house is the nearest thing in town to a mushroom cave.
            ("Truffle", "Sebastian"),
        };

        private static readonly Dictionary<string, string> ByNpc = Pairs.ToDictionary(p => p.Npc, p => p.Villager, StringComparer.Ordinal);
        private static readonly Dictionary<string, string> ByVillager = Pairs.ToDictionary(p => p.Villager, p => p.Npc, StringComparer.Ordinal);

        /// <summary>Terraria NPCID numbers for the town NPCs above, so Stardew can match a resident by type.</summary>
        private static readonly Dictionary<string, int> TypeIds = new(StringComparer.Ordinal)
        {
            ["Guide"] = 22,
            ["Merchant"] = 17,
            ["Nurse"] = 18,
            ["Demolitionist"] = 38,
            ["ArmsDealer"] = 19,
            ["Dryad"] = 20,
            ["Angler"] = 369,
            ["Painter"] = 227,
            ["Clothier"] = 54,
            ["GoblinTinkerer"] = 107,
            ["Mechanic"] = 124,
            ["PartyGirl"] = 208,
            ["DD2Bartender"] = 550,
            ["TaxCollector"] = 441,
            ["Stylist"] = 353,
            ["Steampunker"] = 178,
            ["WitchDoctor"] = 228,
            ["Cyborg"] = 209,
            ["Pirate"] = 229,
            ["BestiaryGirl"] = 633,
            ["Golfer"] = 588,
            ["Princess"] = 663,
            ["DyeTrader"] = 207,
            ["Wizard"] = 108,
            ["Truffle"] = 160,
        };

        /// <summary>Every Terraria NPC that has a Stardew home, in move-in order.</summary>
        public static IReadOnlyList<string> Npcs { get; } = Pairs.Select(p => p.Npc).ToArray();

        /// <summary>The villager whose home this Terraria NPC takes, or null if it has none.</summary>
        public static string? VillagerOf(string npc) => npc != null && ByNpc.TryGetValue(npc, out string? v) ? v : null;

        /// <summary>The Terraria NPCID of a town NPC by its internal name, or null if it is not a town NPC with a home.</summary>
        public static int? TypeOf(string npc) => npc != null && TypeIds.TryGetValue(npc, out int t) ? t : null;

        /// <summary>The Terraria NPC living in this villager's home, or null if nobody replaces them.</summary>
        public static string? NpcOf(string villager) => villager != null && ByVillager.TryGetValue(villager, out string? n) ? n : null;
    }
}
