using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using Terradew.Core;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>
/// The farmer's hand is either on the Stardew toolbar (farming) or on the Terraria hotbar (fighting).
/// With the Terraria hand, the use button and number keys go to Terraria, aimed at the cursor.
/// </summary>
internal sealed class Hand
{
    public const int OldManType = 37;

    private readonly IModHelper helper;
    private readonly Stations stations;
    private int lockedTool = -1;

    public Hand(IModHelper helper, Stations stations)
    {
        this.helper = helper;
        this.stations = stations;
    }

    public bool Active { get; private set; }
    public int Slot { get; private set; }
    public bool UseHeld { get; private set; }

    public void Reset()
    {
        Active = false;
        UseHeld = false;
        lockedTool = -1;
    }

    public void SetSlot(int slot) => Slot = Math.Clamp(slot, 0, 9);

    public void OnButtonPressed(ButtonPressedEventArgs e, ModConfig config, Bridge bridge)
    {
        if (!Context.IsPlayerFree)
            return;
        if (config.ToggleHand.JustPressed())
        {
            Active = !Active;
            lockedTool = Game1.player.CurrentToolIndex;
            UseHeld = false;
            helper.Input.SuppressActiveKeybinds(config.ToggleHand);
            Game1.playSound(Active ? "swordswipe" : "toolSwap");
            return;
        }

        if (e.Button.IsActionButton() && TryTalk(e.Cursor.AbsolutePixels, bridge))
        {
            helper.Input.Suppress(e.Button);
            return;
        }

        if (!Active)
            return;
        int number = Number(e.Button);
        if (number >= 0)
        {
            Slot = number;
            helper.Input.Suppress(e.Button);
            return;
        }
        if (e.Button.IsUseToolButton())
        {
            helper.Input.Suppress(e.Button);
            // A station is placed on the ground, never used in Terraria, so the use goes no further.
            if (TryPlaceStation(bridge))
                return;
            UseHeld = true;
        }
    }

    /// <summary>The hotbar item in the current slot is a crafting station: place it instead of using it.</summary>
    private bool TryPlaceStation(Bridge bridge)
    {
        InventoryState? inventory = bridge.Inventory;
        if (inventory == null || Slot >= inventory.Items.Count)
            return false;
        ItemStack held = inventory.Items[Slot];
        return !held.IsEmpty && held.Station && stations.TryPlace(bridge, Slot, held);
    }

    public void OnButtonReleased(ButtonReleasedEventArgs e)
    {
        if (e.Button.IsUseToolButton() && UseHeld)
        {
            UseHeld = false;
            if (Active)
                helper.Input.Suppress(e.Button);
        }
    }

    public void OnWheel(MouseWheelScrolledEventArgs e)
    {
        if (!Active || !Context.IsPlayerFree || e.Delta == 0)
            return;
        Slot = (Slot + (e.Delta > 0 ? 9 : 1)) % 10;
    }

    public void Update()
    {
        // The release of the use button is lost while the game is in the background; never hold it past that.
        if (!Game1.game1.IsActive)
            UseHeld = false;
        if (!Active)
            return;
        if (lockedTool >= 0 && Game1.player.CurrentToolIndex != lockedTool)
            Game1.player.CurrentToolIndex = lockedTool;
        if (!Context.IsPlayerFree)
            UseHeld = false;
    }

    private bool TryTalk(Vector2 cursor, Bridge bridge)
    {
        WorldState? world = bridge.World;
        Farmer player = Game1.player;
        if (world == null || player.currentLocation == null || !bridge.WorldIsFor(Maps.PlaceOf(player.currentLocation).Key))
            return false;
        Rectangle feet = player.GetBoundingBox();
        foreach (EntityState entity in world.Entities)
        {
            if (!entity.Has(EntityFlags.Town) && entity.NpcType != OldManType)
                continue;
            (float x, float y) = WorldProjection.ToMap(world.RegionX, world.RegionY, entity.X, entity.Y);
            SpriteBox box = SpriteLayout.Hitbox(x, y, entity);
            bool pointed = box.Contains(cursor.X, cursor.Y);
            bool near = Vector2.Distance(new Vector2(x, y), new Vector2(feet.Center.X, feet.Bottom)) < 64f * 3f;
            if (pointed && near)
            {
                bridge.Send(new NpcInteract { NpcId = entity.Id });
                return true;
            }
        }
        return false;
    }

    private static int Number(SButton button) => button switch
    {
        SButton.D1 => 0,
        SButton.D2 => 1,
        SButton.D3 => 2,
        SButton.D4 => 3,
        SButton.D5 => 4,
        SButton.D6 => 5,
        SButton.D7 => 6,
        SButton.D8 => 7,
        SButton.D9 => 8,
        SButton.D0 => 9,
        _ => -1,
    };
}
