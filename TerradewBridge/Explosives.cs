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
/// ground and still collide with tiles. The bombs and dynamite among them also tell Stardew, which breaks the Stardew
/// breakables their blast reaches.
/// </summary>
public sealed class Explosives : GlobalProjectile
{
    /// <summary>Share of each axis's speed kept per tick. A thrown bomb slides a few tiles, then stops.</summary>
    private const float Slide = 0.93f;

    /// <summary>Blast radius in tiles of a tile-breaker, by a word in its internal name. One Terraria tile is one Stardew tile.</summary>
    private static readonly (string Word, int Radius)[] BlastByName = { ("Dynamite", 6), ("Bomb", 3) };

    /// <summary>Thrown explosives that only hurt. The Happy Grenade is named like a bomb, so it is listed here.</summary>
    private static readonly HashSet<int> NoBlast = new() { ProjectileID.HappyBomb };

    /// <summary>
    /// Thrown shots that are not explosives but still fall in Terraria: Beenade keeps aiStyle 14 and Molotov Cocktail 68,
    /// so the explosive set does not cover them.
    /// </summary>
    private static readonly HashSet<int> ThrownExtra = new() { ProjectileID.Beenade, ProjectileID.MolotovCocktail };

    public override bool InstancePerEntity => true;

    private Vector2 before;
    private Vector2 kept;
    private bool hasKept;

    public override bool PreAI(Projectile projectile)
    {
        // Terraria moves a shot after its AI, so a pull applied there is still on the velocity now. Take it back first.
        if (hasKept && IsPull(projectile.velocity.Y - kept.Y) && InFarmersPlace(projectile))
            projectile.velocity.Y = kept.Y;
        before = projectile.velocity;
        return true;
    }

    public override void PostAI(Projectile projectile)
    {
        if (!Thrown(projectile) || !InFarmersPlace(projectile))
        {
            hasKept = false;
            return;
        }
        // Vanilla pulls a thrown explosive down during its AI. Undo that pull, then let both axes slow to a stop.
        if (IsPull(projectile.velocity.Y - before.Y))
            projectile.velocity.Y = before.Y;
        projectile.velocity *= Slide;
        kept = projectile.velocity;
        hasKept = true;
    }

    public override void OnKill(Projectile projectile, int timeLeft)
    {
        // Only the farmer's own explosives reach Stardew; a Terraria creature's bomb does not break Stardew stone.
        if (projectile.owner != Main.myPlayer)
            return;
        int radius = BlastTiles(projectile.type);
        if (radius == 0)
            return;
        BridgeSystem bridge = BridgeSystem.Instance;
        WorldStamp.PlaceState? place = FarmerPlace(projectile);
        if (place == null)
        {
            bridge.Mod.Logger.Debug($"Terradew did not send a {NameOf(projectile.type)} blast: it went off outside the farmer's place.");
            return;
        }
        (float x, float y) = WorldProjection.ToMap(place.OriginX, place.OriginY, projectile.Center.X, projectile.Center.Y);
        float tileX = x / WorldProjection.StardewTilePixels;
        float tileY = y / WorldProjection.StardewTilePixels;
        bridge.Mod.Logger.Info($"Terradew sent a {NameOf(projectile.type)} blast of {radius} tiles at {tileX:0.0},{tileY:0.0} in {place.Key} to Stardew.");
        bridge.Send(new Explosion
        {
            PlaceKey = place.Key,
            TileX = tileX,
            TileY = tileY,
            Radius = radius,
        });
    }

    /// <summary>
    /// A shot the farmer threw: an explosive that is not a rocket (rockets are ranged and fly straight), or Beenade or Molotov.
    /// Terraria creatures' shots are never the farmer's.
    /// </summary>
    private static bool Thrown(Projectile projectile)
    {
        if (projectile.owner != Main.myPlayer)
            return false;
        if (ThrownExtra.Contains(projectile.type))
            return true;
        bool explosive = projectile.aiStyle == ProjAIStyleID.Explosive || ProjectileID.Sets.Explosive[projectile.type];
        return explosive && projectile.DamageType != DamageClass.Ranged;
    }

    /// <summary>
    /// The blast radius in tiles of a thrown explosive, or 0 when it breaks no tiles. Bombs and dynamite break tiles
    /// (every bomb variant, by its name); grenades and the other throws only hurt.
    /// </summary>
    private static int BlastTiles(int type)
    {
        if (NoBlast.Contains(type))
            return 0;
        string name = NameOf(type);
        foreach ((string word, int radius) in BlastByName)
        {
            if (name.Contains(word))
                return radius;
        }
        return 0;
    }

    /// <summary>
    /// A small rise in the southward speed over one tick is Terraria's gravity. A bigger change is a bounce off a wall
    /// or a sticky bomb stopping, and is kept.
    /// </summary>
    private static bool IsPull(float rise) => rise > 0f && rise <= 0.5f;

    private static string NameOf(int type) => ProjectileID.Search.TryGetName(type, out string name) ? name : $"projectile {type}";

    private static bool InFarmersPlace(Projectile projectile) => FarmerPlace(projectile) != null;

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
