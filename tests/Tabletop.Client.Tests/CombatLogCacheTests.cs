using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class CombatLogCacheTests
{
    static readonly GameInfo Game = Samples.Game;   // a property: each read mints a new game
    static readonly RoundStarted Round1 = new(1, [Samples.Team1, Samples.Team2], []);
    static readonly Guid Rifle = Samples.Army.Units[0].Models[0].Weapons[0].Id;

    static EventApplier Applier(params EventPayload[] history)
    {
        var a = new EventApplier(Samples.Board, Game);
        a.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, Samples.P1, p, null)));
        return a;
    }

    static StepCompleted Hit(Guid roll) => new(Samples.Frame, "roll-to-hit", null,
        [new RollRecord(roll, "hit", 3, [], [new Die(4, 4, true)], null, null)], []);

    static EventPayload[] Shooting(Guid roll) =>
        [Round1, new SequenceStarted(Samples.Frame, "shooting", new SequenceContext(Samples.Unit, [], [Rifle], "Hold")), Hit(roll), new SequenceFinished(Samples.Frame)];

    [Fact]
    public void The_same_log_state_is_the_same_instance_and_built_once()
    {
        var a = Applier(Shooting(Samples.Roll)); var cache = new CombatLogCache();
        var first = cache.Of(a.Log, null, Game, a.Projection);
        var second = cache.Of(a.Log, null, Game, a.Projection);
        Assert.Same(first, second);
        Assert.Same(first.Entries, second.Entries);
        Assert.Same(first.Entries, first.Entries);
    }

    [Fact]
    public void A_new_event_an_undone_or_a_pending_undo_is_a_new_state()
    {
        var a = Applier(Shooting(Samples.Roll)); var cache = new CombatLogCache();
        var before = cache.Of(a.Log, null, Game, a.Projection);
        a.Apply(new GameEvent(5, Samples.P1, new ScoreSet(Samples.Team1, 1), null));
        var appended = cache.Of(a.Log, null, Game, a.Projection);
        Assert.NotSame(before, appended);
        var predicted = a.ProjectTo(4);
        var pending = cache.Of(a.Log, 4, Game, predicted);
        Assert.NotSame(appended, pending);
        a.Apply(new GameEvent(6, null, new Undone(4), null));
        Assert.NotSame(pending, cache.Of(a.Log, null, Game, a.Projection));
    }

    [Fact]
    public void Its_entries_latest_and_undo_match_a_fresh_build_across_appends_and_an_undone()
    {
        var second = Guid.CreateVersion7();
        var a = Applier(Shooting(Samples.Roll)); var cache = new CombatLogCache();
        void Same()
        {
            var n = new Names(Game, a.Projection); var state = cache.Of(a.Log, null, Game, a.Projection);
            Assert.Equal(Words(CombatLog.Build(a.Log, n)), Words(state.Entries));
            Assert.Equal(CombatLog.Latest(a.Log, n)?.Record.Id, state.Latest?.Record.Id);
            Assert.Equal(UndoPlan.Of(a.Log, n), state.Undo, new PlanEquality());
        }
        Same();
        var seq = a.Log.Count;
        foreach (var p in Shooting(second)) a.Apply(new GameEvent(++seq, Samples.P1, p, null));
        Same();
        a.Apply(new GameEvent(++seq, Samples.P1, new Undone(4), null));
        Same();
        Assert.Equal(Samples.Roll, cache.Of(a.Log, null, Game, a.Projection).Latest!.Record.Id);
    }

    /// <summary>While an undo travels the button plans as if its marker had landed: one press further back.</summary>
    [Fact]
    public void A_pending_undo_plans_over_the_log_with_its_marker()
    {
        var a = Applier([.. Shooting(Samples.Roll), new ScoreSet(Samples.Team1, 1)]);
        var n = new Names(Game, a.Projection);
        var state = new CombatLogCache().Of(a.Log, 4, Game, a.Projection);
        IReadOnlyList<GameEvent> pending = [.. a.Log, new GameEvent(6, null, new Undone(4), null)];
        Assert.Equal(UndoPlan.Of(pending, n), state.Undo, new PlanEquality());
        Assert.Equal(3L, state.Undo!.ToSeq);
    }

    static IEnumerable<string> Words(IReadOnlyList<LogEntry> entries) => entries.SelectMany(e => e is ActivationBlock b
        ? b.Entries.Select(x => $"{b.Header}|{Word(x)}") : [Word(e)]);
    static string Word(LogEntry e) => $"{e.Seq}|{e.Undone}|" + e switch { RollEntry r => r.Collapsed(CombatLog.Pip), LineEntry l => l.Text, _ => "" };

    sealed class PlanEquality : IEqualityComparer<UndoPlan?>
    {
        public bool Equals(UndoPlan? x, UndoPlan? y) => x is null ? y is null
            : y is not null && x.ToSeq == y.ToSeq && x.Action == y.Action && x.Tooltip == y.Tooltip && x.LostRolls.SequenceEqual(y.LostRolls);
        public int GetHashCode(UndoPlan? p) => p?.ToSeq.GetHashCode() ?? 0;
    }
}
