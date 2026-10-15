using Microsoft.Xna.Framework;
using Terradew.Core;
using Terradew.Core.Events;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerradewBridge;

/// <summary>
/// Runs the late-game sequence (see <see cref="CelestialRules"/>). The Lunatic Cultist waits at the Skull
/// Cavern, each Lunar Pillar rises in its own place, and Moon Lord comes to the Farm. Terraria's own
/// triggers for these are blocked by <see cref="CelestialGuard"/>, because they would place the towers at
/// fixed world positions outside the stamped places.
/// </summary>
public static class Celestial
{
    private static bool summoning;

    /// <summary>True while this mod is placing a celestial NPC itself, so the guard lets it through.</summary>
    public static bool Summoning => summoning;

    public static void Reset() => summoning = false;

    public static void Watch(WorldStamp stamp)
    {
        CelestialFlags flags = ReadFlags();
        // Terraria's pillar enemies only spawn while the lunar event is up, which is the case while any pillar or Moon Lord lives.
        NPC.LunarApocalypseIsUp = flags.PillarsAlive != PillarSet.None || flags.MoonLordAlive;

        WorldStamp.PlaceState? place = stamp.Current;
        if (place == null)
            return;
        CelestialAction action = CelestialRules.Step(flags, place.Key);
        if (action.Kind == CelestialActionKind.None)
            return;

        Player player = Main.LocalPlayer;
        Vector2 farmer = new(player.position.X + player.width / 2f, player.position.Y + player.height);
        Vector2 feet;
        int type;
        switch (action.Kind)
        {
            case CelestialActionKind.SpawnCultist:
                // A few tiles off, so the boss does not land on the farmer.
                if (!stamp.TryOpenSpot(farmer, 3, 6, out feet))
                    return;
                type = NPCID.CultistBoss;
                break;
            case CelestialActionKind.SpawnPillar:
                if (action.Target is not Pillar pillar || !stamp.TryOpenSpot(farmer, 12, 40, out feet))
                    return;
                type = PillarType(pillar);
                break;
            case CelestialActionKind.SpawnMoonLord:
                if (!stamp.TryOpenSpot(farmer, 12, 40, out feet))
                    return;
                type = NPCID.MoonLordCore;
                break;
            default:
                return;
        }

        NPC? spawned = Summon(type, feet);
        if (spawned == null)
            return;
        if (action.Target is Pillar towered)
            SetTowerActive(towered);

        string? text = CelestialRules.Announcement(action);
        if (text != null)
            BridgeSystem.Instance.Notice(text, 255, 200, 120);
    }

    private static CelestialFlags ReadFlags()
    {
        PillarSet down = PillarIf(Pillar.Solar, NPC.downedTowerSolar)
            | PillarIf(Pillar.Vortex, NPC.downedTowerVortex)
            | PillarIf(Pillar.Nebula, NPC.downedTowerNebula)
            | PillarIf(Pillar.Stardust, NPC.downedTowerStardust);
        PillarSet alive = PillarIf(Pillar.Solar, NPC.AnyNPCs(NPCID.LunarTowerSolar))
            | PillarIf(Pillar.Vortex, NPC.AnyNPCs(NPCID.LunarTowerVortex))
            | PillarIf(Pillar.Nebula, NPC.AnyNPCs(NPCID.LunarTowerNebula))
            | PillarIf(Pillar.Stardust, NPC.AnyNPCs(NPCID.LunarTowerStardust));
        return new CelestialFlags(
            GolemDown: NPC.downedGolemBoss,
            CultistDown: NPC.downedAncientCultist,
            CultistAlive: NPC.AnyNPCs(NPCID.CultistBoss),
            PillarsDown: down,
            PillarsAlive: alive,
            MoonLordDown: NPC.downedMoonlord,
            MoonLordAlive: NPC.AnyNPCs(NPCID.MoonLordCore));
    }

    private static PillarSet PillarIf(Pillar pillar, bool on) => on ? CelestialRules.Bit(pillar) : PillarSet.None;

