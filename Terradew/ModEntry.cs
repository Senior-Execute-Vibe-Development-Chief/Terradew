using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>
/// The Stardew half of Terradew. Stardew is the world you see and farm in; a Terraria world named
/// "Stardew Valley", running in tModLoader with Terradew Bridge, simulates the creatures, combat, items,
/// coins and town NPCs underneath it. The two talk over a localhost socket.
/// </summary>
public sealed class ModEntry : Mod
{
    private ModConfig config = new();
    private Bridge bridge = null!;
    private SpriteBook sprites = null!;
    private Hand hand = null!;
    private Maps maps = null!;
    private Money money = null!;
    private Vitals vitals = null!;
    private MobRoutes routes = null!;
    private Fishing fishing = null!;
    private Mining mining = null!;
    private WallClimb wallClimb = null!;
    private Bundles bundles = null!;
    private Houses houses = null!;
    private Draw draw = null!;
    private Hud hud = null!;
    private bool helloSent;
    private int tick;
    private const int MaxPendingChats = 8;
    private readonly Queue<NpcChat> pendingChats = new();

    public override void Entry(IModHelper helper)
    {
        config = helper.ReadConfig<ModConfig>();
        bridge = new Bridge(config.Port);
        sprites = new SpriteBook();
        hand = new Hand(helper);
        maps = new Maps();
        money = new Money(helper);
        vitals = new Vitals(Monitor);
        routes = new MobRoutes();
        fishing = new Fishing(helper);
        mining = new Mining();
        wallClimb = new WallClimb(bridge);
        houses = new Houses();
        bundles = new Bundles(Monitor);
        draw = new Draw(bridge, sprites, hand, helper, Monitor);
        hud = new Hud(bridge, sprites, hand, helper);
        var harmony = new Harmony(ModManifest.UniqueID);
        draw.Install(harmony, Monitor);
        mining.Install(harmony, Monitor);
        wallClimb.Install(harmony, Monitor);
        new Clock().Install(harmony, Monitor);

        bridge.Connected += OnConnected;
        bridge.Chat += chat =>
        {
            if (!Context.IsWorldReady)
                return;
            // A reply to the open chat box replaces it; otherwise the chat waits its turn.
            if ((Context.IsPlayerFree && pendingChats.Count == 0) || Game1.activeClickableMenu is ChatMenu)
            {
                Game1.activeClickableMenu = new ChatMenu(bridge, sprites, chat);
                return;
            }
            // A few chats are kept for when the farmer is free; older ones give way past that.
            pendingChats.Enqueue(chat);
            while (pendingChats.Count > MaxPendingChats)
                pendingChats.Dequeue();
        };
        bridge.Died += died => vitals.OnDied(died, bridge);
        bridge.CoinAck += ack => money.Ack(ack);
        bridge.Notice += notice => hud.Notice(notice.Text, new Color(notice.R, notice.G, notice.B));

        helper.Events.GameLoop.SaveLoaded += (_, _) => StartFarm();
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => StopFarm();
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.GameLoop.Saving += OnSaving;
        helper.Events.GameLoop.DayStarted += (_, _) => OnDayStarted();
        helper.Events.Display.RenderedWorld += (_, e) => draw.OnRenderedWorld(e.SpriteBatch);
        helper.Events.Display.RenderingHud += (_, _) => hud.BeforeHud();
        helper.Events.Display.RenderedHud += (_, e) => hud.Draw(e.SpriteBatch);
        helper.Events.Display.MenuChanged += (_, e) => fishing.OnMenuChanged(e.NewMenu, bridge, maps);
        helper.Events.Display.MenuChanged += (_, e) => wallClimb.OnMenuChanged(e.NewMenu);
        helper.Events.Player.Warped += (_, e) => wallClimb.OnWarped(e);
        helper.Events.Input.ButtonPressed += OnButtonPressed;
        helper.Events.Input.ButtonReleased += (_, e) => hand.OnButtonReleased(e);
        helper.Events.Input.MouseWheelScrolled += (_, e) => hand.OnWheel(e);
        helper.Events.Player.InventoryChanged += OnInventoryChanged;
        helper.Events.Content.AssetRequested += (_, e) => houses.OnAssetRequested(e);
        helper.Events.Content.AssetRequested += (_, e) => bundles.OnAssetRequested(e);
        helper.Events.World.ObjectListChanged += (_, e) => { if (Leading) mining.OnObjectListChanged(e, bridge); };
        helper.Events.World.BuildingListChanged += (_, _) => { if (Context.IsWorldReady) houses.Publish(bridge); };
        helper.ConsoleCommands.Add("terradew", "terradew status | terradew <command for Terraria>, such as: summon, boss EyeofCthulhu, give CopperShortsword", OnCommand);
        Monitor.Log($"Terradew will connect to Terraria on 127.0.0.1:{config.Port}.", LogLevel.Info);
    }

    private static bool Leading => Context.IsWorldReady && Context.IsMainPlayer && Context.ScreenId == 0;

