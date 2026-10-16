using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using Terradew.Core.Protocol;
using Terradew.Core.Town;

namespace Terradew;

/// <summary>
/// Acts out what Terraria says each villager is doing about enemies (see Courage in the Terraria mod). Cowards run
/// and shout for help, guards stop and fight, heroes are moved by Terraria while they chase, and a beaten villager
/// goes home hurt. During a boss, an invasion or a bad moon, cowards stay home; heroes and guards keep their routine
/// and act only when an enemy is in reach.
/// </summary>
internal sealed class Bravery
{
    private sealed class Brave
    {
        public VillagerMode Mode;
        public Point Start;
        public int StartFacing = 2;
        public bool Returning;
        /// <summary>The tick a walk back to the start gives up at, so one that cannot finish does not hold the villager off their schedule.</summary>
        public uint ReturnUntil;
        public bool Helped;
        public uint NextFlee;
        public uint NextShout;
    }

    private readonly Bridge bridge;
    private readonly NpcStories stories;
    private readonly IMonitor monitor;
    private readonly Walker walker;
    private readonly Dictionary<string, Brave> states = new(StringComparer.Ordinal);
    private string danger = "";
    private bool loggedError;
    private long resetAtMs;

    /// <summary>A hero stops this many tiles from the enemy: every hero fights with a gun, a bow, a throw or a spell.</summary>
    private const int HeroReach = 3;

    /// <summary>Ticks a villager may spend walking back to where trouble started before the schedule takes over.</summary>
    private const uint ReturnTicks = 1800;

    /// <summary>How fast a hero runs at an enemy, in pixels a tick (a villager walks at 2).</summary>
    private const float ChargeSpeed = 4f;

