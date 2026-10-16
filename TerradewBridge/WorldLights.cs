using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terradew.Core;
using Terradew.Core.Protocol;
using Terradew.Core.World;
using Terraria;
using Terraria.ModLoader;

namespace TerradewBridge;

/// <summary>
/// Hooks the Terraria lighting so the light in the farmer's place reaches Stardew. Terraria adds nearly every dynamic
/// light through Lighting.AddLight, so a torch held by a creature, a glowing creature, a dropped torch and a buff on
/// another entity all light the Stardew map. The farmer's own light, and the light around the farmer's shots and
/// swings, are left out, since Glow, Torches and ShotLights already send those, or it should not be lit at all.
/// </summary>
/// <remarks>
/// The hook is on the tile overload. The Vector2 overloads convert to tiles and call it, so hooking them too would
/// count every light twice. Lights are summed per tile between two ticks, as the engine sums them within a frame.
/// Unsure: whether Terraria adds a light more than once in a tick (for example during a draw as well as an update).
/// If it does, these lights come out brighter than in Terraria. Not checked in game.
/// </remarks>
public sealed class WorldLightSystem : ModSystem
{
    public override void Load() => WorldLights.Attach(Mod);

    public override void Unload() => WorldLights.Detach();
}

/// <summary>The Terraria side of the world lights: collects this tick's lights and sends them to Stardew.</summary>
internal static class WorldLights
{
    /// <summary>While the lights hold still, a frame goes out this often as a heartbeat. Stardew holds the last one between frames.</summary>
    private const int SendEvery = 4;

    /// <summary>
    /// True while Terraria updates the farmer's own player (set by Puppet). Lights added then are the farmer's held
    /// torch, buffs and gear, which Glow and Torches already send, so they are left out. A creature's torch right next
    /// to the farmer still counts.
    /// </summary>
    internal static bool OwnUpdate;

    /// <summary>
    /// Lights within this many tiles of a glowing shot, or of one of the farmer's own shots and swings, are left out.
    /// ShotLights lights a glowing shot; a swing's own light is not wanted on the ground.
    /// </summary>
    private const float SkipTiles = 3f;

    /// <summary>The AI style of the crystal leaf, which is drawn bright and does not light the map (as in WorldStamp).</summary>
    private const int CrystalLeafAiStyle = 42;

    private static Mod? logMod;
    private static bool warned;

    /// <summary>Set by <see cref="Publish"/>: lights are collected only while the farmer's place is known.</summary>
    private static bool collecting;

    /// <summary>The place the collected lights are in. A light from another place is dropped.</summary>
    private static WorldStamp.PlaceState? capturing;

    /// <summary>The centres of glowing shots and the farmer's own shots as of the last tick. Lights near them are left out.</summary>
    private static readonly List<Vector2> skipCentres = new();

    /// <summary>The light added to each tile since the last tick, summed as the Lighting engine sums it.</summary>
    private static readonly Dictionary<(int X, int Y), (float R, float G, float B)> sums = new();

    /// <summary>The place and lights of the last frame sent, so a frame goes out when they change.</summary>
    private static string sentPlace = "";
    private static List<WorldLight> sentLights = new();

    internal static void Attach(Mod mod)
    {
        logMod = mod;
        On_Lighting.AddLight_int_int_float_float_float += AddLightHook;
    }

    internal static void Detach()
    {
        On_Lighting.AddLight_int_int_float_float_float -= AddLightHook;
        collecting = false;
        capturing = null;
        sums.Clear();
        skipCentres.Clear();
        sentPlace = "";
        sentLights = new List<WorldLight>();
    }

