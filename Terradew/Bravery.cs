using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Pathfinding;
using Terradew.Core.Protocol;
using Terradew.Core.Town;

namespace Terradew;

/// <summary>
/// Acts out what Terraria says each villager is doing about enemies (see Courage in the Terraria mod). Cowards run
/// and shout for help, guards stop and fight, heroes are moved by Terraria while they chase, and a beaten villager
/// goes home hurt. During a boss, an invasion or a bad moon, heroes wait outside their door and everyone else
/// stays home.
/// </summary>
internal sealed class Bravery
{
    private sealed class Brave
    {
        public VillagerMode Mode;
        public PathFindController? Saved;
        public Point Start;
        public int StartFacing = 2;
        public bool Returning;
        public bool Helped;
        public uint NextFlee;
        public uint NextShout;
    }

    private readonly Bridge bridge;
    private readonly NpcStories stories;
    private readonly IMonitor monitor;
    private readonly Dictionary<string, Brave> states = new(StringComparer.Ordinal);
    private string danger = "";
    private bool loggedError;
    private long resetAtMs;

    public Bravery(Bridge bridge, NpcStories stories, IMonitor monitor)
    {
        this.bridge = bridge;
        this.stories = stories;
        this.monitor = monitor;
    }

    /// <summary>The villager a Terraria town NPC type plays, if anyone.</summary>
    public static string? VillagerFor(int npcType)
    {
        string? npc = TownHomes.Npcs.FirstOrDefault(n => TownHomes.TypeOf(n) == npcType);
        return npc == null ? null : TownHomes.VillagerOf(npc);
    }

    /// <summary>A new day: Terraria wakes everyone up, and schedules start over.</summary>
    public void Reset()
    {
        states.Clear();
        danger = "";
        // Terraria may still be sending last night's knockouts until it hears the day has started.
        resetAtMs = Environment.TickCount64;
        if (!Context.IsWorldReady)
            return;
        foreach (string npc in TownHomes.Npcs)
        {
            if (TownHomes.VillagerOf(npc) is string villager && Game1.getCharacterFromName(villager) is NPC v)
            {
                v.followSchedule = true;
                v.addedSpeed = 0f;
            }
        }
    }

    public void Update(uint ticks)
    {
        if (!Context.IsWorldReady || Game1.CurrentEvent != null || Game1.isFestival() || Environment.TickCount64 - resetAtMs < 2000)
            return;
        try
        {
            VillagerMoves? moves = bridge.Moves;
            var byType = new Dictionary<int, VillagerMove>();
            if (moves != null)
            {
                foreach (VillagerMove move in moves.Moves)
                    byType[move.NpcType] = move;
            }
            bool heroFighting = byType.Values.Any(m => m.Mode == (byte)VillagerMode.Fighting);
            string now = moves?.Danger ?? "";
            if (now != danger)
            {
                string was = danger;
                danger = now;
                if (now.Length > 0)
                    Shelter(now, was.Length == 0);
                else
                    EndShelter();
            }

            foreach (string npc in TownHomes.Npcs)
            {
                string? villager = TownHomes.VillagerOf(npc);
                if (villager == null || Cast.Playing(villager) is not TownResident resident)
                    continue;
                NPC? v = Game1.getCharacterFromName(villager);
                if (v?.currentLocation == null)
                    continue;
                if (!states.TryGetValue(villager, out Brave? state))
                    states[villager] = state = new Brave();
                NpcContent? lines = stories.ContentOf(npc);
                bool has = byType.TryGetValue(resident.NpcType, out VillagerMove move);
                var mode = has ? (VillagerMode)move.Mode : VillagerMode.Following;
                if (mode != state.Mode)
                {
                    Leave(v, state, lines);
                    Enter(v, state, mode, lines);
                    state.Mode = mode;
                }
                if (has)
                    During(v, state, move, resident, lines, heroFighting, ticks);
                if (state.Returning && v.controller == null)
                {
                    v.controller = state.Saved;
                    state.Saved = null;
                    state.Returning = false;
                }
            }
        }
        catch (Exception error)
        {
            if (!loggedError)
                monitor.Log($"Terradew could not act out the villagers' fights. {error.Message}", LogLevel.Warn);
            loggedError = true;
        }
    }

