using System;
using Microsoft.Xna.Framework;
using Terradew.Core.Protocol;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerradewBridge;

/// <summary>
/// Tells Stardew how much light the Terraria character gives off from its buffs and gear, so the farmer glows the
/// same way. Terraria lights the character from these sources every tick, so the numbers are read from the player.
/// </summary>
/// <remarks>
/// Strengths are 0 to 255 and are the largest of the active sources, not a sum: two glows do not make a brighter one.
/// The table is from memory of vanilla, not measured. Unsure: the wing and necklace strengths and colours, and the
/// helmet and Shine strengths. Light pets use Main.lightPet, so any modded light pet counts too.
/// </remarks>
internal static class Glow
{
    /// <summary>An unchanged report is still resent this often.</summary>
    private const int ResendTicks = 60;

    /// <summary>A light pet (Fairy, Wisp in a Bottle, Shadow Orb, Magic Lantern and others with Main.lightPet set).</summary>
    private const int LightPetLight = 140;
    /// <summary>The Shine potion's glow around the character.</summary>
    private const int ShineLight = 110;
    /// <summary>The Mining Helmet's lamp.</summary>
    private const int MiningHelmetLight = 120;
    /// <summary>The Ultrabright Helmet, the brightest helmet lamp.</summary>
    private const int UltraBrightHelmetLight = 200;
    /// <summary>Fairy Wings and Tattered Fairy Wings, which shine while flying.</summary>
    private const int FairyWingsLight = 90;
    /// <summary>The Jellyfish Necklace and Jellyfish Diving Gear, which glow underwater.</summary>
    private const int JellyfishLight = 60;

    private static readonly Color Warm = new(255, 230, 180);
    private static readonly Color White = new(255, 255, 255);
    private static readonly Color Fairy = new(255, 190, 230);
    private static readonly Color Water = new(120, 190, 255);

    /// <summary>How many accessory and armor slots are checked for glowing gear. Vanity slots are not checked.</summary>
    private const int EquipSlots = 10;

    private static PlayerGlow? last;
    private static int lastSent = int.MinValue / 2;

    public static void Publish(Link link, Player player, int tick)
    {
        if (player == null || !player.active)
            return;
        PlayerGlow now = Read(player);
        bool same = last != null && last.Light == now.Light && last.Color == now.Color;
        if (same && tick - lastSent < ResendTicks)
            return;
        last = now;
        lastSent = tick;
        link.Send(now);
    }

    private static PlayerGlow Read(Player player)
    {
        int light = 0;
        Color colour = default;
        void Offer(int strength, Color shade)
        {
            if (strength <= light)
                return;
            light = strength;
            colour = shade;
        }

        for (int i = 0; i < player.buffType.Length; i++)
        {
            int buff = player.buffType[i];
            if (buff > 0 && player.buffTime[i] > 0 && buff < Main.lightPet.Length && Main.lightPet[buff])
                Offer(LightPetLight, Warm);
        }
        if (player.HasBuff(BuffID.Shine))
            Offer(ShineLight, Warm);

        if (player.head == ArmorIDs.Head.MiningHelmet)
            Offer(MiningHelmetLight, Warm);
        if (player.head == ArmorIDs.Head.UltraBrightHelmet)
            Offer(UltraBrightHelmetLight, White);

        for (int i = 0; i < EquipSlots; i++)
        {
            int type = player.armor[i].type;
            if (type == ItemID.FairyWings || type == ItemID.TatteredFairyWings)
                Offer(FairyWingsLight, Fairy);
            if (player.wet && (type == ItemID.JellyfishNecklace || type == ItemID.JellyfishDivingGear))
                Offer(JellyfishLight, Water);
        }

        return new PlayerGlow
        {
            Light = (byte)Math.Clamp(light, 0, 255),
            Color = light > 0 ? colour.PackedValue : 0u,
        };
    }
}
