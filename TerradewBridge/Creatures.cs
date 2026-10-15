using System;
using Microsoft.Xna.Framework;
using Terradew.Core;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerradewBridge;

/// <summary>
/// Creature movement on a top-down map. Flyers and worms keep Terraria's own flight. Walkers aim
/// straight at their goal on both axes, through Stardew's path when it has one, and collide with the
/// stamped blocks at their feet. Creatures outside the farmer's current place hold still.
/// </summary>
public sealed class Creatures : GlobalNPC
{
    public override bool InstancePerEntity => true;

    private bool ready;
    private int seenType;
    private bool flyer;
    private Vector2 wander;
    private int pace;
    private int patience;
    private int stagger;
    private bool chasing;
    private bool away;
    private Vector2 plane;
    private int fitWidth = -1;
    private int fitHeight = -1;
    /// <summary>Ticks into the current hop, or -1 while a slime rests on the ground.</summary>
    private int hopTick = -1;
    private int restTicks;
    /// <summary>How high a slime is in its hop this tick: 0 on the ground, 1 at the top.</summary>
    private float hopArc;

    /// <summary>A slime rests this many ticks between hops, and each hop lasts <see cref="SlimeHopTicks"/>.</summary>
    private const int SlimeHopTicks = 24;
    private const int SlimeRestMin = 18;
    private const int SlimeRestMax = 30;
    /// <summary>A slime's speed in the air, in Terraria pixels per tick. A whole hop covers about 29 Terraria pixels.</summary>
    private const float SlimeHopSpeed = 1.2f;

    public static bool IsFlyer(NPC npc) => npc.TryGetGlobalNPC(out Creatures self) && self.ready && self.flyer;

    public static bool IsChasing(NPC npc) => npc.TryGetGlobalNPC(out Creatures self) && self.chasing;

    /// <summary>How high a slime is in its hop this tick, from 0 on the ground to 1 at the top. Zero for anything else.</summary>
    public static float HopArc(NPC npc) => npc.TryGetGlobalNPC(out Creatures self) && self.ready ? self.hopArc : 0f;

    /// <summary>
    /// A worm segment inside a solid block is underground: Stardew draws it faded, and it neither takes
    /// the farmer's melee swings nor touches the farmer.
    /// </summary>
    public static bool IsBuried(NPC npc)
        => npc.aiStyle == NPCAIStyleID.Worm && Collision.SolidCollision(npc.position, npc.width, npc.height);

