using System;
using System.Collections.Generic;
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
/// another entity all light the Stardew map. Only the farmer's own light and the shots' light are left out, since
/// Glow, Torches and ShotLights already send those.
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
    /// <summary>A frame goes out every this many ticks while lights are on. Stardew holds the last one between frames.</summary>
    private const int SendEvery = 4;

    /// <summary>Lights within this many tiles of the farmer's centre are left out. Glow and Torches light the farmer.</summary>
    private const float FarmerSkipTiles = 3f;

    /// <summary>Lights within this many tiles of a glowing shot are left out. ShotLights lights the shot.</summary>
    private const float ShotSkipTiles = 2f;

    /// <summary>The AI style of the crystal leaf, which is drawn bright and does not light the map (as in WorldStamp).</summary>
    private const int CrystalLeafAiStyle = 42;

    private static Mod? logMod;
    private static bool warned;

    /// <summary>Set by <see cref="Publish"/>: lights are collected only while the farmer's place is known.</summary>
    private static bool collecting;

    /// <summary>The place the collected lights are in. A light from another place is dropped.</summary>
    private static WorldStamp.PlaceState? capturing;

    /// <summary>The farmer's centre and the glowing shots' centres as of the last tick. Lights near them are left out.</summary>
    private static Vector2 farmerCentre;
    private static readonly List<Vector2> shotCentres = new();

    /// <summary>The light added to each tile since the last tick, summed as the Lighting engine sums it.</summary>
    private static readonly Dictionary<(int X, int Y), (float R, float G, float B)> sums = new();

    /// <summary>Whether the last frame sent had any lights, so an empty frame goes out once when the last ones end.</summary>
    private static bool sentSome;

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
        shotCentres.Clear();
        sentSome = false;
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
            foreach (LightTile light in WorldLightMerge.Merge(tiles, WorldLightFrame.MaxLights))
                frame.Lights.Add(Project(place, light));
        }

        collecting = place != null && player != null && player.active;
        capturing = place;
        if (collecting)
        {
            farmerCentre = player.Center;
            Collect(place!);
        }
        else
        {
            shotCentres.Clear();
        }

        if (frame.Lights.Count == 0)
        {
            if (!sentSome)
                return;
            sentSome = false;
            link.Send(frame);
            return;
        }
        if (tick % SendEvery != 0)
            return;
        sentSome = true;
        link.Send(frame);
    }

    /// <summary>Finds the glowing shots in the place, whose light ShotLights already sends.</summary>
    private static void Collect(WorldStamp.PlaceState place)
    {
        shotCentres.Clear();
        for (int i = 0; i < Main.maxProjectiles; i++)
        {
            Projectile projectile = Main.projectile[i];
            if (!projectile.active || projectile.light <= 0f || projectile.aiStyle == CrystalLeafAiStyle)
                continue;
            if (place.ContainsWorld(projectile.Center))
                shotCentres.Add(projectile.Center);
        }
    }

    /// <summary>The Stardew map tile, at the tile's centre, and the colour channels as bytes.</summary>
    private static WorldLight Project(WorldStamp.PlaceState place, LightTile light)
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
        if (!collecting || capturing == null || r + g + b <= 0f || !capturing.ContainsTile(i, j))
            return;
        Vector2 centre = new(i * 16f + 8f, j * 16f + 8f);
        float farmerLimit = FarmerSkipTiles * 16f;
        if (Vector2.DistanceSquared(centre, farmerCentre) < farmerLimit * farmerLimit)
            return;
        float shotLimit = ShotSkipTiles * 16f;
        foreach (Vector2 shot in shotCentres)
        {
            if (Vector2.DistanceSquared(centre, shot) < shotLimit * shotLimit)
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
