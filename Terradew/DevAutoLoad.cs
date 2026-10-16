using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace Terradew;

/// <summary>
/// Dev restart: tools/terradew leaves a marker file in SMAPI's data folder before it starts Stardew. When the
/// title screen has stayed up for a moment, this takes the marker and loads the most recently played save, so
/// a test restart does not need a click through the menus.
/// </summary>
internal sealed class DevAutoLoad
{
    private const string MarkerName = "terradew-autoload";

    /// <summary>Ticks the title screen stays up before loading, so the game finishes its own start-up first.</summary>
    private const int TitleTicks = 30;

    private readonly IMonitor monitor;
    private int titleTicks;
    private bool finished;

    public DevAutoLoad(IMonitor monitor)
    {
        this.monitor = monitor;
    }

    /// <summary>Runs every tick, on the title screen or in the world.</summary>
    public void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (finished)
            return;
        try
        {
            if (Game1.activeClickableMenu is not TitleMenu)
            {
                titleTicks = 0;
                return;
            }
            if (++titleTicks < TitleTicks)
                return;

            string marker = Path.Combine(Constants.DataPath, MarkerName);
            finished = true;
            if (!File.Exists(marker))
                return;
            File.Delete(marker);

            string? save = NewestSave();
            if (save == null)
            {
                monitor.Log("Terradew dev restart found no save to load; the title screen stays.", LogLevel.Warn);
                return;
            }
            // As the load menu's save slot does: SaveGame.Load hands the game its loader, which the game steps itself.
            SaveGame.Load(save);
            Game1.exitActiveMenu();
            monitor.Log($"Terradew is loading your last save {save} (dev restart).", LogLevel.Info);
        }
        catch (Exception error)
        {
            finished = true;
            monitor.Log($"Terradew could not load your last save for the dev restart. {error.Message}", LogLevel.Warn);
        }
    }

    /// <summary>The save folder whose main save file was written most recently, or null if there is none.</summary>
    private static string? NewestSave()
    {
        string saves = Constants.SavesPath;
        if (!Directory.Exists(saves))
            return null;
        string? newest = null;
        DateTime newestWritten = DateTime.MinValue;
        foreach (string folder in Directory.GetDirectories(saves))
        {
            // A save folder's main file has the same name as the folder.
            string name = Path.GetFileName(folder);
            string main = Path.Combine(folder, name);
            if (!File.Exists(main))
                continue;
            DateTime written = File.GetLastWriteTimeUtc(main);
            if (written > newestWritten)
            {
                newestWritten = written;
                newest = name;
            }
        }
        return newest;
    }
}
