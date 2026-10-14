using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ModLoader;
using Terradew.Core;

namespace TerradewBridge;

public sealed class TerradewMod : Mod
{
    public override void Load()
    {
        Logger.Info($"Terradew bridge will write {BridgeFile.DefaultPath}");
    }
}

public sealed class BridgeSystem : ModSystem
{
    private struct PlaneSlot
    {
        public bool Ready;
        public int Type;
        public float WanderX;
        public float WanderY;
        public bool Glider;
        public int Pace;
    }

    private struct ObstacleRegion
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
    }

    private readonly List<BridgeEntity> tracked = new();
    private PlaneSlot[] plane = new PlaneSlot[256];
    private BridgeFile? bridge;
    private int lastHitSerial;
    private bool autoPauseWasOn;
    private bool frameSkipSaved;
    private FrameSkipMode savedFrameSkip;
    private TimeSpan savedSleep;
    private int collisionSequence = -1;
    private readonly Dictionary<int, ObstacleRegion> regions = new();
    private int cursorX = 40;
    private int cursorY = 40;
    private int rowHeight;
    private readonly Dictionary<int, RouteMap.Step> routeSteps = new();
    private int routeSequence = -1;
    private int routeTick = -1;
    private int routeHash;
    private int worldTicks;
    private int ticksSeenByUi = -1;

    public override void OnWorldLoad()
    {
        bridge ??= BridgeFile.Open();
        lastHitSerial = 0;
        foreach (BridgeHit hit in bridge.ReadHits())
            lastHitSerial = Math.Max(lastHitSerial, hit.Serial);
        Array.Clear(plane);
        collisionSequence = -1;
        regions.Clear();
        cursorX = 40;
        cursorY = 40;
        rowHeight = 0;
        routeTick = -1;
        routeSequence = -1;
        routeSteps.Clear();
        worldTicks = 0;
        BlankWorld();
        ticksSeenByUi = -1;
        autoPauseWasOn = Main.autoPause;
        Main.autoPause = false;
        savedFrameSkip = Main.FrameSkipMode;
        frameSkipSaved = true;
        Main.FrameSkipMode = FrameSkipMode.Off;
        if (Main.instance != null)
        {
            savedSleep = Main.instance.InactiveSleepTime;
            Main.instance.InactiveSleepTime = TimeSpan.Zero;
        }
    }

    public override void OnWorldUnload()
    {
        if (autoPauseWasOn)
            Main.autoPause = true;
        autoPauseWasOn = false;
        if (frameSkipSaved)
            Main.FrameSkipMode = savedFrameSkip;
        frameSkipSaved = false;
        if (Main.instance != null)
            Main.instance.InactiveSleepTime = savedSleep;
        bridge?.Dispose();
        bridge = null;
    }

    public override void UpdateUI(GameTime gameTime)
    {
        if (bridge == null || Main.gameMenu || Main.instance == null)
            return;
        Main.autoPause = false;
        Main.FrameSkipMode = FrameSkipMode.Off;
        Main.gamePaused = false;
        Main.instance.InactiveSleepTime = TimeSpan.Zero;
        bool worldSkipped = !Main.instance.IsActive && worldTicks == ticksSeenByUi && ticksSeenByUi >= 0;
        if (worldSkipped)
            StepWhileUnfocused();
        else if (Main.gamePaused)
            bridge.TouchTerraria();
        ticksSeenByUi = worldTicks;
    }

    public override void PostUpdateNPCs()
    {
        worldTicks++;
        Publish();
    }

    private void StepWhileUnfocused()
    {
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            if (!Main.npc[i].active)
                continue;
            try
            {
                Main.npc[i].UpdateNPC(i);
            }
            catch (Exception)
            {
                // One bad creature should not stop the rest of the background step.
            }
        }
        Publish();
    }

    private void Publish()
    {
        if (bridge == null)
            return;

        ApplyHits();
        Player player = Main.LocalPlayer;
        BridgeSnapshot seen = bridge.Read();
        ApplyObstacles(player, seen);
        FollowFarmer(seen);
        float originX = player.position.X + player.width / 2f;
        float originY = player.position.Y + player.height;
        float radius = WorldProjection.TrackRadiusPixels;
        tracked.Clear();
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (!npc.active || npc.life <= 0)
            {
                if (i < plane.Length)
                    plane[i].Ready = false;
                continue;
            }
            var flags = BridgeFlags.None;
            if (npc.townNPC)
                flags |= BridgeFlags.Town;
            if (npc.boss)
                flags |= BridgeFlags.Boss;
            if (npc.aiStyle == NPCAIStyleID.Worm)
            {
                flags |= BridgeFlags.Worm;
                if (npc.behindTiles)
                    flags |= BridgeFlags.Lurk;
            }
            if (Slot(npc.whoAmI).Glider)
                flags |= BridgeFlags.Glider;
            float footX = npc.position.X + npc.width / 2f;
            float footY = npc.position.Y + npc.height;
            float dx = footX - originX;
            float dy = footY - originY;
            if (dx * dx + dy * dy > radius * radius)
                continue;
            SpriteDump.Npc(npc);
            int direction = npc.spriteDirection == 0 ? 1 : npc.spriteDirection;
            tracked.Add(new BridgeEntity(
                npc.whoAmI,
                npc.type,
                footX,
                footY,
                npc.life,
                npc.lifeMax,
                flags,
                npc.FullName,
                npc.townNPC ? 0 : npc.damage,
                (short)npc.frame.X,
                (short)npc.frame.Y,
                (short)npc.frame.Width,
                (short)npc.frame.Height,
                direction));
        }

        tracked.Sort((a, b) =>
        {
            int rank = Rank(b).CompareTo(Rank(a));
            if (rank != 0)
                return rank;
            return DistanceSquared(a, originX, originY).CompareTo(DistanceSquared(b, originX, originY));
        });
        BridgeEquipment equipment = Gear(player);
        ExportGear(equipment);
        int regionX = 0;
        int regionY = 0;
        if (regions.TryGetValue(seen.LocationHash, out ObstacleRegion region))
        {
            regionX = region.X;
            regionY = region.Y;
        }
        bridge.WriteTerraria(originX, originY, tracked, equipment, regionX, regionY);
    }

    private void BlankWorld()
    {
        int maxX = Main.maxTilesX;
        int maxY = Main.maxTilesY;
        for (int x = 0; x < maxX; x++)
        {
            for (int y = 0; y < maxY; y++)
                Main.tile[x, y].ClearEverything();
        }
        Mod.Logger.Info($"Terradew cleared the world ({maxX} by {maxY}).");
    }

    public void Steer(NPC npc)
    {
        if (!npc.active || npc.life <= 0)
            return;
        ref PlaneSlot slot = ref Slot(npc.whoAmI);
        bool worm = npc.aiStyle == NPCAIStyleID.Worm;
        if (!slot.Ready || slot.Type != npc.type)
        {
            slot.Ready = true;
            slot.Type = npc.type;
            slot.Glider = npc.noGravity && !worm;
            slot.WanderX = npc.position.X;
            slot.WanderY = npc.position.Y;
            slot.Pace = 40;
        }
        bool keepsOwnFlight = PlaneMotion.UsesRealAltitude(slot.Glider, worm);
        npc.noGravity = true;
        npc.noTileCollide = true;
        if (keepsOwnFlight)
            return;
        Player player = Main.LocalPlayer;
        bool chasing = !npc.townNPC && !npc.friendly && npc.damage > 0 && npc.target == player.whoAmI;
        EnsureRoutes();
        float fromX = npc.position.X;
        float fromY = npc.position.Y;
        float goalX = chasing ? player.position.X : slot.WanderX;
        float goalY = chasing ? player.position.Y : slot.WanderY;
        float arrive = chasing ? 20f : 8f;
        if (chasing && TryRoute(npc.whoAmI, out float routeX, out float routeY))
        {
            fromX = npc.position.X + npc.width / 2f;
            fromY = npc.position.Y + npc.height;
            goalX = routeX;
            goalY = routeY;
            arrive = 3f;
        }
        bool there = MathF.Abs(fromX - goalX) <= arrive && MathF.Abs(fromY - goalY) <= arrive;
        if (!chasing && there)
        {
            npc.velocity.X = 0f;
            npc.velocity.Y = 0f;
            if (slot.Pace > 0)
                slot.Pace--;
            else
                PickWander(npc, ref slot);
            return;
        }
        float speed = npc.townNPC ? 0.85f : npc.aiStyle == NPCAIStyleID.Slime ? 1f : 1.5f;
        PlaneMotion.PlaneStep step = PlaneMotion.Aim(fromX, fromY, goalX, goalY, speed, arrive);
        PlaneMotion.FeetBox feet = PlaneMotion.Feet(npc.position.X, npc.position.Y, npc.width, npc.height);
        Vector2 allowed = Collision.TileCollision(new Vector2(feet.X, feet.Y), new Vector2(step.X, step.Y), feet.Width, feet.Height);
        npc.velocity.X = allowed.X;
        npc.velocity.Y = allowed.Y;
        if (MathF.Abs(step.X) > 0.05f)
        {
            npc.direction = step.X < 0f ? -1 : 1;
            npc.spriteDirection = npc.direction;
        }
    }

    private void EnsureRoutes()
    {
        if (bridge == null || routeTick == worldTicks)
            return;
        routeTick = worldTicks;
        BridgeSnapshot seen = bridge.Read();
        if (!RouteMap.TryRead(out RouteMap.Page page) || page.LocationHash != seen.LocationHash)
        {
            routeSteps.Clear();
            routeHash = 0;
            return;
        }
        if (page.Sequence == routeSequence)
            return;
        routeSequence = page.Sequence;
        routeHash = page.LocationHash;
        routeSteps.Clear();
        foreach (RouteMap.Step step in page.Steps)
            routeSteps[step.NpcId] = step;
    }

    private bool TryRoute(int npcId, out float worldX, out float worldY)
    {
        worldX = 0f;
        worldY = 0f;
        if (!routeSteps.TryGetValue(npcId, out RouteMap.Step step))
            return false;
        if (!regions.TryGetValue(routeHash, out ObstacleRegion region))
            return false;
        worldX = (region.X + step.TileX) * WorldProjection.TerrariaTilePixels + 8f;
        worldY = (region.Y + step.TileY) * WorldProjection.TerrariaTilePixels + 8f;
        return true;
    }

    private void PickWander(NPC npc, ref PlaneSlot slot)
    {
        float east = PlaneMotion.Spread(npc.whoAmI + worldTicks);
        float south = PlaneMotion.Spread(npc.whoAmI * 3 + worldTicks);
        if (MathF.Abs(east) < 48f)
            east = east < 0f ? -48f : 48f;
        if (MathF.Abs(south) < 48f)
            south = south < 0f ? -48f : 48f;
        slot.WanderX = npc.position.X + east;
        slot.WanderY = npc.position.Y + south;
        slot.Pace = 75;
    }

    private void ApplyObstacles(Player player, BridgeSnapshot seen)
    {
        if (!CollisionMap.TryRead(out CollisionMap.Grid grid))
            return;
        if (grid.Sequence == collisionSequence)
            return;
        collisionSequence = grid.Sequence;
        ObstacleRegion region = EnsureRegion(player, seen, grid);
        Stamp(region, grid);
    }

    private ObstacleRegion EnsureRegion(Player player, BridgeSnapshot seen, CollisionMap.Grid grid)
    {
        if (regions.TryGetValue(grid.LocationHash, out ObstacleRegion existing))
        {
            if (existing.Width != grid.Width || existing.Height != grid.Height)
                ClearRegion(existing);
            existing.Width = grid.Width;
            existing.Height = grid.Height;
            regions[grid.LocationHash] = existing;
            return existing;
        }
        ObstacleRegion region = regions.Count == 0
            ? AnchorFirst(player, seen, grid)
            : Pack(grid.Width, grid.Height);
        regions[grid.LocationHash] = region;
        Mod.Logger.Info($"Terradew placed location {grid.LocationHash} at {region.X},{region.Y} ({region.Width} by {region.Height}).");
        return region;
    }

    private ObstacleRegion AnchorFirst(Player player, BridgeSnapshot seen, CollisionMap.Grid grid)
    {
        int tile = (int)WorldProjection.TerrariaTilePixels;
        int farmerTileX = (int)Math.Floor(seen.StardewX / (tile * WorldProjection.Scale));
        int farmerTileY = (int)Math.Floor(seen.StardewY / (tile * WorldProjection.Scale));
        int originX = (int)Math.Floor(player.position.X / tile) - farmerTileX;
        int originY = (int)Math.Floor(player.position.Y / tile) - farmerTileY;
        (originX, originY) = Fit(originX, originY, grid.Width, grid.Height);
        cursorX = originX + grid.Width + 40;
        cursorY = originY;
        rowHeight = grid.Height;
        if (cursorX >= LimitX())
        {
            cursorX = 40;
            cursorY = originY + grid.Height + 40;
            rowHeight = 0;
        }
        return new ObstacleRegion { X = originX, Y = originY, Width = grid.Width, Height = grid.Height };
    }

    private ObstacleRegion Pack(int width, int height)
    {
        for (int attempt = 0; attempt < 64; attempt++)
        {
            if (cursorX + width > LimitX())
            {
                cursorX = 40;
                cursorY += Math.Max(rowHeight, 1) + 40;
                rowHeight = 0;
            }
            (int x, int y) = Fit(cursorX, cursorY, width, height);
            cursorX = x + width + 40;
            rowHeight = Math.Max(rowHeight, height);
            if (!Overlaps(x, y, width, height))
                return new ObstacleRegion { X = x, Y = y, Width = width, Height = height };
        }
        (int fallbackX, int fallbackY) = Fit(40, 40, width, height);
        return new ObstacleRegion { X = fallbackX, Y = fallbackY, Width = width, Height = height };
    }

    private static int LimitX()
        => Math.Max(3, Main.maxTilesX - 2);

    private static int LimitY()
    {
        int underworld = Main.UnderworldLayer;
        int limit = underworld > 80 ? underworld - 2 : Main.maxTilesY - 2;
        return Math.Max(3, limit);
    }

    private static (int X, int Y) Fit(int x, int y, int width, int height)
    {
        int maxX = Math.Max(2, LimitX() - width);
        int maxY = Math.Max(2, LimitY() - height);
        return (Math.Clamp(x, 2, maxX), Math.Clamp(y, 2, maxY));
    }

    private bool Overlaps(int x, int y, int width, int height)
    {
        foreach (ObstacleRegion region in regions.Values)
        {
            bool apart = x + width + 8 <= region.X || region.X + region.Width + 8 <= x
                || y + height + 8 <= region.Y || region.Y + region.Height + 8 <= y;
            if (!apart)
                return true;
        }
        return false;
    }

    private static void ClearRegion(ObstacleRegion region)
    {
        for (int y = 0; y < region.Height; y++)
        {
            int tileY = region.Y + y;
            if (tileY < 0 || tileY >= Main.maxTilesY)
                continue;
            for (int x = 0; x < region.Width; x++)
            {
                int tileX = region.X + x;
                if (tileX < 0 || tileX >= Main.maxTilesX)
                    continue;
                Main.tile[tileX, tileY].ClearTile();
            }
        }
    }

    private static void Stamp(ObstacleRegion region, CollisionMap.Grid grid)
    {
        ClearRegion(region);
        for (int y = 0; y < grid.Height; y++)
        {
            int tileY = region.Y + y;
            if (tileY < 0 || tileY >= Main.maxTilesY)
                continue;
            for (int x = 0; x < grid.Width; x++)
            {
                if (!grid.Blocked[y * grid.Width + x])
                    continue;
                int tileX = region.X + x;
                if (tileX < 0 || tileX >= Main.maxTilesX)
                    continue;
                Tile tile = Main.tile[tileX, tileY];
                tile.HasTile = true;
                tile.TileType = TileID.Stone;
            }
        }
    }

    private void FollowFarmer(BridgeSnapshot seen)
    {
        if (bridge == null || Main.instance == null)
            return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!seen.StardewLinked(now))
            return;
        if (!regions.TryGetValue(seen.LocationHash, out ObstacleRegion region))
            return;
        Player player = Main.LocalPlayer;
        player.noKnockback = true;
        player.immune = true;
        player.immuneTime = Math.Max(player.immuneTime, 2);
        if (player.dead || player.statLife <= 0)
        {
            player.dead = false;
            player.statLife = Math.Max(1, player.statLifeMax2);
            player.respawnTimer = 0;
        }
        (float homeX, float homeY) = WorldProjection.Home(region.X, region.Y, seen.StardewX, seen.StardewY);
        (float bodyX, float bodyY) = WorldProjection.FromFeet(homeX, homeY, player.width, player.height);
        if (Main.instance.IsActive)
            return;
        float east = bodyX - player.position.X;
        player.position.X = bodyX;
        player.position.Y = bodyY;
        player.velocity = Vector2.Zero;
        player.fallStart = (int)(player.position.Y / 16f);
        if (east < -0.01f)
            player.direction = -1;
        else if (east > 0.01f)
            player.direction = 1;
    }

    private ref PlaneSlot Slot(int id)
    {
        if (id >= plane.Length)
            Array.Resize(ref plane, id + 1);
        return ref plane[id];
    }

    private static BridgeEquipment Gear(Player player)
    {
        if (player.armor == null || player.armor.Length < 13)
            return default;
        Item head = Visible(player, 10, 0);
        Item body = Visible(player, 11, 1);
        Item legs = Visible(player, 12, 2);
        return new BridgeEquipment(
            player.HeldItem?.type ?? 0,
            head?.type ?? 0,
            body?.type ?? 0,
            legs?.type ?? 0,
            player.head,
            player.body,
            player.legs,
            player.Male ? 0 : 1);
    }

    private static Item Visible(Player player, int vanityIndex, int armorIndex)
    {
        Item vanity = player.armor[vanityIndex];
        if (vanity != null && vanity.type > ItemID.None)
            return vanity;
        return player.armor[armorIndex];
    }

    private static void ExportGear(BridgeEquipment equipment)
    {
        SpriteDump.Item(equipment.HeldItem);
        SpriteDump.Item(equipment.HeadItem);
        SpriteDump.Item(equipment.BodyItem);
        SpriteDump.Item(equipment.LegItem);
        SpriteDump.Head(equipment.HeadSlot);
        SpriteDump.Body(equipment.BodySlot, Main.LocalPlayer.Male);
        SpriteDump.Legs(equipment.LegSlot);
    }

    private void ApplyHits()
    {
        if (bridge == null)
            return;
        foreach (BridgeHit hit in bridge.ReadHits())
        {
            if (hit.Serial <= lastHitSerial)
                continue;
            lastHitSerial = Math.Max(lastHitSerial, hit.Serial);
            if (hit.NpcId < 0 || hit.NpcId >= Main.maxNPCs || hit.Damage <= 0)
                continue;
            NPC npc = Main.npc[hit.NpcId];
            if (!npc.active || npc.townNPC || npc.dontTakeDamage)
                continue;
            if (npc.behindTiles && npc.aiStyle == NPCAIStyleID.Worm)
                continue;
            npc.SimpleStrikeNPC(hit.Damage, hit.Direction, knockBack: 4f);
        }
    }

    private static float DistanceSquared(BridgeEntity entity, float originX, float originY)
    {
        float dx = entity.X - originX;
        float dy = entity.Y - originY;
        return dx * dx + dy * dy;
    }

    private static int Rank(BridgeEntity entity)
    {
        if (entity.Flags.HasFlag(BridgeFlags.Boss))
            return 2;
        if (entity.Flags.HasFlag(BridgeFlags.Town))
            return 1;
        return 0;
    }
}

