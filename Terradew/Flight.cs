using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>
/// Terraria movement in Stardew. Accessories, buffs and mounts change the walking speed. Wings and rocket boots fly:
/// hold Space and steer with the movement keys, with Terraria's drift, over anything in the way, until the flight
/// time runs out or Space is let go. A mount that jumps leaps forward in an arc on Space. Either way the farmer comes
/// down on the nearest open ground, never inside a wall. Flying works outdoors and in the mines, not indoors.
/// </summary>
internal sealed class Flight
{
    private enum Mode
    {
        Ground,
        Flying,
        Jumping,
        Landing,
    }

    /// <summary>Stardew pixels a tick for each Terraria pixel a tick: Terraria's walk (3) is Stardew's walk (5).</summary>
    private const float SpeedScale = 5f / 3f;
    private const float MostFlySpeed = 12f;
    /// <summary>How high the farmer flies, in screen pixels.</summary>
    private const float FlyHeight = 36f;
    private const float JumpHeight = 52f;
    private const int JumpTicks = 40;
    /// <summary>The leap moves forward until a little past the top of the arc; the rest is the drop.</summary>
    private const float JumpForward = 0.62f;
    private const float FallSpeed = 9f;
    /// <summary>How far to look for open ground, in tiles.</summary>
    private const int SearchTiles = 30;

    private static Flight? instance;

    private readonly Bridge bridge;
    private IMonitor? monitor;
    private Mode mode;
    private Vector2 velocity;
    private float height;
    private int fuel;
    private int jumpTick;
    private Vector2 jumpStep;
    private Vector2 takeoff;
    private Vector2? landing;
    private GameLocation? where;

    public Flight(Bridge bridge)
    {
        this.bridge = bridge;
        instance = this;
    }

    /// <summary>True while the farmer is off the ground, for the pulse: walking enemies cannot reach them.</summary>
    public static bool Airborne => instance is { mode: not Mode.Ground };

    /// <summary>How far above the ground the farmer is drawn, in screen pixels.</summary>
    public static float Lift => instance?.height ?? 0f;

    /// <summary>The farmer is drawn a little bigger in the air, as if closer to the camera.</summary>
    public static float Grow => 1f + 0.15f * System.Math.Clamp(Lift / FlyHeight, 0f, 1.2f);

    public void Install(Harmony harmony, IMonitor monitor)
    {
        this.monitor = monitor;
        try
        {
            var move = AccessTools.Method(typeof(Farmer), nameof(Farmer.MovePosition));
            if (move != null)
                harmony.Patch(move, prefix: new HarmonyMethod(typeof(Flight), nameof(BeforeMove)));
            else
                monitor.Log("Terradew could not find Farmer.MovePosition; flying is off.", LogLevel.Warn);
            var speed = AccessTools.Method(typeof(Farmer), nameof(Farmer.getMovementSpeed));
            if (speed != null)
                harmony.Patch(speed, postfix: new HarmonyMethod(typeof(Flight), nameof(AfterSpeed)));
            else
                monitor.Log("Terradew could not find Farmer.getMovementSpeed; Terraria speed accessories do nothing in Stardew.", LogLevel.Warn);
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not patch farmer movement, so flying and Terraria speed are off: {error.Message}", LogLevel.Warn);
        }
    }

    /// <summary>In the air the flight moves the farmer, so Stardew's walking (and its walls) are skipped.</summary>
    private static bool BeforeMove(Farmer __instance)
        => !(instance != null && instance.mode != Mode.Ground && __instance.IsLocalPlayer);

    private static void AfterSpeed(Farmer __instance, ref float __result)
    {
        Flight? self = instance;
        if (self == null || !__instance.IsLocalPlayer || !self.bridge.Linked)
            return;
        Movement? movement = self.bridge.Movement;
        if (movement == null)
            return;
        __result *= Math.Clamp(movement.Speed, 0.5f, 2.5f);
    }

