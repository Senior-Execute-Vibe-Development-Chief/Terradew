using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Tools;
using Terradew.Core;

namespace Terradew;

public sealed class ModEntry : Mod
{
    private readonly Queue<BridgeHit> pendingHits = new();
    private readonly HashSet<int> hitThisSwing = new();
    private readonly SpriteBook sprites = new();
    private BridgeFile? bridge;
    private bool wasSwinging;
    private int nextHitSerial = 2;
    private string lastObstacleLocation = "";

    public override void Entry(IModHelper helper)
    {
        bridge = BridgeFile.Open();
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.Display.RenderedWorld += OnRenderedWorld;
        helper.Events.Display.RenderedHud += OnRenderedHud;
        helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        helper.ConsoleCommands.Add("terradew", "terradew status — show the Terraria bridge.", OnCommand);
        this.Monitor.Log($"Terradew bridge open at {BridgeFile.DefaultPath}", LogLevel.Info);
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (bridge == null || !Context.IsWorldReady)
            return;
        var player = Game1.player;
        string location = PlaceName(player);
        Rectangle feetBox = player.GetBoundingBox();
        bridge.WriteStardew(new StardewPulse(feetBox.Center.X, feetBox.Bottom, StableHash(location)));
        if (e.IsMultipleOf(20) || location != lastObstacleLocation)
        {
            lastObstacleLocation = location;
            WriteObstacles(player);
        }
        BridgeSnapshot snapshot = bridge.Read();
        if (snapshot.TerrariaLinked(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) && snapshot.LocationHash == StableHash(location))
            MobRoutes.Publish(player, snapshot, location, StableHash(location));
        CollectHits(player);
        TouchMarkers(player);
        while (pendingHits.Count > 0)
            bridge.WriteHit(pendingHits.Dequeue());
    }