    public override void PostAI(NPC npc)
    {
        if (!BridgeSystem.Active || !npc.active || npc.life <= 0)
            return;
        if (WallOfFlesh.Owns(npc))
            return;
        Fit(npc);
        Courage.Steer(npc);
        if (Followers.Hold(npc))
        {
            away = false;
            chasing = false;
            plane = Vector2.Zero;
            Land();
            return;
        }
        bool worm = npc.aiStyle == NPCAIStyleID.Worm;
        if (!ready || seenType != npc.type)
        {
            ready = true;
            seenType = npc.type;
            flyer = npc.noGravity && !worm;
            wander = npc.position;
            pace = 40;
            patience = PlaneMotion.WanderPatience;
            hopTick = -1;
            restTicks = 0;
            hopArc = 0f;
        }

        BridgeSystem bridge = BridgeSystem.Instance;
        WorldStamp.PlaceState? place = bridge.Linked ? bridge.Stamp.Current : null;
        bool here = place != null && place.ContainsWorld(npc.Center);
        // Flying bosses keep chasing across places. A walking boss is laid out against the farmer's place,
        // so away from it the boss holds still like everything else.
        away = !here && !(npc.boss && PlaneMotion.UsesRealAltitude(flyer, worm));
        if (away)
        {
            // Creatures stay where the farmer left them.
            chasing = false;
            plane = Vector2.Zero;
            npc.velocity = Vector2.Zero;
            Land();
            return;
        }

        npc.noGravity = true;
        Player player = Main.LocalPlayer;
        chasing = here && !npc.townNPC && !npc.friendly && npc.damage > 0 && npc.target == player.whoAmI && !player.dead;
        if (PlaneMotion.UsesRealAltitude(flyer, worm))
        {
            npc.noTileCollide = true;
            // Their own AI keeps its speeds, but the body is drawn at half size, so it travels half as far.
            npc.position -= npc.velocity * (1f - SpriteLayout.BodyScale);
            if (npc.boss && flyer && place?.Layout != null)
                ContainBoss(npc, place);
            return;
        }
        npc.noTileCollide = true;

        if (npc.justHit)
        {
            stagger = 12;
            plane = npc.velocity;
        }
        if (stagger > 0)
        {
            stagger--;
            plane *= 0.85f;
            Land();
            Collide(npc, plane);
            return;
        }

        Vector2 feet = new(npc.position.X + npc.width / 2f, npc.position.Y + npc.height);
        Vector2 goal;
        float arrive;
        if (chasing)
        {
            goal = bridge.Stamp.TryRoute(npc.whoAmI, out Vector2 step)
                ? step
                : Puppet.FeetOf(player);
            arrive = 3f;
        }
        else
        {
            goal = wander;
            arrive = 8f;
            patience--;
            bool there = Vector2.Distance(feet, goal) <= arrive;
            if (there || patience <= 0)
            {
                plane = Vector2.Zero;
                npc.velocity = Vector2.Zero;
                if (pace > 0 && patience > 0)
                {
                    pace--;
                    return;
                }
                PickWander(npc, feet, place);
                return;
            }
        }

        // Terraria pixels per tick; one is four Stardew pixels. The farmer walks about 1.25 and Stardew's people 0.5.
        float speed = npc.townNPC ? 0.5f : npc.aiStyle == NPCAIStyleID.Slime ? SlimeHopSpeed : npc.boss ? 1.6f : npc.damage <= 0 ? 0.5f : 0.8f;
        // A slime only moves while it is in the air. Between hops it sits still on the ground.
        if (npc.aiStyle == NPCAIStyleID.Slime && !Hop(npc))
        {
            npc.velocity = Vector2.Zero;
            return;
        }
        PlaneMotion.PlaneStep aim = PlaneMotion.Aim(feet.X, feet.Y, goal.X, goal.Y, speed, arrive);
        Vector2 move = new(aim.X, aim.Y);
        if (chasing)
            move += Spread(npc, feet);
        Collide(npc, move);
        if (MathF.Abs(aim.X) > 0.05f)
        {
            npc.direction = aim.X < 0f ? -1 : 1;
            npc.spriteDirection = npc.direction;
        }
    }

    /// <summary>
    /// A slime rests on the ground for a while, then makes one hop: for its length it moves towards its goal and
    /// its arc rises and falls. Returns false while it rests.
    /// </summary>
    private bool Hop(NPC npc)
    {
        if (hopTick < 0)
        {
            if (restTicks > 0)
            {
                restTicks--;
                hopArc = 0f;
                return false;
            }
            hopTick = 0;
        }
        hopTick++;
        hopArc = MathF.Sin(MathF.PI * hopTick / SlimeHopTicks);
        if (hopTick >= SlimeHopTicks)
        {
            hopTick = -1;
            hopArc = 0f;
            restTicks = Main.rand.Next(SlimeRestMin, SlimeRestMax + 1);
        }
        return true;
    }

    /// <summary>A slime that is pushed, held or left in another place lands and starts its next rest.</summary>
    private void Land()
    {
        hopTick = -1;
        hopArc = 0f;
    }