    /// <summary>Call every tick while the world is ready.</summary>
    public void Update(IModHelper helper)
    {
        Farmer player = Game1.player;
        GameLocation? location = player.currentLocation;
        Movement? movement = bridge.Linked ? bridge.Movement : null;
        if (location == null || !ReferenceEquals(location, where) || Game1.eventUp)
        {
            // A new place (or a cutscene) always starts on the ground.
            Reset();
            where = location;
            return;
        }
        bool space = helper.Input.IsDown(SButton.Space);
        bool free = Context.IsPlayerFree;
        switch (mode)
        {
            case Mode.Ground:
                fuel = movement?.FlightTicks ?? 0;
                height = 0f;
                // Each flight or leap starts with a fresh press, as in Terraria.
                if (!free || movement == null || helper.Input.GetState(SButton.Space) != SButtonState.Pressed || !CanFlyHere(location))
                    break;
                if (movement.Mounted)
                {
                    if (movement.MountJumps)
                        StartJump(player, movement);
                }
                else if (movement.FlightTicks > 0)
                {
                    mode = Mode.Flying;
                    takeoff = player.Position;
                    velocity = Vector2.Zero;
                }
                break;
            case Mode.Flying:
                height += (FlyHeight + MathF.Sin(Game1.ticks / 10f) * 3f - height) * 0.15f;
                if (!free || !space || fuel <= 0 || movement == null || movement.Mounted || !CanFlyHere(location))
                {
                    StartLanding(player, location);
                    break;
                }
                fuel--;
                Steer(player, movement);
                Glide(player, location, velocity);
                break;
            case Mode.Jumping:
                jumpTick++;
                height = JumpHeight * MathF.Sin(MathF.PI * jumpTick / JumpTicks);
                if (jumpTick <= JumpTicks * JumpForward)
                {
                    Glide(player, location, jumpStep);
                    break;
                }
                // Past the top: come down here if it is open ground, else drop to open ground.
                if (landing == null)
                {
                    if (Open(location, player, player.Position))
                        landing = player.Position;
                    else
                        StartLanding(player, location);
                }
                if (jumpTick >= JumpTicks)
                    Land(player);
                break;
            case Mode.Landing:
                Descend(player, location);
                break;
        }
    }

    private static bool CanFlyHere(GameLocation location)
        => location.IsOutdoors || location is MineShaft || location is VolcanoDungeon;

    private void Reset()
    {
        mode = Mode.Ground;
        height = 0f;
        velocity = Vector2.Zero;
        landing = null;
    }

    private void StartJump(Farmer player, Movement movement)
    {
        mode = Mode.Jumping;
        jumpTick = 0;
        landing = null;
        takeoff = player.Position;
        Vector2 direction = Held();
        if (direction == Vector2.Zero)
            direction = Facing(player.FacingDirection);
        // A leap covers about three tiles at walking pace, more on a faster mount.
        float speed = player.getMovementSpeed() * 1.4f;
        jumpStep = direction * speed;
        Game1.playSound("dwoop");
    }

    /// <summary>Terraria's wing drift: the keys push, and with no keys the farmer slows gently.</summary>
    private void Steer(Farmer player, Movement movement)
    {
        float most = Math.Clamp(movement.FlySpeed * SpeedScale, 4f, MostFlySpeed);
        Vector2 push = Held();
        if (push != Vector2.Zero)
        {
            velocity += push * (most / 20f);
            player.FacingDirection = Math.Abs(push.X) > Math.Abs(push.Y) ? (push.X > 0 ? 1 : 3) : (push.Y > 0 ? 2 : 0);
        }
        else
            velocity *= 0.94f;
        if (velocity.Length() > most)
            velocity = Vector2.Normalize(velocity) * most;
    }

    /// <summary>The movement keys (or the left stick) held now, as a direction of length one or zero.</summary>
    private static Vector2 Held()
    {
        KeyboardState keys = Game1.input.GetKeyboardState();
        var push = Vector2.Zero;
        if (Game1.isOneOfTheseKeysDown(keys, Game1.options.moveUpButton)) push.Y -= 1f;
        if (Game1.isOneOfTheseKeysDown(keys, Game1.options.moveDownButton)) push.Y += 1f;
        if (Game1.isOneOfTheseKeysDown(keys, Game1.options.moveLeftButton)) push.X -= 1f;
        if (Game1.isOneOfTheseKeysDown(keys, Game1.options.moveRightButton)) push.X += 1f;
        Vector2 stick = Game1.input.GetGamePadState().ThumbSticks.Left;
        if (push == Vector2.Zero && stick.LengthSquared() > 0.09f)
            push = new Vector2(stick.X, -stick.Y);
        return push == Vector2.Zero ? push : Vector2.Normalize(push);
    }