    private static int PillarType(Pillar pillar) => pillar switch
    {
        Pillar.Solar => NPCID.LunarTowerSolar,
        Pillar.Vortex => NPCID.LunarTowerVortex,
        Pillar.Nebula => NPCID.LunarTowerNebula,
        _ => NPCID.LunarTowerStardust,
    };

    private static void SetTowerActive(Pillar pillar)
    {
        switch (pillar)
        {
            case Pillar.Solar: NPC.TowerActiveSolar = true; break;
            case Pillar.Vortex: NPC.TowerActiveVortex = true; break;
            case Pillar.Nebula: NPC.TowerActiveNebula = true; break;
            default: NPC.TowerActiveStardust = true; break;
        }
    }

    /// <summary>Places an NPC with its feet on a tile the stamp found open.</summary>
    private static NPC? Summon(int type, Vector2 feet)
    {
        summoning = true;
        try
        {
            int who = NPC.NewNPC(new EntitySource_Misc("Terradew celestial"), (int)feet.X, (int)feet.Y, type);
            if (who < 0 || who >= Main.maxNPCs)
                return null;
            NPC npc = Main.npc[who];
            (float x, float y) = WorldProjection.FromFeet(feet.X, feet.Y, npc.width, npc.height);
            npc.position = new Vector2(x, y);
            npc.velocity = Vector2.Zero;
            npc.netUpdate = true;
            return npc;
        }
        finally
        {
            summoning = false;
        }
    }
}

/// <summary>
/// Stops Terraria from starting the celestial sequence on its own, and keeps the Cultist and Moon Lord in
/// their places. Creatures.cs exempts bosses from place pinning, so these two are pinned here instead.
/// </summary>
public sealed class CelestialGuard : GlobalNPC
{
    private bool anchored;
    private Vector2 anchor;

    public override bool InstancePerEntity => true;

    private static bool Guarded(int type) => type is NPCID.CultistBoss or NPCID.CultistTablet
        or NPCID.LunarTowerSolar or NPCID.LunarTowerVortex or NPCID.LunarTowerNebula or NPCID.LunarTowerStardust
        or NPCID.MoonLordCore;

    private static string? HomeOf(int type) => type switch
    {
        NPCID.CultistBoss => CelestialRules.CultistPlace,
        NPCID.MoonLordCore or NPCID.MoonLordHead or NPCID.MoonLordHand => CelestialRules.MoonLordPlace,
        _ => null,
    };

    public override void OnSpawn(NPC npc, IEntitySource source)
    {
        if (!BridgeSystem.Active || Celestial.Summoning || !Guarded(npc.type))
            return;
        // The Celestial Sigil and Terraria's own countdown after the pillars may bring Moon Lord, but only to the Farm.
        if (npc.type == NPCID.MoonLordCore && BridgeSystem.Instance.Stamp.Current?.Key == CelestialRules.MoonLordPlace)
            return;
        npc.active = false;
    }

    public override void PostAI(NPC npc)
    {
        if (!BridgeSystem.Active || !npc.active || npc.life <= 0)
            return;
        string? home = HomeOf(npc.type);
        if (home == null)
            return;
        WorldStamp stamp = BridgeSystem.Instance.Stamp;
        if (!stamp.TryGet(home, out WorldStamp.PlaceState place) || place.Rect.Width <= 0)
            return;

        if (!anchored)
        {
            anchor = npc.position;
            anchored = true;
        }
        bool farmerHere = stamp.Current?.Key == home;
        if (farmerHere && place.ContainsWorld(npc.Center))
        {
            anchor = npc.position;
            return;
        }
        // Frozen where the farmer left it, and pulled back if it flew out of its place.
        npc.position = anchor;
        npc.velocity = Vector2.Zero;
    }
}

/// <summary>The Celestial Sigil only works on the Farm, where Moon Lord comes.</summary>
public sealed class CelestialSigil : GlobalItem
{
    public override bool CanUseItem(Item item, Player player)
    {
        if (!BridgeSystem.Active || item.type != ItemID.CelestialSigil)
            return true;
        if (BridgeSystem.Instance.Stamp.Current?.Key == CelestialRules.MoonLordPlace)
            return true;
        BridgeSystem.Instance.Notice("The Celestial Sigil only works on the Farm.", 200, 230, 255);
        return false;
    }
}
