using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terradew.Core;
using Terradew.Core.Protocol;
using Terradew.Core.Town;
using Terradew.Core.World;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace TerradewBridge;

/// <summary>
/// Terraria town NPCs live in the homes of the Stardew villagers (see TownHomes). Each new Stardew morning,
/// one NPC whose Terraria conditions are met, and whose home Stardew has sent, moves in. Residents who died come back.
/// </summary>
public sealed class Town
{
    private readonly List<HomeEntry> homes = new();
    /// <summary>Resident NPC internal name to the place key of its house.</summary>
    private readonly Dictionary<string, string> residents = new();
    private bool visitedOcean;

    public void Clear()
    {
        homes.Clear();
        residents.Clear();
        visitedOcean = false;
        sawBloodMoon = sawEclipse = sawParty = false;
        Followers.Clear();
    }

    public void Load(string savedHomes, string savedResidents)
    {
        Clear();
        foreach (string line in (savedHomes ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split('|');
            if (parts.Length == 4 && int.TryParse(parts[2], out int x) && int.TryParse(parts[3], out int y))
                homes.Add(new HomeEntry(parts[0], parts[1], x, y));
        }
        foreach (string line in (savedResidents ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split('|');
            if (parts.Length == 2)
            {
                if (parts[0] == "#ocean")
                    visitedOcean = true;
                else
                    residents[parts[0]] = parts[1];
            }
        }
    }

    public string SaveHomes() => string.Join("\n", homes.Select(h => $"{h.Npc}|{h.PlaceKey}|{h.TileX}|{h.TileY}"));

    public string SaveResidents()
    {
        var lines = residents.Select(r => $"{r.Key}|{r.Value}").ToList();
        if (visitedOcean)
            lines.Add("#ocean|1");
        return string.Join("\n", lines);
    }

    public void SetHomes(Homes message)
    {
        homes.Clear();
        homes.AddRange(message.Entries);
        foreach (string name in residents.Keys.ToList())
        {
            HomeEntry? home = HomeOf(name);
            if (home != null)
                residents[name] = home.Value.PlaceKey;
            else
                residents.Remove(name);
        }
    }

    private HomeEntry? HomeOf(string npc)
    {
        foreach (HomeEntry home in homes)
        {
            if (home.Npc == npc && home.PlaceKey != null)
                return home;
        }
        return null;
    }

    public void Watch(WorldStamp.PlaceState? place)
    {
        if (place?.Layout != null && place.Layout.Biome == (byte)Biome.Ocean)
            visitedOcean = true;
    }

    public void NewDay(Link link, Player player)
    {
        Watch(BridgeSystem.Instance.Stamp.Current);
        Respawn();
        AdoptStrays();
        TownFacts facts = Facts(player);
        string? next = TownRules.Waiting(facts).FirstOrDefault(n => HomeOf(n) != null);
        if (next != null && NPCID.Search.TryGetId(next, out int type) && HomeOf(next) is HomeEntry home)
        {
            NPC? npc = SpawnAt(type, home);
            if (npc != null)
            {
                residents[next] = home.PlaceKey;
                BridgeSystem.Instance.Notice($"{npc.GivenOrTypeName} the {Lang.GetNPCNameValue(type)} has moved into {TownHomes.VillagerOf(next)}'s old home!", 120, 255, 120);
            }
        }
        SendFacts(link);
        // Stardew has picked this morning's talk from the facts it had; passing happenings start over.
        sawBloodMoon = sawEclipse = sawParty = false;
    }

    private bool sawBloodMoon;
    private bool sawEclipse;
    private bool sawParty;

    /// <summary>Passing happenings are held until the next Stardew morning, when villagers talk about them.</summary>
    private void Latch()
    {
        sawBloodMoon |= Main.bloodMoon;
        sawEclipse |= Main.eclipse;
        sawParty |= Terraria.GameContent.Events.BirthdayParty.PartyIsUp;
    }

    /// <summary>The <see cref="Terradew.Core.Town.Happenings"/> ids that are true in this world.</summary>
    private IEnumerable<string> Happenings()
    {
        var all = new (bool Yes, string Id)[]
        {
            (NPC.downedMoonlord, "moonLord"),
            (NPC.downedAncientCultist, "cultist"),
            (NPC.downedGolemBoss, "golem"),
            (NPC.downedPlantBoss, "plantera"),
            (NPC.downedMechBoss1 || NPC.downedMechBoss2 || NPC.downedMechBoss3, "mechs"),
            (Main.hardMode, "hardmode"),
            (NPC.downedBoss3, "skeletron"),
            (NPC.downedBoss2, "evilBoss"),
            (NPC.downedQueenBee, "queenBee"),
            (NPC.downedBoss1, "eye"),
            (NPC.downedMartians, "martians"),
            (NPC.downedPirates, "pirates"),
            (NPC.downedFrost, "frostLegion"),
            (NPC.downedGoblins, "goblins"),
            (sawBloodMoon, "bloodMoon"),
            (sawEclipse, "eclipse"),
            (sawParty, "party"),
        };
        return all.Where(a => a.Yes).Select(a => a.Id);
    }

    private TownFacts Facts(Player player)
    {
        bool gun = player.inventory.Any(i => !i.IsAir && i.useAmmo == AmmoID.Bullet);
        bool explosive = player.inventory.Any(i => !i.IsAir && (i.type == ItemID.Bomb || i.type == ItemID.Dynamite || i.type == ItemID.Grenade || i.type == ItemID.StickyBomb));
        var present = new List<string>();
        foreach (string name in residents.Keys)
            present.Add(name);
        return new TownFacts(
            player.statLifeMax,
            Inventory.Coins(player),
            gun,
            explosive,
            NPC.downedBoss1,
            NPC.downedBoss2,
            NPC.downedBoss3,
            NPC.downedQueenBee,
            Main.hardMode,
            NPC.downedGoblins,
            visitedOcean,
            present,
            NPC.downedMechBoss1 || NPC.downedMechBoss2 || NPC.downedMechBoss3,
            NPC.downedGolemBoss,
            NPC.downedPlantBoss,
            NPC.downedPirates,
            NPC.savedBartender,
            NPC.savedTaxCollector,
            NPC.savedStylist,
            NPC.savedGolfer,
            NPC.savedWizard);
    }

    private void Respawn()
    {
        foreach (var resident in residents.ToList())
        {
            if (!NPCID.Search.TryGetId(resident.Key, out int type) || NPC.AnyNPCs(type))
                continue;
            HomeEntry? home = HomeOf(resident.Key);
            if (home == null)
                continue;
            if (SpawnAt(type, home.Value) != null)
                BridgeSystem.Instance.Notice($"The {Lang.GetNPCNameValue(type)} is back home.", 120, 255, 120);
        }
    }

    /// <summary>Town NPCs that came with the world, like the Guide, take a free house if there is one.</summary>
    private void AdoptStrays()
    {
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (!npc.active || !npc.townNPC)
                continue;
            string name = NPCID.Search.GetName(npc.type);
            if (residents.ContainsKey(name))
            {
                HomeEntry? home = HomeOf(name);
                if (home != null)
                    Settle(npc, home.Value);
                continue;
            }
            HomeEntry? own = HomeOf(name);
            if (own == null)
                continue;
            residents[name] = own.Value.PlaceKey;
            Settle(npc, own.Value);
        }
    }

    private static NPC? SpawnAt(int type, HomeEntry home)
    {
        if (!BridgeSystem.Instance.Stamp.TryGet(home.PlaceKey, out WorldStamp.PlaceState place))
            return null;
        Vector2 feet = Feet(place, home);
        int who = NPC.NewNPC(new EntitySource_Misc("Terradew town"), (int)feet.X, (int)feet.Y, type);
        if (who < 0 || who >= Main.maxNPCs)
            return null;
        NPC npc = Main.npc[who];
        Settle(npc, home);
        return npc;
    }

    private static void Settle(NPC npc, HomeEntry home)
    {
        if (home.PlaceKey == null || !BridgeSystem.Instance.Stamp.TryGet(home.PlaceKey, out WorldStamp.PlaceState place))
            return;
        Vector2 feet = Feet(place, home);
        npc.homeTileX = (int)(feet.X / 16f);
        npc.homeTileY = (int)(feet.Y / 16f);
        npc.homeless = false;
        (float x, float y) = WorldProjection.FromFeet(feet.X, feet.Y, npc.width, npc.height);
        npc.position = new Vector2(x, y);
        npc.velocity = Vector2.Zero;
    }

    private static Vector2 Feet(WorldStamp.PlaceState place, HomeEntry home)
        => new((place.OriginX + home.TileX) * 16f + 8f, (place.OriginY + home.TileY + 1) * 16f - 2f);

    public void SendFacts(Link link)
    {
        var facts = new WorldFacts
        {
            Hardmode = Main.hardMode,
            DownedEye = NPC.downedBoss1,
            DownedEvilBoss = NPC.downedBoss2,
            DownedSkeletron = NPC.downedBoss3,
            DownedQueenBee = NPC.downedQueenBee,
            DownedWall = Main.hardMode,
        };
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (!npc.active || !npc.townNPC)
                continue;
            string name = NPCID.Search.GetName(npc.type);
            residents.TryGetValue(name, out string? home);
            // The portrait must be on disk by the time Stardew opens a shop this resident runs.
            int head = Terraria.GameContent.TownNPCProfiles.GetHeadIndexSafe(npc);
            SpriteDump.NpcHead(head);
            // Stardew draws the villager this NPC plays from its sprite sheet.
            SpriteDump.Npc(npc.type);
            facts.Residents.Add(new TownResident(npc.type, npc.GivenOrTypeName, home ?? "", head, Main.npcFrameCount[npc.type], NPCID.Sets.ExtraFramesCount[npc.type]));
        }
        Latch();
        facts.Happenings.AddRange(Happenings());
        link.Send(facts);
    }

    /// <summary>The Old Man waits at the Skull Cavern entrance until Skeletron is beaten.</summary>
    public static void KeepOldMan(WorldStamp stamp)
    {
        WorldStamp.PlaceState? place = stamp.Current;
        if (NPC.downedBoss3 || place == null || place.Key != "SkullCave" || NPC.AnyNPCs(NPCID.OldMan) || NPC.AnyNPCs(NPCID.SkeletronHead))
            return;
        Player player = Main.LocalPlayer;
        Vector2 farmer = new(player.position.X + player.width / 2f, player.position.Y + player.height);
        if (!stamp.TryOpenSpot(farmer, 3, 8, out Vector2 feet))
            return;
        NPC.NewNPC(new EntitySource_Misc("Terradew old man"), (int)feet.X, (int)feet.Y, NPCID.OldMan);
    }
}
