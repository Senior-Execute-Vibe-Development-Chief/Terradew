using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Characters;
using Terradew.Core.Protocol;
using Terradew.Core.Town;

namespace Terradew;

/// <summary>
/// The written side of the town: each villager's lines, gift tastes and heart events come from assets/npcs, the
/// villager's Terraria NPC follows their schedule, and Terraria items can be given as gifts.
/// </summary>
internal sealed class NpcStories
{
    private const string SpokenKey = "Terradew.Spoken";
    private const string RewardsKey = "Terradew.EventRewards";

    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly Bridge bridge;
    /// <summary>Content by Terraria NPC name. Files that fail to load are left out.</summary>
    private readonly Dictionary<string, NpcContent> content = new(StringComparer.Ordinal);
    /// <summary>Gift requests waiting on Terraria, by seq: the villager the gift is for.</summary>
    private readonly Dictionary<int, string> pendingGifts = new();
    private int giftSeq;

    public NpcStories(IModHelper helper, IMonitor monitor, Bridge bridge)
    {
        this.helper = helper;
        this.monitor = monitor;
        this.bridge = bridge;
        foreach (string npc in TownHomes.Npcs)
        {
            try
            {
                NpcContent? lines = helper.Data.ReadJsonFile<NpcContent>($"assets/npcs/{npc}.json");
                if (lines == null)
                    monitor.Log($"Terradew has no lines for the {npc} (assets/npcs/{npc}.json); they keep the villager's own.", LogLevel.Warn);
                else if (lines.Npc != npc || lines.Villager != TownHomes.VillagerOf(npc))
                    monitor.Log($"Terradew skipped assets/npcs/{npc}.json: it names {lines.Npc} as {lines.Villager}.", LogLevel.Warn);
                else
                    content[npc] = lines;
            }
            catch (Exception error)
            {
                monitor.Log($"Terradew could not read assets/npcs/{npc}.json. {error.Message}", LogLevel.Warn);
            }
        }
    }

    public NpcContent? ContentOf(string npc) => content.TryGetValue(npc, out NpcContent? lines) ? lines : null;

    private NpcContent? ContentOfVillager(string villager)
        => TownHomes.NpcOf(villager) is string npc ? ContentOf(npc) : null;

    public void Reset() => pendingGifts.Clear();

    // ---- Talking and gifts ----

    /// <summary>
    /// The farmer acts on a villager. True when Terradew handled it: a Terraria gift, or the Terraria chat once the
    /// day's talk is done. False leaves it to Stardew.
    /// </summary>
    public bool Interact(NPC npc, Farmer who, Backpack backpack, Hand hand)
    {
        if (Game1.CurrentEvent != null || Game1.isFestival())
            return false;
        string? npcName = TownHomes.NpcOf(npc.Name);
        int? type = npcName == null ? null : TownHomes.TypeOf(npcName);
        if (type == null || Cast.Playing(npc.Name) == null)
            return false;
        if (!who.friendshipData.TryGetValue(npc.Name, out Friendship friendship))
            return false;

        if (backpack.TerrariaOwns(hand.Slot))
        {
            bool spouse = who.spouse == npc.Name;
            if (friendship.GiftsToday >= 1)
            {
                Game1.drawObjectDialogue($"You've already given {npc.displayName} a gift today.");
                return true;
            }
            if (friendship.GiftsThisWeek >= 2 && !spouse && !npc.isBirthday())
            {
                Game1.drawObjectDialogue($"You've already given {npc.displayName} two gifts this week.");
                return true;
            }
            if (!bridge.IsConnected)
                return false;
            int seq = ++giftSeq;
            pendingGifts[seq] = npc.Name;
            bridge.Send(new GiftItem { Seq = seq, Slot = hand.Slot, NpcType = type.Value });
            return true;
        }

        // Once today's talk is done, a second click opens the Terraria chat with its shop and services.
        bool giftable = who.ActiveObject != null && !Backpack.IsPlaceholder(who.ActiveObject) && who.ActiveObject.canBeGivenAsGift();
        if (friendship.TalkedToToday && !giftable && npc.CurrentDialogue.Count == 0 && bridge.IsConnected)
        {
            bridge.Send(new NpcInteract { NpcId = -1, NpcType = type.Value });
            return true;
        }
        return false;
    }

