using Terradew.Core.Events;
using Terraria;

namespace TerradewBridge;

/// <summary>
/// Invasions attack one Stardew place each (see <see cref="InvasionHomes"/>). Terraria normally walks an
/// invasion across its world toward the spawn point; here it stays pinned to the place it is attacking,
/// so invaders only spawn while the farmer is there, and they come in from the map's edges.
/// </summary>
public static class Invasions
{
    private static int seen;

    public static void Reset() => seen = 0;

    public static void Watch(WorldStamp stamp)
    {
        // The Terradew world has no shadow orbs. Beating the evil boss stands in for breaking one,
        // which is what lets the Goblin Army and meteors come.
        if (NPC.downedBoss2)
            WorldGen.shadowOrbSmashed = true;

        int type = Main.invasionType;
        if (type != seen)
        {
            if (type > 0)
                BridgeSystem.Instance.Notice(InvasionHomes.Started(type), 255, 160, 80);
            else if (seen > 0)
                BridgeSystem.Instance.Notice(InvasionHomes.Ended(seen), 120, 255, 120);
            seen = type;
        }
        if (type <= 0)
            return;
        string? home = InvasionHomes.HomeOf(type);
        if (home != null && stamp.TryGet(home, out WorldStamp.PlaceState place) && place.Rect.Width > 0)
            Main.invasionX = place.Rect.X + place.Rect.Width / 2;
    }

    public static bool IsInvader(NPC npc) => NPC.GetNPCInvasionGroup(npc.type) > 0;

    /// <summary>Invaders only belong in the place their invasion is attacking.</summary>
    public static bool Allowed(NPC npc, WorldStamp.PlaceState? current)
    {
        int group = NPC.GetNPCInvasionGroup(npc.type);
        if (group <= 0)
            return true;
        string? home = InvasionHomes.HomeOf(group);
        return home == null || current?.Key == home;
    }
}
