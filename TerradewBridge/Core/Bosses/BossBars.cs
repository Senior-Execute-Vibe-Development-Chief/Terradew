using System;
using System.Collections.Generic;
using Terradew.Core.Protocol;

namespace Terradew.Core.Bosses;

/// <summary>
/// The boss bars for the farmer's place. A boss with parts (Eater of Worlds segments, Skeletron's hands) is one bar:
/// its parts are the creatures whose <see cref="EntityState.RealLife"/> names the boss's head, and their life is summed.
/// Each other boss, such as each Twin, is its own bar. Bars keep the order Terraria sent the bosses in.
/// </summary>
public static class BossBars
{
    /// <summary>The most bars shown at once. Further bosses wait until one of these is beaten.</summary>
    public const int MaxBars = 3;

    /// <summary>One bar: the boss's name, and its life summed over all of its parts.</summary>
    public readonly record struct Bar(string Name, int Life, int LifeMax);

    /// <summary>The bars for these creatures, at most <see cref="MaxBars"/>.</summary>
    public static List<Bar> Group(IReadOnlyList<EntityState> entities)
    {
        var bosses = new HashSet<int>();
        foreach (EntityState entity in entities)
        {
            if (entity.Has(EntityFlags.Boss))
                bosses.Add(Key(entity));
        }

        var order = new List<int>();
        var groups = new Dictionary<int, Bar>();
        foreach (EntityState entity in entities)
        {
            int key = Key(entity);
            if (!bosses.Contains(key))
                continue;
            if (!groups.TryGetValue(key, out Bar bar))
            {
                order.Add(key);
                bar = new Bar(entity.Name ?? "", 0, 0);
            }
            // The head names the bar. A part only names it while the head is not in this place.
            string name = !IsPart(entity) && entity.Has(EntityFlags.Boss) ? entity.Name ?? "" : bar.Name;
            groups[key] = new Bar(name, bar.Life + Math.Max(0, entity.Life), bar.LifeMax + Math.Max(0, entity.LifeMax));
        }

        var bars = new List<Bar>();
        foreach (int key in order)
        {
            if (bars.Count == MaxBars)
                break;
            bars.Add(groups[key]);
        }
        return bars;
    }

    /// <summary>A part belongs to the head its RealLife names. Everything else is keyed by its own id.</summary>
    private static bool IsPart(EntityState entity) => entity.RealLife >= 0 && entity.RealLife != entity.Id;

    private static int Key(EntityState entity) => IsPart(entity) ? entity.RealLife : entity.Id;
}
