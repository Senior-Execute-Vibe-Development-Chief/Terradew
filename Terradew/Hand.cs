using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using Terradew.Core;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>
/// The farmer's hand is the selected hotbar slot. A Terraria item in that slot takes the use button and is aimed at
/// the cursor; a Stardew item leaves the use button to Stardew. The hotbar is the first ten slots of the shared backpack.
/// </summary>
internal sealed class Hand
{
    public const int OldManType = 37;

    private readonly IModHelper helper;
    private readonly Stations stations;
    private readonly Backpack backpack;
    /// <summary>The hotbar slot seen on the last update, so a wheel step past the end of the bar can be wrapped.</summary>
    private int lastSlot = -1;

    public Hand(IModHelper helper, Stations stations, Backpack backpack)
    {
        this.helper = helper;
        this.stations = stations;
        this.backpack = backpack;
    }

    /// <summary>True when the selected hotbar slot holds a Terraria item.</summary>
    public bool Active => Context.IsWorldReady && backpack.TerrariaOwns(Slot);

    /// <summary>The selected hotbar slot: Stardew's current tool index, which its number keys and wheel set.</summary>
    public int Slot => Math.Clamp(Game1.player.CurrentToolIndex, 0, 9);

    public bool UseHeld { get; private set; }

    /// <summary>A click shorter than this many ticks is held this long, so Terraria sees it even between its frames.</summary>
    private const int ShortestUse = 4;
    private int heldTicks;
    private bool released;
    /// <summary>The button that started the hold, read straight from the device each tick.</summary>
    private SButton holdButton;

    public void Reset()
    {
        UseHeld = false;
        lastSlot = -1;
    }

    public void OnButtonPressed(ButtonPressedEventArgs e, ModConfig config, Bridge bridge)
    {
        if (!Context.IsPlayerFree)
            return;

        if (e.Button.IsActionButton() && TryTalk(e.Cursor.AbsolutePixels, bridge))
        {
            helper.Input.Suppress(e.Button);
            return;
        }

        // Stardew's - and = keys select slots 11 and 12, which the bar does not have.
        if (e.Button is SButton.OemMinus or SButton.OemPlus)
        {
            helper.Input.Suppress(e.Button);
            return;
        }

        if (Active && e.Button.IsUseToolButton())
        {
            helper.Input.Suppress(e.Button);
            // A station is placed on the ground, never used in Terraria, so the use goes no further.
            if (TryPlaceStation(bridge))
                return;
            UseHeld = true;
            heldTicks = 0;
            released = false;
            holdButton = e.Button;
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

    /// <summary>
    /// The release is read from the device in <see cref="Update"/>: SMAPI can report a suppressed button as released
    /// while it is still held, which ended a hold after one use.
    /// </summary>
    public void OnButtonReleased(ButtonReleasedEventArgs e)
    {
        if (e.Button.IsUseToolButton() && UseHeld && Active)
            helper.Input.Suppress(e.Button);
    }

    /// <summary>Whether a button is really down, ignoring SMAPI's suppression.</summary>
    private static bool IsDown(SButton button)
    {
        switch (button)
        {
            case SButton.MouseLeft:
                return Mouse.GetState().LeftButton == ButtonState.Pressed;
            case SButton.MouseRight:
                return Mouse.GetState().RightButton == ButtonState.Pressed;
            case SButton.MouseMiddle:
                return Mouse.GetState().MiddleButton == ButtonState.Pressed;
        }
        if (button.TryGetKeyboard(out Keys key))
            return Keyboard.GetState().IsKeyDown(key);
        if (button.TryGetController(out Buttons pad))
            return GamePad.GetState(PlayerIndex.One).IsButtonDown(pad);
        return false;
    }

    public void Update()
    {
        // The release of the use button is lost while the game is in the background; never hold it past that.
        if (!Game1.game1.IsActive)
            UseHeld = false;
        WrapToolIndex();
        if (UseHeld && ++heldTicks >= ShortestUse && (released || !IsDown(holdButton)))
        {
            UseHeld = false;
            released = false;
        }
        if (!Active || !Context.IsPlayerFree)
            UseHeld = false;
    }

    /// <summary>
    /// Stardew's wheel moves the tool index through 0-11, but the bar has ten slots. Stepping up from slot 9 wraps to 0;
    /// stepping down from slot 0 (or anything else past the end) comes back to 9.
    /// </summary>
    private void WrapToolIndex()
    {
        Farmer player = Game1.player;
        if (player.CurrentToolIndex is 10 or 11)
            player.CurrentToolIndex = lastSlot == 9 ? 0 : 9;
        lastSlot = Slot;
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
            // A town NPC playing a villager is talked to as that villager.
            if (entity.Has(EntityFlags.Town) && Cast.Plays(entity.NpcType))
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
}