    /// <summary>
    /// Sends this tick's lights to Stardew, and starts collecting the next tick's. Called every tick. A null place
    /// means the farmer is not in the Terraria place, so nothing is collected and an empty frame clears the lights.
    /// </summary>
    public static void Publish(Link link, WorldStamp.PlaceState? place, Player player, int tick)
    {
        var tiles = new List<LightTile>(sums.Count);
        foreach (KeyValuePair<(int X, int Y), (float R, float G, float B)> pair in sums)
            tiles.Add(new LightTile(pair.Key.X, pair.Key.Y, pair.Value.R, pair.Value.G, pair.Value.B));
        sums.Clear();

        // Lights collected for another place cannot be drawn here, so they are dropped.
        bool same = ReferenceEquals(capturing, place);
        var frame = new WorldLightFrame { PlaceKey = place?.Key ?? "" };
        if (place != null && same)
        {
            foreach (LightSpot light in WorldLightMerge.Merge(tiles, WorldLightFrame.MaxLights))
                frame.Lights.Add(Project(place, light));
        }

        collecting = place != null && player != null && player.active;
        capturing = place;
        if (collecting)
        {
            Collect(place!, player);
        }
        else
        {
            skipCentres.Clear();
        }

        // A frame goes out as soon as the lights change, so a light that ends vanishes at once. While they hold still,
        // one goes out every few ticks as a heartbeat. An empty frame is only sent when there was something to clear.
        bool changed = frame.PlaceKey != sentPlace || !frame.Lights.SequenceEqual(sentLights);
        if (!changed && (tick % SendEvery != 0 || frame.Lights.Count == 0))
            return;
        sentPlace = frame.PlaceKey;
        sentLights = new List<WorldLight>(frame.Lights);
        link.Send(frame);
    }

    /// <summary>
    /// Finds the glowing shots in the place, whose light ShotLights already sends, and the farmer's own shots and
    /// swings, whose light is left out.
    /// </summary>
    private static void Collect(WorldStamp.PlaceState place, Player player)
    {
        skipCentres.Clear();
        for (int i = 0; i < Main.maxProjectiles; i++)
        {
            Projectile projectile = Main.projectile[i];
            if (!projectile.active)
                continue;
            bool own = projectile.owner == player.whoAmI;
            bool glowing = projectile.light > 0f && projectile.aiStyle != CrystalLeafAiStyle;
            if (!own && !glowing)
                continue;
            if (place.ContainsWorld(projectile.Center))
                skipCentres.Add(projectile.Center);
        }
    }

    /// <summary>The Stardew map tile, at the tile's centre, and the colour channels as bytes.</summary>
    private static WorldLight Project(WorldStamp.PlaceState place, LightSpot light)
    {
        (float x, float y) = WorldProjection.ToMap(place.OriginX, place.OriginY, light.X * 16f + 8f, light.Y * 16f + 8f);
        return new WorldLight(
            x / WorldProjection.StardewTilePixels,
            y / WorldProjection.StardewTilePixels,
            WorldLightMerge.ToByte(light.R),
            WorldLightMerge.ToByte(light.G),
            WorldLightMerge.ToByte(light.B));
    }

    /// <summary>Calls the real AddLight, then records the light for the farmer's place.</summary>
    private static void AddLightHook(On_Lighting.orig_AddLight_int_int_float_float_float orig, int i, int j, float r, float g, float b)
    {
        orig(i, j, r, g, b);
        try
        {
            Capture(i, j, r, g, b);
        }
        catch (Exception error)
        {
            Warn($"Terradew could not send a Terraria light to Stardew, so that light stays dark there: {error.Message}");
        }
    }

    private static void Capture(int i, int j, float r, float g, float b)
    {
        if (!collecting || OwnUpdate || capturing == null || r + g + b <= 0f || !capturing.ContainsTile(i, j))
            return;
        Vector2 centre = new(i * 16f + 8f, j * 16f + 8f);
        float skipLimit = SkipTiles * 16f;
        foreach (Vector2 skip in skipCentres)
        {
            if (Vector2.DistanceSquared(centre, skip) < skipLimit * skipLimit)
                return;
        }
        (int, int) key = (i, j);
        sums.TryGetValue(key, out (float R, float G, float B) sum);
        sums[key] = (sum.R + r, sum.G + g, sum.B + b);
    }

    private static void Warn(string text)
    {
        if (warned)
            return;
        warned = true;
        logMod?.Logger.Warn(text);
    }
}
