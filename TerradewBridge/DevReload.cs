using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;

namespace TerradewBridge;

/// <summary>
/// Dev loop helper. When TerradewBridge.tmod changes on disk (a command-line build), it saves the
/// single-player world, reloads the mods, and walks back into the same world with the same character,
/// so the game never has to be closed. Only acts in single player, in a running world.
/// </summary>
public sealed class DevReload : ModSystem
{
    private const string MarkerName = "TerradewReload.txt";
    private const string ModFileName = "TerradewBridge.tmod";
    private const int PollTicks = 60;
    private const int StablePolls = 2;

    private string tmodPath = "";
    private DateTime seenWriteUtc;
    private long seenLength = -1;
    private DateTime pendingWriteUtc;
    private long pendingLength = -1;
    private bool hasPending;
    private int stablePolls;
    private int ticks;
    private bool reloading;

    public override void Load()
    {
        try
        {
            tmodPath = Path.Combine(Terraria.ModLoader.ModLoader.ModPath, ModFileName);
            TryStamp(tmodPath, out seenWriteUtc, out seenLength);
            Mod.Logger.Info($"Terradew dev reload armed; watching {tmodPath}");
            Resume();
        }
        catch (Exception error)
        {
            Mod.Logger.Warn($"Terradew dev reload did not start: {error.Message}");
        }
    }

    public override void PostUpdateEverything()
    {
        if (reloading || Main.gameMenu || Main.netMode != NetmodeID.SinglePlayer)
            return;
        if (++ticks % PollTicks != 0)
            return;
        if (NewBuildIsStable())
            Reload();
    }

    /// <summary>True once the file has changed since the mod loaded and has held still for <see cref="StablePolls"/> polls.</summary>
    private bool NewBuildIsStable()
    {
        if (!TryStamp(tmodPath, out DateTime writeUtc, out long length))
            return false;
        if (writeUtc == seenWriteUtc && length == seenLength)
        {
            hasPending = false;
            return false;
        }
        if (!hasPending || writeUtc != pendingWriteUtc || length != pendingLength)
        {
            // The build may still be writing: start counting from this new reading.
            hasPending = true;
            pendingWriteUtc = writeUtc;
            pendingLength = length;
            stablePolls = 0;
            return false;
        }
        return ++stablePolls >= StablePolls;
    }

    private void Reload()
    {
        reloading = true;
        string marker = Path.Combine(Main.SavePath, MarkerName);
        try
        {
            Mod.Logger.Info("Terradew sees a new build; saving and reloading");
            File.WriteAllText(marker, Main.LocalPlayer.name + "\n" + Main.worldName + "\n");
            log4net.ILog log = Mod.Logger;
            WorldGen.SaveAndQuit(() => RequestModReload(log));
        }
        catch (Exception error)
        {
            reloading = false;
            TryDelete(marker);
            Mod.Logger.Warn($"Terradew dev reload failed: {error.Message}");
        }
    }

    /// <summary>The SaveAndQuit callback. Asks tModLoader to load the mods again (ModLoader.Reload).</summary>
    private static void RequestModReload(log4net.ILog log)
    {
        try
        {
            MethodInfo? reload = typeof(Terraria.ModLoader.ModLoader).GetMethod("Reload", BindingFlags.NonPublic | BindingFlags.Static);
            if (reload == null)
                throw new MissingMethodException("ModLoader.Reload");
            reload.Invoke(null, null);
        }
        catch (Exception error)
        {
            log.Warn($"Terradew dev reload could not reload the mods: {error.InnerException?.Message ?? error.Message}");
        }
    }

    /// <summary>In the freshly loaded mod: if a reload marker exists, queue re-entry into that world and character.</summary>
    private void Resume()
    {
        string marker = Path.Combine(Main.SavePath, MarkerName);
        if (!File.Exists(marker))
            return;
        string[] lines = File.ReadAllLines(marker);
        TryDelete(marker);
        string playerName = lines.Length > 0 ? lines[0] : "";
        string worldName = lines.Length > 1 ? lines[1] : "";
        if (playerName.Length == 0 || worldName.Length == 0)
        {
            Mod.Logger.Warn("Terradew dev reload marker was incomplete; staying in the menu.");
            return;
        }

        FieldInfo? field = typeof(Terraria.ModLoader.ModLoader).GetField("OnSuccessfulLoad", BindingFlags.NonPublic | BindingFlags.Static);
        if (field == null)
        {
            Mod.Logger.Warn("Terradew dev reload could not find ModLoader.OnSuccessfulLoad; staying in the menu.");
            return;
        }
        log4net.ILog log = Mod.Logger;
        Action enter = () => EnterWorld(log, playerName, worldName);
        var existing = (Action?)field.GetValue(null);
        field.SetValue(null, (Action?)Delegate.Combine(existing, enter));
        Mod.Logger.Info($"Terradew dev reload is returning to world \"{worldName}\" as {playerName}.");
    }

    /// <summary>The same steps as the -skipselect launch option, by name. Leaves the menu alone when a name is missing.</summary>
    private static void EnterWorld(log4net.ILog log, string playerName, string worldName)
    {
        try
        {
            Call(null, typeof(WorldGen), "clearWorld");
            Call(null, typeof(Main), "LoadWorlds");
            WorldFileData? world = Main.WorldList.FirstOrDefault(w => w.Name == worldName);
            if (world == null)
            {
                log.Warn($"Terradew dev reload could not find world \"{worldName}\"; staying in the menu.");
                return;
            }
            Call(world, typeof(WorldFileData), "SetAsActive");

            Call(null, typeof(Main), "LoadPlayers");
            object? playerList = typeof(Main).GetField("PlayerList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
                ?? typeof(Main).GetProperty("PlayerList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            PlayerFileData? player = (playerList as IEnumerable<PlayerFileData>)?.FirstOrDefault(p => p.Name == playerName);
            if (player == null)
            {
                log.Warn($"Terradew dev reload could not find character {playerName}; staying in the menu.");
                return;
            }
            Call(player, typeof(PlayerFileData), "SetAsActive");

            Call(null, typeof(WorldGen), "playWorld");
        }
        catch (Exception error)
        {
            log.Warn($"Terradew dev reload could not re-enter the world: {error.InnerException?.Message ?? error.Message}");
        }
    }

    private static object? Call(object? target, Type type, string name)
    {
        MethodInfo method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            ?? throw new MissingMethodException(type.FullName, name);
        return method.Invoke(target, null);
    }

    private static bool TryStamp(string path, out DateTime writeUtc, out long length)
    {
        writeUtc = default;
        length = -1;
        try
        {
            if (!File.Exists(path))
                return false;
            var info = new FileInfo(path);
            writeUtc = info.LastWriteTimeUtc;
            length = info.Length;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
