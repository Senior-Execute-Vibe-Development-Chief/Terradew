using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terradew.Core;
using Terradew.Core.Protocol;
using Terraria;
using Terraria.ID;

namespace TerradewBridge;

/// <summary>
/// Sends the Terraria dust inside the farmer's place to Stardew, which draws each mote at its own spot in the world.
/// Dust used to be drawn only into the farmer's picture (see <see cref="Body"/>), so the dust from creatures, shots,
/// explosions and ore never showed in Stardew.
/// </summary>
internal static class Dusts
{
    /// <summary>Whether the last frame sent had any motes, so that an empty frame goes out once when the last one ends.</summary>
    private static bool sentSome;

    /// <summary>The dust in the place this tick, by its distance from the farmer and its index in Main.dust. Reused each tick.</summary>
    private static readonly List<(float DistanceSq, int Index)> nearby = new();

    public static void Publish(Link link, WorldStamp.PlaceState? place, Player player)
    {
        nearby.Clear();
        if (place != null)
            Gather(place, player);
        if (nearby.Count == 0)
        {
            if (!sentSome)
                return;
            sentSome = false;
            link.Send(new DustFrame { PlaceKey = place?.Key ?? "" });
            return;
        }
        // Over the cap, the motes nearest the farmer are the ones that show.
        if (nearby.Count > DustFrame.MaxDusts)
        {
            nearby.Sort((a, b) => a.DistanceSq.CompareTo(b.DistanceSq));
            nearby.RemoveRange(DustFrame.MaxDusts, nearby.Count - DustFrame.MaxDusts);
        }
        SpriteDump.DustSheet();
        var frame = new DustFrame { PlaceKey = place!.Key };
        foreach ((float _, int index) in nearby)
            frame.Dusts.Add(Mote(place, Main.dust[index]));
        sentSome = true;
        link.Send(frame);
    }

    /// <summary>Collects the active Terraria dust inside the place. Modded dust is left out: Stardew has no sheet for it.</summary>
    private static void Gather(WorldStamp.PlaceState place, Player player)
    {
        Vector2 middle = player.Center;
        for (int i = 0; i < Main.maxDust; i++)
        {
            Dust dust = Main.dust[i];
            if (dust == null || !dust.active || dust.scale <= 0f || dust.type < 0 || dust.type >= DustID.Count)
                continue;
            if (dust.frame.Width <= 0 || dust.frame.Height <= 0 || !place.ContainsWorld(dust.position))
                continue;
            nearby.Add((Vector2.DistanceSquared(dust.position, middle), i));
        }
    }

    private static DustMote Mote(WorldStamp.PlaceState place, Dust dust)
    {
        (float x, float y) = WorldProjection.ToMap(place.OriginX, place.OriginY, dust.position.X, dust.position.Y);
        // White input, not Terraria's lighting, as Body drew it: Stardew's own night and caves dim the dust with the world.
        Color color = dust.GetAlpha(Color.White);
        return new DustMote(x, y, (ushort)dust.type,
            (short)dust.frame.X, (short)dust.frame.Y, (short)dust.frame.Width, (short)dust.frame.Height,
            color.R, color.G, color.B, color.A, dust.scale, dust.rotation);
    }
}
