using System;
using Terradew.Core.Economy;
using Terradew.Core.Time;

/// <summary>Tests for Coins, CoinLedger and ClockMap.</summary>
internal static class EconomyClockTests
{
    public static int Run()
    {
        int failed = 0;

        failed += T.Check("clock: morning is day at time zero", () =>
        {
            var (day, time) = ClockMap.ToTerraria(600, 0f);
            T.Assert(day, "should be day");
            T.Assert(time == 5400, "expected 5400 but got " + time);
        });

        failed += T.Check("clock: evening is night at time zero", () =>
        {
            var (day, time) = ClockMap.ToTerraria(1930, 0f);
            T.Assert(!day, "should be night");
            T.Assert(time == 0, "expected 0 but got " + time);
        });

        failed += T.Check("clock: 2am is night 6.5 hours in", () =>
        {
            var (day, time) = ClockMap.ToTerraria(2600, 0f);
            T.Assert(!day, "should be night");
            T.Assert(time == 23400, "expected 23400 but got " + time);
        });

        failed += T.Check("clock: staying up runs the whole Terraria night to dawn", () =>
        {
            var (night, late) = ClockMap.ToTerraria(2700, 0f);
            T.Assert(!night && late == 27000, "3am is night, 7.5 hours in, got " + late);
            var (day, dawn) = ClockMap.ToTerraria(2830, 0f);
            T.Assert(day && dawn == 0, "4:30am is Terraria's dawn");
            T.Assert(ClockMap.ToTerraria(2950, 0f).DayTime, "5:50am is day");
        });

        failed += T.Check("clock: Stardew ten-minute step rolls the hour", () =>
        {
            T.Assert(ClockMap.StardewNext(2600) == 2610, "2600 to 2610");
            T.Assert(ClockMap.StardewNext(2650) == 2700, "2650 to 2700");
            T.Assert(ClockMap.StardewNext(2950) == ClockMap.StardewMorning, "5:50am to 6am");
        });

        failed += T.Check("clock: fraction adds progress through the ten minutes", () =>
        {
            var (day, time) = ClockMap.ToTerraria(1200, 0.5f);
            T.Assert(day, "should be day");
            T.Assert(time == (12 * 60 + 5 - 270) * 60, "expected " + (12 * 60 + 5 - 270) * 60 + " but got " + time);
        });

        failed += T.Check("clock: fraction is clamped to [0,1]", () =>
        {
            var (_, low) = ClockMap.ToTerraria(1200, -3f);
            var (_, high) = ClockMap.ToTerraria(1200, 7f);
            T.Assert(low == (12 * 60 - 270) * 60, "low clamp");
            T.Assert(high == (12 * 60 + 10 - 270) * 60, "high clamp");
        });

        failed += T.Check("clock: phase lengths", () =>
        {
            T.Assert(ClockMap.PhaseLength(true) == 54000, "day length");
            T.Assert(ClockMap.PhaseLength(false) == 32400, "night length");
        });

        failed += T.Check("clock: same phase jumps straight to the target", () =>
        {
            var (day, time) = ClockMap.Next(true, 100, true, 4000);
            T.Assert(day && time == 4000, "same phase");
        });

        failed += T.Check("clock: crossing a phase runs out the current one", () =>
        {
            var (day, time) = ClockMap.Next(true, 100, false, 0);
            T.Assert(day, "stays day until the game crosses");
            T.Assert(time == ClockMap.PhaseLength(true) - 1, "expected PhaseLength-1 but got " + time);

            var (nightDay, nightTime) = ClockMap.Next(false, 100, true, 0);
            T.Assert(!nightDay, "stays night until the game crosses");
            T.Assert(nightTime == ClockMap.PhaseLength(false) - 1, "night expected PhaseLength-1 but got " + nightTime);
        });

        failed += T.Check("coins: stardew gold converts to silver", () =>
        {
            T.Assert(Coins.FromStardew(3) == 300, "3g should be 300 copper");
            T.Assert(Coins.CopperPerStardewGold == Coins.PerSilver, "one g is one silver");
        });

        failed += T.Check("coins: ToStardew floors and clamps", () =>
        {
            T.Assert(Coins.ToStardew(1234) == 12, "floors");
            T.Assert(Coins.ToStardew(99) == 0, "under one g");
            T.Assert(Coins.ToStardew(-5) == 0, "negative is zero");
            T.Assert(Coins.ToStardew(long.MaxValue) == int.MaxValue, "clamps high");
        });

        failed += T.Check("coins: split", () =>
        {
            var parts = Coins.Split(1234567);
            T.Assert(parts == (1, 23, 45, 67), "1234567 splits to 1p 23g 45s 67c");
            var zero = Coins.Split(-1);
            T.Assert(zero == (0, 0, 0, 0), "negative splits to zero");
            var big = Coins.Split(250_000_000);
            T.Assert(big.Platinum == 250 && big.Gold == 0, "platinum exceeds 100");
        });

        failed += T.Check("coins: format", () =>
        {
            T.Assert(Coins.Format(1234567) == "1p 23g 45s 67c", "full: " + Coins.Format(1234567));
            T.Assert(Coins.Format(0) == "0c", "zero");
            T.Assert(Coins.Format(-40) == "0c", "negative");
            T.Assert(Coins.Format(10_000) == "1g", "only gold");
            T.Assert(Coins.Format(1_000_000) == "1p", "only platinum");
            T.Assert(Coins.Format(1_000_100) == "1p 1s", "skips zero parts");
            T.Assert(Coins.Format(7) == "7c", "only copper");
        });

        failed += T.Check("ledger: record then ack matches the expected total", () =>
        {
            var ledger = new CoinLedger();
            var entry = ledger.Record(500);
            T.Assert(entry == (1, 500), "first entry");
            T.Assert(ledger.Expected == 500, "expected before ack: " + ledger.Expected);
            T.Assert(ledger.StardewMoney == 5, "stardew money before ack");
            ledger.Ack(1, 500);
            T.Assert(ledger.ConfirmedTotal == 500, "confirmed");
            T.Assert(ledger.Pending.Count == 0, "pending cleared");
            T.Assert(ledger.Expected == 500, "expected after ack: " + ledger.Expected);
            T.Assert(ledger.LastAck == 1, "last ack");
            T.Assert(ledger.NextSeq == 2, "next seq");
        });

        failed += T.Check("ledger: stale ack is ignored", () =>
        {
            var ledger = new CoinLedger();
            ledger.Record(100);
            ledger.Record(200);
            ledger.Ack(2, 300);
            ledger.Record(50);
            ledger.Ack(1, 9999);
            T.Assert(ledger.LastAck == 2, "last ack stays 2");
            T.Assert(ledger.ConfirmedTotal == 300, "confirmed stays 300");
            T.Assert(ledger.Pending.Count == 1 && ledger.Pending[0].Seq == 3, "pending seq 3 kept");
            T.Assert(ledger.Expected == 350, "expected: " + ledger.Expected);
        });

        failed += T.Check("ledger: pending survives until it is acked", () =>
        {
            var ledger = new CoinLedger();
            ledger.Record(100);
            ledger.Record(200);
            T.Assert(ledger.Expected == 300, "two pending");
            ledger.Ack(1, 100);
            T.Assert(ledger.Pending.Count == 1, "one left");
            T.Assert(ledger.Pending[0] == (2, 200), "seq 2 still pending");
            T.Assert(ledger.Expected == 300, "expected stays 300");
        });

        failed += T.Check("ledger: zero delta is not recorded", () =>
        {
            var ledger = new CoinLedger();
            var entry = ledger.Record(0);
            T.Assert(entry == (0, 0), "returns zero entry");
            T.Assert(ledger.Pending.Count == 0, "nothing pending");
            T.Assert(ledger.NextSeq == 1, "sequence not consumed");
        });

        failed += T.Check("ledger: expected never goes below zero", () =>
        {
            var ledger = new CoinLedger(100);
            ledger.Record(-500);
            T.Assert(ledger.Expected == 0, "clamped to zero");
            T.Assert(ledger.StardewMoney == 0, "no stardew money");
            ledger.Ack(1, -20);
            T.Assert(ledger.ConfirmedTotal == 0, "negative total clamped");
        });

        failed += T.Check("ledger: constructor restores state", () =>
        {
            var ledger = new CoinLedger(1000, new[] { (4, 250L), (5, -100L) }, 6, 3);
            T.Assert(ledger.Expected == 1150, "expected from restored state");
            T.Assert(ledger.NextSeq == 6 && ledger.LastAck == 3, "restored sequence");
            ledger.Ack(4, 1200);
            T.Assert(ledger.Pending.Count == 1 && ledger.Pending[0].Seq == 5, "seq 5 remains");
        });

        return failed;
    }
}
