using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terradew.Core;
using Terradew.Core.Protocol;
using Terradew.Core.Town;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerradewBridge;

/// <summary>
/// How followed town NPCs meet enemies. Heroes chase what they see and answer calls for help, guards fight
/// where they stand, and cowards run and shout for a hero. Nobody dies: a beaten NPC faints, goes home and is
/// back the next morning. Stardew is told what each one is doing so the villager can act it out.
/// </summary>
public sealed class Courage : GlobalNPC
{
    public override bool InstancePerEntity => true;

    /// <summary>Town NPC types knocked out today.</summary>
    private static readonly HashSet<int> Knocked = new();

    private VillagerMode mode;
    private int threat = -1;
    private int answering = -1;
    private Vector2 anchor;
    private int cooldown;
    private float lastAi0;
    private float swingTotal = 1f;

    public static void Clear() => Knocked.Clear();

    /// <summary>Everyone knocked out yesterday is back on their feet.</summary>
    public static void NewDay()
    {
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (npc.active && npc.townNPC && Knocked.Contains(npc.type))
            {
                npc.life = npc.lifeMax;
                npc.dontTakeDamage = false;
                if (npc.TryGetGlobalNPC(out Courage self))
                    self.mode = VillagerMode.Following;
            }
        }
        Knocked.Clear();
    }

    public static Role RoleOf(NPC npc) => TownCourage.RoleOf(NPCID.Search.GetName(npc.type));

    /// <summary>The most serious danger going on, or "" when the day is calm.</summary>
    public static string Danger()
    {
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            if (Main.npc[i].active && Main.npc[i].boss)
                return "boss";
        }
        if (Main.invasionType > 0)
            return "invasion";
        if (Main.pumpkinMoon)
            return "pumpkinMoon";
        if (Main.snowMoon)
            return "frostMoon";
        if (Main.eclipse)
            return "eclipse";
        if (Main.bloodMoon)
            return "bloodMoon";
        return "";
    }

    /// <summary>
    /// Decides what a followed town NPC does about nearby enemies. It never moves the NPC: it stays pinned to its
    /// villager, and Stardew walks the villager (chasing, running away) around whatever is in the way.
    /// </summary>
    public static void Steer(NPC npc)
    {
        if (BridgeSystem.Active && npc.townNPC && npc.TryGetGlobalNPC(out Courage self))
            self.Think(npc);
    }

    private void Think(NPC npc)
    {
        if (Knocked.Contains(npc.type))
        {
            mode = VillagerMode.KnockedOut;
            threat = answering = -1;
            npc.dontTakeDamage = true;
            return;
        }
        BridgeSystem bridge = BridgeSystem.Instance;
        if (!bridge.Linked || !Followers.TrySpot(npc, out VillagerSpot spot, out WorldStamp.PlaceState place, out Vector2 home)
            || bridge.Stamp.Current?.Key != spot.PlaceKey)
        {
            mode = VillagerMode.Following;
            threat = answering = -1;
            return;
        }
        if (cooldown > 0)
            cooldown--;
        Role role = RoleOf(npc);
        Vector2 feet = Feet(npc);
        if (mode != VillagerMode.Fighting)
        {
            anchor = home;
            feet = home;
        }
        float reach = TownCourage.Engage(role, Danger().Length > 0, DangerTiles(npc));
        int seen = Nearest(feet, reach * 16f, place);
        bool swinging = Attacking(npc);

        switch (role)
        {
            case Role.Hero:
            {
                float leash = TownCourage.Leash * 16f;
                int target = Valid(threat, place) ? threat : cooldown == 0 ? seen : -1;
                if (target < 0 && Valid(answering, place))
                    target = answering;
                // A swing in progress is finished, so the leash does not turn the hero around mid-attack.
                if (target >= 0 && !swinging && Vector2.Distance(anchor, Feet(Main.npc[target])) > leash)
                {
                    // Too far from where the fight started: walk back and let it go for a while.
                    target = -1;
                    cooldown = 180;
                }
                threat = target;
                if (target < 0)
                {
                    answering = -1;
                    mode = VillagerMode.Following;
                    return;
                }
                mode = VillagerMode.Fighting;
                return;
            }
            case Role.Guard:
            {
                // A guard keeps its ground while swinging, even if the enemy has just stepped out of reach.
                int hold = seen >= 0 ? seen : swinging && Valid(threat, place) ? threat : -1;
                threat = hold;
                mode = hold >= 0 ? VillagerMode.Defending : VillagerMode.Following;
                return;
            }
            default:
                if (seen >= 0)
                {
                    if (mode != VillagerMode.Fleeing || threat != seen)
                        CallForHelp(home, Main.npc[seen], place);
                    threat = seen;
                    mode = VillagerMode.Fleeing;
                }
                else if (mode == VillagerMode.Fleeing && Valid(threat, place) && Vector2.Distance(home, Feet(Main.npc[threat])) < TownCourage.Safe * 16f)
                {
                    // Still too close for comfort: keep running.
                }
                else
                {
                    threat = -1;
                    mode = VillagerMode.Following;
                }
                return;
        }
    }

    /// <summary>The nearest hero close enough to reach the enemy comes running.</summary>
    private static void CallForHelp(Vector2 victim, NPC enemy, WorldStamp.PlaceState place)
    {
        Vector2 at = Feet(enemy);
        Courage? best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (!npc.active || !npc.townNPC || Knocked.Contains(npc.type) || RoleOf(npc) != Role.Hero || !npc.TryGetGlobalNPC(out Courage hero))
                continue;
            if (hero.threat >= 0 || !Followers.TrySpot(npc, out VillagerSpot spot, out _, out Vector2 home) || spot.PlaceKey != place.Key)
                continue;
            if (Vector2.Distance(home, at) > TownCourage.Leash * 16f)
                continue;
            float distance = Vector2.Distance(home, victim);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = hero;
            }
        }
        if (best != null)
            best.answering = enemy.whoAmI;
    }

    internal static Vector2 Feet(NPC npc) => new(npc.position.X + npc.width / 2f, npc.position.Y + npc.height);

    /// <summary>Terraria's own danger range for a town NPC, in tiles. Zero when it has none.</summary>
    private static float DangerTiles(NPC npc) => Math.Max(0, NPCID.Sets.DangerDetectRange[npc.type]) / 16f;

    /// <summary>True while Terraria's town NPC is in an attack (throw, shoot, heal, magic or swing).</summary>
    private static bool Attacking(NPC npc) => npc.ai[0] >= 10f && npc.ai[0] <= 15f;

    /// <summary>A town NPC that is up and about: not knocked out, and not a fainted one waiting for morning.</summary>
    public static bool Standing(NPC npc)
        => npc.active && npc.townNPC && npc.life > 0 && !npc.dontTakeDamage && !Knocked.Contains(npc.type);

    /// <summary>The nearest standing town NPC of the current place within a range, or -1. Hostile creatures go for these.</summary>
    public static int NearestStanding(Vector2 feet, float range, WorldStamp.PlaceState place)
    {
        int best = -1;
        float bestDistance = range;
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (!Standing(npc) || !Followers.TrySpot(npc, out VillagerSpot spot, out _, out _) || spot.PlaceKey != place.Key)
                continue;
            float distance = Vector2.Distance(feet, Feet(npc));
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return best;
    }

    private static bool Hostile(NPC npc)
        => npc.active && npc.life > 0 && !npc.friendly && !npc.townNPC && npc.damage > 0 && npc.lifeMax > 5 && !npc.dontTakeDamage
           && !NPCID.Sets.CountsAsCritter[npc.type];

    private static bool Valid(int index, WorldStamp.PlaceState place)
        => index >= 0 && index < Main.maxNPCs && Hostile(Main.npc[index]) && place.ContainsWorld(Main.npc[index].Center);

    private static int Nearest(Vector2 feet, float range, WorldStamp.PlaceState place)
    {
        int best = -1;
        float bestDistance = range;
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (!Hostile(npc) || !place.ContainsWorld(npc.Center))
                continue;
            float distance = Vector2.Distance(feet, Feet(npc));
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return best;
    }

    /// <summary>Tells Stardew what every busy villager is doing: often while anyone is busy, now and then otherwise.</summary>
    public static void Publish(Link? link, int worldTicks)
    {
        if (link == null || !BridgeSystem.Instance.Linked)
            return;
        var moves = new VillagerMoves { Danger = Danger() };
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (!npc.active || !npc.townNPC || !npc.TryGetGlobalNPC(out Courage self) || self.mode == VillagerMode.Following)
                continue;
            moves.Moves.Add(self.Move(npc));
        }
        if (worldTicks % (moves.Moves.Count > 0 ? 6 : 60) == 0)
            link.Send(moves);
    }

    private VillagerMove Move(NPC npc)
    {
        string key = "";
        float tileX = 0, tileY = 0, threatX = 0, threatY = 0;
        if (Followers.TrySpot(npc, out VillagerSpot spot, out WorldStamp.PlaceState place, out _))
        {
            key = spot.PlaceKey;
            Vector2 feet = Feet(npc);
            (float x, float y) = WorldProjection.ToMap(place.OriginX, place.OriginY, feet.X, feet.Y);
            (tileX, tileY) = (x / WorldProjection.StardewTilePixels, y / WorldProjection.StardewTilePixels);
            if (threat >= 0 && threat < Main.maxNPCs && Main.npc[threat].active)
            {
                Vector2 enemy = Feet(Main.npc[threat]);
                (x, y) = WorldProjection.ToMap(place.OriginX, place.OriginY, enemy.X, enemy.Y);
                (threatX, threatY) = (x / WorldProjection.StardewTilePixels, y / WorldProjection.StardewTilePixels);
            }
        }
        // Town attacks: 10 throw, 12 shoot, 13 heal, 14 magic, 15 melee swing.
        float ai0 = npc.ai[0];
        bool attacking = ai0 >= 10f && ai0 <= 15f;
        if (ai0 == 15f && lastAi0 != 15f)
            swingTotal = Math.Max(1f, npc.ai[1]);
        lastAi0 = ai0;
        short frame = attacking ? (short)(npc.frame.Y / Math.Max(1, npc.frame.Height)) : (short)-1;
        int swingItem = ai0 == 15f ? SwingItem(npc.type) : 0;
        float swing = swingItem > 0 ? Math.Clamp(1f - npc.ai[1] / swingTotal, 0f, 1f) : 0f;
        byte facing = (byte)(npc.direction < 0 ? 3 : 1);
        return new VillagerMove(npc.type, (byte)mode, key, tileX, tileY, facing, frame, swingItem, swing, threatX, threatY);
    }

    /// <summary>The weapon a melee town NPC swings, for Stardew to draw.</summary>
    private static int SwingItem(int type) => type == NPCID.DyeTrader ? ItemID.DyeTradersScimitar : 0;

    /// <summary>A beaten town NPC faints instead of dying, and stays out of fights until morning.</summary>
    public override bool CheckDead(NPC npc)
    {
        if (!BridgeSystem.Active || !npc.townNPC)
            return true;
        npc.life = 1;
        npc.dontTakeDamage = true;
        Knocked.Add(npc.type);
        mode = VillagerMode.KnockedOut;
        threat = answering = -1;
        return false;
    }

    /// <summary>A fainted NPC does nothing at all: no wandering, no attacks. Others turn to face the enemy first.</summary>
    public override bool PreAI(NPC npc)
    {
        if (!BridgeSystem.Active || !npc.townNPC)
            return true;
        if (Knocked.Contains(npc.type))
            return false;
        if (npc.TryGetGlobalNPC(out Courage self))
            self.Face(npc);
        return true;
    }

    /// <summary>
    /// Terraria's town NPCs only attack what they face, and Stardew's facing would otherwise keep them turned away
    /// from it. Heroes and guards turn to the nearest enemy in Terraria's danger range before their AI decides.
    /// Stardew's facing is put back after the AI each tick, so this is what the AI sees.
    /// </summary>
    private void Face(NPC npc)
    {
        if (RoleOf(npc) == Role.Coward || !Followers.TrySpot(npc, out _, out _, out _))
            return;
        WorldStamp.PlaceState? place = BridgeSystem.Instance.Stamp.Current;
        float range = Math.Max(0, NPCID.Sets.DangerDetectRange[npc.type]);
        if (place == null || range <= 0f)
            return;
        int enemy = Nearest(Feet(npc), range, place);
        if (enemy < 0)
            return;
        npc.direction = Main.npc[enemy].Center.X < npc.Center.X ? -1 : 1;
        npc.spriteDirection = npc.direction;
    }

    public override void TownNPCAttackStrength(NPC npc, ref int damage, ref float knockback)
    {
        if (BridgeSystem.Active)
            damage = Math.Max(1, (int)Math.Round(damage * TownCourage.Damage(RoleOf(npc))));
    }

    public override void SetDefaults(NPC npc)
    {
        if (!BridgeSystem.Active || !npc.townNPC)
            return;
        npc.lifeMax = Math.Max(1, (int)Math.Round(npc.lifeMax * TownCourage.Health(RoleOf(npc))));
        npc.life = npc.lifeMax;
    }
}

/// <summary>The harmless cowards lose their attack while the mod is loaded, and get it back when it unloads.</summary>
public sealed class CourageSystem : ModSystem
{
    private readonly Dictionary<int, int> saved = new();

    public override void PostSetupContent()
    {
        foreach (string name in TownCourage.Npcs)
        {
            if (!TownCourage.IsHarmless(name) || !NPCID.Search.TryGetId(name, out int type))
                continue;
            saved[type] = NPCID.Sets.AttackType[type];
            NPCID.Sets.AttackType[type] = -1;
        }
    }

    public override void Unload()
    {
        foreach ((int type, int attack) in saved)
            NPCID.Sets.AttackType[type] = attack;
        saved.Clear();
    }
}
