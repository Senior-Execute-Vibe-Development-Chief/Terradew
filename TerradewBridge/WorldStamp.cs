using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terradew.Core;
using Terradew.Core.Protocol;
using GridTile = Terradew.Core.Routing.Tile;
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
        public bool StampedHardmode;

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
    private readonly Dictionary<int, string> typeNames = new();
    private WorldLayout? layout;
    private string lastPlace = "";
    private Vector2 lastFeet;
    private readonly List<(int Index, int Type)> followers = new();

    /// <summary>Chasers this close to the farmer when the farmer leaves a place come along.</summary>
    private const int FollowTiles = 8;

    /// <summary>Ticks after arriving before followers come through, a little after the farmer's own grace.</summary>
    private const int FollowDelay = 50;

    public PlaceState? Current { get; private set; }

    /// <summary>Ticks since the puppet last changed place. The puppet is immune for a moment after a snap.</summary>
    public int TicksInPlace { get; private set; }

    public void Clear()
    {
        places.Clear();
        routes.Clear();
        unplaced.Clear();
        typeNames.Clear();
        layout = null;
        Current = null;
        lastPlace = "";
        followers.Clear();
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
        if (moved || newFloor || map.Version != place.StampedVersion || !place.StampedThisVisit || Main.hardMode != place.StampedHardmode)
            StampPlace(place, newFloor);
    }

    private const int WardLeaves = 12;

    /// <summary>
    /// The Dryad's Ward is drawn by Terraria's own code, not from its sprite, so Stardew would see nothing.
    /// It goes over as a spinning ring of leaves around the Dryad instead.
    /// </summary>
    private static void AddWardLeaves(WorldState state, Projectile ward, int index)
    {
        int leaf = ProjectileID.Search.TryGetId("Leaf", out int found) ? found : ward.type;
        float radius = Math.Max(ward.width, ward.height) >= 48 ? Math.Max(ward.width, ward.height) / 2f : 96f;
        float spin = Main.GameUpdateCount * 0.05f;
        int frames = Math.Max(1, Main.projFrames[leaf]);
        for (int k = 0; k < WardLeaves; k++)
        {
            float angle = spin + k * MathHelper.TwoPi / WardLeaves;
            state.Projectiles.Add(new ProjectileState
            {
                Id = Main.maxProjectiles + index * WardLeaves + k,
                ProjType = leaf,
                X = ward.Center.X + (float)Math.Cos(angle) * radius,
                Y = ward.Center.Y + (float)Math.Sin(angle) * radius,
                Rotation = angle + MathHelper.PiOver2,
                Frame = (short)((Main.GameUpdateCount / 5 + k) % frames),
                FrameCount = (short)frames,
                Direction = 1,
                Alpha = 0,
                Scale = 1f,
                Hostile = false,
            });
        }
        SpriteDump.Projectile(leaf);
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
        (ushort block, int wall) = Blocks(BiomeNow(place));
        Fill(place.Rect, block, wall, map, place.OriginX, place.OriginY);
        place.StampedVersion = map.Version;
        place.StampedHardmode = Main.hardMode;
        place.StampedThisVisit = true;
        if (newFloor)
        {
            place.StampedFloor = map.Floor;
            ClearCreatures(place);
            Lairs.OnNewFloor(place);
        }
    }

    /// <summary>The place's biome, with hardmode's Hallow spread applied.</summary>
    private static Biome BiomeNow(PlaceState place)
        => Main.hardMode && Places.HallowsInHardmode(place.Key) ? Biome.Hallow : (Biome)place.Layout!.Biome;

    private static (ushort Block, int Wall) Blocks(Biome biome) => biome switch
    {
        Biome.Forest => (TileID.Grass, -1),
        Biome.Corruption => (WorldGen.crimson ? TileID.Crimstone : TileID.Ebonstone, -1),
        Biome.Hallow => (TileID.Pearlstone, -1),
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

    private void GatherFollowers(PlaceState left)
    {
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (npc.active && !npc.boss && !npc.townNPC && Creatures.IsChasing(npc) && left.ContainsWorld(npc.Center)
                && Vector2.Distance(npc.Bottom, lastFeet) <= FollowTiles * 16f && !followers.Contains((i, npc.type)))
                followers.Add((i, npc.type));
        }
    }

    /// <summary>Chasers come through the door behind the farmer, but never into houses and other places without spawns.</summary>
    private void BringFollowers(PlaceState place, Player player)
    {
        if (place.Layout == null || !place.Layout.Spawns)
        {
            followers.Clear();
            return;
        }
        Vector2 farmer = new(player.position.X + player.width / 2f, player.position.Y + player.height);
        foreach (var (i, type) in followers)
        {
            NPC npc = Main.npc[i];
            if (!npc.active || npc.type != type || npc.townNPC || npc.boss || place.ContainsWorld(npc.Center) || !TryOpenNear(place, farmer, 2, 5, out Vector2 feet))
                continue;
            (float x, float y) = WorldProjection.FromFeet(feet.X, feet.Y, npc.width, npc.height);
            npc.position = new Vector2(x, y);
            npc.velocity = Vector2.Zero;
            npc.netUpdate = true;
        }
        followers.Clear();
    }

    /// <summary>
    /// An open tile between <paramref name="minTiles"/> and <paramref name="maxTiles"/> from a point, nearest first,
    /// that can be walked to from the point, so nothing lands behind a wall.
    /// </summary>
    private static bool TryOpenNear(PlaceState place, Vector2 from, int minTiles, int maxTiles, out Vector2 feet)
    {
        feet = default;
        int cx = (int)(from.X / 16f);
        int cy = (int)(from.Y / 16f);
        for (int ring = minTiles; ring <= maxTiles; ring++)
        {
            int start = Main.rand.Next(8 * ring);
            for (int n = 0; n < 8 * ring; n++)
            {
                (int dx, int dy) = RingPoint(ring, (start + n) % (8 * ring));
                if (!place.IsOpen(cx + dx, cy + dy) || !Reachable(place, cx, cy, dx, dy, maxTiles))
                    continue;
                feet = new Vector2((cx + dx) * 16f + 8f, (cy + dy + 1) * 16f - 2f);
                return true;
            }
        }
        return false;
    }

    /// <summary>Whether the tile (dx, dy) from (cx, cy) joins it by open tiles inside the square of radius <paramref name="radius"/>.</summary>
    private static bool Reachable(PlaceState place, int cx, int cy, int dx, int dy, int radius)
    {
        int size = radius * 2 + 1;
        int left = cx - radius;
        int top = cy - radius;
        var path = Terradew.Core.Routing.GridPath.Find(size, size, (x, y) => !place.IsOpen(left + x, top + y),
            new GridTile(radius, radius), new GridTile(radius + dx, radius + dy), maxNodes: size * size, simplify: false);
        return path != null;
    }

    /// <summary>The n-th tile on the square ring at distance r, going around.</summary>
    private static (int, int) RingPoint(int r, int n)
    {
        int side = n / (2 * r);
        int along = n % (2 * r) - r;
        return side switch
        {
            0 => (along, -r),
            1 => (r, along),
            2 => (-along, r),
            _ => (-r, -along),
        };
    }

    /// <summary>A random open tile near the current map's edge, where its entrances are, at least <paramref name="minTiles"/> from the farmer.</summary>
    public bool TryEdgeSpot(Vector2 farmerFeet, int minTiles, out Vector2 feet)
    {
        feet = default;
        PlaceState? place = Current;
        if (place?.Layout == null)
            return false;
        MapLayout map = place.Layout;
        int farmerX = (int)(farmerFeet.X / 16f) - place.OriginX;
        int farmerY = (int)(farmerFeet.Y / 16f) - place.OriginY;
        const int Margin = 3;
        for (int attempt = 0; attempt < 120; attempt++)
        {
            int x, y;
            int side = Main.rand.Next(4);
            if (side < 2)
            {
                x = side == 0 ? Main.rand.Next(Margin) : map.Width - 1 - Main.rand.Next(Margin);
                y = Main.rand.Next(map.Height);
            }
            else
            {
                x = Main.rand.Next(map.Width);
                y = side == 2 ? Main.rand.Next(Margin) : map.Height - 1 - Main.rand.Next(Margin);
            }
            if (map.IsBlocked(x, y) || Math.Max(Math.Abs(x - farmerX), Math.Abs(y - farmerY)) < minTiles)
                continue;
            feet = new Vector2((place.OriginX + x) * 16f + 8f, (place.OriginY + y + 1) * 16f - 2f);
            return true;
        }
        return false;
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
            // Followers still on their way are kept, so a quick second door does not strand them.
            if (places.TryGetValue(lastPlace, out PlaceState? left))
            {
                left.StampedThisVisit = false;
                GatherFollowers(left);
            }
            lastPlace = pulse.PlaceKey;
            TicksInPlace = 0;
        }
        else
        {
            TicksInPlace++;
        }
        Current = place;
        if (place != null && TicksInPlace == FollowDelay)
            BringFollowers(place, player);
        lastFeet = new Vector2(player.position.X + player.width / 2f, player.position.Y + player.height);

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
                state.Entities.Add(Describe(npc, NameOf(npc)));
                SpriteDump.Npc(npc.type);
                if (npc.townNPC) SpriteDump.NpcHead(Terraria.GameContent.TownNPCProfiles.GetHeadIndexSafe(npc));
            }
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile projectile = Main.projectile[i];
                if (!projectile.active || !place.ContainsWorld(projectile.Center))
                    continue;
                if (projectile.type == ProjectileID.DryadsWardCircle)
                {
                    AddWardLeaves(state, projectile, i);
                    continue;
                }
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
                    Color = projectile.GetAlpha(Microsoft.Xna.Framework.Color.White).PackedValue,
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

    /// <summary>
    /// Town NPCs have their own given names. Every other NPC's full name depends only on its type, so it is
    /// looked up once per type rather than rebuilt for each NPC each tick.
    /// </summary>
    private string NameOf(NPC npc)
    {
        if (npc.townNPC)
            return npc.GivenOrTypeName;
        if (!typeNames.TryGetValue(npc.type, out string? name))
        {
            name = npc.FullName;
            typeNames[npc.type] = name;
        }
        return name;
    }

    private static EntityState Describe(NPC npc, string name)
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
            if (Creatures.IsBuried(npc))
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
            Name = name,
            // As Terraria draws it in full light: GetAlpha for the body, then GetColor for the tinted pass of coloured NPCs like slimes.
            Color = npc.GetAlpha(Microsoft.Xna.Framework.Color.White).PackedValue,
            Tint = npc.color == default ? 0u : npc.GetColor(Microsoft.Xna.Framework.Color.White).PackedValue,
            Look = npc.townNPC || NPCID.Sets.ActsLikeTownNPC[npc.type] ? SpriteDump.Look(npc) : "",
        };
    }
}
