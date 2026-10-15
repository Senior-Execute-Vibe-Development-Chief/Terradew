using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terradew.Core;
using Terradew.Core.Protocol;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerradewBridge;

/// <summary>
/// Thrown explosives in the farmer's place keep no gravity: the plane has no down, so they slide to a stop on the
/// ground and still collide with tiles. The ones that break tiles also tell Stardew, which breaks the Stardew
/// breakables their blast reaches.
/// </summary>
public sealed class Explosives : GlobalProjectile
{
    /// <summary>Share of each axis's speed kept per tick. A thrown bomb slides a few tiles, then stops.</summary>
    private const float Slide = 0.93f;

    /// <summary>Explosives that break tiles, with their blast radius in tiles. One Terraria tile is one Stardew tile.</summary>
    private static readonly Dictionary<int, int> BlastTiles = new()
    {
        [ProjectileID.Bomb] = 3,
        [ProjectileID.StickyBomb] = 3,
        [ProjectileID.BouncyBomb] = 3,
        [ProjectileID.DirtBomb] = 3,
        [ProjectileID.DirtStickyBomb] = 3,
        [ProjectileID.ScarabBomb] = 3,
        [ProjectileID.Dynamite] = 6,
        [ProjectileID.StickyDynamite] = 6,
        [ProjectileID.BouncyDynamite] = 6,
    };

    /// <summary>
    /// Every thrown explosive. Grenades only hurt, so they are here without a blast. Rockets are not listed: they fly straight.
    /// </summary>
    private static readonly HashSet<int> Thrown = new(BlastTiles.Keys)
    {
        ProjectileID.Grenade,
        ProjectileID.StickyGrenade,
        ProjectileID.BouncyGrenade,
        ProjectileID.HappyBomb,
    };

    public override bool InstancePerEntity => true;

    private Vector2 before;

    public override bool PreAI(Projectile projectile)
    {
        before = projectile.velocity;
        return true;
    }

    public override void PostAI(Projectile projectile)
    {
        if (!Thrown.Contains(projectile.type) || FarmerPlace(projectile) == null)
            return;
        // Vanilla pulls a bomb down each tick. Undo that pull, then let both axes slow to a stop.
        if (projectile.velocity.Y > before.Y)
            projectile.velocity.Y = before.Y;
        projectile.velocity *= Slide;
    }

    public override void OnKill(Projectile projectile, int timeLeft)
    {
        // Only the farmer's own explosives reach Stardew; a Terraria creature's bomb does not break Stardew stone.
        if (projectile.owner != Main.myPlayer || !BlastTiles.TryGetValue(projectile.type, out int radius))
            return;
        WorldStamp.PlaceState? place = FarmerPlace(projectile);
        if (place == null)
            return;
        (float x, float y) = WorldProjection.ToMap(place.OriginX, place.OriginY, projectile.Center.X, projectile.Center.Y);
        BridgeSystem.Instance.Send(new Explosion
        {
            PlaceKey = place.Key,
            TileX = x / WorldProjection.StardewTilePixels,
            TileY = y / WorldProjection.StardewTilePixels,
            Radius = radius,
        });
    }

    /// <summary>The farmer's place, when Terraria is linked to Stardew and the projectile is inside it. Null otherwise.</summary>
    private static WorldStamp.PlaceState? FarmerPlace(Projectile projectile)
    {
        BridgeSystem bridge = BridgeSystem.Instance;
        if (!BridgeSystem.Active || !bridge.Linked)
            return null;
        WorldStamp.PlaceState? place = bridge.Stamp.Current;
        return place != null && place.ContainsWorld(projectile.Center) ? place : null;
    }
}
