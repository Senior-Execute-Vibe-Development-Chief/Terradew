using System;
using System.Collections.Generic;
using System.Linq;

namespace Terradew.Core.Town
{
    /// <summary>
    /// Things in the Terraria world that town NPCs talk about. Terraria reports the ids that are true; on the next
    /// Stardew morning each NPC says their line for the most important new one. Lasting ones are said once ever;
    /// passing ones (a blood moon last night) every time they happen.
    /// </summary>
    public static class Happenings
    {
        // Most important first.
        private static readonly (string Id, bool Passing)[] All =
        {
            ("moonLord", false),
            ("cultist", false),
            ("golem", false),
            ("plantera", false),
            ("mechs", false),
            ("hardmode", false),
            ("skeletron", false),
            ("evilBoss", false),
            ("queenBee", false),
            ("eye", false),
            ("martians", false),
            ("pirates", false),
            ("frostLegion", false),
            ("goblins", false),
            ("bloodMoon", true),
            ("eclipse", true),
            ("party", true),
        };

        public static IReadOnlyList<string> Ids { get; } = All.Select(a => a.Id).ToArray();

        public static bool IsKnown(string id) => All.Any(a => a.Id == id);

        public static bool IsPassing(string id) => All.Any(a => a.Id == id && a.Passing);

        /// <summary>
        /// The happening to talk about this morning: the most important id in <paramref name="now"/> that is passing,
        /// or lasting and not in <paramref name="spoken"/>. Null when there is nothing new.
        /// </summary>
        public static string? Pick(IEnumerable<string> now, ICollection<string> spoken)
        {
            var set = new HashSet<string>(now ?? Array.Empty<string>(), StringComparer.Ordinal);
            foreach ((string id, bool passing) in All)
            {
                if (set.Contains(id) && (passing || !spoken.Contains(id)))
                    return id;
            }
            return null;
        }
    }

    /// <summary>Tags Terraria gives an item for gift tastes, so tables can say "#explosive" instead of naming every bomb.</summary>
    public static class GiftTags
    {
        public static IReadOnlyList<string> All { get; } = new[]
        {
            "#weapon", "#melee", "#ranged", "#magic", "#summon", "#tool", "#ammo", "#explosive",
            "#potion", "#food", "#fish", "#bait", "#ore", "#bar", "#gem", "#seed", "#plant",
            "#dye", "#vanity", "#armor", "#accessory", "#furniture", "#block", "#bossSummon", "#material",
            "#rare", "#junk",
        };

        public static bool IsKnown(string tag) => All.Contains(tag);
    }

    public enum Taste
    {
        Loved,
        Liked,
        Neutral,
        Disliked,
        Hated,
    }

    /// <summary>How a town NPC takes a Terraria item as a gift. Stardew items use Stardew's own gift tastes.</summary>
    public static class GiftJudge
    {
        /// <summary>Names beat tags; within each, the order is loved, hated, liked, disliked, neutral. Unlisted items are neutral.</summary>
        public static Taste Judge(GiftTable table, string itemName, IEnumerable<string> tags)
        {
            if (table == null)
                return Taste.Neutral;
            Taste[] order = { Taste.Loved, Taste.Hated, Taste.Liked, Taste.Disliked, Taste.Neutral };
            foreach (Taste taste in order)
            {
                if (Of(table, taste).Terraria.Any(t => string.Equals(t, itemName, StringComparison.OrdinalIgnoreCase)))
                    return taste;
            }
            var itemTags = new HashSet<string>(tags ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (Taste taste in order)
            {
                if (Of(table, taste).Terraria.Any(t => t.StartsWith("#", StringComparison.Ordinal) && itemTags.Contains(t)))
                    return taste;
            }
            return Taste.Neutral;
        }

        public static GiftTaste Of(GiftTable table, Taste taste) => taste switch
        {
            Taste.Loved => table.Loved,
            Taste.Liked => table.Liked,
            Taste.Disliked => table.Disliked,
            Taste.Hated => table.Hated,
            _ => table.Neutral,
        };

        /// <summary>Friendship points, as Stardew gives them. Birthdays count eight times.</summary>
        public static int Points(Taste taste, bool birthday)
        {
            int points = taste switch
            {
                Taste.Loved => 80,
                Taste.Liked => 45,
                Taste.Disliked => -20,
                Taste.Hated => -40,
                _ => 20,
            };
            return birthday ? points * 8 : points;
        }

        /// <summary>The emote Stardew shows for each taste.</summary>
        public static int Emote(Taste taste) => taste switch
        {
            Taste.Loved => 20,
            Taste.Liked => 32,
            Taste.Disliked => 8,
            Taste.Hated => 12,
            _ => 32,
        };

        /// <summary>Shop discount for a friend: 2% a heart, up to 20%.</summary>
        public static double Discount(int friendshipPoints)
        {
            int hearts = Math.Max(0, Math.Min(10, friendshipPoints / 250));
            return hearts * 0.02;
        }
    }
}