public sealed class PlanePlayer : ModPlayer
{
    private Vector2 positionAtStart;
    private int widthAtStart;
    private int heightAtStart;

    public override void PreUpdate()
    {
        positionAtStart = Player.position;
        widthAtStart = Player.width;
        heightAtStart = Player.height;
        PlaneMotion.FeetBox feet = PlaneMotion.Feet(Player.position.X, Player.position.Y, Player.width, Player.height);
        Player.position = new Vector2(feet.X, feet.Y);
        Player.width = feet.Width;
        Player.height = feet.Height;
        Player.gravity = 0f;
        Player.velocity = Vector2.Zero;
        Player.noKnockback = true;
        Player.immune = true;
        Player.immuneTime = Math.Max(Player.immuneTime, 2);
    }

    public override void PostUpdate()
    {
        Player.width = widthAtStart > 0 ? widthAtStart : Player.width;
        Player.height = heightAtStart > 0 ? heightAtStart : Player.height;
        Player.position = positionAtStart;
        Player.velocity = Vector2.Zero;
        Player.fallStart = (int)(Player.position.Y / 16f);
        if (Player.dead || Player.statLife <= 0)
        {
            Player.dead = false;
            Player.statLife = Math.Max(1, Player.statLifeMax2);
            Player.respawnTimer = 0;
        }
    }
}

public sealed class PlaneNpc : GlobalNPC
{
    public override bool InstancePerEntity => false;

    public override void PostAI(NPC npc)
    {
        ModContent.GetInstance<BridgeSystem>().Steer(npc);
    }
}

public sealed class SummonCommand : ModCommand
{
    public override string Command => "summon";
    public override CommandType Type => CommandType.Chat;
    public override string Usage => "/summon";
    public override string Description => "Place a green slime a few tiles to your side";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        Player player = caller.Player;
        int x = (int)player.Center.X + player.direction * 16 * 8;
        int y = (int)player.Center.Y;
        int who = NPC.NewNPC(new EntitySource_Misc("Terradew summon"), x, y, NPCID.GreenSlime, Target: player.whoAmI);
        string name = who >= 0 && who < Main.maxNPCs ? Main.npc[who].FullName : "nothing";
        Main.NewText($"Summoned {name} nearby.", 80, 200, 80);
    }
}
