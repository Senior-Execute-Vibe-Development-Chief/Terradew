using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Terradew.Core.Town
{
    /// <summary>
    /// Turns a written <see cref="NpcEvent"/> into a Stardew event (Data/Events key and script). Every event plays
    /// on a small stage of open tiles, 7 wide and 3 tall, that Stardew finds at runtime; the NPC stands in the
    /// middle, the farmer two tiles right and any other NPCs to the left and below. Actors never leave the stage,
    /// so the script cannot walk anyone into a wall whatever the map looks like.
    /// </summary>
    public static class EventCompiler
    {
        public const int HalfWidth = 3;
        public const int HalfHeight = 1;
        public const int MaxWith = 2;

        /// <summary>Places an event may happen besides "home". FarmHouse is for married couples only.</summary>
        public static IReadOnlyList<string> Places { get; } = new[] { "Town", "Beach", "Forest", "Mountain", "BusStop", "Saloon", "Farm", "FarmHouse" };

        public static IReadOnlyList<string> Music { get; } = new[] { "none", "continue", "playful", "sweet", "50s", "echos", "jaunty", "musicboxsong", "event1", "event2" };

        public static IReadOnlyList<string> Sounds { get; } = new[] { "dwop", "coin", "doorClose", "crafting", "gulp", "pickUpItem", "explosion", "thunder", "dog", "cat", "shwip", "yoba", "croak", "fishBite" };

        public static IReadOnlyDictionary<string, int> Emotes { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["empty"] = 4,
            ["question"] = 8,
            ["angry"] = 12,
            ["exclamation"] = 16,
            ["heart"] = 20,
            ["sleep"] = 24,
            ["sad"] = 28,
            ["happy"] = 32,
            ["x"] = 36,
            ["pause"] = 40,
        };

        public static IReadOnlyList<int> HeartLevels { get; } = new[] { 2, 4, 6, 8, 10, 14 };

        /// <summary>The Data/Events id for an event: unique across every NPC.</summary>
        public static string EventId(NpcContent npc, NpcEvent e) => $"Terradew_{npc.Npc}_{e.Id}";

        /// <summary>The Data/Events location the event belongs to. "home" resolves to the villager's home location.</summary>
        public static string Location(NpcEvent e, string homeLocation) => e.Where == "home" ? homeLocation : e.Where;

        /// <summary>
        /// Compiles the event. <paramref name="villagerOf"/> maps a Terraria NPC name to its Stardew villager name.
        /// (centerX, centerY) is the NPC's tile in the middle of the stage. Throws <see cref="FormatException"/> when the event is not valid.
        /// </summary>
        public static (string Key, string Script) Compile(NpcContent npc, NpcEvent e, Func<string, string?> villagerOf, int centerX, int centerY)
        {
            List<string> problems = Problems(npc, e, villagerOf);
            if (problems.Count > 0)
                throw new FormatException(string.Join("; ", problems));

            var actors = new Dictionary<string, Actor>(StringComparer.Ordinal)
            {
                ["self"] = new Actor(npc.Villager, 0, 0, 2),
                ["farmer"] = new Actor("farmer", 2, 0, 3),
            };
            for (int i = 0; i < e.With.Count; i++)
            {
                string name = villagerOf(e.With[i])!;
                actors[e.With[i]] = i == 0 ? new Actor(name, -2, 0, 1) : new Actor(name, 0, 1, 0);
            }

            var key = new StringBuilder(EventId(npc, e));
            key.Append($"/Friendship {npc.Villager} {Math.Min(e.Hearts, 10) * 250}");
            if (e.Time == "day")
                key.Append("/Time 600 1900");
            else if (e.Time == "night")
                key.Append("/Time 1900 2600");
            if (e.Weather == "sunny" || e.Weather == "rainy")
                key.Append($"/Weather {e.Weather}");
            if (npc.Romance && e.Hearts == 10)
                key.Append($"/Dating {npc.Villager}");
            if (e.Hearts == 14)
                key.Append($"/Spouse {npc.Villager}");
            if (e.Where == "home")
                key.Append($"/NpcVisibleHere {npc.Villager}");

            var script = new List<string>
            {
                e.Music == "continue" ? "continue" : e.Music,
                $"{centerX} {centerY}",
                string.Join(" ", actors.Values.Select(a => $"{a.Name} {centerX + a.X} {centerY + a.Y} {a.Facing}")),
                "skippable",
                $"viewport {centerX} {centerY} true",
                "pause 600",
            };
            foreach (EventStep step in e.Steps)
                script.Add(Step(step, actors));
            script.Add("pause 400");
            script.Add(e.After.Length > 0 ? $"end dialogue {npc.Villager} \"{Text(e.After)}\"" : "end");
            return (key.ToString(), string.Join("/", script));
        }

        /// <summary>Everything wrong with an event, empty when it compiles. Positions are checked by playing it through.</summary>
        public static List<string> Problems(NpcContent npc, NpcEvent e, Func<string, string?> villagerOf)
        {
            var problems = new List<string>();
            string at = $"{npc.Npc} event '{e.Id}'";
            if (string.IsNullOrWhiteSpace(e.Id) || !e.Id.All(c => char.IsLetterOrDigit(c) || c == '_'))
                problems.Add($"{at}: id must be letters, digits or _");
            if (!HeartLevels.Contains(e.Hearts))
                problems.Add($"{at}: hearts must be one of {string.Join(", ", HeartLevels)}");
            if (e.Hearts == 14 && !npc.Romance)
                problems.Add($"{at}: 14-heart events are for romance NPCs");
            if (e.Where != "home" && !Places.Contains(e.Where))
                problems.Add($"{at}: where '{e.Where}' is not home or one of {string.Join(", ", Places)}");
            if (e.Where == "FarmHouse" && e.Hearts != 14)
                problems.Add($"{at}: FarmHouse events must be 14-heart (married)");
            if (e.Time != "day" && e.Time != "night" && e.Time != "any")
                problems.Add($"{at}: time must be day, night or any");
            if (e.Weather != "sunny" && e.Weather != "rainy" && e.Weather != "any")
                problems.Add($"{at}: weather must be sunny, rainy or any");
            if (!Music.Contains(e.Music))
                problems.Add($"{at}: music '{e.Music}' is not one of {string.Join(", ", Music)}");
            if (e.With.Count > MaxWith)
                problems.Add($"{at}: at most {MaxWith} other NPCs");
            foreach (string other in e.With)
            {
                if (other == npc.Npc || villagerOf(other) == null)
                    problems.Add($"{at}: '{other}' in With is not another town NPC");
            }
            if (e.With.Distinct().Count() != e.With.Count)
                problems.Add($"{at}: With lists an NPC twice");
            if (e.Steps.Count < 3)
                problems.Add($"{at}: needs at least 3 steps");
            if (!e.Steps.Any(s => s.Say != null))
                problems.Add($"{at}: nobody says anything");
            if (e.After.Length > 0)
                CheckText(problems, at, "after", e.After);
            if (e.RewardCount < 1 || e.RewardCount > 999)
                problems.Add($"{at}: rewardCount must be 1-999");

            var spots = new Dictionary<string, (int X, int Y)>(StringComparer.Ordinal) { ["self"] = (0, 0), ["farmer"] = (2, 0) };
            for (int i = 0; i < e.With.Count && i < MaxWith; i++)
                spots[e.With[i]] = i == 0 ? (-2, 0) : (0, 1);

            for (int i = 0; i < e.Steps.Count; i++)
            {
                EventStep s = e.Steps[i];
                string where = $"{at} step {i + 1}";
                int kinds = new object?[] { s.Say, s.Emote, s.Move, s.Face, s.Jump, s.Sound, s.Question }.Count(x => x != null) + (s.Pause != 0 ? 1 : 0);
                if (kinds != 1)
                {
                    problems.Add($"{where}: set exactly one of say, emote, move, face, pause, jump, sound, question");
                    continue;
                }
                if (s.Say != null)
                {
                    ActorKnown(problems, where, s.Who ?? "self", spots, allowFarmer: false);
                    CheckText(problems, where, "say", s.Say);
                }
                else if (s.Emote != null)
                {
                    ActorKnown(problems, where, s.Who ?? "self", spots, allowFarmer: true);
                    if (!Emotes.ContainsKey(s.Emote))
                        problems.Add($"{where}: emote '{s.Emote}' is not one of {string.Join(", ", Emotes.Keys)}");
                }
                else if (s.Move != null)
                {
                    if (!ActorKnown(problems, where, s.Move, spots, allowFarmer: true))
                        continue;
                    if (s.Dx == 0 && s.Dy == 0)
                        problems.Add($"{where}: move needs dx or dy");
                    (int x, int y) = spots[s.Move];
                    // Moves go along x first, then y; check both legs tile by tile.
                    foreach ((int px, int py) in Path(x, y, s.Dx, s.Dy))
                    {
                        if (Math.Abs(px) > HalfWidth || Math.Abs(py) > HalfHeight)
                        {
                            problems.Add($"{where}: {s.Move} walks off the stage to ({px}, {py}); stay within x -{HalfWidth}..{HalfWidth}, y -{HalfHeight}..{HalfHeight} of the NPC's start");
                            break;
                        }
                        if (spots.Any(o => o.Key != s.Move && o.Value == (px, py)))
                        {
                            problems.Add($"{where}: {s.Move} walks into another actor at ({px}, {py})");
                            break;
                        }
                    }
                    spots[s.Move] = (x + s.Dx, y + s.Dy);
                }
                else if (s.Face != null)
                {
                    ActorKnown(problems, where, s.Face, spots, allowFarmer: true);
                    string toward = s.Toward ?? "";
                    if (Direction(toward) < 0 && !spots.ContainsKey(toward))
                        problems.Add($"{where}: toward must be up, down, left, right or an actor in the scene");
                    else if (toward == s.Face)
                        problems.Add($"{where}: an actor cannot face themselves");
                }
                else if (s.Jump != null)
                {
                    ActorKnown(problems, where, s.Jump, spots, allowFarmer: true);
                }
                else if (s.Sound != null)
                {
                    if (!Sounds.Contains(s.Sound))
                        problems.Add($"{where}: sound '{s.Sound}' is not one of {string.Join(", ", Sounds)}");
                }
                else if (s.Question != null)
                {
                    ActorKnown(problems, where, s.Who ?? "self", spots, allowFarmer: false);
                    CheckPlain(problems, where, "question", s.Question);
                    if (s.Answers == null || s.Answers.Count < 2 || s.Answers.Count > 3)
                    {
                        problems.Add($"{where}: a question needs 2 or 3 answers");
                        continue;
                    }
                    foreach (EventAnswer a in s.Answers)
                    {
                        CheckPlain(problems, where, "answer", a.Text);
                        CheckText(problems, where, "reply", a.Reply);
                        if (a.Friendship < -100 || a.Friendship > 100)
                            problems.Add($"{where}: answer friendship must be -100..100");
                    }
                }
                else if (s.Pause < 100 || s.Pause > 5000)
                {
                    problems.Add($"{where}: pause must be 100-5000 ms");
                }
            }
            return problems;
        }

        private static IEnumerable<(int X, int Y)> Path(int x, int y, int dx, int dy)
        {
            for (int i = 1; i <= Math.Abs(dx); i++)
                yield return (x + Math.Sign(dx) * i, y);
            for (int i = 1; i <= Math.Abs(dy); i++)
                yield return (x + dx, y + Math.Sign(dy) * i);
        }

        private static bool ActorKnown(List<string> problems, string where, string actor, Dictionary<string, (int X, int Y)> spots, bool allowFarmer)
        {
            if (!spots.ContainsKey(actor) || (!allowFarmer && actor == "farmer"))
            {
                problems.Add($"{where}: '{actor}' is not {(allowFarmer ? "self, farmer" : "self")} or an NPC in With");
                return false;
            }
            return true;
        }

        /// <summary>Dialogue text: Stardew portrait and page codes are fine; characters that break a script are not.</summary>
        public static void CheckText(List<string> problems, string where, string field, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                problems.Add($"{where}: {field} is empty");
            else if (text.IndexOfAny(new[] { '"', '/', '\\', '|', '{', '}', '\n' }) >= 0)
                problems.Add($"{where}: {field} must not contain \" / \\ | {{ }} or line breaks: {text}");
            else if (text.Contains("(break)"))
                problems.Add($"{where}: {field} must not contain (break)");
        }

        /// <summary>Question and answer text also must not use # or $ codes.</summary>
        private static void CheckPlain(List<string> problems, string where, string field, string text)
        {
            CheckText(problems, where, field, text);
            if (text != null && text.IndexOfAny(new[] { '#', '$', '^' }) >= 0)
                problems.Add($"{where}: {field} must be plain text without # $ ^: {text}");
        }

        private static string Step(EventStep s, Dictionary<string, Actor> actors)
        {
            if (s.Say != null)
                return $"speak {actors[s.Who ?? "self"].Name} \"{Text(s.Say)}\"";
            if (s.Emote != null)
                return $"emote {actors[s.Who ?? "self"].Name} {Emotes[s.Emote]}";
            if (s.Move != null)
            {
                Actor a = actors[s.Move];
                var moves = new List<string>();
                if (s.Dx != 0)
                    moves.Add($"move {a.Name} {s.Dx} 0 {(s.Dx > 0 ? 1 : 3)}");
                if (s.Dy != 0)
                    moves.Add($"move {a.Name} 0 {s.Dy} {(s.Dy > 0 ? 2 : 0)}");
                a.X += s.Dx;
                a.Y += s.Dy;
                a.Facing = s.Dy != 0 ? (s.Dy > 0 ? 2 : 0) : (s.Dx > 0 ? 1 : 3);
                return string.Join("/", moves);
            }
            if (s.Face != null)
            {
                Actor a = actors[s.Face];
                int facing = Direction(s.Toward ?? "");
                if (facing < 0)
                {
                    Actor other = actors[s.Toward!];
                    int dx = other.X - a.X;
                    int dy = other.Y - a.Y;
                    facing = Math.Abs(dx) >= Math.Abs(dy) ? (dx >= 0 ? 1 : 3) : (dy > 0 ? 2 : 0);
                }
                a.Facing = facing;
                return $"faceDirection {a.Name} {facing}";
            }
            if (s.Jump != null)
                return $"jump {actors[s.Jump].Name}";
            if (s.Sound != null)
                return $"playSound {s.Sound}";
            if (s.Question != null)
            {
                string speaker = actors[s.Who ?? "self"].Name;
                var text = new StringBuilder($"quickQuestion {s.Question}");
                foreach (EventAnswer answer in s.Answers!)
                    text.Append('#').Append(answer.Text);
                foreach (EventAnswer answer in s.Answers!)
                {
                    text.Append("(break)").Append($"speak {speaker} \"{Text(answer.Reply)}\"");
                    if (answer.Friendship != 0)
                        text.Append($"\\friendship {speaker} {answer.Friendship}");
                }
                return text.ToString();
            }
            return $"pause {s.Pause}";
        }

        private static string Text(string text) => text.Trim();

        public static int Direction(string name) => name switch
        {
            "up" => 0,
            "right" => 1,
            "down" => 2,
            "left" => 3,
            _ => -1,
        };

        private sealed class Actor
        {
            public Actor(string name, int x, int y, int facing)
            {
                Name = name;
                X = x;
                Y = y;
                Facing = facing;
            }

            public string Name { get; }
            public int X { get; set; }
            public int Y { get; set; }
            public int Facing { get; set; }
        }
    }
}
