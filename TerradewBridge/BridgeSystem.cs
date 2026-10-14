using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terradew.Core;
using Terradew.Core.Protocol;
using Terradew.Core.Time;
using Terradew.Core.World;
using Terraria;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace TerradewBridge;

public sealed class TerradewMod : Mod
{
    public override void Load()
    {
        Logger.Info($"Terradew bridge listens on 127.0.0.1:{Link.DefaultPort} in the world named \"{BridgeSystem.WorldName}\".");
    }
}

/// <summary>
/// The Terraria half of Terradew. It only acts in the world named <see cref="WorldName"/>;
/// every other world plays as normal Terraria.
/// </summary>
public sealed class BridgeSystem : ModSystem
{
    public const string WorldName = "Stardew Valley";

    private Link? link;
    private int seenConnection;
    private bool helloSent;
    private bool started;
    private bool initialized;
    private string worldFarmId = "";
    private int worldDay;
    private string savedLayout = "";
    private string savedHomes = "";
    private string savedResidents = "";
    private bool autoPauseWasOn;
    private bool settingsSaved;
    private int worldTicks;
    private int ticksSeenByUi = -1;
    private long lastTerrariaTickMs;
    private MethodInfo? doUpdateInWorld;
    private bool doUpdateInWorldFailed;

    public static BridgeSystem Instance => ModContent.GetInstance<BridgeSystem>();

    /// <summary>True only in the Terradew world, in single player, once a world is running.</summary>
    public static bool Active => !Main.gameMenu && Main.netMode == NetmodeID.SinglePlayer && Main.worldName == WorldName;

    public FarmerPulse? Pulse { get; private set; }

    public long PulseAtMs { get; private set; }

    public bool Linked => link != null && link.Connected && Pulse != null && Link.NowMs() - PulseAtMs < 3000;

    public bool WrongFarm { get; private set; }

    public int WorldTicks => worldTicks;

    public WorldStamp Stamp { get; } = new();

    public Town Town { get; } = new();

    public override void OnWorldLoad()
    {
        started = false;
        Pulse = null;
        WrongFarm = false;
        worldTicks = 0;
        ticksSeenByUi = -1;
    }

    public override void OnWorldUnload()
    {
        if (settingsSaved)
        {
            Main.autoPause = autoPauseWasOn;
            settingsSaved = false;
        }
        link?.Dispose();
        link = null;
        started = false;
        initialized = false;
        worldFarmId = "";
        worldDay = 0;
        savedLayout = "";
        savedHomes = "";
        savedResidents = "";
        Stamp.Clear();
        Town.Clear();
    }

    public override void SaveWorldData(TagCompound tag)
    {
        if (Main.worldName != WorldName)
            return;
        tag["initialized"] = initialized;
        tag["farmId"] = worldFarmId;
        tag["day"] = worldDay;
        tag["layout"] = started ? Stamp.SaveLayout() : savedLayout;
        tag["homes"] = started ? Town.SaveHomes() : savedHomes;
        tag["residents"] = started ? Town.SaveResidents() : savedResidents;
    }

    public override void LoadWorldData(TagCompound tag)
    {
        initialized = tag.ContainsKey("initialized") && tag.GetBool("initialized");
        worldFarmId = tag.ContainsKey("farmId") ? tag.GetString("farmId") : "";
        worldDay = tag.ContainsKey("day") ? tag.GetInt("day") : 0;
        savedLayout = tag.ContainsKey("layout") ? tag.GetString("layout") : "";
        savedHomes = tag.ContainsKey("homes") ? tag.GetString("homes") : "";
        savedResidents = tag.ContainsKey("residents") ? tag.GetString("residents") : "";
    }

    private void Start()
    {
        started = true;
        if (!initialized)
        {
            BlankWorld();
            initialized = true;
        }
        Stamp.Load(savedLayout, Shape());
        Town.Load(savedHomes, savedResidents);
        autoPauseWasOn = Main.autoPause;
        settingsSaved = true;
        try
        {
            link = Link.Listen();
        }
        catch (Exception error)
        {
            Mod.Logger.Error($"Terradew could not listen on port {Link.DefaultPort}: {error.Message}");
        }
    }

    public static WorldShape Shape()
        => new(Main.maxTilesX, Main.maxTilesY, (int)Main.worldSurface, (int)Main.rockLayer, Main.UnderworldLayer);

