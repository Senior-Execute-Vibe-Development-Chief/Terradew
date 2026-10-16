using System;
using Microsoft.Xna.Framework;
using StardewValley;
using Terradew.Core;
using Terradew.Core.Protocol;
using Terradew.Core.World;

namespace Terradew;

/// <summary>
/// Lights the Stardew map with the Terraria light in the farmer's place, so a torch held by a creature, a glowing
/// creature or a dropped torch lights the ground as it does in Terraria. Terraria's lights arrive as soft blobs, each
/// one Stardew light that is moved and recoloured as Terraria sends them. Nothing is lit for another place, and nothing
/// is lit where Stardew is not drawing darkness: outdoors in daylight, a bright room or a house stays as it is.
/// </summary>
internal static class WorldLights
{
    /// <summary>Stardew light ids are this prefix followed by the slot number.</summary>
    private const string Prefix = "Terradew.World.";

    /// <summary>The most Terraria lights shown at once. Terraria sends no more than this.</summary>
    private const int MaxLights = WorldLightFrame.MaxLights;

    /// <summary>The light radius floor, in Stardew tiles, so even a faint Terraria light reaches its neighbours.</summary>
    private const float BaseRadius = 0.6f;

    /// <summary>How much the brightest channel adds to the radius, in Stardew tiles.</summary>
    private const float RadiusPerLevel = 1.2f;

    /// <summary>
    /// Whether Stardew draws darkness in this location, so a light shows: caves and mines always, outdoors only after
    /// dark, and never in a bright indoor room. Elsewhere a Stardew light would paint colour over a daylit scene.
    /// </summary>
    internal static bool LitHere(GameLocation location)
    {
        if (Maps.PlaceOf(location).Band != Band.Surface)
            return true;
        return location.IsOutdoors && Game1.isDarkOut();
    }

    /// <summary>How many of this mod's lights are on at the moment, so clearing only touches those.</summary>
    private static int lit;

    /// <summary>The location the lights were made in. A location change clears them, since each light is bound to one location.</summary>
    private static string litPlace = "";

    /// <summary>Called every tick while the farmer can act. Shows the frame's lights, or clears them when there are none for this place.</summary>
    public static void Update(Bridge bridge)
    {
        Farmer player = Game1.player;
        GameLocation? location = player.currentLocation;
        if (location == null)
        {
            Clear();
            return;
        }
        string here = location.NameOrUniqueName;
        WorldLightFrame? frame = bridge.WorldLights;
        if (frame == null || frame.PlaceKey != Maps.PlaceOf(location).Key || !LitHere(location))
        {
            Clear();
            return;
        }
        if (litPlace != here)
            Clear();

        int count = Math.Min(MaxLights, frame.Lights.Count);
        for (int slot = 0; slot < count; slot++)
            Place(slot, frame.Lights[slot], here);
        for (int slot = count; slot < lit; slot++)
            Game1.currentLightSources.Remove(IdOf(slot));
        lit = count;
        litPlace = count > 0 ? here : "";
    }

    /// <summary>Forgets every light, such as when the farm closes or the farmer returns to the title screen.</summary>
    public static void Reset() => Clear();

    /// <summary>
    /// Lights one Terraria light. The light is bound to <paramref name="place"/> through onlyLocation, so Stardew never
    /// draws it in another location.
    /// </summary>
    private static void Place(int slot, WorldLight light, string place)
    {
        string id = IdOf(slot);
        var position = new Vector2(light.X * WorldProjection.StardewTilePixels, light.Y * WorldProjection.StardewTilePixels);
        float radius = BaseRadius + RadiusPerLevel * Math.Max(light.R, Math.Max(light.G, light.B)) / 255f;
        Color color = StardewColor(light);
        if (Game1.currentLightSources.TryGetValue(id, out LightSource? source) && source != null)
        {
            source.position.Value = position;
            source.radius.Value = radius;
            source.color.Value = color;
            return;
        }
        Game1.currentLightSources[id] = new LightSource(id, LightSource.sconceLight, position, radius, color, LightSource.LightContext.None, 0L, onlyLocation: place);
    }

    /// <summary>Stardew lights subtract their colour, so the visible colour is inverted.</summary>
    private static Color StardewColor(WorldLight light) => new(255 - light.R, 255 - light.G, 255 - light.B);

    private static void Clear()
    {
        for (int slot = 0; slot < lit; slot++)
            Game1.currentLightSources.Remove(IdOf(slot));
        lit = 0;
        litPlace = "";
    }

    private static string IdOf(int slot) => Prefix + slot;
}
