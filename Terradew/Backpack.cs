using System.Diagnostics.CodeAnalysis;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Objects;
using Terradew.Core.Protocol;
using SObject = StardewValley.Object;

namespace Terradew;

/// <summary>
/// The one 50-slot backpack that Stardew and Terraria share. Main slot i holds a real Stardew item or a Terraria item,
/// never both: while Terraria holds slot i, Stardew keeps a placeholder there so its own pickups skip the slot.
/// Terraria fills the slots Stardew holds with blockers, told by the <see cref="SlotMask"/> message.
/// </summary>
internal sealed class Backpack
{
    /// <summary>Main slots. Slots 0-9 are the hotbar.</summary>
    public const int Size = 50;
    public const int Hotbar = 10;
    /// <summary>The Data/Objects key of the placeholder. Its qualified item id is "(O)" plus this.</summary>
    public const string PlaceholderId = "Terradew.TerrariaSlot";

    private const string QualifiedId = "(O)" + PlaceholderId;

    /// <summary>The placeholder's own sprite: empty, since the HUD draws the Terraria item over it. Sprite 0 of Stardew's objects is weeds.</summary>
    private const string BlankTexture = "Mods/Terradew/Blank";
    private const string TypeKey = "Terradew/Type";
    private const string StackKey = "Terradew/Stack";
    private const string NameKey = "Terradew/Name";

    private readonly Bridge bridge;
    private readonly IMonitor monitor;
    /// <summary>The last Terraria state reconciled. A state is reconciled once, when it is first seen.</summary>
    private InventoryState? lastState;
    private int seq;
    private int lastSentSeq;
    /// <summary>The mask last sent on this connection. Null when none was sent yet, so the next one always goes out.</summary>
    private ulong? lastSentMask;

    public Backpack(Bridge bridge, IMonitor monitor)
    {
        this.bridge = bridge;
        this.monitor = monitor;
    }

    /// <summary>True for the placeholder that stands in for a Terraria item.</summary>
    public static bool IsPlaceholder([NotNullWhen(true)] Item? item) => item != null && item.QualifiedItemId == QualifiedId;

    /// <summary>True when Terraria holds this main slot, so Stardew keeps a placeholder there.</summary>
    public bool TerrariaOwns(int slot) => IsPlaceholder(Get(slot));

    /// <summary>The real Stardew item in this main slot, or null for an empty slot or a placeholder.</summary>
    public Item? StardewItem(int slot)
    {
        Item? item = Get(slot);
        return IsPlaceholder(item) ? null : item;
    }

    /// <summary>
    /// The Terraria item shown in this main slot: Terraria's own report when it is connected, else the placeholder's
    /// remembered copy. Default when Terraria holds nothing there.
    /// </summary>
    public ItemStack TerrariaItem(int slot)
    {
        InventoryState? state = bridge.Inventory;
        if (state != null)
            return slot >= 0 && slot < state.Items.Count ? state.Items[slot] : default;
        Item? item = Get(slot);
        return IsPlaceholder(item) ? ReadPlaceholder(item) : default;
    }

    /// <summary>Swaps two main slots, here and in Terraria. The mask follows at once.</summary>
    public void Swap(int a, int b)
    {
        if (a < 0 || b < 0 || a >= Size || b >= Size || a == b || !Context.IsWorldReady)
            return;
        Farmer player = Game1.player;
        (player.Items[a], player.Items[b]) = (player.Items[b], player.Items[a]);
        Send(new InventoryAction { Kind = InventoryKind.Swap, A = a, B = b });
        SendMask(player);
    }

    /// <summary>Sends an inventory action, stamped with the next sequence number. Terraria echoes it back in AckSeq.</summary>
    public void Send(InventoryAction action)
    {
        action.Seq = ++seq;
        if (bridge.Send(action))
            lastSentSeq = action.Seq;
    }

    /// <summary>Trashes the real Stardew item in a main slot. A Terraria placeholder is never removed this way.</summary>
    public void RemoveStardew(int slot)
    {
        if (slot < 0 || slot >= Size || TerrariaOwns(slot) || !Context.IsWorldReady)
            return;
        Game1.player.Items[slot] = null;
        SendMask(Game1.player);
    }

    /// <summary>Called every tick: keeps the backpack at full size, reconciles Terraria's state and sends the mask.</summary>
    public void Update()
    {
        InventoryState? state = bridge.Inventory;
        if (state == null)
            ResetConnection();
        if (!Context.IsWorldReady)
            return;

        Farmer player = Game1.player;
        Grow(player);
        ClearCursor(player);
        // While an action is in flight, a state from before it is stale: wait for one that has applied it.
        if (state != null && !ReferenceEquals(state, lastState) && state.AckSeq >= lastSentSeq)
        {
            lastState = state;
            Reconcile(player, state);
        }
        SendMask(player);
    }

