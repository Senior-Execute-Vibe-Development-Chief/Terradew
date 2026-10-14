using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terradew.Core;
using Terradew.Core.Protocol;
using Terradew.Core.World;
using Terraria;
using Terraria.ID;

namespace TerradewBridge;

/// <summary>
/// Gives every Stardew place a fixed rectangle in the Terraria world at the right depth, fills it with
/// that place's biome blocks wherever the farmer cannot walk, and reports what is inside it each tick.
/// </summary>
public sealed class WorldStamp
{
    /// <summary>Solid biome blocks around each map, so biome counts reach Terraria's thresholds and walkers stay in.</summary>
    public const int Pad = 12;

    public sealed class PlaceState
    {
        public string Key = "";
        public Region Rect;
        public MapLayout? Layout;
        public int StampedVersion = int.MinValue;
        public int StampedFloor = -1;
        public bool StampedThisVisit;

        public int OriginX => Rect.X + Pad;
        public int OriginY => Rect.Y + Pad;

        public bool ContainsTile(int tileX, int tileY) => Rect.Contains(tileX, tileY);

        public bool ContainsWorld(Vector2 world) => ContainsTile((int)(world.X / 16f), (int)(world.Y / 16f));

        /// <summary>Stardew map pixels to Terraria world pixels.</summary>
        public Vector2 ToWorld(float stardewX, float stardewY)
        {
            (float x, float y) = WorldProjection.ToWorld(OriginX, OriginY, stardewX, stardewY);
            return new Vector2(x, y);
        }

        public bool IsOpen(int tileX, int tileY)
            => Layout != null && !Layout.IsBlocked(tileX - OriginX, tileY - OriginY);
    }

    private readonly Dictionary<string, PlaceState> places = new();
    private readonly Dictionary<string, Dictionary<int, RouteEntry>> routes = new();
    private readonly HashSet<string> unplaced = new();
    private WorldLayout? layout;
    private string lastPlace = "";

    public PlaceState? Current { get; private set; }

    /// <summary>Ticks since the puppet last changed place. The puppet is immune for a moment after a snap.</summary>
    public int TicksInPlace { get; private set; }

    public void Clear()
    {
        places.Clear();
        routes.Clear();
        unplaced.Clear();
        layout = null;
        Current = null;
        lastPlace = "";
    }

    public void Load(string saved, WorldShape shape)
    {
        Clear();
        List<Region> regions = WorldLayout.Deserialize(saved ?? "");
        layout = new WorldLayout(shape, regions);
        foreach (Region region in regions)
            places[region.Key] = new PlaceState { Key = region.Key, Rect = region };
    }

    public string SaveLayout() => layout == null ? "" : WorldLayout.Serialize(layout.Regions);

    public bool TryGet(string key, out PlaceState place) => places.TryGetValue(key, out place!);

    public void Receive(MapLayout map, WorldShape shape)
    {
        layout ??= new WorldLayout(shape);
        var biome = (Biome)map.Biome;
        bool ocean = biome == Biome.Ocean;
        if (!layout.TryPlace(map.PlaceKey, map.Width + Pad * 2, map.Height + Pad * 2, (Band)map.Band, ocean, out Region region))
        {
            if (unplaced.Add(map.PlaceKey))
                BridgeSystem.Instance.Mod.Logger.Warn($"Terradew has no room for {map.PlaceKey} ({map.Width} by {map.Height}). Use a large world.");
            return;
        }
        if (!places.TryGetValue(map.PlaceKey, out PlaceState? place))
        {
            place = new PlaceState { Key = map.PlaceKey };
            places[map.PlaceKey] = place;
            BridgeSystem.Instance.Mod.Logger.Info($"Terradew placed {map.PlaceKey} at {region.X},{region.Y} ({region.Width} by {region.Height}).");
        }
        bool moved = place.Rect != region;
        if (moved && place.Rect.Width > 0)
            Fill(place.Rect, 0, -1, null, 0, 0);
        place.Rect = region;
        place.Layout = map;
        bool newFloor = map.IsMineSlot && map.Floor != place.StampedFloor;
        if (moved || newFloor || map.Version != place.StampedVersion || !place.StampedThisVisit)
            StampPlace(place, newFloor);
    }

    public void ReceiveRoutes(Routes message)
    {
        var steps = new Dictionary<int, RouteEntry>();
        foreach (RouteEntry step in message.Steps)
            steps[step.NpcId] = step;
        routes[message.PlaceKey] = steps;
    }