    public void OnGiftTaken(GiftTaken taken)
    {
        string? villager = pendingGifts.TryGetValue(taken.Seq, out string? name) ? name : null;
        pendingGifts.Remove(taken.Seq);
        if (!taken.Ok || villager == null || !Context.IsWorldReady)
            return;
        try
        {
            NPC? npc = Game1.getCharacterFromName(villager);
            NpcContent? lines = ContentOfVillager(villager);
            if (npc == null || lines == null || !Game1.player.friendshipData.TryGetValue(villager, out Friendship friendship))
                return;
            Taste taste = GiftJudge.Judge(lines.Gifts, taken.ItemName, taken.Tags);
            Game1.player.changeFriendship(GiftJudge.Points(taste, npc.isBirthday()), npc);
            npc.doEmote(GiftJudge.Emote(taste));
            friendship.GiftsToday++;
            friendship.GiftsThisWeek++;
            friendship.LastGiftDate = new WorldDate(Game1.Date);
            string text = GiftJudge.Of(lines.Gifts, taste).Text;
            if (text.Length > 0)
            {
                npc.CurrentDialogue.Push(new Dialogue(npc, null, text));
                Game1.drawDialogue(npc);
            }
            SendFriendships();
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not hand over a gift. {error.Message}", LogLevel.Warn);
        }
    }

    // ---- Following ----

    public void Update(uint ticks)
    {
        try
        {
            // Villagers nobody plays leave cutscenes and festivals too.
            Game1.CurrentEvent?.actors.RemoveAll(actor => !Cast.Shown(actor));
            if (ticks % 30 == 0)
                SendSpots();
            if (ticks % 600 == 0)
            {
                SendFriendships();
                GiveRewards();
            }
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not update the town NPCs. {error.Message}", LogLevel.Warn);
        }
    }

    private void SendSpots()
    {
        if (!bridge.IsConnected)
            return;
        var message = new VillagerSpots();
        Utility.ForEachVillager(npc =>
        {
            if (npc.currentLocation == null || Cast.Playing(npc.Name) is not TownResident resident)
                return true;
            Rectangle box = npc.GetBoundingBox();
            message.Spots.Add(new VillagerSpot(resident.NpcType, Maps.PlaceOf(npc.currentLocation).Key,
                box.Center.X / 64f, box.Bottom / 64f, (byte)npc.FacingDirection));
            return true;
        });
        bridge.Send(message);
    }

    public void SendFriendships()
    {
        if (!bridge.IsConnected || !Context.IsWorldReady)
            return;
        var message = new Friendships();
        foreach (string npc in TownHomes.Npcs)
        {
            if (TownHomes.TypeOf(npc) is not int type || TownHomes.VillagerOf(npc) is not string villager)
                continue;
            int points = Game1.player.friendshipData.TryGetValue(villager, out Friendship friendship) ? friendship.Points : 0;
            message.Entries.Add(new FriendshipEntry(type, points));
        }
        bridge.Send(message);
    }

    // ---- Mornings ----

    public void OnDayStarted()
    {
        try
        {
            SendFriendships();
            GiveRewards();
            Situations();
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not start the town's day. {error.Message}", LogLevel.Warn);
        }
    }

