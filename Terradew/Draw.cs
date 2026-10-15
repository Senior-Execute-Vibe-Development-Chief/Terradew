using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using Terradew.Core;
using Terradew.Core.Combat;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>
/// Draws Terraria's creatures, projectiles, dropped items and the farmer's Terraria gear on the Stardew map.
/// It hooks the location's own draw so everything sorts by depth with trees, buildings and the farmer;
/// if that hook is unavailable it draws on top of the world instead.
/// </summary>
internal sealed class Draw
{
    private static Draw? instance;

    /// <summary>Terraria NPC types the drawing treats specially.</summary>
    private const int WallOfFleshType = 113;
    private const int WallOfFleshEyeType = 114;
    private const int MoonLordCoreType = 398;
    /// <summary>The crystal leaf's laser shot, which Terraria draws as a beam along its flight rather than from its sprite.</summary>
    private const int CrystalLeafShotType = 227;

    /// <summary>A hopping slime's height at its jump's peak, in Stardew pixels, before its size scales it.</summary>
    private const float HopHeight = 14f;
    /// <summary>How far a slime travels, in Terraria pixels, for each hop. Walking it on from one hop to the next.</summary>
    private const float HopTerrariaPixels = 32f;
    /// <summary>The crystal leaf's laser, as Terraria pixels long and Stardew pixels thick.</summary>
    private const float LaserTerrariaLength = 12f;
    private const float LaserThickness = 6f;
    private static readonly Color LaserColour = new(150, 255, 170);

    /// <summary>
    /// Depth of the flesh band. Stardew draws larger depths later, so this sits above every creature south of
    /// the wall's edge, which hides them inside the flesh. The wall's own sprites sit just above the band.
    /// </summary>
    private const float WallBandDepth = 0.9998f;
    private const float WallPartDepth = 0.9999f;

    private readonly Bridge bridge;
    private readonly SpriteBook sprites;
    private readonly Hand hand;
    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private bool drewSorted;
    private bool loggedDrawError;
    /// <summary>Hop state by entity id. Rebuilt from each new world state, so slimes that left the world drop out.</summary>
    private Dictionary<int, HopState> hops = new();
    private WorldState? hopWorld;

    /// <summary>A hopping slime's position in the last world state, and its phase through its hops (radians; each half turn is one hop).</summary>
    private sealed class HopState
    {
        public float X;
        public float Y;
        public float Phase;
    }

    public Draw(Bridge bridge, SpriteBook sprites, Hand hand, IModHelper helper, IMonitor monitor)
    {
        this.bridge = bridge;
        this.sprites = sprites;
        this.hand = hand;
        this.helper = helper;
        this.monitor = monitor;
        instance = this;
    }

