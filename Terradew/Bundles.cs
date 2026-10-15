using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace Terradew;

/// <summary>
/// Community Center bundles for the mashup. Stardew's own monsters are gone (Terraria creatures replace them),
/// so the monster drops that some bundles ask for can no longer be gathered. Each one is swapped for a
/// mineral the mines still give, in every bundle that asks for it. Quantities stay as they are.
/// </summary>
internal sealed class Bundles
{
    /// <summary>Index of the item list in a Data/Bundles value: "Name/Reward/Items/Color/...".</summary>
    private const int ItemsField = 2;

    /// <summary>Monster drop (bare item id) to the mineral that replaces it.</summary>
    public static readonly IReadOnlyDictionary<string, string> Swaps = new Dictionary<string, string>
    {
        ["766"] = "378", // Slime -> Copper Ore
        ["767"] = "80",  // Bat Wing -> Quartz
        ["768"] = "82",  // Solar Essence -> Fire Quartz
        ["769"] = "66",  // Void Essence -> Amethyst
    };

    private readonly IMonitor monitor;

    public Bundles(IMonitor monitor)
    {
        this.monitor = monitor;
    }

    public void OnAssetRequested(AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo("Data/Bundles"))
            return;
        e.Edit(asset =>
        {
            IDictionary<string, string> bundles = asset.AsDictionary<string, string>().Data;
            foreach (string key in bundles.Keys.ToList())
            {
                string? retuned = Retune(key, bundles[key]);
                if (retuned != null)
                    bundles[key] = retuned;
            }
        });
    }

    /// <summary>Returns the bundle value with its item ids swapped, or null if nothing changed.</summary>
    private string? Retune(string key, string value)
    {
        string[] fields = value.Split('/');
        if (fields.Length <= ItemsField)
        {
            monitor.Log($"Terradew left bundle {key} alone: its value has no item list.", LogLevel.Warn);
            return null;
        }

        // Items are written as groups of three: id, quantity, quality. Anything else is left alone.
        string[] tokens = fields[ItemsField].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length % 3 != 0)
        {
            monitor.Log($"Terradew left bundle {key} alone: its item list is not in id/quantity/quality groups.", LogLevel.Warn);
            return null;
        }

        bool changed = false;
        for (int i = 0; i < tokens.Length; i += 3)
        {
            bool qualified = tokens[i].StartsWith("(O)", StringComparison.Ordinal);
            string bare = qualified ? tokens[i][3..] : tokens[i];
            if (!Swaps.TryGetValue(bare, out string? replacement))
                continue;
            tokens[i] = qualified ? "(O)" + replacement : replacement;
            changed = true;
        }
        if (!changed)
            return null;

        fields[ItemsField] = string.Join(' ', tokens);
        monitor.Log($"Terradew retuned bundle {key} for the mashup.", LogLevel.Trace);
        return string.Join('/', fields);
    }
}
