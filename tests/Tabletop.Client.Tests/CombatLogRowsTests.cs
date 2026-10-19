using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>The log panel keeps the rows of the leading entries that draw the same and rebuilds the rest.</summary>
public class CombatLogRowsTests
{
    static readonly RoundStarted Round1 = new(1, [Samples.Team1, Samples.Team2], []);
    static readonly Guid Rifle = Samples.Army.Units[0].Models[0].Weapons[0].Id;

    /// <summary>Everything a row draws, nested entries included, as text: the test's own reading, independent of Same.</summary>
    static string Drawn(LogEntry e) => $"{e.Seq}|{e.Undone}|" + e switch
    {
        ActivationBlock b => $"{b.Header}|{b.Chain}|{b.Spell}|{string.Join(";", b.Entries.Select(Drawn))}",
        RollEntry r => $"{r.Record.Id}|{r.Title}|{r.Collapsed(CombatLog.Pip)}|{string.Join(",", r.Dice.Select(d => d.Text))}|{string.Join(",", r.Amendments.Select(a => $"{a.Text}{a.Undone}"))}"
            + $"|{string.Join(",", r.Applied)}|{string.Join(",", r.Details)}",
        LineEntry l => l.Text,
        _ => "",
    };

    [Fact]
    public void Each_appended_event_keeps_every_row_but_the_last_and_kept_rows_draw_the_same()
    {
        var log = CombatLogFixtureTests.Log(); var n = new Names(CombatLogFixtureTests.Game, null);
        var drawn = CombatLog.Build([], n);
        for (var i = 1; i <= log.Count; i++)
        {
            var next = CombatLog.Build(log.Take(i).ToList(), n);
            var kept = CombatLogRows.Kept(drawn, next);
            Assert.True(kept >= drawn.Count - 1, $"event {i}: kept {kept} of {drawn.Count}");
            for (var k = 0; k < kept; k++) Assert.Equal(Drawn(drawn[k]), Drawn(next[k]));
            for (var k = kept; k < Math.Min(drawn.Count, next.Count); k++) Assert.NotEqual(Drawn(drawn[k]), Drawn(next[k]));
            drawn = next;
        }
    }

    [Fact]
    public void A_fresh_build_of_the_same_log_keeps_every_row()
    {
        var log = CombatLogFixtureTests.Log(); var n = new Names(CombatLogFixtureTests.Game, null);
        var entries = CombatLog.Build(log, n);
        Assert.Equal(entries.Count, CombatLogRows.Kept(entries, CombatLog.Build(log, n)));
    }

    [Fact]
    public void An_undone_rebuilds_from_the_first_entry_it_strikes()
    {
        static EventPayload[] Shooting(Guid roll) =>
        [
            new SequenceStarted(Samples.Frame, "shooting", new SequenceContext(Samples.Unit, [], [Rifle], "Hold")),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [new RollRecord(roll, "hit", 3, [], [new Die(4, 4, true)], null, null)], []),
            new SequenceFinished(Samples.Frame),
        ];
        var a = new EventApplier(Samples.Board, Samples.Game); var seq = 0;
        foreach (var p in new[] { Round1 }.Concat(Shooting(Guid.CreateVersion7())).Concat(Shooting(Guid.CreateVersion7()))) a.Apply(new GameEvent(++seq, Samples.P1, p, null));
        var before = CombatLog.Build(a.Log, new Names(Samples.Game, a.Projection));
        var undoneFrom = before.First(e => e.Seq > 4).Seq;
        a.Apply(new GameEvent(++seq, Samples.P1, new Undone(4), null));
        var after = CombatLog.Build(a.Log, new Names(Samples.Game, a.Projection));
        var kept = CombatLogRows.Kept(before, after);
        Assert.Equal(before.TakeWhile(e => e.Seq < undoneFrom).Count(), kept);
        Assert.True(after[kept].Undone);
    }
}