    private void Enter(NPC v, Brave state, VillagerMode mode, NpcContent? lines)
    {
        switch (mode)
        {
            case VillagerMode.Fleeing:
                Save(v, state);
                state.Helped = false;
                state.NextFlee = 0;
                v.addedSpeed = 2f;
                v.doEmote(16);
                Shout(v, lines?.Courage.Help);
                break;
            case VillagerMode.Defending:
            case VillagerMode.Fighting:
                Save(v, state);
                v.controller = null;
                v.Halt();
                Shout(v, lines?.Courage.Battle);
                break;
            case VillagerMode.KnockedOut:
                state.Saved = null;
                state.Returning = false;
                v.controller = null;
                v.addedSpeed = 0f;
                v.Halt();
                v.followSchedule = false;
                Game1.warpCharacter(v, v.DefaultMap, new Point((int)(v.DefaultPosition.X / 64f), (int)(v.DefaultPosition.Y / 64f)));
                v.doEmote(28);
                if (!string.IsNullOrEmpty(lines?.Courage.Hurt))
                {
                    v.CurrentDialogue.Clear();
                    v.CurrentDialogue.Push(new Dialogue(v, null, lines.Courage.Hurt));
                }
                break;
        }
    }

    private void Leave(NPC v, Brave state, NpcContent? lines)
    {
        switch (state.Mode)
        {
            case VillagerMode.Fleeing:
                v.addedSpeed = 0f;
                if (state.Helped && lines != null && lines.Courage.Thanks.Count > 0)
                {
                    v.doEmote(20);
                    v.CurrentDialogue.Push(new Dialogue(v, null, Pick(lines.Courage.Thanks)));
                }
                GoBack(v, state);
                break;
            case VillagerMode.Defending:
            case VillagerMode.Fighting:
                GoBack(v, state);
                break;
        }
    }

    private void During(NPC v, Brave state, VillagerMove move, TownResident resident, NpcContent? lines, bool heroFighting, uint ticks)
    {
        bool here = move.PlaceKey == Maps.PlaceOf(v.currentLocation).Key;
        var threat = new Vector2(move.ThreatX * 64f, move.ThreatY * 64f);
        switch ((VillagerMode)move.Mode)
        {
            case VillagerMode.Fleeing:
                state.Helped |= heroFighting;
                if (here && (ticks >= state.NextFlee || v.controller == null))
                {
                    state.NextFlee = ticks + 60;
                    Flee(v, threat);
                }
                if (ticks >= state.NextShout)
                {
                    state.NextShout = ticks + 240;
                    if (Game1.random.NextDouble() < 0.35)
                        Shout(v, lines?.Courage.Help);
                }
                break;
            case VillagerMode.Defending:
                v.controller = null;
                if (here)
                    v.faceGeneralDirection(threat);
                ShowFrame(v, move, resident);
                break;
            case VillagerMode.Fighting:
                v.controller = null;
                if (!here)
                    break;
                var target = new Vector2(move.TileX * 64f - 32f, move.TileY * 64f - 48f);
                bool moved = Vector2.Distance(target, v.Position) > 0.5f;
                v.Position = target;
                v.faceDirection(move.Facing);
                if (move.Frame >= 0)
                    ShowFrame(v, move, resident);
                else if (moved)
                    v.animateInFacingDirection(Game1.currentGameTime);
                else
                    v.Halt();
                if (ticks >= state.NextShout)
                {
                    state.NextShout = ticks + 300;
                    if (Game1.random.NextDouble() < 0.25)
                        Shout(v, lines?.Courage.Battle);
                }
                break;
            case VillagerMode.KnockedOut:
                v.controller = null;
                break;
        }
    }

    /// <summary>Runs to the first reachable tile well away from the enemy, trying straight away first.</summary>
    private static void Flee(NPC v, Vector2 threat)
    {
        Point center = v.GetBoundingBox().Center;
        var me = new Vector2(center.X, center.Y);
        Vector2 away = me - threat;
        if (away.LengthSquared() < 1f)
            away = new Vector2((float)Game1.random.NextDouble() - 0.5f, (float)Game1.random.NextDouble() - 0.5f);
        away.Normalize();
        float[] turns = { 0f, 0.8f, -0.8f, 1.6f, -1.6f };
        float[] reaches = { TownCourage.Safe, TownCourage.Safe / 2f };
        foreach (float reach in reaches)
        {
            foreach (float turn in turns)
            {
                float cos = MathF.Cos(turn), sin = MathF.Sin(turn);
                var dir = new Vector2(away.X * cos - away.Y * sin, away.X * sin + away.Y * cos);
                Vector2 spot = me / 64f + dir * reach;
                var tile = new Point((int)spot.X, (int)spot.Y);
                if (tile.X < 0 || tile.Y < 0)
                    continue;
                var path = new PathFindController(v, v.currentLocation, tile, Facing(dir));
                if (path.pathToEndPoint == null || path.pathToEndPoint.Count == 0)
                    continue;
                v.controller = path;
                return;
            }
        }
    }

