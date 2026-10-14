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

    private readonly Bridge bridge;
    private readonly SpriteBook sprites;
    private readonly Hand hand;
    private readonly IModHelper helper;
    private bool drewSorted;

    public Draw(Bridge bridge, SpriteBook sprites, Hand hand, IModHelper helper)
    {
        this.bridge = bridge;
        this.sprites = sprites;
        this.hand = hand;
        this.helper = helper;
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
        catch (Exception)
        {
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
        WorldState? world = bridge.World;
        if (world == null || !bridge.WorldIsFor(key))
            return;

        foreach (EntityState entity in world.Entities)
        {
            (float x, float y) = WorldProjection.ToMap(world.RegionX, world.RegionY, entity.X, entity.Y);
            if (!Utility.isOnScreen(new Vector2(x, y), 64 * 6))
                continue;
            DrawEntity(b, entity, x, y, sorted);
        }
        foreach (DropState drop in world.Drops)
        {
            (float x, float y) = WorldProjection.ToMap(world.RegionX, world.RegionY, drop.X, drop.Y);
            if (Utility.isOnScreen(new Vector2(x, y), 64))
                DrawDrop(b, drop, x, y, sorted);
        }
        foreach (ProjectileState projectile in world.Projectiles)
        {
            (float x, float y) = WorldProjection.ToMap(world.RegionX, world.RegionY, projectile.X, projectile.Y);
            if (Utility.isOnScreen(new Vector2(x, y), 64 * 4))
                DrawProjectile(b, projectile, x, y, sorted);
        }
        DrawHeld(b, world, sorted);
        DrawGear(b, sorted);
    }

    private static float Depth(float stardewY, bool sorted, float nudge = 0f)
        => sorted ? Math.Clamp((stardewY + nudge) / 10000f, 0f, 1f) : 0f;

    private void DrawEntity(SpriteBatch b, EntityState entity, float x, float y, bool sorted)
    {
        float depth = Depth(y, sorted);
        float alpha = (255 - entity.Alpha) / 255f;
        if (entity.Has(EntityFlags.Buried))
            alpha *= 0.45f;
        if (alpha <= 0.02f)
            return;
        float scale = SpriteLayout.DrawScale * (entity.Scale <= 0f ? 1f : entity.Scale);
        float bodyHeight = Math.Max(4, (int)entity.Height) * SpriteLayout.DrawScale;
        bool airborne = entity.Has(EntityFlags.Flyer) || entity.Has(EntityFlags.Worm) || Math.Abs(entity.Rotation) > 0.01f;

        if (!entity.Has(EntityFlags.Flyer))
        {
            var shadow = Game1.GlobalToLocal(Game1.viewport, new Vector2(x, y));
            float shadowScale = Math.Clamp(entity.Width / 10f, 1.5f, 8f);
            b.Draw(Game1.shadowTexture, shadow, Game1.shadowTexture.Bounds, Color.Black * 0.35f * alpha, 0f,
                new Vector2(Game1.shadowTexture.Width / 2f, Game1.shadowTexture.Height / 2f), shadowScale, SpriteEffects.None, Math.Max(0f, depth - 0.0001f));
        }

        Texture2D? sheet = sprites.Load(SpriteNames.Npc(entity.NpcType));
        var effects = entity.Direction == 1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        if (sheet != null && entity.FrameW > 0 && entity.FrameH > 0)
        {
            var source = Clamp(new Rectangle(entity.FrameX, entity.FrameY, entity.FrameW, entity.FrameH), sheet);
            Vector2 position;
            Vector2 origin;
            if (airborne)
            {
                position = Game1.GlobalToLocal(Game1.viewport, new Vector2(x, y - bodyHeight / 2f));
                origin = new Vector2(source.Width / 2f, source.Height / 2f);
            }
            else
            {
                position = Game1.GlobalToLocal(Game1.viewport, new Vector2(x, y));
                origin = new Vector2(source.Width / 2f, source.Height);
            }
            b.Draw(sheet, position, source, Color.White * alpha, entity.Rotation, origin, scale, effects, depth);
        }
        else
        {
            int size = entity.Has(EntityFlags.Boss) ? 96 : 40;
            var local = Game1.GlobalToLocal(Game1.viewport, new Vector2(x, y));
            Color color = entity.Has(EntityFlags.Town) ? new Color(90, 170, 90) : entity.Has(EntityFlags.Boss) ? new Color(180, 40, 40) : new Color(220, 190, 120);
            b.Draw(Game1.staminaRect, new Rectangle((int)local.X - size / 2, (int)local.Y - size, size, size), null, color * alpha, 0f, Vector2.Zero, SpriteEffects.None, depth);
        }

        var head = Game1.GlobalToLocal(Game1.viewport, new Vector2(x, y - bodyHeight - 12f));
        if (entity.Has(EntityFlags.Hostile) && entity.LifeMax > 0 && entity.Life < entity.LifeMax && !entity.Has(EntityFlags.Boss))
        {
            int width = 48;
            float fill = Math.Clamp(entity.Life / (float)entity.LifeMax, 0f, 1f);
            b.Draw(Game1.staminaRect, new Rectangle((int)head.X - width / 2, (int)head.Y, width, 6), null, Color.Black * 0.6f, 0f, Vector2.Zero, SpriteEffects.None, Math.Min(1f, depth + 0.0002f));
            b.Draw(Game1.staminaRect, new Rectangle((int)head.X - width / 2 + 1, (int)head.Y + 1, (int)((width - 2) * fill), 4), null, Color.Lerp(Color.Red, Color.LimeGreen, fill), 0f, Vector2.Zero, SpriteEffects.None, Math.Min(1f, depth + 0.0003f));
        }
        if (entity.Has(EntityFlags.Town) || entity.NpcType == Hand.OldManType)
        {
            Vector2 size = Game1.smallFont.MeasureString(entity.Name);
            b.DrawString(Game1.smallFont, entity.Name, head - new Vector2(size.X / 2f, size.Y), Color.White, 0f, Vector2.Zero, 1f, SpriteEffects.None, Math.Min(1f, depth + 0.0003f));
        }
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
        float alpha = (255 - projectile.Alpha) / 255f;
        var effects = projectile.Direction == -1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        var position = Game1.GlobalToLocal(Game1.viewport, new Vector2(x, y));
        float scale = SpriteLayout.DrawScale * (projectile.Scale <= 0f ? 1f : projectile.Scale);
        b.Draw(sheet, position, source, Color.White * alpha, projectile.Rotation, new Vector2(source.Width / 2f, source.Height / 2f), scale, effects, Depth(y, sorted, 32f));
    }

    private void DrawDrop(SpriteBatch b, DropState drop, float x, float y, bool sorted)
    {
        Texture2D? icon = sprites.Load(SpriteNames.Item(drop.ItemType));
        if (icon == null)
            return;
        float bob = (float)Math.Sin((Game1.ticks + drop.Id * 13) / 12.0) * 4f;
        var position = Game1.GlobalToLocal(Game1.viewport, new Vector2(x, y + bob));
        float scale = 3f * Math.Min(1f, 32f / Math.Max(icon.Width, icon.Height));
        b.Draw(icon, position, null, Color.White, 0f, new Vector2(icon.Width / 2f, icon.Height / 2f), scale, SpriteEffects.None, Depth(y, sorted));
        if (drop.Stack > 1)
            b.DrawString(Game1.tinyFont, drop.Stack.ToString(), position + new Vector2(10f, 4f), Color.White, 0f, Vector2.Zero, 1f, SpriteEffects.None, Math.Min(1f, Depth(y, sorted) + 0.0001f));
    }

    /// <summary>The Terraria item in the farmer's hand while it is being used, swung or aimed toward the cursor.</summary>
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
        DrawPiece(b, feet, "legs", gear.LegSlot, Shown(2, 12), 0, 0f, away, front, flip, depth);
        DrawPiece(b, feet, "body", gear.BodySlot, Shown(1, 11), gear.Male ? 0 : 1, 40f, away, front, flip, depth);
        DrawPiece(b, feet, "head", gear.HeadSlot, Shown(0, 10), 0, 72f, away, front, flip, depth);
    }

    private void DrawPiece(SpriteBatch b, Vector2 feet, string part, int slot, int item, int variant, float lift, bool away, bool front, SpriteEffects flip, float depth)
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
        b.Draw(texture, feet - new Vector2(0f, y), null, Color.White, 0f, new Vector2(texture.Width / 2f, texture.Height), 2f, effect, depth);
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