    public override void PostUpdateEverything()
    {
        if (!Active)
            return;
        Tick();
    }

    /// <summary>One bridge step. Runs after the world updates, or from the UI when Terraria skipped the world.</summary>
    private void Tick()
    {
        if (!started)
            Start();
        worldTicks++;
        lastTerrariaTickMs = Link.NowMs();
        Main.autoPause = false;
        if (link == null)
            return;

        if (link.ConnectionCount != seenConnection)
        {
            seenConnection = link.ConnectionCount;
            helloSent = false;
            Pulse = null;
            Inventory.ForceResend();
        }
        if (link.Connected && !helloSent)
        {
            helloSent = link.Send(new Hello { From = Side.Terraria, WorldFarmId = worldFarmId, TotalDays = worldDay });
            Town.SendFacts(link);
        }

        while (link.TryReceive(out Message message))
            Handle(message);

        if (WrongFarm || Pulse == null)
            return;

        SyncClock(Pulse);
        Player player = Main.LocalPlayer;
        Stamp.Publish(link, player, Pulse, worldTicks);
        Town.Watch(Stamp.Current);
        Town.KeepOldMan(Stamp);
        Inventory.Publish(link, player, worldTicks);
        if (worldTicks % 120 == 0)
            Town.SendFacts(link);
    }

    private void Handle(Message message)
    {
        Player player = Main.LocalPlayer;
        switch (message)
        {
            case Hello hello:
                if (hello.Protocol != Hello.CurrentProtocol)
                {
                    Notice($"Terradew versions differ (Stardew {hello.Protocol}, Terraria {Hello.CurrentProtocol}). Update both mods.", 255, 80, 80);
                    WrongFarm = true;
                    break;
                }
                Inventory.ResumeCoins(hello.CoinSeq);
                Inventory.ForceResend();
                if (worldFarmId.Length == 0)
                    worldFarmId = hello.FarmId;
                WrongFarm = worldFarmId != hello.FarmId;
                if (WrongFarm)
                    Notice("This Terraria world belongs to a different Stardew farm. Load the matching farm, or make a new world named Stardew Valley.", 255, 80, 80);
                else if (hello.TotalDays < worldDay)
                    Notice("Stardew went back to an older day than this Terraria world remembers. Terraria keeps its progress.", 255, 200, 80);
                break;
            case FarmerPulse pulse:
                if (!WrongFarm)
                {
                    Pulse = pulse;
                    PulseAtMs = Link.NowMs();
                }
                break;
            case MapLayout layout:
                if (!WrongFarm)
                    Stamp.Receive(layout, Shape());
                break;
            case Routes routes:
                Stamp.ReceiveRoutes(routes);
                break;
            case AdjustCoins coins:
                Inventory.AdjustCoins(link!, player, coins);
                break;
            case InventoryAction action:
                Inventory.Act(player, action);
                break;
            case RequestRecipes:
                link!.Send(Crafting.List(player));
                break;
            case Craft craft:
                Crafting.Make(player, craft);
                link!.Send(Crafting.List(player));
                break;
            case NpcInteract interact:
                Shops.Talk(link!, player, interact.NpcId);
                break;
            case Buy buy:
                Shops.Buy(link!, player, buy);
                break;
            case Sell sell:
                Shops.Sell(link!, player, sell);
                break;
            case NurseHeal heal:
                Shops.NurseHeal(link!, player, heal.NpcId);
                break;
            case GiveItem give:
                Inventory.Give(player, give);
                break;
            case Hurt hurt:
                Puppet.HurtFromStardew(player, hurt.Amount);
                break;
            case Heal heal:
                if (heal.Life > 0)
                    player.Heal(heal.Life);
                if (heal.Mana > 0)
                {
                    player.statMana = Math.Min(player.statManaMax2, player.statMana + heal.Mana);
                    player.ManaEffect(heal.Mana);
                }
                break;
            case SaveWorld save:
                worldFarmId = save.FarmId;
                worldDay = save.TotalDays;
                SaveNow();
                break;
            case DayStarted day:
                worldDay = Math.Max(worldDay, day.TotalDays);
                Town.NewDay(link!, player);
                break;
            case Homes homes:
                Town.SetHomes(homes);
                break;
            case Command command:
                Notice(Commands.Run(player, command.Text), 200, 230, 255);
                break;
        }
    }