    private static void WriteObstacles(Farmer player)
    {
        GameLocation? location = player.currentLocation;
        if (location?.Map == null || location.Map.Layers.Count == 0)
            return;
        int width = location.Map.Layers[0].LayerWidth;
        int height = location.Map.Layers[0].LayerHeight;
        if (width <= 0 || height <= 0)
            return;
        var mask = CollisionMask.Buildings | CollisionMask.Furniture | CollisionMask.Objects | CollisionMask.TerrainFeatures;
        bool[] blocked = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool solid = location.isWaterTile(x, y)
                    || location.IsTileBlockedBy(new Vector2(x, y), mask, CollisionMask.None, false);
                blocked[y * width + x] = solid;
            }
        }
        CollisionMap.Publish(StableHash(PlaceName(player)), width, height, blocked);
    }

    private void CollectHits(Farmer player)
    {
        bool swinging = player.UsingTool && player.CurrentTool is MeleeWeapon;
        if (!swinging)
        {
            wasSwinging = false;
            return;
        }
        if (!wasSwinging)
            hitThisSwing.Clear();
        wasSwinging = true;
        if (bridge == null || player.CurrentTool is not MeleeWeapon weapon)
            return;
        BridgeSnapshot snapshot = bridge.Read();
        if (!snapshot.TerrariaLinked(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
            return;
        Rectangle swipe = weapon.mostRecentArea;
        if (swipe.Width <= 0 || swipe.Height <= 0)
            return;
        int damage = Math.Max(1, (weapon.minDamage.Value + weapon.maxDamage.Value) / 2);
        foreach (BridgeEntity entity in snapshot.Entities)
        {
            if (entity.Flags.HasFlag(BridgeFlags.Lurk) || entity.Flags.HasFlag(BridgeFlags.Town))
                continue;
            if (!hitThisSwing.Add(entity.NpcId))
                continue;
            (float x, float y) = Place(snapshot, player, entity);
            Rectangle body = BodyBox(x, y, entity);
            if (!swipe.Intersects(body))
            {
                hitThisSwing.Remove(entity.NpcId);
                continue;
            }
            int direction = entity.X >= snapshot.TerrariaPlayerX ? 1 : -1;
            pendingHits.Enqueue(new BridgeHit(nextHitSerial, entity.NpcId, damage, direction));
            nextHitSerial += 2;
        }
    }

    private void TouchMarkers(Farmer player)
    {
        if (bridge == null || player.temporarilyInvincible)
            return;
        BridgeSnapshot snapshot = bridge.Read();
        if (!snapshot.TerrariaLinked(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
            return;
        Rectangle farmerBox = player.GetBoundingBox();
        foreach (BridgeEntity entity in snapshot.Entities)
        {
            if (entity.ContactDamage <= 0 || entity.Flags.HasFlag(BridgeFlags.Lurk) || entity.Flags.HasFlag(BridgeFlags.Town))
                continue;
            (float x, float y) = Place(snapshot, player, entity);
            Rectangle body = BodyBox(x, y, entity);
            if (!farmerBox.Intersects(body))
                continue;
            player.takeDamage(entity.ContactDamage, true, null);
            break;
        }
    }

    private static Rectangle BodyBox(float groundX, float groundY, BridgeEntity entity)
    {
        SpriteBox box = SpriteLayout.Bounds(groundX, groundY, entity);
        return new Rectangle((int)box.X, (int)box.Y, Math.Max(1, (int)box.Width), Math.Max(1, (int)box.Height));
    }

    private static (float X, float Y) Place(BridgeSnapshot snapshot, Farmer player, BridgeEntity entity)
    {
        if (snapshot.RegionX != 0 || snapshot.RegionY != 0)
            return WorldProjection.ToMap(snapshot.RegionX, snapshot.RegionY, entity.X, entity.Y);
        (float anchorX, float anchorY) = Anchor(snapshot, player);
        Rectangle feet = player.GetBoundingBox();
        return WorldProjection.ToStardew(
            feet.Center.X,
            feet.Bottom,
            anchorX,
            anchorY,
            entity.X,
            entity.Y);
    }

    private static (float X, float Y) Anchor(BridgeSnapshot snapshot, Farmer player)
    {
        bool samePlace = snapshot.LocationHash == StableHash(PlaceName(player));
        Rectangle feet = player.GetBoundingBox();
        return WorldProjection.FollowAnchor(
            feet.Center.X,
            feet.Bottom,
            snapshot.StardewX,
            snapshot.StardewY,
            snapshot.TerrariaPlayerX,
            snapshot.TerrariaPlayerY,
            samePlace);
    }

    private static string PlaceName(Farmer player)
        => player.currentLocation?.NameOrUniqueName ?? "";

    private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        if (bridge == null || !Context.IsWorldReady)
            return;
        BridgeSnapshot snapshot = bridge.Read();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string line = snapshot.TerrariaLinked(now)
            ? $"Terraria linked · {snapshot.Entities.Count} on this map"
            : "Terraria silent";
        DrawLine(e.SpriteBatch, line, 16);
    }

    private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
    {
        if (bridge == null || !Context.IsWorldReady)
            return;
        BridgeSnapshot snapshot = bridge.Read();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!snapshot.TerrariaLinked(now))
            return;

        foreach (BridgeEntity entity in snapshot.Entities)
        {
            (float x, float y) = Place(snapshot, Game1.player, entity);
            var ground = new Vector2(x, y);
            if (!Utility.isOnScreen(ground, 128))
                continue;
            DrawCreature(e.SpriteBatch, entity, ground);
        }
        DrawGear(e.SpriteBatch, snapshot.Equipment);
    }

    private void DrawCreature(SpriteBatch batch, BridgeEntity entity, Vector2 ground)
    {
        bool worm = entity.Flags.HasFlag(BridgeFlags.Worm);
        bool glider = entity.Flags.HasFlag(BridgeFlags.Glider);
        var body = ground - new Vector2(0f, glider ? SpriteLayout.GliderLift : 0f);
        var localGround = Game1.GlobalToLocal(Game1.viewport, ground);
        var localBody = Game1.GlobalToLocal(Game1.viewport, body);
        int size = entity.Flags.HasFlag(BridgeFlags.Boss) ? 28 : 16;
        Color color = entity.Flags.HasFlag(BridgeFlags.Boss) ? new Color(180, 40, 40)
            : entity.Flags.HasFlag(BridgeFlags.Town) ? new Color(90, 170, 90)
            : worm ? new Color(120, 80, 40)
            : glider ? new Color(120, 170, 210)
            : new Color(220, 190, 120);
        if (worm)
            color *= 0.75f;
        if (entity.Flags.HasFlag(BridgeFlags.Lurk))
            color *= 0.45f;

        batch.Draw(Game1.shadowTexture, localGround + new Vector2(0f, 8f), Game1.shadowTexture.Bounds, Color.Black * 0.35f, 0f, new Vector2(Game1.shadowTexture.Width / 2f, Game1.shadowTexture.Height / 2f), worm ? 1.2f : 2f, SpriteEffects.None, 0f);
        string file = SpriteNames.Npc(entity.NpcType, entity.FrameX, entity.FrameY, entity.FrameW, entity.FrameH);
        Texture2D? texture = entity.FrameW > 0 && entity.FrameH > 0 ? sprites.Load(file) : null;
        if (texture == null)
        {
            batch.Draw(Game1.staminaRect, new Rectangle((int)localBody.X - size / 2, (int)localBody.Y - size / 2, size, size), color);
        }
        else
        {
            float alpha = entity.Flags.HasFlag(BridgeFlags.Lurk) ? 0.45f : 1f;
            var effect = entity.Direction < 0 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            var origin = new Vector2(texture.Width / 2f, texture.Height);
            batch.Draw(texture, localBody, null, Color.White * alpha, 0f, origin, SpriteLayout.DrawScale, effect, 0f);
        }
        batch.DrawString(Game1.smallFont, entity.Name, localBody + new Vector2(size / 2 + 4, -8f), Color.White);
    }

    private void DrawGear(SpriteBatch batch, BridgeEquipment gear)
    {
        Farmer farmer = Game1.player;
        Rectangle box = farmer.GetBoundingBox();
        var feet = Game1.GlobalToLocal(Game1.viewport, new Vector2(box.Center.X, box.Bottom));
        int face = farmer.FacingDirection;
        bool away = face == 0;
        bool front = face == 2;
        var flip = face == 3 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        const float scale = 2f;
        DrawPiece(batch, feet, "legs", gear.LegSlot, gear.LegItem, 0, 0f, away, front, flip, scale);
        DrawPiece(batch, feet, "body", gear.BodySlot, gear.BodyItem, gear.BodyVariant, 40f, away, front, flip, scale);
        DrawPiece(batch, feet, "head", gear.HeadSlot, gear.HeadItem, 0, 72f, away, front, flip, scale);
        if (gear.HeldItem <= 0)
            return;
        Texture2D? weapon = sprites.Load(SpriteNames.Item(gear.HeldItem));
        if (weapon == null)
            return;
        float side = face == 3 ? -36f : face == 1 ? 36f : 0f;
        float lift = face == 0 ? 96f : 52f;
        var effect = face == 3 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        batch.Draw(
            weapon,
            feet + new Vector2(side, -lift),
            null,
            Color.White,
            0f,
            new Vector2(weapon.Width / 2f, weapon.Height / 2f),
            scale,
            effect,
            0f);
    }

    private void DrawPiece(SpriteBatch batch, Vector2 feet, string part, int slot, int item, int variant, float lift, bool away, bool front, SpriteEffects flip, float scale)
    {
        if (slot <= 0 && item <= 0)
            return;
        Texture2D? texture = null;
        var effect = flip;
        if (front && item > 0)
        {
            texture = sprites.Load(SpriteNames.Item(item));
            effect = SpriteEffects.None;
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
        }
        if (texture == null)
            return;
        float y = front ? lift : 0f;
        batch.Draw(texture, feet - new Vector2(0f, y), null, Color.White, 0f, new Vector2(texture.Width / 2f, texture.Height), scale, effect, 0f);
    }

    private void OnCommand(string command, string[] args)
    {
        if (bridge == null)
            return;
        BridgeSnapshot snapshot = bridge.Read();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        this.Monitor.Log(
            snapshot.TerrariaLinked(now)
                ? $"Linked. {snapshot.Entities.Count} NPCs. Player at {snapshot.TerrariaPlayerX:0},{snapshot.TerrariaPlayerY:0}."
                : "Terraria is not writing to the bridge.",
            LogLevel.Info);
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        bridge?.Dispose();
        bridge = BridgeFile.Open();
    }

    private static void DrawLine(SpriteBatch batch, string text, int y)
    {
        batch.DrawString(Game1.smallFont, text, new Vector2(24, y), Color.White, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
    }

    private static int StableHash(string text)
    {
        int hash = 23;
        foreach (char c in text)
            hash = unchecked(hash * 31 + c);
        return hash;
    }
}
