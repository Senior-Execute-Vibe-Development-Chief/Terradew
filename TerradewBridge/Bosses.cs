using Terradew.Core.World;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerradewBridge;

/// <summary>
/// Summons that need a place the Stardew map cannot hold. The Guide Voodoo Doll is thrown in the
/// underworld mine floors instead of into lava, and a Lihzahrd Power Cell wakes Golem in the temple floors.
/// Every other summon item works as normal Terraria, because each place is stamped as its biome.
/// </summary>
public sealed class Bosses : GlobalItem
{
    public override void SetDefaults(Item item)
    {
        if (item.type != ItemID.GuideVoodooDoll && item.type != ItemID.LihzahrdPowerCell)
            return;
        item.useStyle = ItemUseStyleID.HoldUp;
        item.useTime = 45;
        item.useAnimation = 45;
        item.consumable = true;
    }

    public override bool CanUseItem(Item item, Player player)
    {
        if (!BridgeSystem.Active)
            return true;
        Biome? biome = CurrentBiome();
        if (item.type == ItemID.GuideVoodooDoll)
            return biome == Biome.Underworld && !NPC.AnyNPCs(NPCID.WallofFlesh);
        if (item.type == ItemID.LihzahrdPowerCell)
            return biome == Biome.Temple && NPC.downedPlantBoss && !NPC.AnyNPCs(NPCID.Golem);
        return true;
    }

    public override bool? UseItem(Item item, Player player)
    {
        if (!BridgeSystem.Active || player.whoAmI != Main.myPlayer)
            return null;
        if (item.type == ItemID.GuideVoodooDoll)
        {
            NPC.SpawnWOF(player.Center);
            return true;
        }
        if (item.type == ItemID.LihzahrdPowerCell)
        {
            NPC.SpawnOnPlayer(player.whoAmI, NPCID.Golem);
            return true;
        }
        return null;
    }

    private static Biome? CurrentBiome()
    {
        WorldStamp.PlaceState? place = BridgeSystem.Instance.Stamp.Current;
        return place?.Layout == null ? null : (Biome)place.Layout.Biome;
    }
}

/// <summary>Bosses that Terraria hides in the world instead of behind an item.</summary>
public static class Lairs
{
    /// <summary>
    /// Plantera's Bulb grows in the underground jungle once the three mechanical bosses are down.
    /// The jungle mine floors stand in for it: each fresh floor has a one in eight chance to wake Plantera.
    /// </summary>
    public static void OnNewFloor(WorldStamp.PlaceState place)
    {
        if (place.Layout == null || place.Layout.Biome != (byte)Biome.Jungle)
            return;
        if (!Main.hardMode || !NPC.downedMechBoss1 || !NPC.downedMechBoss2 || !NPC.downedMechBoss3 || NPC.AnyNPCs(NPCID.Plantera))
            return;
        if (Main.rand.Next(8) != 0)
            return;
        NPC.SpawnOnPlayer(Main.myPlayer, NPCID.Plantera);
    }
}
