using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;
using Terradew.Core;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>
/// Lights the Stardew map with the glow of Terraria's shots, such as a flare or a magic bolt. Each glowing shot
/// gets one Stardew light that follows it. Only the <see cref="MaxLights"/> nearest the farmer are lit, and
/// nothing is lit while the Terraria world is not linked for the place the farmer stands in.
/// </summary>
internal static class ShotLights
{
    /// <summary>Stardew light ids are this prefix followed by the slot number.</summary>
    private const string Prefix = "Terradew_Shot_";

    /// <summary>The most shot lights at once, so Stardew is not slowed by a crowd of them.</summary>
    private const int MaxLights = 24;

    /// <summary>The light radius, in Stardew tiles, that a full-strength shot light gives.</summary>
    private const float FullRadius = 1.5f;

    /// <summary>The <see cref="ProjectileState.Light"/> value of a full-strength shot light.</summary>
    private const float FullLight = 255f;

    /// <summary>Visible colour used when a shot sends no colour of its own.</summary>
    private static readonly Color DefaultColor = new(255, 230, 180);

    private static readonly List<(float Distance, Vector2 Position, ProjectileState Shot)> candidates = new();

    /// <summary>The location the lights were last placed in. A change of location clears them.</summary>
    private static GameLocation? lastLocation;

    /// <summary>How many of this mod's lights are on at the moment, so clearing only touches those.</summary>
    private static int lit;

    /// <summary>Called every tick while the farmer can act. Places one light per glowing shot near the farmer.</summary>
    public static void Update(Bridge bridge)
    {
        Farmer player = Game1.player;
        GameLocation? location = player.currentLocation;
        if (location != lastLocation)
        {
            Clear();
            lastLocation = location;
        }
        if (location == null)
            return;

        WorldState? world = bridge.World;
        if (world == null || !bridge.WorldIsFor(Maps.PlaceOf(location).Key))
        {
            Clear();
            return;
        }

        Rectangle box = player.GetBoundingBox();
        var farmer = new Vector2(box.Center.X, box.Center.Y);
        candidates.Clear();
        foreach (ProjectileState shot in world.Projectiles)
        {
            if (shot.Light == 0)
                continue;
            // Same placement as the shot itself: a riding shot floats from the farmer's feet, the rest sit on the map.
            (float x, float y) = shot.Attached
                ? (box.Center.X + shot.OffsetX * SpriteLayout.DrawScale, box.Bottom + shot.OffsetY * SpriteLayout.DrawScale)
                : WorldProjection.ToMap(world.RegionX, world.RegionY, shot.X, shot.Y);
            var position = new Vector2(x, y);
            candidates.Add((Vector2.DistanceSquared(position, farmer), position, shot));
        }
        candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        int count = Math.Min(MaxLights, candidates.Count);
        string here = location.NameOrUniqueName;
        for (int slot = 0; slot < count; slot++)
            Place(slot, candidates[slot].Position, candidates[slot].Shot, here);
        for (int slot = count; slot < lit; slot++)
            Game1.currentLightSources.Remove(IdOf(slot));
        lit = count;
    }

    /// <summary>Forgets every light, such as when the farm closes or the farmer returns to the title screen.</summary>
    public static void Reset()
    {
        Clear();
        lastLocation = null;
    }

    /// <summary>
    /// Lights one shot. The light is bound to <paramref name="place"/> through onlyLocation, so Stardew never draws it
    /// in another location. Clear() takes these lights down on a location change, before the new location is lit.
    /// </summary>
    private static void Place(int slot, Vector2 position, ProjectileState shot, string place)
    {
        string id = IdOf(slot);
        float radius = shot.Light / FullLight * FullRadius;
        Color color = StardewColor(shot.LightColor);
        if (Game1.currentLightSources.TryGetValue(id, out LightSource? light) && light != null)
        {
            light.position.Value = position;
            light.radius.Value = radius;
            light.color.Value = color;
            return;
        }
        Game1.currentLightSources[id] = new LightSource(id, LightSource.sconceLight, position, radius, color, LightSource.LightContext.None, 0L, onlyLocation: place);
    }

    /// <summary>Stardew lights subtract their colour, so the visible colour is inverted.</summary>
    private static Color StardewColor(uint packed)
    {
        Color visible = packed != 0 ? new Color { PackedValue = packed } : DefaultColor;
        return new Color(255 - visible.R, 255 - visible.G, 255 - visible.B);
    }

    private static void Clear()
    {
        for (int slot = 0; slot < lit; slot++)
            Game1.currentLightSources.Remove(IdOf(slot));
        lit = 0;
    }

    private static string IdOf(int slot) => Prefix + slot;
}