    /// <summary>The world point a ground chaser should walk its feet to next, from Stardew's path search.</summary>
    public bool TryRoute(int npcId, out Vector2 feet)
    {
        feet = default;
        PlaceState? place = Current;
        if (place == null || !routes.TryGetValue(place.Key, out var steps) || !steps.TryGetValue(npcId, out RouteEntry step))
            return false;
        feet = new Vector2((place.OriginX + step.TileX) * 16f + 8f, (place.OriginY + step.TileY + 1) * 16f - 2f);
        return true;
    }

    private void StampPlace(PlaceState place, bool newFloor)
    {
        MapLayout map = place.Layout!;
        (ushort block, int wall) = Blocks((Biome)map.Biome);
        Fill(place.Rect, block, wall, map, place.OriginX, place.OriginY);
        place.StampedVersion = map.Version;
        place.StampedThisVisit = true;
        if (newFloor)
        {
            place.StampedFloor = map.Floor;
            ClearCreatures(place);
            Lairs.OnNewFloor(place);
        }
    }

    private static (ushort Block, int Wall) Blocks(Biome biome) => biome switch
    {
        Biome.Forest => (TileID.Grass, -1),
        Biome.Corruption => (TileID.Ebonstone, -1),
        Biome.Desert => (TileID.Sandstone, -1),
        Biome.Jungle => (TileID.JungleGrass, -1),
        Biome.Underworld => (TileID.Ash, -1),
        Biome.Dungeon => (TileID.BlueDungeonBrick, WallID.BlueDungeonUnsafe),
        Biome.Temple => (TileID.LihzahrdBrick, WallID.LihzahrdBrickUnsafe),
        _ => (TileID.Stone, -1),
    };

    /// <summary>Solid everywhere except the tiles the farmer can walk on. A null map clears the rectangle.</summary>
    private static void Fill(Region rect, ushort block, int wall, MapLayout? map, int originX, int originY)
    {
        int left = Math.Max(1, rect.X);
        int top = Math.Max(1, rect.Y);
        int right = Math.Min(Main.maxTilesX - 1, rect.X + rect.Width);
        int bottom = Math.Min(Main.maxTilesY - 1, rect.Y + rect.Height);
        for (int x = left; x < right; x++)
        {
            for (int y = top; y < bottom; y++)
            {
                Tile tile = Main.tile[x, y];
                tile.ClearEverything();
                if (map == null)
                    continue;
                if (map.IsBlocked(x - originX, y - originY))
                {
                    tile.HasTile = true;
                    tile.TileType = block;
                }
                if (wall >= 0)
                    tile.WallType = (ushort)wall;
            }
        }
    }

