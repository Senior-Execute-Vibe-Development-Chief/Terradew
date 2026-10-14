using System;
using System.Collections.Generic;

namespace Terradew.Core.Economy;

/// <summary>
/// Terraria coin math. Currency is stored as copper (long):
/// 100 copper = 1 silver, 100 silver = 1 gold, 100 gold = 1 platinum.
/// One Stardew "g" equals one silver.
/// </summary>
public static class Coins
{
    public const long PerSilver = 100, PerGold = 10_000, PerPlatinum = 1_000_000;
    public const long CopperPerStardewGold = PerSilver;

    public static long FromStardew(long gold) => gold * CopperPerStardewGold;

    public static int ToStardew(long copper)
    {
        if (copper <= 0)
            return 0;
        long gold = copper / CopperPerStardewGold;
        return (int)Math.Min(gold, int.MaxValue);
    }

    public static (int Platinum, int Gold, int Silver, int Copper) Split(long copper)
    {
        if (copper <= 0)
            return (0, 0, 0, 0);
        long platinum = copper / PerPlatinum;
        long rest = copper % PerPlatinum;
        long gold = rest / PerGold;
        rest %= PerGold;
        long silver = rest / PerSilver;
        long coppers = rest % PerSilver;
        return ((int)Math.Min(platinum, int.MaxValue), (int)gold, (int)silver, (int)coppers);
    }

    public static string Format(long copper)
    {
        if (copper <= 0)
            return "0c";
        var parts = Split(copper);
        var pieces = new List<string>(4);
        if (parts.Platinum > 0)
            pieces.Add(parts.Platinum + "p");
        if (parts.Gold > 0)
            pieces.Add(parts.Gold + "g");
        if (parts.Silver > 0)
            pieces.Add(parts.Silver + "s");
        if (parts.Copper > 0)
            pieces.Add(parts.Copper + "c");
        return string.Join(" ", pieces);
    }
}

/// <summary>
/// Stardew-side mirror of the Terraria coin total. Stardew records the deltas it
/// caused; Terraria acknowledges them with a total.
/// </summary>
public sealed class CoinLedger
{
    private readonly List<(int Seq, long Delta)> _pending;

    public CoinLedger(long confirmedTotal = 0, IEnumerable<(int Seq, long Delta)>? pending = null, int nextSeq = 1, int lastAck = 0)
    {
        ConfirmedTotal = confirmedTotal;
        _pending = pending == null ? new List<(int Seq, long Delta)>() : new List<(int Seq, long Delta)>(pending);
        NextSeq = nextSeq;
        LastAck = lastAck;
    }

    public long ConfirmedTotal { get; private set; }

    public IReadOnlyList<(int Seq, long Delta)> Pending => _pending;

    public int NextSeq { get; private set; }

    public int LastAck { get; private set; }

    public (int Seq, long Delta) Record(long deltaCopper)
    {
        if (deltaCopper == 0)
            return (0, 0);
        var entry = (NextSeq, deltaCopper);
        NextSeq++;
        _pending.Add(entry);
        return entry;
    }

    public void Ack(int seq, long total)
    {
        if (seq < LastAck)
            return;
        LastAck = seq;
        ConfirmedTotal = Math.Max(0, total);
        _pending.RemoveAll(entry => entry.Seq <= seq);
    }

    public long Expected
    {
        get
        {
            long total = ConfirmedTotal;
            foreach (var entry in _pending)
                total += entry.Delta;
            return Math.Max(0, total);
        }
    }

    public int StardewMoney => Coins.ToStardew(Expected);
}
