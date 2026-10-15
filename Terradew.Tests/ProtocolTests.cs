using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Terradew.Core;
using Terradew.Core.Protocol;

internal static class ProtocolTests
{
    public static int Run()
    {
        int failed = 0;
        failed += T.Check("every message survives a roundtrip", () =>
        {
            var pulse = new FarmerPulse
            {
                Tick = 9,
                PlaceKey = "Farm",
                FeetX = 64f,
                FeetY = float.NaN,
                Facing = 2,
                Flags = PulseFlags.Free | PulseFlags.UseHeld,
                Slot = 3,
                AimX = 10f,
                AimY = 20f,
                TimeOfDay = 1350,
                TimeFraction = 0.5f,
                TotalDays = 30,
            };
            var copy = (FarmerPulse)Roundtrip(pulse);
            T.Assert(copy.PlaceKey == "Farm" && copy.FeetX == 64f && copy.FeetY == 0f, "pulse fields, NaN becomes zero");
            T.Assert(copy.Has(PulseFlags.UseHeld) && !copy.Has(PulseFlags.HandActive) && copy.Slot == 3, "pulse flags");

            var layout = new MapLayout { PlaceKey = "mine:upper#1", Width = 3, Height = 2, Biome = 9, Band = 1, Spawns = true, Version = 4, WaterTiles = 12, Blocked = new[] { true, false, false, false, false, true } };
            var layoutCopy = (MapLayout)Roundtrip(layout);
            T.Assert(layoutCopy.IsBlocked(0, 0) && !layoutCopy.IsBlocked(1, 0) && layoutCopy.IsBlocked(2, 1), "blocked bits");
            T.Assert(layoutCopy.IsBlocked(-1, 0) && layoutCopy.IsBlocked(3, 0), "outside the map is blocked");
            T.Assert(layoutCopy.Version == 4 && layoutCopy.WaterTiles == 12 && layoutCopy.Spawns, "layout fields");

            var state = new WorldState { Tick = 5, PlaceKey = "Town", RegionX = 400, RegionY = 300, Life = 80, LifeMax = 100 };
            state.Entities.Add(new EntityState { Id = 4, NpcType = 1, X = 1f, Y = 2f, Width = 32, Height = 24, Flags = EntityFlags.Hostile | EntityFlags.Chasing, Name = "Grün Slime", Scale = 1f });
            state.Projectiles.Add(new ProjectileState { Id = 1, ProjType = 14, X = 5f, Y = 6f, FrameCount = 1, Hostile = true });
            state.Drops.Add(new DropState(2, 71, 30, 7f, 8f));
            state.Buffs.Add(new BuffState(1, 600));
            var stateCopy = (WorldState)Roundtrip(state);
            T.Assert(stateCopy.Entities[0].Name == "Grün Slime" && stateCopy.Entities[0].Has(EntityFlags.Chasing), "entity");
            T.Assert(stateCopy.Projectiles[0].ProjType == 14 && stateCopy.Drops[0].Stack == 30 && stateCopy.Buffs[0].Time == 600, "lists");
            T.Assert(stateCopy.RegionX == 400 && stateCopy.Life == 80, "state fields");

            var inventory = new InventoryState { Coins = 123456, HeadSlot = 2, Male = false, WingsSlot = 7, ShieldSlot = 8, BackSlot = 9, FrontSlot = 10, ShoeSlot = 11 };
            inventory.Items.Add(new ItemStack { ItemType = 3507, Stack = 1, Name = "Copper Shortsword", Damage = 5 });
            var inventoryCopy = (InventoryState)Roundtrip(inventory);
            T.Assert(inventoryCopy.Items[0].Name == "Copper Shortsword" && inventoryCopy.Coins == 123456, "inventory");
            T.Assert(inventoryCopy.HeadSlot == 2 && !inventoryCopy.Male, "armor slots still round-trip");
            T.Assert(inventoryCopy.WingsSlot == 7 && inventoryCopy.ShieldSlot == 8 && inventoryCopy.BackSlot == 9 && inventoryCopy.FrontSlot == 10 && inventoryCopy.ShoeSlot == 11, "accessory slots");
            var dyed = new InventoryState { HeadTint = 0xFF8C8CFF, BodyTint = 0x11223344, LegTint = 1, WingsTint = 2, ShieldTint = 3, BackTint = 4, FrontTint = 5, ShoeTint = 0xFFFFFFFF };
            var dyedCopy = (InventoryState)Roundtrip(dyed);
            T.Assert(dyedCopy.HeadTint == 0xFF8C8CFF && dyedCopy.BodyTint == 0x11223344 && dyedCopy.LegTint == 1, "armor dye tints");
            T.Assert(dyedCopy.WingsTint == 2 && dyedCopy.ShieldTint == 3 && dyedCopy.BackTint == 4 && dyedCopy.FrontTint == 5 && dyedCopy.ShoeTint == 0xFFFFFFFF, "accessory dye tints");
            T.Assert(new InventoryState().HeadTint == DyeTint.None, "undyed by default");

            T.Assert(((InventoryState)Roundtrip(new InventoryState { AckSeq = 41 })).AckSeq == 41, "inventory ack seq");
            var action = (InventoryAction)Roundtrip(new InventoryAction { Kind = InventoryKind.Swap, A = 3, B = 47, Seq = 9 });
            T.Assert(action.Kind == InventoryKind.Swap && action.A == 3 && action.B == 47 && action.Seq == 9, "inventory action seq");
            var mask = (SlotMask)Roundtrip(new SlotMask { Owned = SlotMask.Bit(0) | SlotMask.Bit(49) });
            T.Assert(mask.Has(0) && mask.Has(49) && !mask.Has(1) && !mask.Has(50) && SlotMask.Bit(50) == 0, "slot mask");
            var heal = (Heal)Roundtrip(new Heal { Life = 20, Mana = 5, WellFedSeconds = 300 });
            T.Assert(heal.Life == 20 && heal.Mana == 5 && heal.WellFedSeconds == 300, "heal well fed");

            var recipes = new RecipeList();
            recipes.Recipes.Add(new RecipeEntry { RecipeIndex = 2, Result = new ItemStack { ItemType = 36, Stack = 1 }, Ingredients = new List<ItemStack> { new ItemStack { ItemType = 9, Stack = 10 } }, CanCraft = true });
            var recipesCopy = (RecipeList)Roundtrip(recipes);
            T.Assert(recipesCopy.Recipes[0].Ingredients[0].Stack == 10 && recipesCopy.Recipes[0].CanCraft, "recipes");

            var shop = new ShopStock { NpcId = 3, Name = "Merchant" };
            shop.Entries.Add(new ShopEntry(new ItemStack { ItemType = 8, Stack = 1 }, 50));
            T.Assert(((ShopStock)Roundtrip(shop)).Entries[0].Price == 50, "shop");

            var facts = new WorldFacts { DownedEye = true };
            facts.Residents.Add(new TownResident(22, "Andrew", "TerrariaHouse1", 7));
            T.Assert(((WorldFacts)Roundtrip(facts)).Residents[0].HomePlaceKey == "TerrariaHouse1", "facts");
            T.Assert(((WorldFacts)Roundtrip(facts)).Residents[0].HeadIndex == 7, "facts head index");

            var homes = new Homes();
            homes.Entries.Add(new HomeEntry("Nurse", "Hospital", 3, 4));
            HomeEntry homeCopy = ((Homes)Roundtrip(homes)).Entries[0];
            T.Assert(homeCopy.Npc == "Nurse" && homeCopy.PlaceKey == "Hospital" && homeCopy.TileX == 3 && homeCopy.TileY == 4, "homes");

            T.Assert(((AdjustCoins)Roundtrip(new AdjustCoins { Seq = 7, Delta = -500 })).Delta == -500, "coins");
            T.Assert(((Hello)Roundtrip(new Hello { From = Side.Terraria, FarmId = "123", WorldFarmId = "99" })).WorldFarmId == "99", "hello");
            var chat = (NpcChat)Roundtrip(new NpcChat { NpcId = 4, NpcType = 22, Name = "Guide", Text = "Hi", HasShop = true, HealCost = 75, HeadIndex = 7 });
            T.Assert(chat.NpcType == 22 && chat.Name == "Guide" && chat.HasShop && chat.HealCost == 75 && chat.HeadIndex == 7, "chat");
            T.Assert(((Routes)Roundtrip(new Routes { PlaceKey = "Farm", Steps = new List<RouteEntry> { new RouteEntry(1, 2, 3) } })).Steps[0].TileY == 3, "routes");
            var ask = (RequestRecipes)Roundtrip(new RequestRecipes { Near = new StationsNearby { ItemTypes = new List<int> { 36, 35 }, Water = true } });
            T.Assert(ask.Near.ItemTypes.Count == 2 && ask.Near.ItemTypes[1] == 35 && ask.Near.Water, "recipes request");
            var craft = (Craft)Roundtrip(new Craft { RecipeIndex = 4, Times = 2, Near = new StationsNearby { ItemTypes = new List<int> { 36 } } });
            T.Assert(craft.RecipeIndex == 4 && craft.Times == 2 && craft.Near.ItemTypes[0] == 36 && !craft.Near.Water, "craft");
            var place = (PlaceStation)Roundtrip(new PlaceStation { Seq = 3, Slot = 5, ItemType = 36 });
            T.Assert(place.Seq == 3 && place.Slot == 5 && place.ItemType == 36, "place station");
            var placed = (StationPlaced)Roundtrip(new StationPlaced { Seq = 3, ItemType = 36, Ok = true });
            T.Assert(placed.Seq == 3 && placed.ItemType == 36 && placed.Ok, "station placed");
        });

        failed += T.Check("the link carries messages both ways and reconnects", () =>
        {
            int port = 47000 + new Random().Next(1, 900);
            using var terraria = Link.Listen(port);
            var stardew = Link.Connect(port);
            WaitFor(() => terraria.Connected && stardew.Connected, "connect");
            T.Assert(stardew.Send(new Command { Text = "summon" }), "send");
            Message got = WaitForMessage(terraria);
            T.Assert(got is Command c && c.Text == "summon", "terraria got the command");
            terraria.Send(new Notice { Text = "hi" });
            T.Assert(WaitForMessage(stardew) is Notice n && n.Text == "hi", "stardew got the notice");
            int before = terraria.ConnectionCount;
            stardew.Dispose();
            var again = Link.Connect(port);
            WaitFor(() => terraria.ConnectionCount > before && again.Connected, "reconnect");
            again.Send(new Hurt { Amount = 3 });
            T.Assert(WaitForMessage(terraria) is Hurt h && h.Amount == 3, "after reconnect");
            again.Dispose();
        });

        failed += T.Check("a disconnected link drops sends instead of blocking", () =>
        {
            using var lonely = Link.Connect(1);
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < 10000; i++)
                lonely.Send(new Hurt { Amount = i });
            T.Assert(timer.ElapsedMilliseconds < 1000 && !lonely.Connected, "no blocking");
        });
        return failed;
    }

    private static Message Roundtrip(Message message)
    {
        byte[] frame = Wire.Encode(message);
        int length = BitConverter.ToInt32(frame, 0);
        T.Assert(length == frame.Length - 4, "length prefix");
        byte[] payload = frame[4..];
        return Wire.Decode(payload, payload.Length);
    }

    private static void WaitFor(Func<bool> condition, string what)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.ElapsedMilliseconds > 5000)
                throw new Exception("timed out: " + what);
            Thread.Sleep(10);
        }
    }

    private static Message WaitForMessage(Link link)
    {
        Message? got = null;
        WaitFor(() => link.TryReceive(out got!), "message");
        return got!;
    }
}