    public void Install(Harmony harmony, IMonitor monitor)
    {
        try
        {
            var target = AccessTools.Method(typeof(GameLocation), nameof(GameLocation.draw), new[] { typeof(SpriteBatch) });
            if (target == null)
            {
                monitor.Log("Terradew could not find GameLocation.draw; creatures will draw on top of the world.", LogLevel.Warn);
                return;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(Draw), nameof(AfterLocationDraw)));
        }
        catch (Exception error)
        {
            monitor.Log($"Terradew could not hook world drawing; creatures will draw on top of the world. {error.Message}", LogLevel.Warn);
        }
    }

    private static void AfterLocationDraw(GameLocation __instance, SpriteBatch b)
    {
        if (instance == null || __instance != Game1.currentLocation || !Context.IsWorldReady)
            return;
        try
        {
            instance.World(b, sorted: true);
            instance.drewSorted = true;
        }
        catch (Exception error)
        {
            // Logged once: a fault here would otherwise repeat every frame.
            if (instance.loggedDrawError)
                return;
            instance.loggedDrawError = true;
            instance.monitor.Log($"Terradew could not draw the sorted creatures; they fall back to drawing over the world. {error.Message}", LogLevel.Warn);
        }
    }

    public void OnRenderedWorld(SpriteBatch b)
    {
        if (!drewSorted)
            World(b, sorted: false);
        drewSorted = false;
    }

    private void World(SpriteBatch b, bool sorted)
    {
        Farmer player = Game1.player;
        if (player.currentLocation == null)
            return;
        string key = Maps.PlaceOf(player.currentLocation).Key;
        WallFight? wall = ActiveWall(key);
        // The band goes first so that, when the world is drawn unsorted, the creatures and gear draw over it.
        if (wall != null)
            DrawWallBand(b, player.currentLocation, wall, sorted);
        TerrariaBody.DrawOver(b, Depth(player.GetBoundingBox().Bottom, sorted, 1f));
        WorldState? world = bridge.World;
        if (world == null || !bridge.WorldIsFor(key))
            return;
        // Hops advance once per new world state, so drawing the same state again (or twice in a frame) does not hop it further.
        if (!ReferenceEquals(world, hopWorld))
        {
            hopWorld = world;
            AdvanceHops(world);
        }

        foreach (EntityState entity in world.Entities)
        {
            // A town NPC playing a villager is drawn as that villager, by Stardew.
            if (entity.Has(EntityFlags.Town) && Cast.Plays(entity.NpcType))
                continue;
            (float x, float y) = WorldProjection.ToMap(world.RegionX, world.RegionY, entity.X, entity.Y);
            // A big body reaches well past its feet point, so the cull margin grows with its hitbox.
            int reach = 64 * 6 + (int)(Math.Max(entity.Width, entity.Height) * WorldProjection.Scale);
            if (!Utility.isOnScreen(new Vector2(x, y), reach))
                continue;
            DrawEntity(b, entity, x, y, sorted, wall);
        }
        foreach (DropState drop in world.Drops)
        {
            (float x, float y) = WorldProjection.ToMap(world.RegionX, world.RegionY, drop.X, drop.Y);
            if (Utility.isOnScreen(new Vector2(x, y), 64))
                DrawDrop(b, drop, x, y, sorted);
        }
        foreach (ProjectileState projectile in world.Projectiles)
        {
            (float x, float y) = projectile.Attached
                ? AttachedAt(player, projectile)
                : WorldProjection.ToMap(world.RegionX, world.RegionY, projectile.X, projectile.Y);
            if (Utility.isOnScreen(new Vector2(x, y), 64 * 4))
                DrawProjectile(b, projectile, x, y, sorted);
        }
        DrawSwings(b, key, sorted);
        // The Terraria character already wears its armor and holds its item.
        if (TerrariaBody.Showing)
            return;
        DrawHeld(b, world, sorted);
        DrawGear(b, sorted);
    }

    private static float Depth(float stardewY, bool sorted, float nudge = 0f)
        => sorted ? Math.Clamp((stardewY + nudge) / 10000f, 0f, 1f) : 0f;

    /// <summary>The Wall of Flesh fight, when it runs in the farmer's place. Null otherwise.</summary>
    private WallFight? ActiveWall(string placeKey)
    {
        WallFight? wall = bridge.Wall;
        return wall != null && wall.Active && wall.PlaceKey == placeKey ? wall : null;
    }

    private static Vector2 Snap(Vector2 point) => new(MathF.Round(point.X), MathF.Round(point.Y));

    private static bool IsWallPart(int type) => type == WallOfFleshType || type == WallOfFleshEyeType;

    /// <summary>
    /// Terraria splits some bosses into parts that each arrive as their own entity: Skeletron Prime's head and
    /// arms (127-131), Golem's free head and fists (246-249), and Moon Lord's head, hands and core (396-398).
    /// Each part is drawn around its own hitbox rather than standing on its feet.
    /// </summary>
    private static bool IsComposite(int type) => type is >= 127 and <= 131 or >= 246 and <= 249 or >= 396 and <= 398;

    /// <summary>
    /// Moves each slime's hop on from its last position. A slime that moved walks its phase on by the distance it
    /// travelled, so it hops as it goes; one that stood still settles onto the ground at the nearest landing.
    /// </summary>
    private void AdvanceHops(WorldState world)
    {
        var next = new Dictionary<int, HopState>();
        foreach (EntityState entity in world.Entities)
        {
            if (!entity.Has(EntityFlags.Hopper))
                continue;
            if (!hops.TryGetValue(entity.Id, out HopState? hop))
                hop = new HopState { X = entity.X, Y = entity.Y };
            float dx = entity.X - hop.X;
            float dy = entity.Y - hop.Y;
            float moved = MathF.Sqrt(dx * dx + dy * dy);
            if (moved > 0.05f)
                hop.Phase = (hop.Phase + moved * MathF.PI / HopTerrariaPixels) % MathF.PI;
            else
            {
                // Each landing is a multiple of pi; ease towards the nearest one.
                float landing = MathF.Round(hop.Phase / MathF.PI) * MathF.PI;
                hop.Phase += (landing - hop.Phase) * 0.25f;
            }
            hop.X = entity.X;
            hop.Y = entity.Y;
            next[entity.Id] = hop;
        }
        hops = next;
    }

    /// <summary>How far a slime is lifted off the ground right now, in Stardew pixels. Zero for one that is not hopping.</summary>
    private float HopLift(EntityState entity)
    {
        if (!hops.TryGetValue(entity.Id, out HopState? hop))
            return 0f;
        // The lift is HopHeight scaled by the slime's size, and follows |sin| so each hop rises and lands.
        float height = HopHeight * Math.Clamp(entity.Height / 24f, 0.5f, 2.5f);
        return height * MathF.Abs(MathF.Sin(hop.Phase));
    }

    private void DrawEntity(SpriteBatch b, EntityState entity, float x, float y, bool sorted, WallFight? wall)
    {
        Texture2D? sheet = sprites.Load(SpriteNames.NpcSheet(entity.NpcType, entity.Look)) ?? sprites.Load(SpriteNames.Npc(entity.NpcType));
        // Vanilla draws the Moon Lord core as one large body texture with no NPC sprite. Draw nothing rather than a square.
        if (sheet == null && entity.NpcType == MoonLordCoreType)
            return;
        // Terraria's own colour for the body (see-through slimes and ghosts, glowing things), then the tint pass for coloured NPCs.
        Color body = entity.Color != 0 ? new Color { PackedValue = entity.Color } : Color.White * ((255 - entity.Alpha) / 255f);
        Color tint = new() { PackedValue = entity.Tint };
        float fade = entity.Has(EntityFlags.Buried) ? 0.45f : 1f;
        float alpha = Math.Max(body.A, tint.A) / 255f * fade;
        if (alpha <= 0.02f)
            return;

        bool onWall = wall != null && IsWallPart(entity.NpcType);
        float scale = SpriteLayout.DrawScale * (entity.Scale <= 0f ? 1f : entity.Scale);
        float bodyHeight = Math.Max(2, (int)entity.Height) * WorldProjection.Scale;
        // Terraria's Wall of Flesh sprites face west (and are flipped to face east). The vertical wall turns them a
        // quarter clockwise so the mouth and eyes face north.
        float rotation = onWall ? MathF.PI / 2f : entity.Rotation;
        bool airborne = entity.Has(EntityFlags.Flyer) || entity.Has(EntityFlags.Worm) || IsComposite(entity.NpcType) || Math.Abs(rotation) > 0.01f;
        // A hopping slime is lifted by its hop, and its shadow stays on the ground, shrinking and fading as it rises.
        float lift = entity.Has(EntityFlags.Hopper) && !airborne && !onWall ? HopLift(entity) : 0f;
        float aloft = lift / (HopHeight * Math.Clamp(entity.Height / 24f, 0.5f, 2.5f));
        Rectangle? source = sheet != null && entity.FrameW > 0 && entity.FrameH > 0
            ? Clamp(new Rectangle(entity.FrameX, entity.FrameY, entity.FrameW, entity.FrameH), sheet)
            : null;
        // Once turned north, the sprite's width runs north to south, so its thickness is the band it covers.
        float thickness = source.HasValue ? source.Value.Width * scale : 96f;
        float depth = onWall ? (sorted ? WallPartDepth : 0f) : Depth(y, sorted);
        var effects = entity.Direction == 1 && !onWall ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

        float drawnWidth = source.HasValue ? source.Value.Width * scale : entity.Width * WorldProjection.Scale;
        if (!entity.Has(EntityFlags.Flyer) && !onWall)
        {
            var shadow = Snap(Game1.GlobalToLocal(Game1.viewport, new Vector2(x, y)));
            // Stardew's shadow is 12 pixels across: as wide as the body is drawn, a little narrower for big ones.
            float shadowScale = Math.Clamp(drawnWidth * 0.7f / Game1.shadowTexture.Width, 2f, 12f) * (1f - 0.35f * aloft);
            b.Draw(Game1.shadowTexture, shadow, Game1.shadowTexture.Bounds, Color.Black * 0.6f * alpha * (1f - 0.5f * aloft), 0f,
                new Vector2(Game1.shadowTexture.Width / 2f, Game1.shadowTexture.Height / 2f), shadowScale, SpriteEffects.None, Math.Max(0f, depth - 0.0001f));
        }

        // The middle of the body, in Stardew world pixels. Flyers, parts and worms are centred on it; grounded bodies stand on their feet.
        bool centred = onWall || airborne;
        Vector2 centre = onWall
            ? new Vector2(x, (wall?.WallY ?? 0) + thickness / 2f)
            : airborne ? new Vector2(x, y - bodyHeight / 2f) : new Vector2(x, y - lift);
        // Whole screen pixels, so each art pixel covers the same number of them and nothing shimmers as it moves.
        Vector2 local = Snap(Game1.GlobalToLocal(Game1.viewport, centre));

        if (sheet != null && source.HasValue)
        {
            Rectangle frame = source.Value;
            Vector2 origin = centred
                ? new Vector2(frame.Width / 2, frame.Height / 2)
                : new Vector2(frame.Width / 2, frame.Height);
            if (body.A > 0)
                b.Draw(sheet, local, frame, body * fade, rotation, origin, scale, effects, depth);
            if (tint.A > 0)
                b.Draw(sheet, local, frame, tint * fade, rotation, origin, scale, effects, depth + 0.00001f);
        }
        else
        {
            // Same anchor as the sprite, so a missing sprite is a stand-in for the same body.
            int size = entity.Has(EntityFlags.Boss) ? 96 : 40;
            Color color = entity.Has(EntityFlags.Town) ? new Color(90, 170, 90) : entity.Has(EntityFlags.Boss) ? new Color(180, 40, 40) : new Color(220, 190, 120);
            int top = centred ? (int)local.Y - size / 2 : (int)local.Y - size;
            b.Draw(Game1.staminaRect, new Rectangle((int)local.X - size / 2, top, size, size), null, color * alpha, 0f, Vector2.Zero, SpriteEffects.None, depth);
        }

        if (entity.LifeMax > 0 && entity.Life > 0 && entity.Life < entity.LifeMax && !entity.Has(EntityFlags.Town))
        {
            // The lower edge of the drawn body: the feet when grounded, the bottom of the picture when centred or flying.
            float halfHeight = source.HasValue ? (onWall ? thickness : source.Value.Height * scale) / 2f : bodyHeight / 2f;
            float bottom = centred ? local.Y + halfHeight : local.Y;
            DrawHealthBar(b, entity, local.X, bottom, drawnWidth, depth);
        }
        var head = Game1.GlobalToLocal(Game1.viewport, new Vector2(x, y - bodyHeight - 12f));
        if (entity.Has(EntityFlags.Town) || entity.NpcType == Hand.OldManType)
        {
            Vector2 size = Game1.smallFont.MeasureString(entity.Name);
            b.DrawString(Game1.smallFont, entity.Name, head - new Vector2(size.X / 2f, size.Y), Color.White, 0f, Vector2.Zero, 1f, SpriteEffects.None, Math.Min(1f, depth + 0.0003f));
        }
    }

    /// <summary>
    /// Terraria's health bar: a thin bar just below the creature, under its feet when it stands and under its body
    /// when it is centred or flying. It is as wide as the body, within 36 to 96 pixels, and its fill goes green,
    /// yellow, then red as the creature loses life.
    /// </summary>
    private static void DrawHealthBar(SpriteBatch b, EntityState entity, float centreX, float bottom, float drawnWidth, float depth)
    {
        int width = Math.Clamp((int)(drawnWidth * 0.8f), 36, 96);
        float fill = Math.Clamp(entity.Life / (float)entity.LifeMax, 0f, 1f);
        Color colour = fill >= 0.66f ? Color.Lerp(Color.Yellow, Color.LimeGreen, (fill - 0.66f) / 0.34f)
            : fill >= 0.33f ? Color.Lerp(Color.Red, Color.Yellow, (fill - 0.33f) / 0.33f)
            : Color.Red;
        int left = (int)MathF.Round(centreX) - width / 2;
        int top = (int)MathF.Round(bottom) + 4;
        b.Draw(Game1.staminaRect, new Rectangle(left, top, width, 8), null, Color.Black * 0.7f, 0f, Vector2.Zero, SpriteEffects.None, Math.Min(1f, depth + 0.0002f));
        b.Draw(Game1.staminaRect, new Rectangle(left + 1, top + 1, width - 2, 6), null, Color.Black * 0.4f, 0f, Vector2.Zero, SpriteEffects.None, Math.Min(1f, depth + 0.0003f));
        int filled = (int)((width - 2) * fill);
        if (filled > 0)
            b.Draw(Game1.staminaRect, new Rectangle(left + 1, top + 1, filled, 6), null, colour, 0f, Vector2.Zero, SpriteEffects.None, Math.Min(1f, depth + 0.0004f));
    }

    /// <summary>
    /// The vertical Wall of Flesh: flesh across the whole map width, from the wall's north edge down to the
    /// bottom of the map. Terraria's wall body texture is not exported, so it is drawn as a flesh-red band with a
    /// lighter veined edge along the top.
    /// </summary>
    private static void DrawWallBand(SpriteBatch b, GameLocation location, WallFight wall, bool sorted)
    {
        int width = location.Map.DisplayWidth;
        int mapHeight = location.Map.DisplayHeight;
        if (width <= 0 || wall.WallY >= mapHeight)
            return;
        float depth = sorted ? WallBandDepth : 0f;
        Vector2 top = Game1.GlobalToLocal(Game1.viewport, new Vector2(0f, wall.WallY));
        int left = (int)top.X;
        int edge = (int)top.Y;
        int height = mapHeight - wall.WallY;
        b.Draw(Game1.staminaRect, new Rectangle(left, edge, width, height), null, new Color(120, 20, 30) * 0.9f, 0f, Vector2.Zero, SpriteEffects.None, depth);
        b.Draw(Game1.staminaRect, new Rectangle(left, edge, width, 8), null, new Color(200, 80, 90) * 0.9f, 0f, Vector2.Zero, SpriteEffects.None, depth);
        for (int vein = 40; vein < width; vein += 96)
            b.Draw(Game1.staminaRect, new Rectangle(left + vein, edge, 3, 8), null, new Color(90, 10, 20) * 0.9f, 0f, Vector2.Zero, SpriteEffects.None, depth);
    }

    /// <summary>
    /// Where an attached shot sits in Stardew world pixels: the farmer's feet plus its Terraria offset, scaled to
    /// the half-size sprites. Following the farmer's feet keeps it on the picture, not on the Terraria body.
    /// </summary>
    private static (float x, float y) AttachedAt(Farmer player, ProjectileState projectile)
    {
        Rectangle box = player.GetBoundingBox();
        return (box.Center.X + projectile.OffsetX * SpriteLayout.DrawScale, box.Bottom + projectile.OffsetY * SpriteLayout.DrawScale);
    }

    private void DrawProjectile(SpriteBatch b, ProjectileState projectile, float x, float y, bool sorted)
    {
        Texture2D? sheet = sprites.Load(SpriteNames.Projectile(projectile.ProjType));
        if (sheet == null)
            return;
        int frames = Math.Max(1, (int)projectile.FrameCount);
        int height = Math.Max(1, sheet.Height / frames);
        int frame = Math.Clamp((int)projectile.Frame, 0, frames - 1);
        var source = new Rectangle(0, frame * height, sheet.Width, height);
        Color color = projectile.Color != 0 ? new Color { PackedValue = projectile.Color } : Color.White * ((255 - projectile.Alpha) / 255f);
        var effects = projectile.Direction == -1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        // Whole screen pixels for attached shots, which move with the farmer, so the crystal does not shimmer.
        var position = projectile.Attached
            ? Snap(Game1.GlobalToLocal(Game1.viewport, new Vector2(x, y)))
            : Game1.GlobalToLocal(Game1.viewport, new Vector2(x, y));
        float scale = SpriteLayout.DrawScale * (projectile.Scale <= 0f ? 1f : projectile.Scale);
        if (projectile.ProjType == CrystalLeafShotType)
        {
            DrawLaser(b, sheet, source, position, projectile.Rotation, projectile.Scale <= 0f ? 1f : projectile.Scale, Depth(y, sorted, 32f));
            return;
        }
        b.Draw(sheet, position, source, color, projectile.Rotation, new Vector2(source.Width / 2f, source.Height / 2f), scale, effects, Depth(y, sorted, 32f));
    }

    /// <summary>
    /// The crystal leaf's shot is a beam that Terraria draws in code along its flight, and its transparency can leave
    /// it invisible in Stardew. So it is drawn at full strength in a light green, its own texture stretched along its
    /// direction of travel (its rotation) and centred on the shot.
    /// </summary>
    private static void DrawLaser(SpriteBatch b, Texture2D sheet, Rectangle source, Vector2 position, float rotation, float scale, float depth)
    {
        float length = LaserTerrariaLength * WorldProjection.Scale * scale;
        var stretch = new Vector2(length / source.Width, LaserThickness * scale / source.Height);
        b.Draw(sheet, position, source, LaserColour, rotation, new Vector2(source.Width / 2f, source.Height / 2f), stretch, SpriteEffects.None, depth);
    }

    private void DrawDrop(SpriteBatch b, DropState drop, float x, float y, bool sorted)
    {
        Texture2D? icon = sprites.Load(SpriteNames.Item(drop.ItemType));
        if (icon == null)
            return;
        float bob = (float)Math.Sin((Game1.ticks + drop.Id * 13) / 12.0) * 4f;
        var position = Snap(Game1.GlobalToLocal(Game1.viewport, new Vector2(x, y + bob)));
        var ground = Snap(Game1.GlobalToLocal(Game1.viewport, new Vector2(x, y + 12f)));
        // Terraria's own size, one icon pixel to two screen pixels, but never more than about half a tile.
        float scale = SpriteLayout.DrawScale * Math.Min(1f, 16f / Math.Max(icon.Width, icon.Height));
        b.Draw(Game1.shadowTexture, ground, Game1.shadowTexture.Bounds, Color.Black * 0.45f, 0f,
            new Vector2(Game1.shadowTexture.Width / 2f, Game1.shadowTexture.Height / 2f), 2f, SpriteEffects.None, Math.Max(0f, Depth(y, sorted) - 0.0001f));
        b.Draw(icon, position, null, Color.White, 0f, new Vector2(icon.Width / 2, icon.Height / 2), scale, SpriteEffects.None, Depth(y, sorted));
        if (drop.Stack > 1)
            b.DrawString(Game1.tinyFont, drop.Stack.ToString(), position + new Vector2(10f, 4f), Color.White, 0f, Vector2.Zero, 1f, SpriteEffects.None, Math.Min(1f, Depth(y, sorted) + 0.0001f));
    }

    /// <summary>The Terraria item in the farmer's hand while it is being used, swung or aimed toward the cursor.</summary>
    /// <summary>A villager fighting with a blade swings it in front of them, sweeping from overhead down to the ground.</summary>
    private void DrawSwings(SpriteBatch b, string placeKey, bool sorted)
    {
        VillagerMoves? moves = bridge.Moves;
        if (moves == null)
            return;
        foreach (VillagerMove move in moves.Moves)
        {
            if (move.SwingItem <= 0 || move.PlaceKey != placeKey || Bravery.VillagerFor(move.NpcType) is not string name)
                continue;
            NPC? villager = Game1.getCharacterFromName(name);
            Texture2D? icon = villager == null ? null : sprites.Load(SpriteNames.Item(move.SwingItem));
            if (villager == null || icon == null || villager.currentLocation != Game1.player.currentLocation)
                continue;
            Rectangle box = villager.GetBoundingBox();
            var shoulder = new Vector2(box.Center.X, box.Bottom - 72f);
            var position = Game1.GlobalToLocal(Game1.viewport, shoulder);
            float depth = Depth(box.Bottom, sorted, 2f);
            float sweep = -1.75f + 2.8f * move.Swing;
            const float scale = 3f;
            if (move.Facing == 1)
                b.Draw(icon, position, null, Color.White, sweep + MathF.PI / 4f, new Vector2(0f, icon.Height), scale, SpriteEffects.None, depth);
            else
                b.Draw(icon, position, null, Color.White, -sweep - MathF.PI / 4f, new Vector2(icon.Width, icon.Height), scale, SpriteEffects.FlipHorizontally, depth);
        }
    }

    private void DrawHeld(SpriteBatch b, WorldState world, bool sorted)
    {
        HeldState held = world.Held;
        if (!hand.Active || held.ItemType <= 0 || held.Animation <= 0)
            return;
        Texture2D? icon = sprites.Load(SpriteNames.Item(held.ItemType));
        if (icon == null)
            return;
        Farmer player = Game1.player;
        Rectangle box = player.GetBoundingBox();
        var shoulder = new Vector2(box.Center.X, box.Bottom - 72f);
        Vector2 cursor = helper.Input.GetCursorPosition().AbsolutePixels;
        Vector2 toward = cursor - shoulder;
        float angle = toward.LengthSquared() < 1f ? 0f : (float)Math.Atan2(toward.Y, toward.X);
        float depth = Depth(box.Bottom, sorted, 2f);
        var position = Game1.GlobalToLocal(Game1.viewport, shoulder);
        const float scale = 3f;
        if (held.UseStyle == 1)
        {
            float rotation = MeleeArc.BladeRotation(angle, held.Animation, held.AnimationMax) + MathF.PI / 4f;
            b.Draw(icon, position, null, Color.White, rotation, new Vector2(0f, icon.Height), scale, SpriteEffects.None, depth);
        }
        else
        {
            var effects = Math.Cos(angle) < 0 ? SpriteEffects.FlipVertically : SpriteEffects.None;
            b.Draw(icon, position + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 24f, null, Color.White, angle, new Vector2(icon.Width / 2f, icon.Height / 2f), scale, effects, depth);
        }
    }

    /// <summary>Terraria armor over the farmer: side and back views from the armor sheet, the item icon facing the camera.</summary>
    private void DrawGear(SpriteBatch b, bool sorted)
    {
        InventoryState? gear = bridge.Inventory;
        if (gear == null || gear.Equipment.Count < 13)
            return;
        Farmer farmer = Game1.player;
        Rectangle box = farmer.GetBoundingBox();
        var feet = Game1.GlobalToLocal(Game1.viewport, new Vector2(box.Center.X, box.Bottom));
        float depth = Depth(box.Bottom, sorted, 1f);
        int face = farmer.FacingDirection;
        bool away = face == 0;
        bool front = face == 2;
        var flip = face == 3 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        int Shown(int armor, int vanity) => !gear.Equipment[vanity].IsEmpty ? gear.Equipment[vanity].ItemType : gear.Equipment[armor].IsEmpty ? 0 : gear.Equipment[armor].ItemType;
        // Legs, body and head share one layer; the small offsets order them within it.
        DrawPiece(b, feet, "legs", gear.LegSlot, Shown(2, 12), 0, 0f, away, front, flip, Math.Min(1f, depth), gear.LegTint);
        DrawPiece(b, feet, "body", gear.BodySlot, Shown(1, 11), gear.Male ? 0 : 1, 40f, away, front, flip, Math.Min(1f, depth + 0.0001f), gear.BodyTint);
        DrawPiece(b, feet, "head", gear.HeadSlot, Shown(0, 10), 0, 72f, away, front, flip, Math.Min(1f, depth + 0.0002f), gear.HeadTint);
        // Accessories: wings and back behind the body, shoes just over the legs, shield and front over the head.
        DrawAccessory(b, feet, "wings", gear.WingsSlot, away, flip, Math.Clamp(depth - 0.0001f, 0f, 1f), gear.WingsTint);
        DrawAccessory(b, feet, "back", gear.BackSlot, away, flip, Math.Clamp(depth - 0.0001f, 0f, 1f), gear.BackTint);
        DrawAccessory(b, feet, "shoe", gear.ShoeSlot, away, flip, Math.Clamp(depth + 0.00005f, 0f, 1f), gear.ShoeTint);
        DrawAccessory(b, feet, "shield", gear.ShieldSlot, away, flip, Math.Clamp(depth + 0.0003f, 0f, 1f), gear.ShieldTint);
        DrawAccessory(b, feet, "front", gear.FrontSlot, away, flip, Math.Clamp(depth + 0.0003f, 0f, 1f), gear.FrontTint);
    }

    /// <summary>The packed 0xRRGGBBAA dye tint from the Terraria side, as a multiply colour. 0xFFFFFFFF is plain white.</summary>
    private static Color TintOf(uint packed) => new Color((int)(packed >> 24) & 0xFF, (int)(packed >> 16) & 0xFF, (int)(packed >> 8) & 0xFF, (int)packed & 0xFF);

    /// <summary>A worn Terraria accessory drawn at the feet like armor, with no item icon fallback. A missing sprite file is skipped.</summary>
    private void DrawAccessory(SpriteBatch b, Vector2 feet, string part, int slot, bool away, SpriteEffects flip, float depth, uint tint)
    {
        if (slot <= 0)
            return;
        string file = SpriteNames.Accessory(part, slot);
        Texture2D? texture = away ? sprites.BackView(file) : sprites.Load(file);
        if (texture == null)
            return;
        b.Draw(texture, feet, null, TintOf(tint), 0f, new Vector2(texture.Width / 2f, texture.Height), 2f, flip, depth);
    }

    private void DrawPiece(SpriteBatch b, Vector2 feet, string part, int slot, int item, int variant, float lift, bool away, bool front, SpriteEffects flip, float depth, uint tint)
    {
        if (slot <= 0 && item <= 0)
            return;
        Texture2D? texture = null;
        var effect = flip;
        bool icon = false;
        if (front && item > 0)
        {
            texture = sprites.Load(SpriteNames.Item(item));
            effect = SpriteEffects.None;
            icon = true;
        }
        else if (slot > 0)
        {
            string file = SpriteNames.Armor(part, slot, variant);
            texture = away ? sprites.BackView(file) : sprites.Load(file);
        }
        if (texture == null && item > 0)
        {
            texture = sprites.Load(SpriteNames.Item(item));
            effect = SpriteEffects.None;
            icon = true;
        }
        if (texture == null)
            return;
        float y = front ? lift : 0f;
        // Item icons are the item itself, so only the armor and accessory sprites take the dye.
        b.Draw(texture, feet - new Vector2(0f, y), null, icon ? Color.White : TintOf(tint), 0f, new Vector2(texture.Width / 2f, texture.Height), 2f, effect, depth);
    }

    private static Rectangle Clamp(Rectangle source, Texture2D sheet)
    {
        int x = Math.Clamp(source.X, 0, Math.Max(0, sheet.Width - 1));
        int y = Math.Clamp(source.Y, 0, Math.Max(0, sheet.Height - 1));
        int width = Math.Clamp(source.Width, 1, sheet.Width - x);
        int height = Math.Clamp(source.Height, 1, sheet.Height - y);
        return new Rectangle(x, y, width, height);
    }
}