    public void Notice(string text, byte r = 255, byte g = 255, byte b = 255)
    {
        Main.NewText(text, r, g, b);
        link?.Send(new Notice { Text = text, R = r, G = g, B = b });
    }

    public void Send(Message message) => link?.Send(message);

    private void SaveNow()
    {
        try
        {
            WorldFile.SaveWorld();
            Player.SavePlayer(Main.ActivePlayerFileData);
            Mod.Logger.Info($"Terradew saved the world and character for Stardew day {worldDay}.");
        }
        catch (Exception error)
        {
            Mod.Logger.Error($"Terradew could not save: {error}");
        }
    }

    private static void SyncClock(FarmerPulse pulse)
    {
        if (pulse.TimeOfDay < 600)
            return;
        (bool day, double time) = ClockMap.ToTerraria(pulse.TimeOfDay, pulse.TimeFraction);
        (bool nextDay, double nextTime) = ClockMap.Next(Main.dayTime, Main.time, day, time);
        if (nextDay == Main.dayTime)
            Main.time = nextTime;
        else
            Main.time = ClockMap.PhaseLength(Main.dayTime) - 1;
    }

    private void BlankWorld()
    {
        for (int x = 0; x < Main.maxTilesX; x++)
        {
            for (int y = 0; y < Main.maxTilesY; y++)
                Main.tile[x, y].ClearEverything();
        }
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            if (Main.npc[i].active && !Main.npc[i].townNPC)
                Main.npc[i].active = false;
        }
        Mod.Logger.Info($"Terradew cleared the new world ({Main.maxTilesX} by {Main.maxTilesY}). This happens once per world.");
    }

    /// <summary>
    /// Terraria skips its world update while its window is unfocused, and the farmer plays in Stardew's window.
    /// When that happens the bridge runs the world itself from the UI update.
    /// </summary>
    public override void UpdateUI(GameTime gameTime)
    {
        if (!Active || Main.instance == null)
            return;
        Main.autoPause = false;
        Main.gamePaused = false;
        Main.instance.InactiveSleepTime = TimeSpan.Zero;
        bool skipped = ticksSeenByUi == worldTicks && ticksSeenByUi >= 0 && Link.NowMs() - lastTerrariaTickMs > 12;
        ticksSeenByUi = worldTicks;
        if (!skipped)
            return;
        StepWorld();
        ticksSeenByUi = worldTicks;
    }

    private void StepWorld()
    {
        if (!doUpdateInWorldFailed)
        {
            doUpdateInWorld ??= typeof(Main).GetMethod("DoUpdateInWorld", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (doUpdateInWorld != null)
            {
                try
                {
                    int before = worldTicks;
                    doUpdateInWorld.Invoke(Main.instance, new object[] { new Stopwatch() });
                    if (worldTicks == before)
                        Tick();
                    return;
                }
                catch (Exception error)
                {
                    Mod.Logger.Warn($"Terradew could not run Terraria's world update directly, stepping by hand instead: {error.InnerException?.Message ?? error.Message}");
                }
            }
            doUpdateInWorldFailed = true;
        }

        try
        {
            Main.player[Main.myPlayer].Update(Main.myPlayer);
        }
        catch (Exception error)
        {
            Mod.Logger.Debug($"Terradew background player step: {error.Message}");
        }
        try
        {
            NPC.SpawnNPC();
        }
        catch (Exception)
        {
        }
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            if (!Main.npc[i].active)
                continue;
            try
            {
                Main.npc[i].UpdateNPC(i);
            }
            catch (Exception error)
            {
                Mod.Logger.Debug($"Terradew background NPC step {i}: {error.Message}");
            }
        }
        for (int i = 0; i < Main.maxProjectiles; i++)
        {
            if (!Main.projectile[i].active)
                continue;
            try
            {
                Main.projectile[i].Update(i);
            }
            catch (Exception)
            {
            }
        }
        for (int i = 0; i < Main.maxItems; i++)
        {
            if (!Main.item[i].active)
                continue;
            try
            {
                Main.item[i].UpdateItem(i);
            }
            catch (Exception)
            {
            }
        }
        Tick();
    }
}
