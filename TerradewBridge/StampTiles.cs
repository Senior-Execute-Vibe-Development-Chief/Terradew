using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace TerradewBridge;

/// <summary>
/// The blocks stamped for the farmer's place stand for Stardew's walls, rocks and trees, so nothing in Terraria may
/// break them: not a bomb, not a pickaxe. Stardew breaks its own things, and the stamp follows.
/// </summary>
public sealed class StampTiles : GlobalTile
{
    public override bool CanExplode(int i, int j, int type) => !InFarmersPlace(i, j);

    public override bool CanKillTile(int i, int j, int type, ref bool blockDamaged) => !InFarmersPlace(i, j);

    /// <summary>True when Terraria is linked to Stardew and the tile lies in the farmer's place.</summary>
    internal static bool InFarmersPlace(int i, int j)
    {
        if (!BridgeSystem.Active || BridgeSystem.Instance == null || !BridgeSystem.Instance.Linked)
            return false;
        WorldStamp.PlaceState? place = BridgeSystem.Instance.Stamp.Current;
        return place != null && place.ContainsWorld(new Vector2(i * 16f + 8f, j * 16f + 8f));
    }
}

/// <summary>The stamped walls of dungeon and temple places stay too.</summary>
public sealed class StampWalls : GlobalWall
{
    public override bool CanExplode(int i, int j, int type) => !StampTiles.InFarmersPlace(i, j);
}