    private void StartFarm()
    {
        money.Load();
        maps.Reset();
        vitals.Reset();
        routes.Reset();
        hand.Reset();
        wallClimb.Reset();
        mining.Reset();
        helloSent = false;
    }

    private void StopFarm()
    {
        maps.Reset();
        routes.Reset();
        hand.Reset();
        fishing.Reset();
        wallClimb.Reset();
        pendingChats.Clear();
        mining.Reset();
        Clock.OnDayStarted();
        helloSent = false;
    }

    private void OnConnected()
    {
        helloSent = false;
        maps.Reset();
        routes.Reset();
    }

    private void SendHello()
    {
        helloSent = bridge.Send(new Hello
        {
            From = Side.Stardew,
            FarmId = Game1.uniqueIDForThisGame.ToString(),
            TotalDays = Game1.Date.TotalDays,
            CoinSeq = money.LastAck,
        });
        if (!helloSent)
            return;
        money.Resend(bridge);
        houses.Publish(bridge);
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Leading)
        {
            // Off the farm (title screen, loading), stale chat, deaths and acks are dropped. Other
            // split-screen players must not drain the queue meant for the main screen.
            if (!Context.IsWorldReady)
                bridge.DropTransient();
            return;
        }
        tick++;
        bridge.Pump();
        if (pendingChats.Count > 0 && Context.IsPlayerFree)
            Game1.activeClickableMenu = new ChatMenu(bridge, sprites, pendingChats.Dequeue());
        if (bridge.IsConnected && !helloSent)
            SendHello();
        Farmer player = Game1.player;
        GameLocation? location = player.currentLocation;
        if (location == null)
            return;
        hand.Update();
        if (bridge.IsConnected)
            maps.Update(bridge, location, e.Ticks, force: false);
        SendPulse(player, location);
        if (config.MirrorMoney)
            money.Update(bridge);
        vitals.Update(bridge, config);
        routes.Update(bridge, maps, player, Maps.PlaceOf(location).Key);
    }

    private void SendPulse(Farmer player, GameLocation location)
    {
        if (!bridge.IsConnected)
            return;
        Rectangle feet = player.GetBoundingBox();
        Vector2 aim = Helper.Input.GetCursorPosition().AbsolutePixels;
        var flags = PulseFlags.None;
        if (Context.IsPlayerFree)
            flags |= PulseFlags.Free;
        if (hand.Active)
            flags |= PulseFlags.HandActive;
        if (hand.UseHeld)
            flags |= PulseFlags.UseHeld;
        if (player.swimming.Value)
            flags |= PulseFlags.Swimming;
        if (Game1.game1.IsActive)
            flags |= PulseFlags.Focused;
        bridge.Send(new FarmerPulse
        {
            Tick = tick,
            PlaceKey = Maps.PlaceOf(location).Key,
            FeetX = feet.Center.X,
            FeetY = feet.Bottom,
            Facing = (byte)player.FacingDirection,
            Flags = flags,
            Slot = (byte)hand.Slot,
            AimX = aim.X,
            AimY = aim.Y,
            TimeOfDay = Game1.timeOfDay,
            TimeFraction = Math.Clamp(Game1.gameTimeInterval / 7000f, 0f, 1f),
            TotalDays = Game1.Date.TotalDays,
        });
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        if (!Context.IsMainPlayer)
            return;
        if (config.MirrorMoney)
            money.Update(bridge);
        money.Save();
        bridge.Send(new SaveWorld { FarmId = Game1.uniqueIDForThisGame.ToString(), TotalDays = Game1.Date.TotalDays });
    }

    private void OnDayStarted()
    {
        Clock.OnDayStarted();
        if (!Context.IsMainPlayer)
            return;
        houses.Publish(bridge);
        bridge.Send(new DayStarted { TotalDays = Game1.Date.TotalDays });
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Leading)
            return;
        if (config.OpenMenu.JustPressed() && Context.IsPlayerFree)
        {
            Helper.Input.SuppressActiveKeybinds(config.OpenMenu);
            bridge.Send(new RequestRecipes());
            Game1.activeClickableMenu = new TerrariaMenu(bridge, sprites);
            return;
        }
        hand.OnButtonPressed(e, config, bridge);
    }

    private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
    {
        if (!e.IsLocalPlayer)
            return;
        fishing.OnInventoryChanged(e.Added, e.QuantityChanged, bridge);
        vitals.OnItemsAdded(e.Added);
        if (Leading)
            mining.OnInventoryChanged(e.Added, e.QuantityChanged, bridge);
    }

    private void OnCommand(string name, string[] args)
    {
        if (args.Length == 0 || args[0] == "status")
        {
            string state = bridge.Linked ? $"linked, Terraria place '{bridge.World?.PlaceKey}', {bridge.World?.Entities.Count ?? 0} creatures here"
                : bridge.IsConnected ? "connected, waiting for Terraria frames"
                : $"not connected ({bridge.LastError})";
            Monitor.Log($"Terradew: {state}.", LogLevel.Info);
            return;
        }
        bridge.Send(new Command { Text = string.Join(' ', args) });
    }
}
