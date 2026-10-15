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

    /// <summary>
    /// Stardew pixels a tick of flight for each Terraria pixel a tick of wing speed. Most wings fly at about 6.25 in
    /// Terraria, which comes out a little slower than Stardew's walk (5); faster wings fly faster.
    /// </summary>
    private const float SpeedScale = 4.5f / 6.25f;
    private const float LeastFlySpeed = 3.5f;
    private const float MostFlySpeed = 8f;
    /// <summary>How high the farmer flies, in screen pixels.</summary>
    private const float FlyHeight = 36f;
    private const float JumpHeight = 52f;
    private const int JumpTicks = 40;
    /// <summary>The leap moves forward until a little past the top of the arc; the rest is the drop.</summary>
    private const float JumpSpeed = 4.5f;

    /// <summary>Ticks a leap keeps sliding forward after touching down over a wall or water, looking for ground.</summary>
    private const int CarryTicks = 24;
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
                height = JumpHeight * MathF.Sin(MathF.PI * Math.Min(jumpTick, JumpTicks) / JumpTicks);
                // The leap carries forward the whole way down, and on past its end while there is nowhere to stand.
                Glide(player, location, jumpStep);
                if (mode != Mode.Jumping || jumpTick < JumpTicks)
                    break;
                if (Open(location, player, player.Position))
                {
                    Reset();
                    Game1.playSound("thudStep");
                }
                else if (jumpTick > JumpTicks + CarryTicks)
                    StartLanding(player, location);
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
        // A leap covers about three tiles, more on a faster mount, the same everywhere (Stardew's own walking speed
        // changes with paths and buffs, so it is not used).
        jumpStep = direction * JumpSpeed * Math.Clamp(movement.Speed, 1f, 2.2f);
        Game1.playSound("dwoop");
    }

    /// <summary>Terraria's wing drift: the keys push, and with no keys the farmer slows gently.</summary>
    private void Steer(Farmer player, Movement movement)
    {
        float most = Math.Clamp(movement.FlySpeed * SpeedScale, LeastFlySpeed, MostFlySpeed);
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

    /// <summary>
    /// Moves the farmer through the air, over walls but not off the map or into floor no path reaches (Reach), and
    /// through any exit touched. Each axis is tried on its own, so the farmer slides along a sealed edge.
    /// </summary>
    private void Glide(Farmer player, GameLocation location, Vector2 step)
    {
        if (step == Vector2.Zero)
            return;
        Vector2 next = player.Position + step;
        if (IntoSealed(location, player, new Vector2(next.X, player.Position.Y)))
            next.X = player.Position.X;
        if (IntoSealed(location, player, next))
            next.Y = player.Position.Y;
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

    /// <summary>True when the farmer's box at this position touches a sealed tile it is not already over.</summary>
    private static bool IntoSealed(GameLocation location, Farmer player, Vector2 position)
    {
        Rectangle now = player.GetBoundingBox();
        Rectangle box = now;
        box.Offset((int)(position.X - player.Position.X), (int)(position.Y - player.Position.Y));
        for (int y = box.Top / 64; y <= (box.Bottom - 1) / 64; y++)
        {
            for (int x = box.Left / 64; x <= (box.Right - 1) / 64; x++)
            {
                bool already = x >= now.Left / 64 && x <= (now.Right - 1) / 64 && y >= now.Top / 64 && y <= (now.Bottom - 1) / 64;
                if (!already && Reach.Sealed(location, x, y))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Comes down on the nearest open ground south, east or west (see FindLanding), or back where it took off if
    /// there is none. Over open ground it simply lands where it is.
    /// </summary>
    private void StartLanding(Farmer player, GameLocation location)
    {
        velocity = Vector2.Zero;
        landing = FindLanding(location, player) ?? takeoff;
        mode = Mode.Landing;
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
            player.Position = spot;
        Reset();
        Game1.playSound("thudStep");
    }

    /// <summary>
    /// Where to come down: here if it is open, else the nearest open ground south, east or west, never north (as if
    /// falling). Straight south wins a tie.
    /// </summary>
    private static Vector2? FindLanding(GameLocation location, Farmer player)
    {
        Vector2 here = player.Position;
        if (Open(location, player, here))
            return here;
        Point tile = new((int)MathF.Round(here.X / 64f), (int)MathF.Round(here.Y / 64f));
        Vector2? best = null;
        float bestScore = float.MaxValue;
        for (int dy = 0; dy <= SearchTiles; dy++)
        {
            // Nothing further south can beat what was found.
            if (dy * 64f > bestScore)
                break;
            for (int dx = -SearchTiles; dx <= SearchTiles; dx++)
            {
                Vector2 spot = new((tile.X + dx) * 64f, (tile.Y + dy) * 64f);
                float score = Vector2.Distance(spot, here) + Math.Abs(dx) * 0.01f;
                if (score >= bestScore || !Open(location, player, spot))
                    continue;
                best = spot;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>
    /// Whether the farmer could stand with their position at a spot: on the map, with every tile under them one a
    /// creature could walk on, and touching nothing solid.
    /// </summary>
    private static bool Open(GameLocation location, Farmer player, Vector2 position)
    {
        Rectangle box = player.GetBoundingBox();
        box.Offset((int)(position.X - player.Position.X), (int)(position.Y - player.Position.Y));
        if (box.Left < 0 || box.Top < 0 || box.Right > location.Map.DisplayWidth || box.Bottom > location.Map.DisplayHeight)
            return false;
        for (int y = box.Top / 64; y <= (box.Bottom - 1) / 64; y++)
        {
            for (int x = box.Left / 64; x <= (box.Right - 1) / 64; x++)
            {
                if (!Standable(location, x, y))
                    return false;
            }
        }
        // Stardew's own walking check catches what the tiles miss, such as a building's or a fence's edge.
        return !location.isCollidingPosition(box, Game1.viewport, true, 0, false, player);
    }

    /// <summary>
    /// A tile the farmer can stand on, by the same rules the creatures path with (Maps), plus the ground itself:
    /// no water, a floor tile to stand on, and nothing the map marks impassable. Walking never needs these, as
    /// Stardew's walls keep the farmer off such tiles; dropping from the air does.
    /// </summary>
    private static bool Standable(GameLocation location, int x, int y)
    {
        if (!location.isTileOnMap(new Vector2(x, y)) || location.isWaterTile(x, y))
            return false;
        if (location.Map.GetLayer("Back")?.Tiles[x, y] == null || Reach.Sealed(location, x, y))
            return false;
        if (!location.isTilePassable(new xTile.Dimensions.Location(x, y), Game1.viewport))
            return false;
        return !location.IsTileBlockedBy(new Vector2(x, y), Maps.Mask, Maps.Mask, false);
    }
}
