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
        };

        private static readonly Dictionary<string, string> ByNpc = Pairs.ToDictionary(p => p.Npc, p => p.Villager, StringComparer.Ordinal);
        private static readonly Dictionary<string, string> ByVillager = Pairs.ToDictionary(p => p.Villager, p => p.Npc, StringComparer.Ordinal);

        /// <summary>Every Terraria NPC that has a Stardew home, in move-in order.</summary>
        public static IReadOnlyList<string> Npcs { get; } = Pairs.Select(p => p.Npc).ToArray();

        /// <summary>The villager whose home this Terraria NPC takes, or null if it has none.</summary>
        public static string? VillagerOf(string npc) => npc != null && ByNpc.TryGetValue(npc, out string? v) ? v : null;

        /// <summary>The Terraria NPC living in this villager's home, or null if nobody replaces them.</summary>
        public static string? NpcOf(string villager) => villager != null && ByVillager.TryGetValue(villager, out string? n) ? n : null;
    }
}
