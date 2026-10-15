using System;
using System.Collections.Generic;
using Terradew.Core.Bosses;
using Terradew.Core.Protocol;

/// <summary>Boss bar checks: parts of one boss share a bar, lone bosses get their own, and at most three bars show.</summary>
public static class BossBarTests
{
    public static int Run()
    {
        int failed = 0;

        failed += T.Check("boss parts with the same head make one bar with summed life", () =>
        {
            var entities = new List<EntityState>
            {
                new() { Id = 4, Name = "Eater of Worlds", Flags = EntityFlags.Boss, Life = 300, LifeMax = 500, RealLife = -1 },
                new() { Id = 5, Name = "Eater of Worlds Body", Flags = EntityFlags.Worm, Life = 100, LifeMax = 200, RealLife = 4 },
                new() { Id = 6, Name = "Eater of Worlds Tail", Flags = EntityFlags.Worm, Life = 50, LifeMax = 200, RealLife = 4 },
            };
            List<BossBars.Bar> bars = BossBars.Group(entities);
            T.Assert(bars.Count == 1, $"expected one bar, got {bars.Count}");
            T.Assert(bars[0].Name == "Eater of Worlds" && bars[0].Life == 450 && bars[0].LifeMax == 900, "head names the bar, parts add up");
        });

        failed += T.Check("the Eye of Cthulhu and a walking boss part get separate bars, in Terraria's order", () =>
        {
            var entities = new List<EntityState>
            {
                new() { Id = 9, Name = "Eye of Cthulhu", Flags = EntityFlags.Boss | EntityFlags.Flyer, Life = 1000, LifeMax = 2800, RealLife = -1 },
                new() { Id = 12, Name = "Skeletron Hand", Flags = EntityFlags.Boss, Life = 10, LifeMax = 20, RealLife = -1 },
                new() { Id = 13, Name = "Skeletron Hand", Flags = 0, Life = 7, LifeMax = 9, RealLife = 12 },
                new() { Id = 20, Name = "Zombie", Flags = EntityFlags.Hostile, Life = 5, LifeMax = 5, RealLife = -1 },
            };
            List<BossBars.Bar> bars = BossBars.Group(entities);
            T.Assert(bars.Count == 2, $"expected two bars, got {bars.Count}");
            T.Assert(bars[0].Name == "Eye of Cthulhu" && bars[0].Life == 1000 && bars[0].LifeMax == 2800, "eye bar");
            T.Assert(bars[1].Life == 17 && bars[1].LifeMax == 29, "hand bar counts its part");
        });

        failed += T.Check("no bosses means no bars, and at most three bars show", () =>
        {
            T.Assert(BossBars.Group(new List<EntityState> { new() { Id = 1, Name = "Slime", Life = 3, LifeMax = 3 } }).Count == 0, "no boss, no bar");
            var entities = new List<EntityState>();
            for (int i = 0; i < 5; i++)
                entities.Add(new EntityState { Id = i, Name = $"Boss {i}", Flags = EntityFlags.Boss, Life = 1, LifeMax = 1, RealLife = -1 });
            T.Assert(BossBars.Group(entities).Count == BossBars.MaxBars, "capped at three");
        });

        return failed;
    }
}
