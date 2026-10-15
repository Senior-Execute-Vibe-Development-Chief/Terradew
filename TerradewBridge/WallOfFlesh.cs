using Microsoft.Xna.Framework;
using Terradew.Core;
using Terradew.Core.Bosses;
using Terradew.Core.Protocol;
using Terradew.Core.World;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerradewBridge;

/// <summary>
/// The Wall of Flesh in the underworld mine floors. The wall is a horizontal line across the whole floor that
/// starts at the floor's south edge and climbs north, one Stardew tile per step, faster as its life drops.
/// Everything south of the line is flesh. The farmer takes damage from the wall once their feet are in it, and
/// a ladder up starts a new climb on the floor above, keeping the wall's life.
/// The mouth stays on the wall line, centred on the farmer's X, so Terraria's own attacks aim at the farmer.
/// The eyes sit on the wall line either side of the mouth.
/// </summary>
public sealed class WallOfFlesh : GlobalNPC
{
    /// <summary>How far left and right of the mouth the eyes sit, in Terraria pixels.</summary>
    private const float EyeOffset = 18f * 16f;

    /// <summary>Damage from one touch of the wall. Terraria's own immunity frames stop it repeating every tick.</summary>
    private const int TouchDamage = 100;

    /// <summary>Ticks between sends while the wall lives. Stardew draws the wall between sends.</summary>
    private const int SendEvery = 15;

    /// <summary>The place the wall is climbing in. Changing it restarts the climb at that floor's south edge.</summary>
    private static string placeKey = "";

    /// <summary>The wall's north edge in Stardew map pixels for the current place.</summary>
    private static float wallY;

    private static bool announced;
    private static string announcedPlace = "";
    private static int sinceSent;

    /// <summary>Only the mouth and the eyes take part. Creatures.cs must leave them alone.</summary>
    public static bool Owns(NPC npc) => npc.type == NPCID.WallofFlesh || npc.type == NPCID.WallofFleshEye;

    public override bool AppliesToEntity(NPC entity, bool lateInstantiation) => Owns(entity);

    /// <summary>A new wall, from the Voodoo Doll or otherwise, starts its climb again from the next place it sees.</summary>
    public override void OnSpawn(NPC npc, IEntitySource source)
    {
        if (npc.type == NPCID.WallofFlesh)
            placeKey = "";
    }

    /// <summary>Forgets the climb when the world unloads.</summary>
    public static void Reset()
    {
        placeKey = "";
        wallY = 0f;
        announced = false;
        announcedPlace = "";
        sinceSent = 0;
    }

    public override void PostAI(NPC npc)
    {
        if (!BridgeSystem.Active || !npc.active || npc.life <= 0)
            return;
        // The custom touch below is the wall's only melee. Vanilla contact would hit from half a body north of the line.
        npc.damage = 0;
        Player player = Main.LocalPlayer;
        if (!BridgeSystem.Instance.Linked || !TryFight(BridgeSystem.Instance.Stamp, out WorldStamp.PlaceState? place) || place == null)
        {
            // Out of the underworld the wall waits where it is and cannot tie the farmer.
            npc.velocity = Vector2.Zero;
            npc.position = npc.oldPosition;
            player.ClearBuff(BuffID.TheTongued);
            player.tongued = false;
            return;
        }

        float farmerX = player.position.X + player.width / 2f;
        float farmerFeetY = player.position.Y + player.height;

        if (npc.type == NPCID.WallofFlesh)
        {
            if (placeKey != place.Key)
            {
                placeKey = place.Key;
                wallY = WallClimb.StartY(place.Layout!.Height);
            }
            float life = npc.lifeMax > 0 ? (float)npc.life / npc.lifeMax : 0f;
            wallY = WallClimb.Advance(wallY, WallClimb.Speed(life));
            (_, float feetMapY) = WorldProjection.ToMap(place.OriginX, place.OriginY, farmerX, farmerFeetY);
            if (WallClimb.Touches(wallY, feetMapY) && !player.immune)
                player.Hurt(PlayerDeathReason.ByCustomReason($"{player.name} was swallowed by the Wall of Flesh."), TouchDamage, 0);
        }

        // Vanilla ties the farmer with The Tongued when they leave the band it scanned above and below the
        // wall. The band is the whole floor here, and the wall's own pull never applies.
        Main.wofDrawAreaTop = (int)place.ToWorld(0f, 0f).Y;
        Main.wofDrawAreaBottom = (int)place.ToWorld(0f, place.Layout!.Height * 64f).Y;
        player.ClearBuff(BuffID.TheTongued);
        player.tongued = false;

        // The place key may have just changed under the eyes, so both kinds use the same wall line.
        float lineY = place.ToWorld(0f, wallY).Y;
        npc.velocity = Vector2.Zero;
        if (npc.type == NPCID.WallofFlesh)
        {
            npc.position = new Vector2(farmerX - npc.width / 2f, lineY - npc.height / 2f);
            return;
        }

        float side = EyesBefore(npc) == 0 ? -1f : 1f;
        npc.position = new Vector2(farmerX + side * EyeOffset - npc.width / 2f, lineY - npc.height / 2f);
    }

    /// <summary>Eyes with a lower index are the left one. Two eyes sit on opposite sides of the mouth.</summary>
    private static int EyesBefore(NPC eye)
    {
        int count = 0;
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC other = Main.npc[i];
            if (other.active && other.type == NPCID.WallofFleshEye && other.whoAmI < eye.whoAmI)
                count++;
        }
        return count;
    }

    /// <summary>The farmer's current place, when a wall fight can happen there: an underworld place with a layout.</summary>
    private static bool TryFight(WorldStamp stamp, out WorldStamp.PlaceState? place)
    {
        place = stamp.Current;
        return place?.Layout != null && place.Layout.Biome == (byte)Biome.Underworld;
    }

    /// <summary>
    /// Sends the wall to Stardew about every <see cref="SendEvery"/> ticks while a wall is alive in the farmer's
    /// underworld place, and once with Active false when it is gone. Call it from the bridge tick.
    /// </summary>
    public static void Tick(WorldStamp stamp, Link link)
    {
        NPC? mouth = FindMouth();
        WorldStamp.PlaceState? place = null;
        bool fight = BridgeSystem.Active && mouth != null && TryFight(stamp, out place) && place != null;

        if (!fight)
        {
            if (announced)
            {
                link.Send(new WallFight { Active = false, PlaceKey = announcedPlace });
                announced = false;
                sinceSent = 0;
            }
            return;
        }

        if (announced && ++sinceSent < SendEvery)
            return;
        sinceSent = 0;
        announced = true;
        announcedPlace = place!.Key;
        float life = mouth!.lifeMax > 0 ? (float)mouth.life / mouth.lifeMax : 0f;
        link.Send(new WallFight
        {
            Active = true,
            PlaceKey = place.Key,
            WallY = (int)MathF.Round(wallY),
            Speed = WallClimb.Speed(life),
            Life = mouth.life,
            LifeMax = mouth.lifeMax,
        });
    }

    private static NPC? FindMouth()
    {
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (npc.active && npc.life > 0 && npc.type == NPCID.WallofFlesh)
                return npc;
        }
        return null;
    }
}
