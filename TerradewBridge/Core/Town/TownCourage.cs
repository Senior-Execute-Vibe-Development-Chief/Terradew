using System;
using System.Collections.Generic;
using System.Linq;

namespace Terradew.Core.Town
{
    /// <summary>How a town NPC meets danger.</summary>
    public enum Role
    {
        /// <summary>Flees from enemies and calls for help. Some have no attack at all.</summary>
        Coward,

        /// <summary>Fights what comes close, but never chases.</summary>
        Guard,

        /// <summary>Goes looking for enemies nearby and answers calls for help.</summary>
        Hero,
    }

    /// <summary>What a followed town NPC is doing, as Terraria reports it.</summary>
    public enum VillagerMode : byte
    {
        /// <summary>Walking the villager's Stardew schedule.</summary>
        Following = 0,

        /// <summary>An enemy is near and they are running from it.</summary>
        Fleeing = 1,

        /// <summary>Standing their ground and fighting where they are.</summary>
        Defending = 2,

        /// <summary>Chasing an enemy: Terraria moves them, and the Stardew villager follows.</summary>
        Fighting = 3,

        /// <summary>Beaten in a fight. Back home, and out for the rest of the day.</summary>
        KnockedOut = 4,
    }

    /// <summary>Who is brave, who is not, and how hard each one hits.</summary>
    public static class TownCourage
    {
        private static readonly Dictionary<string, Role> Roles = new(StringComparer.Ordinal)
        {
            ["ArmsDealer"] = Role.Hero,
            ["Pirate"] = Role.Hero,
            ["Guide"] = Role.Hero,
            ["Demolitionist"] = Role.Hero,
            ["Steampunker"] = Role.Hero,
            ["Cyborg"] = Role.Hero,
            ["Wizard"] = Role.Hero,
            ["Dryad"] = Role.Hero,
            ["WitchDoctor"] = Role.Hero,
            ["DD2Bartender"] = Role.Hero,

            ["GoblinTinkerer"] = Role.Guard,
            ["Mechanic"] = Role.Guard,
            ["DyeTrader"] = Role.Guard,
            ["Nurse"] = Role.Guard,
            ["BestiaryGirl"] = Role.Guard,

            ["Merchant"] = Role.Coward,
            ["Angler"] = Role.Coward,
            ["Princess"] = Role.Coward,
            ["Stylist"] = Role.Coward,
            ["TaxCollector"] = Role.Coward,
            ["PartyGirl"] = Role.Coward,
            ["Painter"] = Role.Coward,
            ["Clothier"] = Role.Coward,
            ["Golfer"] = Role.Coward,
            ["Truffle"] = Role.Coward,
        };

        /// <summary>Cowards who do not fight back at all. The other cowards keep a feeble attack for when cornered.</summary>
        private static readonly HashSet<string> Harmless = new(StringComparer.Ordinal)
        {
            "Angler", "Princess", "Stylist", "TaxCollector", "PartyGirl", "Truffle",
        };

        public static Role RoleOf(string npc) => npc != null && Roles.TryGetValue(npc, out Role role) ? role : Role.Guard;

        public static bool IsHarmless(string npc) => npc != null && Harmless.Contains(npc);

        /// <summary>Attack damage multiplier.</summary>
        public static float Damage(Role role) => role switch
        {
            Role.Hero => 1.25f,
            Role.Guard => 1f,
            _ => 0.5f,
        };

        /// <summary>Health multiplier.</summary>
        public static float Health(Role role) => role switch
        {
            Role.Hero => 1.5f,
            Role.Guard => 1f,
            _ => 0.75f,
        };

        /// <summary>
        /// How far, in tiles, a town NPC notices an enemy. Heroes look further during danger (a boss, an invasion);
        /// guards only react to what reaches them; cowards notice early so they can run.
        /// </summary>
        public static float Watch(Role role, bool danger) => role switch
        {
            Role.Hero => danger ? 25f : 12f,
            Role.Guard => 6f,
            _ => 8f,
        };

        /// <summary>The furthest, in tiles, a hero or guard engages an enemy from Terraria's own danger range.</summary>
        public const float EngageCap = 20f;

        /// <summary>
        /// How far, in tiles, a hero or guard engages a hostile creature: Terraria's own danger range for them (capped),
        /// or their watch range when that is further. Cowards keep their watch range, so they run early.
        /// </summary>
        public static float Engage(Role role, bool danger, float dangerTiles)
            => role == Role.Coward ? Watch(role, danger) : Math.Max(Watch(role, danger), Math.Min(dangerTiles, EngageCap));

        /// <summary>Extra reach, in tiles, a fight keeps once it has started, so a step out of range does not end it.</summary>
        public const float FightSlackTiles = 3f;

        /// <summary>
        /// True while an enemy still counts as in range for a hero or guard: inside its engage range, or a little past it
        /// when the fight is already going. An enemy beyond that is no longer a reason to stay out of the routine.
        /// </summary>
        public static bool Holds(float enemyTiles, float engageTiles, bool fighting)
            => enemyTiles <= engageTiles + (fighting ? FightSlackTiles : 0f);

        /// <summary>How far, in tiles, a hero chases from where the fight started before giving up.</summary>
        public const float Leash = 20f;

        /// <summary>How far, in tiles, a coward keeps running once an enemy is this close.</summary>
        public const float Safe = 12f;

        /// <summary>Kinds of danger, most serious first. Terraria reports the first that is true.</summary>
        public static IReadOnlyList<string> Dangers { get; } = new[] { "boss", "invasion", "pumpkinMoon", "frostMoon", "eclipse", "bloodMoon" };

        public static IReadOnlyList<string> Npcs => Roles.Keys.ToArray();
    }
}
