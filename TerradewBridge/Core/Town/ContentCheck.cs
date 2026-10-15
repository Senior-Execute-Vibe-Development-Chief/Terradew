using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Terradew.Core.Town
{
    /// <summary>
    /// Checks an <see cref="NpcContent"/> file. The tests run it over every file; Stardew runs it on load and
    /// leaves out anything broken rather than crash.
    /// </summary>
    public static class ContentCheck
    {
        /// <summary>Town NPCs who can be dated and married.</summary>
        public static IReadOnlyList<string> Romance { get; } = new[] { "Stylist", "Painter", "Clothier", "Golfer", "Pirate", "Nurse", "Truffle" };

        public static IReadOnlyList<string> Festivals { get; } = new[] { "spring13", "spring24", "summer11", "summer28", "fall16", "fall27", "winter8", "winter25" };

        public static IReadOnlyList<string> Days { get; } = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

        public static IReadOnlyList<string> Seasons { get; } = new[] { "spring", "summer", "fall", "winter" };

        private static readonly Regex DialogueKey = new(
            @"^(Introduction|danceRejection|((spring|summer|fall|winter)_)?(Mon|Tue|Wed|Thu|Fri|Sat|Sun)(2|4|6|8|10)?|(spring|summer|fall|winter)_([1-9]|1[0-9]|2[0-8]))$",
            RegexOptions.CultureInvariant);

        /// <summary>The marriage dialogue keys every romance NPC must have.</summary>
        public static IEnumerable<string> MarriageKeys(string villager)
        {
            foreach (string w in new[] { "Indoor_Day", "Rainy_Day", "Indoor_Night", "Rainy_Night" })
                for (int i = 0; i < 5; i++)
                    yield return $"{w}_{i}";
            for (int i = 0; i < 5; i++)
                yield return $"Outdoor_{i}";
            foreach (string a in new[] { "Good", "Neutral", "Bad" })
                for (int i = 0; i < 10; i++)
                    yield return $"{a}_{i}";
            for (int i = 0; i < 4; i++)
            {
                yield return $"OneKid_{i}";
                yield return $"TwoKids_{i}";
                yield return $"NoBed_{i}";
            }
            foreach (string k in new[] { "patio", "spouseRoom", "funLeave", "funReturn", "Outdoor" })
                yield return $"{k}_{villager}";
            foreach (string s in Seasons)
                yield return $"{s}_{villager}";
        }

        public static List<string> Problems(NpcContent c, ISet<string> terrariaItems)
        {
            var p = new List<string>();
            string at = c.Npc.Length > 0 ? c.Npc : "(no npc)";
            if (TownHomes.VillagerOf(c.Npc) is not string villager)
            {
                p.Add($"{at}: npc is not a town NPC in TownHomes");
                return p;
            }
            if (c.Villager != villager)
                p.Add($"{at}: villager must be {villager}");
            if (c.Gender != "Male" && c.Gender != "Female" && c.Gender != "Undefined")
                p.Add($"{at}: gender must be Male, Female or Undefined");
            if (c.Romance != Romance.Contains(c.Npc))
                p.Add($"{at}: romance must be {Romance.Contains(c.Npc).ToString().ToLowerInvariant()}");
            if (c.Child != (c.Npc == "Angler"))
                p.Add($"{at}: child must be {(c.Npc == "Angler" ? "true" : "false")}");

            foreach (var line in c.Dialogue)
            {
                if (!DialogueKey.IsMatch(line.Key))
                    p.Add($"{at}: dialogue key '{line.Key}' is not one Terradew uses");
                Line(p, $"{at} dialogue {line.Key}", line.Value);
            }
            foreach (string need in new[] { "Introduction" }.Concat(Days).Concat(Days.Select(d => d + "4")).Concat(Days.Select(d => d + "8")))
            {
                if (!c.Dialogue.ContainsKey(need))
                    p.Add($"{at}: dialogue needs '{need}'");
            }
            if (c.Dialogue.Count < 40)
                p.Add($"{at}: dialogue has {c.Dialogue.Count} lines; write at least 40");
            if (c.Romance && !c.Dialogue.ContainsKey("danceRejection"))
                p.Add($"{at}: romance NPCs need a danceRejection line");
            Line(p, $"{at} rainy", c.Rainy);

            if (c.Romance)
            {
                foreach (string need in MarriageKeys(villager))
                {
                    if (!c.Marriage.TryGetValue(need, out string? text))
                        p.Add($"{at}: marriage needs '{need}'");
                    else
                        Line(p, $"{at} marriage {need}", text);
                }
            }
            else if (c.Marriage.Count > 0)
            {
                p.Add($"{at}: only romance NPCs have marriage lines");
            }

            foreach (string id in Happenings.Ids)
            {
                if (!c.Situations.TryGetValue(id, out string? text))
                    p.Add($"{at}: situations needs '{id}'");
                else
                    Line(p, $"{at} situation {id}", text);
            }
            foreach (string key in c.Situations.Keys.Where(k => !Happenings.IsKnown(k)))
                p.Add($"{at}: situation '{key}' is not a Happenings id");

            var festivalKeys = new HashSet<string>(Festivals);
            if (c.Romance)
                festivalKeys.UnionWith(Festivals.Select(f => f + "_spouse"));
            foreach (string need in festivalKeys)
            {
                if (!c.Festivals.TryGetValue(need, out string? text))
                    p.Add($"{at}: festivals needs '{need}'");
                else
                    Line(p, $"{at} festival {need}", text);
            }
            foreach (string key in c.Festivals.Keys.Where(k => !festivalKeys.Contains(k)))
                p.Add($"{at}: festival key '{key}' is not used");

            foreach (Taste taste in Enum.GetValues(typeof(Taste)))
            {
                GiftTaste g = GiftJudge.Of(c.Gifts, taste);
                string where = $"{at} gifts {taste}";
                Line(p, where, g.Text);
                if (g.Text.Contains('/'))
                    p.Add($"{where}: text must not contain /");
                foreach (string id in g.Stardew)
                {
                    if (!StardewItems.IsKnown(id))
                        p.Add($"{where}: Stardew id '{id}' is not in StardewItems");
                }
                foreach (string name in g.Terraria)
                {
                    if (name.StartsWith("#", StringComparison.Ordinal) ? !GiftTags.IsKnown(name) : !terrariaItems.Contains(name))
                        p.Add($"{where}: Terraria item '{name}' is not an internal item name or gift tag");
                }
                if (taste != Taste.Neutral && g.Stardew.Count + g.Terraria.Count < 3)
                    p.Add($"{where}: list at least 3 items or tags");
            }

            CourageLines courage = c.Courage ?? new CourageLines();
            void Shouts(string what, List<string> lines, int least)
            {
                if (lines.Count < least)
                    p.Add($"{at}: courage {what} needs at least {least} lines");
                foreach (string line in lines)
                {
                    Line(p, $"{at} courage {what}", line);
                    if (line.Length > 60)
                        p.Add($"{at} courage {what}: '{line}' is over 60 characters; it is shown over their head");
                }
            }
            Shouts("help", courage.Help, 3);
            Shouts("battle", courage.Battle, 3);
            if (courage.Thanks.Count < 2)
                p.Add($"{at}: courage thanks needs at least 2 lines");
            foreach (string line in courage.Thanks)
                Line(p, $"{at} courage thanks", line);
            Line(p, $"{at} courage hurt", courage.Hurt);
            foreach (string kind in TownCourage.Dangers)
            {
                if (!courage.Danger.TryGetValue(kind, out string? text))
                    p.Add($"{at}: courage danger needs '{kind}'");
                else
                    Line(p, $"{at} courage danger {kind}", text);
            }
            foreach (string key in courage.Danger.Keys.Where(k => !TownCourage.Dangers.Contains(k)))
                p.Add($"{at}: courage danger '{key}' is not a danger kind");

            var ids = new HashSet<string>();
            foreach (NpcEvent e in c.Events)
            {
                if (!ids.Add(e.Id))
                    p.Add($"{at}: event id '{e.Id}' is used twice");
                p.AddRange(EventCompiler.Problems(c, e, TownHomes.VillagerOf));
                if (e.Reward.Length > 0 && !terrariaItems.Contains(e.Reward))
                    p.Add($"{at} event '{e.Id}': reward '{e.Reward}' is not an internal item name");
            }
            foreach (int hearts in c.Romance ? new[] { 2, 4, 6, 8, 10, 14 } : new[] { 2, 4, 6, 8, 10 })
            {
                if (!c.Events.Any(e => e.Hearts == hearts))
                    p.Add($"{at}: needs a {hearts}-heart event");
            }
            return p;
        }

        private static void Line(List<string> p, string where, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                p.Add($"{where}: empty");
            else if (text.IndexOfAny(new[] { '\n', '{', '}', '"' }) >= 0)
                p.Add($"{where}: must not contain line breaks, braces or double quotes");
        }
    }
}