    /// <summary>
    /// Chasers that overlap push apart, so a crowd surrounds the farmer instead of stacking on one spot. Terraria's
    /// own walkers are kept apart by the ground they stand on; on a flat map nothing else would.
    /// </summary>
    private static Vector2 Spread(NPC npc, Vector2 feet)
    {
        Vector2 push = Vector2.Zero;
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC other = Main.npc[i];
            if (i == npc.whoAmI || !other.active || other.townNPC || other.friendly || other.aiStyle == NPCAIStyleID.Worm)
                continue;
            Vector2 apart = feet - new Vector2(other.position.X + other.width / 2f, other.position.Y + other.height);
            float room = (npc.width + other.width) * 0.5f;
            float distance = apart.Length();
            if (distance >= room)
                continue;
            apart = distance < 0.01f ? new Vector2(npc.whoAmI < i ? -1f : 1f, 0f) : apart / distance;
            push += apart * (room - distance) * 0.2f;
        }
        float length = push.Length();
        return length > 0.6f ? push * (0.6f / length) : push;
    }

    private void Collide(NPC npc, Vector2 step)
    {
        PlaneMotion.FeetBox box = PlaneMotion.Feet(npc.position.X, npc.position.Y, npc.width, npc.height);
        plane = Collision.TileCollision(new Vector2(box.X, box.Y), step, box.Width, box.Height);
        Show(npc, plane);
    }

    /// <summary>
    /// Terraria animates walkers only while they move sideways on the ground, so a creature walking north
    /// would freeze on one frame. The speed goes into velocity.X, as if walking along the ground, and the
    /// rest of the step moves the body directly.
    /// </summary>
    internal static void Show(NPC npc, Vector2 step)
    {
        float speed = step.Length();
        if (speed < 0.01f)
        {
            npc.velocity = Vector2.Zero;
            return;
        }
        int facing = MathF.Abs(step.X) > 0.05f ? MathF.Sign(step.X) : npc.direction == 0 ? 1 : npc.direction;
        float shown = facing * speed;
        npc.position += new Vector2(step.X - shown, step.Y);
        npc.velocity = new Vector2(shown, 0f);
    }

    /// <summary>Shrinks the hitbox to the drawn size, keeping the feet in place. Runs again if Terraria resizes the body.</summary>
    private void Fit(NPC npc)
    {
        if (npc.width == fitWidth && npc.height == fitHeight)
            return;
        Vector2 feet = new(npc.position.X + npc.width / 2f, npc.position.Y + npc.height);
        npc.width = SpriteLayout.Shrink(npc.width);
        npc.height = SpriteLayout.Shrink(npc.height);
        fitWidth = npc.width;
        fitHeight = npc.height;
        (float x, float y) = WorldProjection.FromFeet(feet.X, feet.Y, npc.width, npc.height);
        npc.position = new Vector2(x, y);
    }

    /// <summary>The farmer's own swings pass over buried worm segments, as they are drawn faded underground.</summary>
    public override bool? CanBeHitByItem(NPC npc, Player player, Item item)
        => BridgeSystem.Active && player.whoAmI == Main.myPlayer && IsBuried(npc) ? false : null;

    /// <summary>The farmer's own shots pass over buried worm segments, as they are drawn faded underground.</summary>
    public override bool? CanBeHitByProjectile(NPC npc, Projectile projectile)
        => BridgeSystem.Active && projectile.owner == Main.myPlayer && IsBuried(npc) ? false : null;

    /// <summary>Creatures left in another Stardew place wait there instead of despawning.</summary>
    public override bool CheckActive(NPC npc) => !(BridgeSystem.Active && away);

    private void PickWander(NPC npc, Vector2 feet, WorldStamp.PlaceState? place)
    {
        pace = 75;
        patience = PlaneMotion.WanderPatience;
        Vector2 center = feet;
        int range = 6;
        if (npc.townNPC && !npc.homeless && npc.homeTileX > 0)
        {
            center = new Vector2(npc.homeTileX * 16f + 8f, (npc.homeTileY + 1) * 16f - 2f);
            range = 5;
        }
        for (int attempt = 0; attempt < 12; attempt++)
        {
            int tileX = (int)(center.X / 16f) + Main.rand.Next(-range, range + 1);
            int tileY = (int)(center.Y / 16f) + Main.rand.Next(-range, range + 1);
            if (place != null && !place.IsOpen(tileX, tileY))
                continue;
            wander = new Vector2(tileX * 16f + 8f, (tileY + 1) * 16f - 2f);
            return;
        }
        wander = feet;
    }

    public override void EditSpawnRate(Player player, ref int spawnRate, ref int maxSpawns)
    {
        if (!BridgeSystem.Active)
            return;
        BridgeSystem bridge = BridgeSystem.Instance;
        WorldStamp.PlaceState? place = bridge.Stamp.Current;
        if (!bridge.Linked || place?.Layout == null || !place.Layout.Spawns)
            maxSpawns = 0;
    }

    public override void EditSpawnRange(Player player, ref int spawnRangeX, ref int spawnRangeY, ref int safeRangeX, ref int safeRangeY)
    {
        if (!BridgeSystem.Active)
            return;
        WorldStamp.PlaceState? place = BridgeSystem.Instance.Stamp.Current;
        if (place?.Layout == null)
            return;
        spawnRangeX = Math.Clamp(place.Rect.Width / 2 + 4, 20, 90);
        spawnRangeY = Math.Clamp(place.Rect.Height / 2 + 4, 16, 70);
        safeRangeX = 6;
        safeRangeY = 4;
    }

    /// <summary>Natural spawns land on an open, off-screen Stardew tile instead of on top of a block.</summary>
    public override void OnSpawn(NPC npc, IEntitySource source)
    {
        ready = false;
        // Only a boss summoned or spawned on its own: parts a boss makes for itself (worm segments, hands) come with it.
        if (BridgeSystem.Active && npc.boss && !npc.townNPC && (source is EntitySource_BossSpawn || source is EntitySource_SpawnNPC) && !Celestial.Summoning && !WallOfFlesh.Owns(npc))
        {
            PlaceBoss(npc);
            return;
        }
        if (!BridgeSystem.Active || npc.townNPC || npc.boss || source is not EntitySource_SpawnNPC)
            return;
        Player player = Main.LocalPlayer;
        Vector2 farmer = Puppet.FeetOf(player);
        WorldStamp stamp = BridgeSystem.Instance.Stamp;
        if (!Invasions.Allowed(npc, stamp.Current))
        {
            npc.active = false;
            return;
        }
        Vector2 feet;
        bool found = Invasions.IsInvader(npc)
            ? stamp.TryEdgeSpot(farmer, 11, out feet) || stamp.TryOpenSpot(farmer, 11, 45, out feet)
            : stamp.TryOpenSpot(farmer, 11, 45, out feet);
        if (!found)
        {
            npc.active = false;
            return;
        }
        (float x, float y) = WorldProjection.FromFeet(feet.X, feet.Y, npc.width, npc.height);
        npc.position = new Vector2(x, y);
        npc.velocity = Vector2.Zero;
    }

    /// <summary>
    /// Terraria drops a summoned boss offscreen, which is often outside the farmer's stamped place, where Stardew would
    /// never draw it. It lands on an open tile near the farmer instead, as the Celestial bosses do.
    /// </summary>
    private static void PlaceBoss(NPC npc)
    {
        if (BridgeSystem.Instance.Stamp.Current?.Layout == null)
            return;
        WorldStamp stamp = BridgeSystem.Instance.Stamp;
        Vector2 farmer = Puppet.FeetOf(Main.LocalPlayer);
        if (!stamp.TryOpenSpot(farmer, 11, 30, out Vector2 feet) && !stamp.TryOpenSpot(farmer, 3, 60, out feet))
            return;
        (float x, float y) = WorldProjection.FromFeet(feet.X, feet.Y, npc.width, npc.height);
        npc.position = new Vector2(x, y);
        npc.velocity = Vector2.Zero;
    }

    /// <summary>
    /// A flying boss hovers over the farmer, and its own AI can carry it past the Stardew map's edge. Stardew draws only
    /// the map, so the boss is held inside the farmer's place over the map, and a push into the edge stops it there.
    /// </summary>
    private static void ContainBoss(NPC npc, WorldStamp.PlaceState place)
    {
        var map = place.Layout!;
        const float Inset = 2f * 16f;
        float minX = (place.OriginX * 16f) + Inset;
        float maxX = ((place.OriginX + map.Width) * 16f) - Inset;
        float minY = (place.OriginY * 16f) + Inset;
        float maxY = ((place.OriginY + map.Height) * 16f) - Inset;
        Vector2 centre = npc.Center;
        float x = Math.Clamp(centre.X, minX, maxX);
        float y = Math.Clamp(centre.Y, minY, maxY);
        if (x == centre.X && y == centre.Y)
            return;
        if (x != centre.X)
            npc.velocity.X = 0f;
        if (y != centre.Y)
            npc.velocity.Y = 0f;
        npc.position += new Vector2(x - centre.X, y - centre.Y);
    }
}