    public Bravery(Bridge bridge, NpcStories stories, IMonitor monitor, Maps maps)
    {
        this.bridge = bridge;
        this.stories = stories;
        this.monitor = monitor;
        walker = new Walker(maps);
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
        walker.Reset();
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
            walker.Update();
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
                    Leave(v, state, lines, ticks);
                    Enter(v, state, mode, lines);
                    state.Mode = mode;
                }
                if (has)
                    During(v, state, move, resident, lines, heroFighting, ticks);
                if (state.Returning && (!walker.Busy(v) || ticks >= state.ReturnUntil))
                {
                    // Back where trouble started: Stardew's schedule picks the day up again. Only cowards stay home
                    // while danger lasts; a walk that cannot finish gives up, so nobody is kept off their routine.
                    state.Returning = false;
                    walker.Stop(v);
                    if (danger.Length == 0 || TownCourage.RoleOf(npc) != Role.Coward)
                    {
                        v.followSchedule = true;
                        v.TryLoadSchedule();
                    }
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
                // Only this class moves them now: Stardew's schedule would keep re-issuing its own paths and fight the run.
                v.followSchedule = false;
                v.controller = null;
                v.Halt();
                state.Helped = false;
                state.NextFlee = 0;
                v.addedSpeed = 2f;
                v.doEmote(16);
                Shout(v, lines?.Courage.Help);
                break;
            case VillagerMode.Defending:
            case VillagerMode.Fighting:
                Save(v, state);
                v.followSchedule = false;
                walker.Stop(v);
                v.Halt();
                Shout(v, lines?.Courage.Battle);
                break;
            case VillagerMode.KnockedOut:
                state.Returning = false;
                walker.Stop(v);
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

    private void Leave(NPC v, Brave state, NpcContent? lines, uint ticks)
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
                GoBack(v, state, ticks);
                break;
            case VillagerMode.Defending:
            case VillagerMode.Fighting:
                GoBack(v, state, ticks);
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
                if (here && (ticks >= state.NextFlee || !walker.Busy(v)))
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
                if (here)
                    TurnToward(v, threat - v.Position);
                ShowFrame(v, move, resident);
                break;
            case VillagerMode.Fighting:
                if (!here)
                    break;
                if (move.Frame >= 0)
                {
                    // An attack pose: stand still, the sheet already shows which way they face.
                    walker.Stop(v);
                    if (v.FacingDirection != move.Facing)
                        v.faceDirection(move.Facing);
                    ShowFrame(v, move, resident);
                }
                else
                {
                    // Run at the enemy around whatever is in the way, and stop in reach. Terraria follows the villager.
                    var enemy = new Point((int)MathF.Floor(move.ThreatX), (int)MathF.Floor(move.ThreatY - 0.5f));
                    var center = v.GetBoundingBox().Center;
                    var me = new Point(center.X / 64, center.Y / 64);
                    if (Math.Max(Math.Abs(me.X - enemy.X), Math.Abs(me.Y - enemy.Y)) > HeroReach)
                        walker.Go(v, enemy, HeroReach, ChargeSpeed);
                    else if (!walker.Busy(v))
                        TurnToward(v, threat - v.Position);
                }
                if (ticks >= state.NextShout)
                {
                    state.NextShout = ticks + 300;
                    if (Game1.random.NextDouble() < 0.25)
                        Shout(v, lines?.Courage.Battle);
                }
                break;
            case VillagerMode.KnockedOut:
                break;
        }
    }

    /// <summary>Runs to the first reachable tile well away from the enemy, trying straight away first.</summary>
    private void Flee(NPC v, Vector2 threat)
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
                if (spot.X < 0 || spot.Y < 0 || walker.OpenNear(v, new Point((int)spot.X, (int)spot.Y), 2) is not Point tile)
                    continue;
                if (walker.Go(v, tile, 0, 2f + v.addedSpeed, Facing(dir)))
                    return;
            }
        }
    }

    private static int Facing(Vector2 dir)
        => Math.Abs(dir.X) > Math.Abs(dir.Y) ? (dir.X > 0 ? 1 : 3) : (dir.Y > 0 ? 2 : 0);

    /// <summary>Turns toward a spot, but only once it is clearly on another side: near a diagonal it would flip every tick.</summary>
    private static void TurnToward(NPC v, Vector2 toward)
    {
        if (toward.LengthSquared() < 64f)
            return;
        int facing = Facing(toward);
        if (facing != v.FacingDirection && MathF.Abs(MathF.Abs(toward.X) - MathF.Abs(toward.Y)) >= 12f)
            v.faceDirection(facing);
    }

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
        // Already away from the schedule: keep the first spot.
        if (state.Returning || state.Mode != VillagerMode.Following)
        {
            state.Returning = false;
            return;
        }
        state.Start = v.TilePoint;
        state.StartFacing = v.FacingDirection;
    }

    /// <summary>Walks back to where trouble started, then picks the schedule up again.</summary>
    private void GoBack(NPC v, Brave state, uint ticks)
    {
        if (v.TilePoint != state.Start)
            walker.Go(v, state.Start, 0, 2f, state.StartFacing);
        state.Returning = true;
        state.ReturnUntil = ticks + ReturnTicks;
    }

    private static void Shout(NPC v, List<string>? lines)
    {
        if (lines == null || lines.Count == 0 || !Utility.isOnScreen(v.Position, 256))
            return;
        v.showTextAboveHead(Pick(lines));
    }

    private static string Pick(List<string> lines) => lines[Game1.random.Next(lines.Count)];

    /// <summary>Danger has begun (or changed): cowards go home. Heroes and guards keep their routine until an enemy is in reach.</summary>
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
            if (!started || TownCourage.RoleOf(npc) != Role.Coward || (states.TryGetValue(villager, out Brave? state) && state.Mode == VillagerMode.KnockedOut))
                continue;
            v.followSchedule = false;
            walker.Stop(v);
            Send(v, v.DefaultMap, new Point((int)(v.DefaultPosition.X / 64f), (int)(v.DefaultPosition.Y / 64f)), 2);
        }
    }

    /// <summary>The danger is over: the cowards sent home pick up today's schedule again.</summary>
    private void EndShelter()
    {
        foreach (string npc in TownHomes.Npcs)
        {
            string? villager = TownHomes.VillagerOf(npc);
            if (villager == null || Cast.Playing(villager) == null || TownCourage.RoleOf(npc) != Role.Coward)
                continue;
            NPC? v = Game1.getCharacterFromName(villager);
            if (v?.currentLocation == null || (states.TryGetValue(villager, out Brave? state) && state.Mode != VillagerMode.Following))
                continue;
            walker.Stop(v);
            v.followSchedule = true;
            v.TryLoadSchedule();
        }
    }

    /// <summary>Walks there when it is on the same map, otherwise moves straight there off screen.</summary>
    private void Send(NPC v, string location, Point tile, int facing)
    {
        if (v.currentLocation.Name == location && walker.Go(v, tile, 0, 2f, facing))
            return;
        v.controller = null;
        Game1.warpCharacter(v, location, tile);
        v.faceDirection(facing);
    }
}
