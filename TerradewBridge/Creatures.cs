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

    public static bool IsFlyer(NPC npc) => npc.TryGetGlobalNPC(out Creatures self) && self.ready && self.flyer;

    public static bool IsChasing(NPC npc) => npc.TryGetGlobalNPC(out Creatures self) && self.chasing;

    public override void PostAI(NPC npc)
    {
        if (!BridgeSystem.Active || !npc.active || npc.life <= 0)
            return;
        Fit(npc);
        bool worm = npc.aiStyle == NPCAIStyleID.Worm;
        if (!ready || seenType != npc.type)
        {
            ready = true;
            seenType = npc.type;
            flyer = npc.noGravity && !worm;
            wander = npc.position;
            pace = 40;
            patience = PlaneMotion.WanderPatience;
        }

        BridgeSystem bridge = BridgeSystem.Instance;
        WorldStamp.PlaceState? place = bridge.Linked ? bridge.Stamp.Current : null;
        bool here = place != null && place.ContainsWorld(npc.Center);
        away = !here && !npc.boss;
        if (away)
        {
            // Creatures stay where the farmer left them.
            chasing = false;
            plane = Vector2.Zero;
            npc.velocity = Vector2.Zero;
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
                : new Vector2(player.position.X + player.width / 2f, player.position.Y + player.height);
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

        float speed = npc.townNPC ? 0.85f : npc.aiStyle == NPCAIStyleID.Slime ? 1.2f : npc.boss ? 2.4f : 1.5f;
        PlaneMotion.PlaneStep aim = PlaneMotion.Aim(feet.X, feet.Y, goal.X, goal.Y, speed, arrive);
        Collide(npc, new Vector2(aim.X, aim.Y));
        if (MathF.Abs(aim.X) > 0.05f)
        {
            npc.direction = aim.X < 0f ? -1 : 1;
            npc.spriteDirection = npc.direction;
        }
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
    private static void Show(NPC npc, Vector2 step)
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
        if (!BridgeSystem.Active || npc.townNPC || npc.boss || source is not EntitySource_SpawnNPC)
            return;
        Player player = Main.LocalPlayer;
        Vector2 farmer = new(player.position.X + player.width / 2f, player.position.Y + player.height);
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
}