    private static Vector2 Facing(int facing) => facing switch
    {
        0 => new Vector2(0f, -1f),
        1 => new Vector2(1f, 0f),
        3 => new Vector2(-1f, 0f),
        _ => new Vector2(0f, 1f),
    };

    /// <summary>Moves the farmer through the air, over walls but not off the map, and through any exit touched.</summary>
    private void Glide(Farmer player, GameLocation location, Vector2 step)
    {
        if (step == Vector2.Zero)
            return;
        Vector2 next = player.Position + step;
        // Exits sit just past the map's edge, so the farmer may reach a little beyond it.
        next.X = Math.Clamp(next.X, -48f, location.Map.DisplayWidth - 16f);
        next.Y = Math.Clamp(next.Y, -48f, location.Map.DisplayHeight - 16f);
        player.Position = next;
        Warp? warp = location.isCollidingWithWarp(player.GetBoundingBox(), player);
        if (warp != null)
        {
            Reset();
            player.warpFarmer(warp);
        }
    }

    /// <summary>
    /// Comes down: straight south to the first open ground, else to the open ground nearest both here and the takeoff
    /// point. Over open ground it simply lands where it is.
    /// </summary>
    private void StartLanding(Farmer player, GameLocation location)
    {
        velocity = Vector2.Zero;
        landing = FindLanding(location, player) ?? takeoff;
        mode = mode == Mode.Jumping ? Mode.Jumping : Mode.Landing;
    }

    private void Descend(Farmer player, GameLocation location)
    {
        height = Math.Max(0f, height - 3f);
        Vector2 target = landing ?? player.Position;
        Vector2 toward = target - player.Position;
        float distance = toward.Length();
        if (distance > FallSpeed)
            Glide(player, location, toward / distance * FallSpeed);
        else
            player.Position = target;
        if (distance <= FallSpeed && height <= 0f)
            Land(player);
    }

    private void Land(Farmer player)
    {
        if (landing is { } spot)
        {
            // A jump that came down over a wall drops the rest of the way.
            if (mode == Mode.Jumping && Vector2.Distance(spot, player.Position) > 1f)
            {
                mode = Mode.Landing;
                return;
            }
            player.Position = spot;
        }
        Reset();
        Game1.playSound("thudStep");
    }

    private static Vector2? FindLanding(GameLocation location, Farmer player)
    {
        Vector2 here = player.Position;
        if (Open(location, player, here))
            return here;
        // South first, as if falling.
        for (int down = 1; down <= SearchTiles; down++)
        {
            Vector2 spot = here + new Vector2(0f, down * 64f);
            if (spot.Y > location.Map.DisplayHeight)
                break;
            if (Open(location, player, spot))
                return spot;
        }
        Vector2 start = instance?.takeoff ?? here;
        Vector2? best = null;
        float bestScore = float.MaxValue;
        Point tile = new((int)(here.X / 64f), (int)(here.Y / 64f));
        for (int dy = -SearchTiles; dy <= SearchTiles; dy++)
        {
            for (int dx = -SearchTiles; dx <= SearchTiles; dx++)
            {
                Vector2 spot = new((tile.X + dx) * 64f, (tile.Y + dy) * 64f);
                float score = Vector2.Distance(spot, here) + Vector2.Distance(spot, start);
                if (score >= bestScore || !Open(location, player, spot))
                    continue;
                best = spot;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>Whether the farmer could stand with their position at a spot: on the map, and touching no wall or water.</summary>
    private static bool Open(GameLocation location, Farmer player, Vector2 position)
    {
        Rectangle box = player.GetBoundingBox();
        box.Offset((int)(position.X - player.Position.X), (int)(position.Y - player.Position.Y));
        if (box.Left < 0 || box.Top < 0 || box.Right > location.Map.DisplayWidth || box.Bottom > location.Map.DisplayHeight)
            return false;
        return !location.isCollidingPosition(box, Game1.viewport, true, 0, false, player);
    }
}