    private static void ClearCreatures(PlaceState place)
    {
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (npc.active && !npc.townNPC && !npc.boss && place.ContainsWorld(npc.Center))
                npc.active = false;
        }
        for (int i = 0; i < Main.maxItems; i++)
        {
            if (Main.item[i].active && place.ContainsWorld(Main.item[i].Center))
                Main.item[i].active = false;
        }
        for (int i = 0; i < Main.maxProjectiles; i++)
        {
            if (Main.projectile[i].active && place.ContainsWorld(Main.projectile[i].Center))
                Main.projectile[i].Kill();
        }
    }

    /// <summary>A random open tile in the current map at least <paramref name="minTiles"/> from the farmer, as feet in world pixels.</summary>
    public bool TryOpenSpot(Vector2 farmerFeet, int minTiles, int maxTiles, out Vector2 feet)
    {
        feet = default;
        PlaceState? place = Current;
        if (place?.Layout == null)
            return false;
        MapLayout map = place.Layout;
        int farmerX = (int)(farmerFeet.X / 16f) - place.OriginX;
        int farmerY = (int)(farmerFeet.Y / 16f) - place.OriginY;
        for (int attempt = 0; attempt < 60; attempt++)
        {
            int x = Main.rand.Next(map.Width);
            int y = Main.rand.Next(map.Height);
            if (map.IsBlocked(x, y))
                continue;
            int dx = x - farmerX;
            int dy = y - farmerY;
            int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (distance < minTiles || distance > maxTiles)
                continue;
            feet = new Vector2((place.OriginX + x) * 16f + 8f, (place.OriginY + y + 1) * 16f - 2f);
            return true;
        }
        return false;
    }

    /// <summary>Picks the current place from the pulse and sends what is inside it.</summary>
    public void Publish(Link link, Player player, FarmerPulse pulse, int tick)
    {
        PlaceState? place = null;
        if (places.TryGetValue(pulse.PlaceKey, out PlaceState? found) && found.Layout != null)
            place = found;
        if (pulse.PlaceKey != lastPlace)
        {
            if (places.TryGetValue(lastPlace, out PlaceState? left))
                left.StampedThisVisit = false;
            lastPlace = pulse.PlaceKey;
            TicksInPlace = 0;
        }
        else
        {
            TicksInPlace++;
        }
        Current = place;

        var state = new WorldState
        {
            Tick = tick,
            PlaceKey = place?.Key ?? "",
            RegionX = place?.OriginX ?? 0,
            RegionY = place?.OriginY ?? 0,
            Life = player.statLife,
            LifeMax = player.statLifeMax2,
            Mana = player.statMana,
            ManaMax = player.statManaMax2,
            Defense = player.statDefense,
            Dead = player.dead,
            Slot = player.selectedItem,
        };
        Item held = player.HeldItem;
        if (held != null && !held.IsAir)
        {
            state.Held = new HeldState
            {
                ItemType = held.type,
                Animation = player.itemAnimation,
                AnimationMax = player.itemAnimationMax,
                Rotation = player.itemRotation,
                Direction = (sbyte)player.direction,
                UseStyle = (byte)held.useStyle,
            };
            SpriteDump.Item(held.type);
        }
        for (int i = 0; i < player.buffType.Length; i++)
        {
            if (player.buffType[i] > 0 && player.buffTime[i] > 0)
            {
                state.Buffs.Add(new BuffState(player.buffType[i], player.buffTime[i]));
                SpriteDump.Buff(player.buffType[i]);
            }
        }

        if (place != null)
        {
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (!npc.active || npc.life <= 0 || !place.ContainsWorld(npc.Center))
                    continue;
                state.Entities.Add(Describe(npc));
                SpriteDump.Npc(npc.type);
            }
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile projectile = Main.projectile[i];
                if (!projectile.active || !place.ContainsWorld(projectile.Center))
                    continue;
                state.Projectiles.Add(new ProjectileState
                {
                    Id = i,
                    ProjType = projectile.type,
                    X = projectile.Center.X,
                    Y = projectile.Center.Y,
                    Rotation = projectile.rotation,
                    Frame = (short)projectile.frame,
                    FrameCount = (short)Math.Max(1, Main.projFrames[projectile.type]),
                    Direction = (sbyte)(projectile.spriteDirection == 0 ? 1 : projectile.spriteDirection),
                    Alpha = (byte)Math.Clamp(projectile.alpha, 0, 255),
                    Scale = projectile.scale,
                    Hostile = projectile.hostile,
                });
                SpriteDump.Projectile(projectile.type);
            }
            for (int i = 0; i < Main.maxItems; i++)
            {
                Item item = Main.item[i];
                if (!item.active || item.IsAir || !place.ContainsWorld(item.Center))
                    continue;
                state.Drops.Add(new DropState(i, item.type, item.stack, item.Center.X, item.Center.Y));
                SpriteDump.Item(item.type);
            }
        }
        link.Send(state);
    }

    private static EntityState Describe(NPC npc)
    {
        var flags = EntityFlags.None;
        bool worm = npc.aiStyle == NPCAIStyleID.Worm;
        if (npc.townNPC)
            flags |= EntityFlags.Town;
        if (npc.boss)
            flags |= EntityFlags.Boss;
        if (worm)
        {
            flags |= EntityFlags.Worm;
            if (Collision.SolidCollision(npc.position, npc.width, npc.height))
                flags |= EntityFlags.Buried;
        }
        if (Creatures.IsFlyer(npc))
            flags |= EntityFlags.Flyer;
        if (npc.friendly)
            flags |= EntityFlags.Friendly;
        if (!npc.friendly && npc.damage > 0)
            flags |= EntityFlags.Hostile;
        if (Creatures.IsChasing(npc))
            flags |= EntityFlags.Chasing;
        return new EntityState
        {
            Id = npc.whoAmI,
            NpcType = npc.type,
            X = npc.position.X + npc.width / 2f,
            Y = npc.position.Y + npc.height,
            Width = (short)npc.width,
            Height = (short)npc.height,
            Life = npc.life,
            LifeMax = npc.lifeMax,
            Flags = flags,
            Direction = (sbyte)(npc.spriteDirection == 0 ? 1 : npc.spriteDirection),
            FrameX = (short)npc.frame.X,
            FrameY = (short)npc.frame.Y,
            FrameW = (short)npc.frame.Width,
            FrameH = (short)npc.frame.Height,
            Rotation = npc.rotation,
            Scale = npc.scale <= 0f ? 1f : npc.scale,
            Alpha = (byte)Math.Clamp(npc.alpha, 0, 255),
            Name = npc.townNPC ? npc.GivenOrTypeName : npc.FullName,
        };
    }
}
