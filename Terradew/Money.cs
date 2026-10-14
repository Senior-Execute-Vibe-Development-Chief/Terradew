using StardewModdingAPI;
using StardewValley;
using Terradew.Core.Economy;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>
/// Terraria coins are the farm's money. Stardew's money counter mirrors the coin total (one gold is one
/// silver). Whatever Stardew changes — shipping income, seeds from Pierre — is sent to Terraria as coins.
/// </summary>
internal sealed class Money
{
    private const string SaveKey = "terradew-coins";

    private sealed class Saved
    {
        public long Confirmed { get; set; }
        public List<long[]> Pending { get; set; } = new();
        public int NextSeq { get; set; } = 1;
        public int LastAck { get; set; }
    }

    private readonly IModHelper helper;
    private CoinLedger ledger = new();
    private int lastSet = -1;

    public Money(IModHelper helper)
    {
        this.helper = helper;
    }

    public long Copper => ledger.Expected;

    public int LastAck => ledger.LastAck;

    public void Load()
    {
        Saved? saved = helper.Data.ReadSaveData<Saved>(SaveKey);
        if (saved == null)
        {
            // First day with Terradew: the farm's existing money moves into Terraria as coins.
            ledger = new CoinLedger();
            lastSet = 0;
        }
        else
        {
            ledger = new CoinLedger(saved.Confirmed, saved.Pending.Select(p => ((int)p[0], p[1])), saved.NextSeq, saved.LastAck);
            lastSet = Game1.player.Money;
        }
    }

    public void Save()
    {
        helper.Data.WriteSaveData(SaveKey, new Saved
        {
            Confirmed = ledger.ConfirmedTotal,
            Pending = ledger.Pending.Select(p => new long[] { p.Seq, p.Delta }).ToList(),
            NextSeq = ledger.NextSeq,
            LastAck = ledger.LastAck,
        });
    }

    /// <summary>Resend every change Terraria has not confirmed yet.</summary>
    public void Resend(Bridge bridge)
    {
        foreach ((int seq, long delta) in ledger.Pending)
            bridge.Send(new AdjustCoins { Seq = seq, Delta = delta });
    }

    public void Ack(CoinAck ack) => ledger.Ack(ack.Seq, ack.Total);

    public void Update(Bridge bridge)
    {
        Farmer player = Game1.player;
        if (lastSet < 0)
            lastSet = player.Money;
        if (player.Money != lastSet)
        {
            (int seq, long delta) = ledger.Record(Coins.FromStardew(player.Money - (long)lastSet));
            if (seq != 0)
                bridge.Send(new AdjustCoins { Seq = seq, Delta = delta });
        }
        int mirrored = ledger.StardewMoney;
        if (player.Money != mirrored)
            player.Money = mirrored;
        lastSet = player.Money;
    }
}