    private static int Facing(Vector2 dir)
        => Math.Abs(dir.X) > Math.Abs(dir.Y) ? (dir.X > 0 ? 1 : 3) : (dir.Y > 0 ? 2 : 0);

    /// <summary>Terraria attack frames sit after Stardew's 64 in the villager's sheet: facing right first, then left.</summary>
    private static void ShowFrame(NPC v, VillagerMove move, TownResident resident)
    {
        if (move.Frame < 0)
            return;
        int count = Math.Max(1, resident.FrameCount);
        int frame = Math.Clamp((int)move.Frame, 0, count - 1);
        v.Sprite.CurrentFrame = Cast.Frames + (move.Facing == 1 ? 0 : count) + frame;
    }

    private static void Save(NPC v, Brave state)
    {
        // Already away from the schedule: keep the first saved controller and spot.
        if (state.Returning || state.Mode != VillagerMode.Following)
        {
            state.Returning = false;
            return;
        }
        state.Saved = v.controller;
        state.Start = v.TilePoint;
        state.StartFacing = v.FacingDirection;
    }

    /// <summary>Walks back to where trouble started, then picks the schedule up again.</summary>
    private static void GoBack(NPC v, Brave state)
    {
        v.controller = v.TilePoint == state.Start ? null : new PathFindController(v, v.currentLocation, state.Start, state.StartFacing);
        if (v.controller?.pathToEndPoint == null)
            v.controller = null;
        state.Returning = true;
    }

    private static void Shout(NPC v, List<string>? lines)
    {
        if (lines == null || lines.Count == 0 || !Utility.isOnScreen(v.Position, 256))
            return;
        v.showTextAboveHead(Pick(lines));
    }

    private static string Pick(List<string> lines) => lines[Game1.random.Next(lines.Count)];

    /// <summary>Danger has begun (or changed): heroes go stand outside their door, everyone else goes home.</summary>
    private void Shelter(string kind, bool started)
    {
        foreach (string npc in TownHomes.Npcs)
        {
            string? villager = TownHomes.VillagerOf(npc);
            if (villager == null || Cast.Playing(villager) == null)
                continue;
            NPC? v = Game1.getCharacterFromName(villager);
            if (v?.currentLocation == null)
                continue;
            NpcContent? lines = stories.ContentOf(npc);
            if (lines != null && lines.Courage.Danger.TryGetValue(kind, out string? line) && line.Length > 0)
                v.CurrentDialogue.Push(new Dialogue(v, null, line));
            if (!started || (states.TryGetValue(villager, out Brave? state) && state.Mode == VillagerMode.KnockedOut))
                continue;
            v.followSchedule = false;
            if (TownCourage.RoleOf(npc) == Role.Hero && Door(v) is (string outside, Point door))
                Send(v, outside, door, 2);
            else
                Send(v, v.DefaultMap, new Point((int)(v.DefaultPosition.X / 64f), (int)(v.DefaultPosition.Y / 64f)), 2);
        }
    }

    /// <summary>The danger is over: everyone picks up today's schedule again.</summary>
    private void EndShelter()
    {
        foreach (string npc in TownHomes.Npcs)
        {
            string? villager = TownHomes.VillagerOf(npc);
            if (villager == null || Cast.Playing(villager) == null)
                continue;
            NPC? v = Game1.getCharacterFromName(villager);
            if (v?.currentLocation == null || (states.TryGetValue(villager, out Brave? state) && state.Mode == VillagerMode.KnockedOut))
                continue;
            v.followSchedule = true;
            v.TryLoadSchedule();
        }
    }

    /// <summary>Walks there when it is on the same map, otherwise moves straight there off screen.</summary>
    private static void Send(NPC v, string location, Point tile, int facing)
    {
        if (v.currentLocation.Name == location)
        {
            var path = new PathFindController(v, v.currentLocation, tile, facing);
            if (path.pathToEndPoint != null && path.pathToEndPoint.Count > 0)
            {
                v.controller = path;
                return;
            }
        }
        v.controller = null;
        Game1.warpCharacter(v, location, tile);
        v.faceDirection(facing);
    }

    /// <summary>The tile just outside a villager's front door: where the way out of their home leads.</summary>
    private static (string, Point)? Door(NPC v)
    {
        GameLocation? home = Game1.getLocationFromName(v.DefaultMap);
        if (home == null)
            return null;
        foreach (Warp warp in home.warps)
        {
            GameLocation? outside = Game1.getLocationFromName(warp.TargetName);
            if (outside != null && outside.IsOutdoors)
                return (warp.TargetName, new Point(warp.TargetX, warp.TargetY));
        }
        return null;
    }
}