    /// <summary>The Terraria item a placeholder remembers, or default when the item is not a placeholder.</summary>
    internal static ItemStack ReadPlaceholder(Item? item)
    {
        var stack = new ItemStack();
        if (!IsPlaceholder(item))
            return stack;
        if (item.modData.TryGetValue(TypeKey, out string? type) && int.TryParse(type, out int itemType))
            stack.ItemType = itemType;
        if (item.modData.TryGetValue(StackKey, out string? count) && int.TryParse(count, out int number))
            stack.Stack = number;
        if (item.modData.TryGetValue(NameKey, out string? name))
            stack.Name = name ?? "";
        return stack;
    }

    public void OnAssetRequested(AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(BlankTexture))
        {
            e.LoadFrom(() => new Texture2D(Game1.graphics.GraphicsDevice, 16, 16), AssetLoadPriority.Exclusive);
            return;
        }
        if (!e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))
            return;
        e.Edit(asset =>
        {
            IDictionary<string, ObjectData> data = asset.AsDictionary<string, ObjectData>().Data;
            data[PlaceholderId] = new ObjectData
            {
                Name = PlaceholderId,
                DisplayName = "Terraria item",
                Description = "Held by your Terraria character.",
                Type = "Basic",
                Category = 0,
                Price = 0,
                Edibility = -300,
                Texture = BlankTexture,
                SpriteIndex = 0,
                ExcludeFromShippingCollection = true,
                ExcludeFromFishingCollection = true,
                ExcludeFromRandomSale = true,
            };
        });
    }

    /// <summary>Terraria is gone or reconnecting: forget what was sent, so the next connection starts clean.</summary>
    private void ResetConnection()
    {
        lastState = null;
        lastSentMask = null;
        seq = 0;
        lastSentSeq = 0;
    }

    private static Item? Get(int slot)
        => slot >= 0 && slot < Size && slot < Game1.player.Items.Count ? Game1.player.Items[slot] : null;

    private static void Grow(Farmer player)
    {
        if (player.maxItems.Value != Size)
            player.maxItems.Value = Size;
        while (player.Items.Count < Size)
            player.Items.Add(null);
    }

    /// <summary>A placeholder on the cursor cannot be carried: it goes into the first empty main slot, or is discarded.</summary>
    private static void ClearCursor(Farmer player)
    {
        Item? held = player.CursorSlotItem;
        if (!IsPlaceholder(held))
            return;
        player.CursorSlotItem = null;
        for (int i = 0; i < Size; i++)
        {
            if (player.Items[i] == null)
            {
                player.Items[i] = held;
                return;
            }
        }
    }

    /// <summary>Makes the main slots agree with Terraria's state. Terraria wins a slot both games filled at once.</summary>
    private void Reconcile(Farmer player, InventoryState state)
    {
        for (int i = 0; i < Size; i++)
        {
            ItemStack t = i < state.Items.Count ? state.Items[i] : default;
            Item? current = player.Items[i];
            if (t.IsEmpty)
            {
                if (IsPlaceholder(current))
                    player.Items[i] = null;
                continue;
            }
            if (IsPlaceholder(current))
            {
                WriteStack(current, t);
                continue;
            }
            if (current != null)
                MoveAside(player, i, current, state);
            player.Items[i] = Placeholder(t);
        }
    }

    /// <summary>A real Stardew item gives way to Terraria's item: it moves to a free slot, or drops at the farmer's feet.</summary>
    private void MoveAside(Farmer player, int slot, Item item, InventoryState state)
    {
        player.Items[slot] = null;
        for (int j = 0; j < Size; j++)
        {
            if (j != slot && player.Items[j] == null && (j >= state.Items.Count || state.Items[j].IsEmpty))
            {
                player.Items[j] = item;
                return;
            }
        }
        Game1.createItemDebris(item, player.getStandingPosition(), player.FacingDirection);
        Game1.addHUDMessage(new HUDMessage($"Backpack full: {item.DisplayName} dropped", HUDMessage.error_type));
        monitor.Log($"Backpack full, dropped {item.DisplayName} at the farmer.", LogLevel.Trace);
    }

    /// <summary>Sends the mask of real Stardew items, when it differs from the last one sent on this connection.</summary>
    private void SendMask(Farmer player)
    {
        if (!bridge.Linked)
            return;
        ulong mask = 0;
        for (int i = 0; i < Size && i < player.Items.Count; i++)
        {
            Item? item = player.Items[i];
            if (item != null && !IsPlaceholder(item))
                mask |= SlotMask.Bit(i);
        }
        if (lastSentMask == mask)
            return;
        if (bridge.Send(new SlotMask { Owned = mask }))
            lastSentMask = mask;
    }

    private static SObject Placeholder(ItemStack t)
    {
        SObject placeholder = ItemRegistry.Create<SObject>(QualifiedId);
        WriteStack(placeholder, t);
        return placeholder;
    }

    private static void WriteStack(Item placeholder, ItemStack t)
    {
        placeholder.modData[TypeKey] = t.ItemType.ToString();
        placeholder.modData[StackKey] = t.Stack.ToString();
        placeholder.modData[NameKey] = t.Name ?? "";
    }
}
