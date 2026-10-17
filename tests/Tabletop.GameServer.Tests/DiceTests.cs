using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.GameServer.Tests;

public class DiceTests
{
    static readonly RollConvention D6 = new(6, null);
    static RollPlan Plan(int? threshold, params RollModifier[] mods) => new("hit", threshold, mods, D6, null, new HashSet<int>());
    static GameState State(params EventPayload[] history)
    {
        var applier = new EventApplier(Samples.Board, Samples.Game);
        applier.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, null, p, null)));
        return new GameState(Samples.Game, Samples.Board, applier, GrimdarkFutureRules.Instance);
    }
    static GameState Seeded(GameState state, long seed) => new(Samples.Game with { RngSeed = seed }, Samples.Board, state.Applier, GrimdarkFutureRules.Instance);

    [Fact]
    public void The_source_is_deterministic_uniform_and_seed_dependent()
    {
        var d = new DiceSource();
        Assert.Equal(d.Draw(7, 3, 6), d.Draw(7, 3, 6));
        Assert.NotEqual(Enumerable.Range(0, 20).Select(i => d.Draw(7, i, 6)), Enumerable.Range(0, 20).Select(i => d.Draw(8, i, 6)));
        var faces = Enumerable.Range(0, 60_000).Select(i => d.Draw(1, i, 6)).GroupBy(f => f).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(6, faces.Count); Assert.All(faces.Values, n => Assert.InRange(n, 9_000, 11_000));
        Assert.Equal([3, 4, 5, 3], Enumerable.Range(0, 4).Select(i => new ScriptedDice(3, 4, 5).Draw(0, i, 6)));
    }

    [Fact]
    public void A_roll_records_raw_modified_success_and_advances_the_cursor()
    {
        var roller = new Roller(new ScriptedDice(3, 4, 5), 0, 0);
        var result = roller.Roll(Samples.Frame, Plan(4, new RollModifier("Stealth", -1)), 3);
        Assert.Equal([(3, 2, false), (4, 3, false), (5, 4, true)], result.Record.Dice.Select(x => (x.Raw, x.Modified, x.Success)));
        Assert.Equal((4, "hit", 3L), (result.Record.Threshold, result.Record.Roll, result.NextCursor));
        Assert.Null(result.ForcedRerolls);
        Assert.Equal(1, RollOutcome.Successes(result.Record)); Assert.Equal(2, RollOutcome.Failures(result.Record));
    }

    [Fact]
    public void Caps_required_raw_and_null_threshold()
    {
        var capped = new Roller(new ScriptedDice(4), 0, 0).Roll(Samples.Frame, Plan(4, new RollModifier("a", 1), new RollModifier("b", 1)) with { Convention = new RollConvention(6, 1) }, 1).Record.Dice[0];
        Assert.Equal((4, 5), (capped.Raw, capped.Modified));
        var fatigued = new Roller(new ScriptedDice(5, 6), 0, 0).Roll(Samples.Frame, Plan(2) with { RequiredRaw = 6 }, 2).Record.Dice;
        Assert.Equal([false, true], fatigued.Select(x => x.Success));
        var rollOff = new Roller(new ScriptedDice(2, 6), 0, 0).Roll(Samples.Frame, Plan(null), 2).Record;
        Assert.Null(rollOff.Threshold); Assert.All(rollOff.Dice, x => Assert.False(x.Success)); Assert.Equal(1, RollOutcome.CountRaw(rollOff, 6));
    }

    [Fact]
    public void A_convention_s_natural_faces_win_over_any_modifier_on_a_thresholded_roll()
    {
        var naturals = Plan(4, new RollModifier("big", 5)) with { Convention = new RollConvention(6, null, AlwaysFails: 1, AlwaysSucceeds: 6) };
        Assert.Equal([(1, 6, false), (2, 7, true)], new Roller(new ScriptedDice(1, 2), 0, 0).Roll(Samples.Frame, naturals, 2).Record.Dice.Select(x => (x.Raw, x.Modified, x.Success)));
        var penalised = naturals with { Modifiers = [new RollModifier("big", -5)] };
        Assert.Equal([(6, 1, true), (5, 0, false)], new Roller(new ScriptedDice(6, 5), 0, 0).Roll(Samples.Frame, penalised, 2).Record.Dice.Select(x => (x.Raw, x.Modified, x.Success)));
        var roller = new Roller(new ScriptedDice(1), 0, 0);
        Assert.False(roller.Judge(naturals, 1).Success);                                             // SetDice judges through the same call
        Assert.True(new Roller(new ScriptedDice(1), 0, 0).Roll(Samples.Frame, Plan(4, new RollModifier("big", 5)), 1).Record.Dice[0].Success);   // a convention without them judges the modified result alone
        Assert.Equal([false, true], new Roller(new ScriptedDice(5, 6), 0, 0).Roll(Samples.Frame, Plan(2) with { RequiredRaw = 6, Convention = naturals.Convention }, 2).Record.Dice.Select(x => x.Success));   // a required raw face still rules
        Assert.False(new Roller(new ScriptedDice(6), 0, 0).Roll(Samples.Frame, Plan(null) with { Convention = naturals.Convention }, 1).Record.Dice[0].Success);   // no threshold, no success
    }

    [Fact]
    public void A_forced_reroll_is_a_dice_rerolled_payload_and_the_cursor_counts_the_extra_draw()
    {
        var plan = Plan(4) with { Roll = "defense", RerollRaw = new HashSet<int> { 6 } };
        var result = new Roller(new ScriptedDice(6, 2, 6), 0, 0).Roll(Samples.Frame, plan, 2);
        Assert.Equal([6, 2], result.Record.Dice.Select(x => x.Raw));                                  // the record holds the dice as rolled
        var rr = Assert.IsType<DiceRerolled>(result.ForcedRerolls);
        Assert.Equal((0, 6, 6), (rr.Replaced.Single().Index, rr.Replaced.Single().Old.Raw, rr.Replaced.Single().New.Raw));   // rerolled once, still a 6: blocked
        Assert.Equal(3, result.NextCursor);
    }

    [Fact]
    public void The_cursor_counts_every_die_ever_drawn_undone_or_not_and_the_seed_from_the_game()
    {
        var hit = new RollRecord(Guid.CreateVersion7(), "hit", 4, [], [new Die(3, 3, false), new Die(5, 5, true)]);
        var state = State(new SequenceStarted(Samples.Frame, "attack-sequence", new SequenceContext(Samples.Unit, [Samples.Unit], [], "Advance")),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [hit], []),
            new DiceRerolled(Samples.Frame, hit.Id, [new DieReplacement(0, new Die(3, 3, false), new Die(6, 6, true))]),
            new DiceEdited(Samples.Frame, hit.Id, [new DieReplacement(1, new Die(5, 5, true), new Die(1, 1, false))]));
        Assert.Equal(3, DiceCursor.DrawnSoFar(state));
        var undone = State(new SequenceStarted(Samples.Frame, "attack-sequence", new SequenceContext(Samples.Unit, [Samples.Unit], [], "Advance")),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [hit], []), new Undone(0));
        Assert.Equal(2, DiceCursor.DrawnSoFar(undone));                                        // an undone roll's dice stay drawn
        Assert.Equal(state.RngSeed, undone.RngSeed);                                          // undo never touches the seed
        Assert.Equal(unchecked((ulong)Samples.Game.RngSeed), state.RngSeed);                  // the stored value, whole; nothing is derived from the id
        Assert.Equal(7UL, Seeded(state, 7).RngSeed);
        Assert.Equal(ulong.MaxValue, Seeded(state, -1).RngSeed);                              // bigint is signed: a negative seed reinterprets, never throws
    }
}