    /// <summary>Each NPC talks about the most important new thing in the Terraria world.</summary>
    private void Situations()
    {
        if (bridge.Facts is not WorldFacts facts)
            return;
        var data = Game1.player.modData;
        var spoken = new HashSet<string>(
            data.TryGetValue(SpokenKey, out string? text) ? text.Split(',', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>(),
            StringComparer.Ordinal);
        string? happening = Happenings.Pick(facts.Happenings, spoken);
        if (happening == null)
            return;
        Utility.ForEachVillager(npc =>
        {
            if (Cast.Playing(npc.Name) == null || ContentOfVillager(npc.Name) is not NpcContent lines)
                return true;
            if (lines.Situations.TryGetValue(happening, out string? line) && line.Length > 0)
                npc.CurrentDialogue.Push(new Dialogue(npc, null, line));
            return true;
        });
        if (!Happenings.IsPassing(happening))
        {
            spoken.Add(happening);
            data[SpokenKey] = string.Join(",", spoken);
        }
    }

    /// <summary>Hands over the Terraria item each seen heart event promises, once.</summary>
    private void GiveRewards()
    {
        if (!Context.IsWorldReady || !bridge.IsConnected || Game1.CurrentEvent != null)
            return;
        var data = Game1.player.modData;
        var given = new HashSet<string>(
            data.TryGetValue(RewardsKey, out string? text) ? text.Split(',', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>(),
            StringComparer.Ordinal);
        bool changed = false;
        foreach (NpcContent lines in content.Values)
        {
            foreach (NpcEvent e in lines.Events)
            {
                string id = EventCompiler.EventId(lines, e);
                if (e.Reward.Length == 0 || given.Contains(id) || !Game1.player.eventsSeen.Contains(id))
                    continue;
                if (!bridge.Send(new GiveItem { Name = e.Reward, Stack = Math.Max(1, e.RewardCount) }))
                    continue;
                given.Add(id);
                changed = true;
            }
        }
        if (changed)
            data[RewardsKey] = string.Join(",", given);
    }

    // ---- Content ----

    public void OnAssetRequested(AssetRequestedEventArgs e)
    {
        IAssetName name = e.NameWithoutLocale;
        if (name.IsEquivalentTo("Data/NPCGiftTastes"))
        {
            e.Edit(asset => Safely("gift tastes", () => EditGiftTastes(asset.AsDictionary<string, string>().Data)), AssetEditPriority.Late);
            return;
        }
        if (name.IsEquivalentTo("Characters/Dialogue/rainy"))
        {
            e.Edit(asset => Safely("rainy lines", () =>
            {
                var data = asset.AsDictionary<string, string>().Data;
                foreach (NpcContent lines in content.Values)
                {
                    if (lines.Rainy.Length > 0)
                        data[lines.Villager] = lines.Rainy;
                }
            }), AssetEditPriority.Late);
            return;
        }
        if (name.StartsWith("Data/Festivals/"))
        {
            string festival = name.BaseName.Substring("Data/Festivals/".Length);
            if (ContentCheck.Festivals.Contains(festival))
                e.Edit(asset => Safely("festival lines", () => EditFestival(festival, asset.AsDictionary<string, string>().Data)), AssetEditPriority.Late);
            return;
        }
        if (name.StartsWith("Data/Events/"))
        {
            string location = name.BaseName.Substring("Data/Events/".Length);
            e.Edit(asset => Safely("heart events", () => EditEvents(location, asset.AsDictionary<string, string>().Data)), AssetEditPriority.Late);
            return;
        }
        foreach (NpcContent lines in content.Values)
        {
            if (name.IsEquivalentTo($"Characters/Dialogue/{lines.Villager}") && lines.Dialogue.Count > 0)
            {
                e.LoadFrom(() => new Dictionary<string, string>(lines.Dialogue), AssetLoadPriority.High);
                return;
            }
            if (name.IsEquivalentTo($"Characters/Dialogue/MarriageDialogue{lines.Villager}") && lines.Romance && lines.Marriage.Count > 0)
            {
                e.LoadFrom(() => new Dictionary<string, string>(lines.Marriage), AssetLoadPriority.High);
                return;
            }
        }
    }

    private void Safely(string what, Action edit)
    {
        try
        {
            edit();
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not write the town's {what}. {error.Message}", LogLevel.Warn);
        }
    }

    private void EditGiftTastes(IDictionary<string, string> data)
    {
        foreach (NpcContent lines in content.Values)
        {
            static string Part(GiftTaste taste) => $"{taste.Text}/{string.Join(" ", taste.Stardew)}";
            GiftTable g = lines.Gifts;
            data[lines.Villager] = string.Join("/", Part(g.Loved), Part(g.Liked), Part(g.Disliked), Part(g.Hated), Part(g.Neutral));
        }
    }

    private void EditFestival(string festival, IDictionary<string, string> data)
    {
        foreach (NpcContent lines in content.Values)
        {
            if (lines.Festivals.TryGetValue(festival, out string? line))
                data[lines.Villager] = line;
            if (lines.Festivals.TryGetValue(festival + "_spouse", out string? spouse))
                data[lines.Villager + "_spouse"] = spouse;
            // The second-year line would bring the villager's own voice back.
            data.Remove(lines.Villager + "_y2");
        }
    }

    private void EditEvents(string location, IDictionary<string, string> data)
    {
        // The villagers' own heart events belong to people who no longer live here.
        var names = new HashSet<string>(Cast.Unplayed, StringComparer.Ordinal);
        foreach (string npc in TownHomes.Npcs)
        {
            if (TownHomes.VillagerOf(npc) is string villager)
                names.Add(villager);
        }
        foreach (string key in data.Keys.ToList())
        {
            if (!key.StartsWith("Terradew_", StringComparison.Ordinal) && IsHeartEvent(key, names))
                data.Remove(key);
        }

        foreach (NpcContent lines in content.Values)
        {
            CharacterHomeData? home = Game1.characterData.TryGetValue(lines.Villager, out CharacterData? character)
                ? character.Home?.FirstOrDefault()
                : null;
            foreach (NpcEvent ev in lines.Events)
            {
                if (home == null && ev.Where == "home")
                    continue;
                string where = EventCompiler.Location(ev, home?.Location ?? "");
                if (where != location)
                    continue;
                try
                {
                    Point center = Stage(location, ev.Where == "home" ? home!.Tile : null);
                    (string key, string script) = EventCompiler.Compile(lines, ev, TownHomes.VillagerOf, center.X, center.Y);
                    string id = EventCompiler.EventId(lines, ev);
                    foreach (string old in data.Keys.Where(k => k == id || k.StartsWith(id + "/", StringComparison.Ordinal)).ToList())
                        data.Remove(old);
                    data[key] = script;
                }
                catch (Exception error)
                {
                    monitor.Log($"Terradew left out the {lines.Npc}'s event '{ev.Id}'. {error.Message}", LogLevel.Warn);
                }
            }
        }
    }

    /// <summary>An event gated on friendship, dating or marriage with one of these villagers.</summary>
    private static bool IsHeartEvent(string key, ISet<string> names)
    {
        string[] parts = key.Split('/');
        for (int i = 1; i < parts.Length; i++)
        {
            string[] words = parts[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2)
                continue;
            string kind = words[0].TrimStart('!');
            if (kind is not ("f" or "Friendship" or "D" or "Dating" or "O" or "Spouse"))
                continue;
            if (words.Skip(1).Any(names.Contains))
                return true;
        }
        return false;
    }

    /// <summary>The middle of an open 7x3 block of floor near the start tile (the map's middle when there is none).</summary>
    private static Point Stage(string locationName, Point? near)
    {
        GameLocation? location = Game1.getLocationFromName(locationName);
        if (location?.Map == null)
            return near ?? new Point(10, 10);
        int width = location.Map.Layers[0].LayerWidth;
        int height = location.Map.Layers[0].LayerHeight;
        Point start = near ?? new Point(width / 2, height / 2);
        for (int r = 0; r < 40; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                        continue;
                    int cx = start.X + dx;
                    int cy = start.Y + dy;
                    if (Open(location, cx, cy, 3, 1))
                        return new Point(cx, cy);
                }
            }
        }
        return start;
    }

    private static bool Open(GameLocation location, int cx, int cy, int halfWidth, int halfHeight)
    {
        for (int y = cy - halfHeight; y <= cy + halfHeight; y++)
        {
            for (int x = cx - halfWidth; x <= cx + halfWidth; x++)
            {
                var tile = new Vector2(x, y);
                if (!location.isTileOnMap(tile) || location.getTileIndexAt(x, y, "Buildings") != -1 || !location.isTilePassable(tile))
                    return false;
                if (location.objects.ContainsKey(tile) || location.GetFurnitureAt(tile) != null)
                    return false;
            }
        }
        return true;
    }
}
