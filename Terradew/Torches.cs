using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using Terradew.Core;
using Terradew.Core.Protocol;
using SObject = StardewValley.Object;

namespace Terradew;

/// <summary>
/// Terraria torches. A torch in the Terraria hand lights the farmer, and a torch put down on the map is a vanilla
/// Stardew torch that remembers its Terraria item type. When the farmer picks that torch up, Terraria gets the item back.
/// </summary>
internal sealed class Torches
{
    /// <summary>The mod data key that holds the Terraria item type of a torch placed on the map.</summary>
    public const string TypeKey = "Terradew/TerrariaItem";
    private const string LightId = "Terradew_HeldLight";
    /// <summary>A torch's warm flame colour (255, 195, 115), inverted: Stardew light colours subtract.</summary>
    private static readonly Color LightColour = new(0, 60, 140);
    /// <summary>Ticks between looks for placed torches. A pick-up is noticed within this many ticks.</summary>
    private const int ScanEvery = 15;

    private readonly Bridge bridge;
    private readonly Hand hand;
    private readonly Backpack backpack;
    /// <summary>The location the held light was made in, and the light strength it was made with.</summary>
    private string heldPlace = "";
    private int heldLight;
    /// <summary>The location the placed torches below are tracked in, and the Terraria item type of each torch on its tile.</summary>
    private string place = "";
    private Dictionary<Vector2, int> placed = new();
    private int ticksSinceScan;
    private const string VanillaTorch = "(O)93";

    public Torches(IModHelper helper, Bridge bridge, Hand hand, Backpack backpack)
    {
        this.bridge = bridge;
        this.hand = hand;
        this.backpack = backpack;
        helper.Events.GameLoop.UpdateTicked += (_, _) => Update();
    }

    /// <summary>Puts a vanilla Stardew torch on a tile for a Terraria torch, remembering the Terraria item type.</summary>
    public static void Place(GameLocation location, Vector2 tile, int itemType)
    {
        var torch = new Torch(1, "93");
        torch.modData[TypeKey] = itemType.ToString();
        torch.TileLocation = tile;
        location.objects.Add(tile, torch);
        torch.initializeLightSource(tile);
        Game1.playSound("woodyStep");
    }

    private void Update()
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer || Context.ScreenId != 0)
        {
            RemoveHeldLight();
            return;
        }
        ItemStack held = backpack.TerrariaOwns(hand.Slot) ? backpack.TerrariaItem(hand.Slot) : default;
        UpdateHeldLight(held.Light);
        UpdatePlaced();
    }

    /// <summary>Keeps one light at the farmer while a light item is in the Terraria hand. It is remade on a location change.</summary>
    private void UpdateHeldLight(int light)
    {
        Farmer player = Game1.player;
        GameLocation? here = player.currentLocation;
        if (light <= 0 || here == null)
        {
            RemoveHeldLight();
            return;
        }
        string name = here.NameOrUniqueName;
        if (!Game1.currentLightSources.ContainsKey(LightId) || heldPlace != name || heldLight != light)
        {
            RemoveHeldLight();
            float radius = light / 200f * 2.5f;
            Game1.currentLightSources[LightId] = new LightSource(LightId, LightSource.lantern, player.getStandingPosition(), radius, LightColour, LightSource.LightContext.None, player.UniqueMultiplayerID);
            heldPlace = name;
            heldLight = light;
        }
        Game1.currentLightSources[LightId].position.Value = player.getStandingPosition();
    }

    private void RemoveHeldLight()
    {
        Game1.currentLightSources.Remove(LightId);
        heldPlace = "";
        heldLight = 0;
    }

    /// <summary>
    /// Finds the torches placed on the map here. A torch that was on the tile last time and is gone now was picked up,
    /// so Terraria gets its item back. A torch that Terraria does not take yet stays on the list for the next tick.
    /// </summary>
    private void UpdatePlaced()
    {
        GameLocation? here = Game1.currentLocation;
        if (here == null)
            return;
        string name = here.NameOrUniqueName;
        if (name != place)
        {
            place = name;
            placed = Scan(here);
            ticksSinceScan = 0;
            return;
        }
        if (++ticksSinceScan < ScanEvery)
            return;
        ticksSinceScan = 0;
        Dictionary<Vector2, int> now = Scan(here);
        foreach (KeyValuePair<Vector2, int> gone in placed)
        {
            if (now.ContainsKey(gone.Key))
                continue;
            if (!bridge.Send(new GiveItem { ItemType = gone.Value, Stack = 1 }))
                now[gone.Key] = gone.Value;
            else
                Game1.player.Items.ReduceId(VanillaTorch, 1); // Stardew's pick-up handed over its own torch as well
        }
        placed = now;
    }

    private static Dictionary<Vector2, int> Scan(GameLocation location)
    {
        var found = new Dictionary<Vector2, int>();
        foreach (KeyValuePair<Vector2, SObject> pair in location.objects.Pairs)
        {
            if (pair.Value.modData.TryGetValue(TypeKey, out string? text) && int.TryParse(text, out int type))
                found[pair.Key] = type;
        }
        return found;
    }
}
