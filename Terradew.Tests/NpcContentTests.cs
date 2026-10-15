using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Terradew.Core.Protocol;
using Terradew.Core.Town;

internal static class NpcContentTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static int Run()
    {
        int failed = 0;

        failed += T.Check("protocol: villager spots, gifts and friendships round-trip", () =>
        {
            var spots = new VillagerSpots();
            spots.Spots.Add(new VillagerSpot(17, "SeedShop", 4.5f, 18f, 2));
            T.Assert(((VillagerSpots)ProtocolTests.Roundtrip(spots)).Spots[0] == spots.Spots[0], "spots");
            var gift = (GiftItem)ProtocolTests.Roundtrip(new GiftItem { Seq = 3, Slot = 12, NpcType = 38 });
            T.Assert(gift.Seq == 3 && gift.Slot == 12 && gift.NpcType == 38, "gift");
            var taken = (GiftTaken)ProtocolTests.Roundtrip(new GiftTaken { Seq = 3, NpcType = 38, Ok = true, ItemName = "Dynamite", DisplayName = "Dynamite", Tags = { "#explosive" } });
            T.Assert(taken.Ok && taken.ItemName == "Dynamite" && taken.Tags.Single() == "#explosive", "taken");
            var friends = new Friendships();
            friends.Entries.Add(new FriendshipEntry(17, 1250));
            T.Assert(((Friendships)ProtocolTests.Roundtrip(friends)).Entries[0].Points == 1250, "friendships");
            var talk = (NpcInteract)ProtocolTests.Roundtrip(new NpcInteract { NpcType = 18 });
            T.Assert(talk.NpcId == -1 && talk.NpcType == 18, "talk by type");
            var facts = new WorldFacts();
            facts.Residents.Add(new TownResident(17, "Bob", "SeedShop", 2, 25, 9));
            facts.Happenings.Add("bloodMoon");
            var factsCopy = (WorldFacts)ProtocolTests.Roundtrip(facts);
            T.Assert(factsCopy.Residents[0].FrameCount == 25 && factsCopy.Residents[0].ExtraFrames == 9 && factsCopy.Happenings.Single() == "bloodMoon", "facts");
        });

        failed += T.Check("gifts: names beat tags, loved beats hated, unlisted is neutral", () =>
        {
            var table = new GiftTable();
            table.Loved.Terraria.AddRange(new[] { "Dynamite", "#gem" });
            table.Hated.Terraria.AddRange(new[] { "#explosive", "Gel" });
            table.Liked.Terraria.Add("#ore");
            T.Assert(GiftJudge.Judge(table, "Dynamite", new[] { "#explosive" }) == Taste.Loved, "named loved beats hated tag");
            T.Assert(GiftJudge.Judge(table, "Bomb", new[] { "#explosive" }) == Taste.Hated, "tag");
            T.Assert(GiftJudge.Judge(table, "Ruby", new[] { "#gem", "#explosive" }) == Taste.Loved, "loved tag first");
            T.Assert(GiftJudge.Judge(table, "gel", Array.Empty<string>()) == Taste.Hated, "names ignore case");
            T.Assert(GiftJudge.Judge(table, "DirtBlock", new[] { "#block" }) == Taste.Neutral, "unlisted");
            T.Assert(GiftJudge.Points(Taste.Loved, true) == 640 && GiftJudge.Points(Taste.Hated, false) == -40, "points");
            T.Assert(GiftJudge.Discount(1249) == 0.08 && GiftJudge.Discount(5000) == 0.2 && GiftJudge.Discount(-50) == 0, "discount");
        });

        failed += T.Check("happenings: most important new one, passing ones repeat", () =>
        {
            var spoken = new HashSet<string> { "eye" };
            T.Assert(Happenings.Pick(new[] { "eye", "goblins" }, spoken) == "goblins", "new one");
            T.Assert(Happenings.Pick(new[] { "eye" }, spoken) == null, "said already");
            T.Assert(Happenings.Pick(new[] { "bloodMoon", "eye" }, new HashSet<string> { "bloodMoon", "eye" }) == "bloodMoon", "passing repeats");
            T.Assert(Happenings.Pick(new[] { "goblins", "hardmode" }, spoken) == "hardmode", "importance order");
        });

        failed += T.Check("events: compile to a stage around the npc", () =>
        {
            var npc = new NpcContent { Npc = "Merchant", Villager = "Pierre" };
            var e = new NpcEvent
            {
                Id = "h2", Hearts = 2, Time = "day", With = { "Guide" },
                Steps =
                {
                    new EventStep { Say = "Hello, @!$h" },
                    new EventStep { Face = "self", Toward = "Guide" },
                    new EventStep { Move = "farmer", Dx = 1, Dy = -1 },
                    new EventStep { Emote = "heart", Who = "Guide" },
                    new EventStep { Question = "Buy something?", Answers = new() { new EventAnswer { Text = "Yes", Friendship = 20, Reply = "Splendid!" }, new EventAnswer { Text = "No", Reply = "Pity." } } },
                },
                After = "Come back soon.",
            };
            T.Assert(EventCompiler.Problems(npc, e, TownHomes.VillagerOf).Count == 0, string.Join("; ", EventCompiler.Problems(npc, e, TownHomes.VillagerOf)));
            (string key, string script) = EventCompiler.Compile(npc, e, TownHomes.VillagerOf, 10, 20);
            T.Assert(key == "Terradew_Merchant_h2/Friendship Pierre 500/Time 600 1900/NpcVisibleHere Pierre", key);
            T.Assert(script.StartsWith("none/10 20/Pierre 10 20 2 farmer 12 20 3 Gunther 8 20 1/skippable/"), script);
            T.Assert(script.Contains("/faceDirection Pierre 3/") && script.Contains("/move farmer 1 0 1/move farmer 0 -1 0/"), script);
            T.Assert(script.Contains("/emote Gunther 20/") && script.Contains("quickQuestion Buy something?#Yes#No(break)speak Pierre \"Splendid!\"\\friendship Pierre 20(break)speak Pierre \"Pity.\"/"), script);
            T.Assert(script.EndsWith("/end dialogue Pierre \"Come back soon.\""), script);
        });

        failed += T.Check("events: walking off the stage or into someone is caught", () =>
        {
            var npc = new NpcContent { Npc = "Merchant", Villager = "Pierre" };
            var e = new NpcEvent { Id = "x", Hearts = 4, Steps = { new EventStep { Say = "Hi" }, new EventStep { Move = "self", Dx = -4 }, new EventStep { Move = "farmer", Dx = -2 } } };
            List<string> problems = EventCompiler.Problems(npc, e, TownHomes.VillagerOf);
            T.Assert(problems.Any(p => p.Contains("off the stage")), "off stage");
            var bump = new NpcEvent { Id = "y", Hearts = 4, Steps = { new EventStep { Say = "Hi" }, new EventStep { Move = "farmer", Dx = -2 }, new EventStep { Pause = 300 } } };
            T.Assert(EventCompiler.Problems(npc, bump, TownHomes.VillagerOf).Any(p => p.Contains("into another actor")), "bump");
        });

        failed += T.Check("content: every town npc has a valid file", () =>
        {
            List<string> problems = Problems(TownHomes.Npcs);
            T.Assert(problems.Count == 0, problems.Count + " problems:\n  " + string.Join("\n  ", problems.Take(60)));
        });

        return failed;
    }

    /// <summary>"Terradew.Tests check Merchant": checks just those files and prints every problem.</summary>
    public static int CheckOnly(IEnumerable<string> npcs)
    {
        List<string> problems = Problems(npcs);
        foreach (string problem in problems)
            Console.WriteLine(problem);
        Console.WriteLine(problems.Count == 0 ? "content ok" : $"{problems.Count} problems");
        return problems.Count == 0 ? 0 : 1;
    }

    private static List<string> Problems(IEnumerable<string> npcs)
    {
        string root = Root();
        var items = new HashSet<string>(File.ReadAllLines(Path.Combine(root, "Terradew.Tests", "Data", "TerrariaItems.txt")).Where(l => l.Length > 0), StringComparer.Ordinal);
        string folder = Path.Combine(root, "Terradew", "assets", "npcs");
        var problems = new List<string>();
        foreach (string npc in npcs)
        {
            string file = Path.Combine(folder, npc + ".json");
            if (!File.Exists(file))
            {
                problems.Add($"{npc}: no file");
                continue;
            }
            NpcContent? content;
            try
            {
                content = JsonSerializer.Deserialize<NpcContent>(File.ReadAllText(file), Json);
            }
            catch (Exception error)
            {
                problems.Add($"{npc}: {error.Message}");
                continue;
            }
            if (content == null || content.Npc != npc)
                problems.Add($"{npc}: npc field must be {npc}");
            else
                problems.AddRange(ContentCheck.Problems(content, items));
        }
        return problems;
    }

    private static string Root()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "README.md")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new Exception("repo root not found");
    }
}
