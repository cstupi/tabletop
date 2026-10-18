using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>Headers, nested casts, consequences, roll-offs, change subjects and the lines outside actions (combat-log spec → The log).</summary>
public class CombatLogBlockTests
{
    static readonly Guid Enemy = Guid.Parse("0198b7ac-2222-7000-8000-000000000002");
    static Guid Rifle => Samples.Army.Units[0].Models[0].Weapons[0].Id;
    static readonly RoundStarted Round1 = new(1, [Samples.Team1, Samples.Team2], []);
    static string Foe => Names.Short(Enemy);

    static (IReadOnlyList<LogEntry> Entries, IReadOnlyList<GameEvent> Log, Names Names) Build(params EventPayload[] history)
    {
        var a = new EventApplier(Samples.Board, Samples.Game);
        a.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, Samples.P1, p, null)));
        var n = new Names(Samples.Game, a.Projection);
        return (CombatLog.Build(a.Log, n), a.Log, n);
    }

    static SequenceStarted Hold(Guid frame) => new(frame, "hold", new SequenceContext(Samples.Unit, [], [], "Hold"));
    /// <summary>As the server sends it: `shooting` with the Hold frame's own context, the targets on its `attack-sequence` children.</summary>
    static SequenceStarted Shooting(Guid frame) => new(frame, "shooting", new SequenceContext(Samples.Unit, [], [], "Hold"));
    static SequenceStarted Attack(Guid frame) => new(frame, "attack-sequence", new SequenceContext(Samples.Unit, [Enemy], [Rifle], "Hold"));
    static RollRecord One(string roll, int threshold, int face, bool success) => new(Guid.CreateVersion7(), roll, threshold, [], [new Die(face, face, success)]);

    [Fact]
    public void A_spawn_placing_a_reserve_does_not_make_it_a_shooting_target()
    {
        Guid place = Guid.CreateVersion7(), shooting = Guid.CreateVersion7(), attack = Guid.CreateVersion7();
        var (entries, _, _) = Build(Round1, Hold(Samples.Frame),
            new SequenceStarted(place, "place-reserve", new SequenceContext(Samples.Unit, [Guid.NewGuid()], [], "Spawn")), new SequenceFinished(place),
            Shooting(shooting), Attack(attack), new SequenceFinished(attack), new SequenceFinished(shooting), new SequenceFinished(Samples.Frame));
        var block = Assert.Single(entries.OfType<ActivationBlock>());
        Assert.Equal($"R1 · Team 1 · Battle Brothers · Hold → shoots {Foe}", block.Header);
        Assert.Equal([Enemy], block.Targets);
    }

    [Fact]
    public void A_hold_that_shoots_names_its_target_with_the_verb()
    {
        Guid shooting = Guid.CreateVersion7(), attack = Guid.CreateVersion7();
        var (entries, _, _) = Build(Round1, Hold(Samples.Frame), Shooting(shooting), Attack(attack),
            new StepCompleted(attack, "roll-to-hit", null, [Samples.Hit], []), new SequenceFinished(attack),
            new SequenceFinished(shooting), new SequenceFinished(Samples.Frame));
        var block = Assert.Single(entries.OfType<ActivationBlock>());
        Assert.Equal($"R1 · Team 1 · Battle Brothers · Hold → shoots {Foe}", block.Header);
        Assert.Equal([Enemy], block.Targets);
    }

    /// <summary>A charge names its target in an answer on the action frame's own step (owner game seq 257, `charge-target`).</summary>
    [Fact]
    public void An_answer_on_the_action_frames_own_step_names_the_target()
    {
        var (entries, _, _) = Build(Round1, new SequenceStarted(Samples.Frame, "charge", new SequenceContext(Samples.Unit, [], [], "Charge")),
            new StepCompleted(Samples.Frame, "charge-target", new SelectedPieces([Enemy]), [], []), new SequenceFinished(Samples.Frame));
        var block = Assert.Single(entries.OfType<ActivationBlock>());
        Assert.Equal($"R1 · Team 1 · Battle Brothers · Charge → {Foe}", block.Header);
        Assert.Equal([Enemy], block.Targets);
    }

    /// <summary>A strike-back is a nested frame whose actor is the target: it adds no target to the charger's header.</summary>
    [Fact]
    public void A_nested_frame_of_another_actor_adds_no_target()
    {
        var back = Guid.CreateVersion7();
        var (entries, _, _) = Build(Round1, Hold(Samples.Frame),
            new SequenceStarted(back, "attack-sequence", new SequenceContext(Enemy, [Samples.Unit], [], null)),
            new SequenceFinished(back), new SequenceFinished(Samples.Frame));
        var block = Assert.Single(entries.OfType<ActivationBlock>());
        Assert.Empty(block.Targets);
        Assert.Equal("R1 · Team 1 · Battle Brothers · Hold", block.Header);
    }

    [Fact]
    public void A_cast_inside_an_action_is_a_nested_header_over_its_casting_roll()
    {
        var cast = Guid.CreateVersion7();
        var casting = One("casting", 4, 5, true);
        var ask = new Ask(Guid.CreateVersion7(), "cast", "Cast which spell?", [Samples.Team1],
            new Options([new AskOption("hex|m", "Hex (3)", []), new AskOption("bolt|m", "Bolt", [])]), false, false, null, cast);
        var (entries, log, n) = Build(Round1, Hold(Samples.Frame),
            new SequenceStarted(cast, "casting", new SequenceContext(Samples.Unit, [], [], "Hold")),
            new AskOpened(ask), new AskAnswered(ask.Id, Samples.Team1, new Chosen("hex|m"), false), new AskClosed(ask.Id, [new TeamAnswer(Samples.Team1, new Chosen("hex|m"))]),
            new StepCompleted(cast, "pick-spell", new Chosen("hex|m"), [], []),
            new StepCompleted(cast, "roll", null, [casting], []),
            new StepCompleted(cast, "targets", new SelectedPieces([Enemy]), [], []),
            new SequenceFinished(cast), new SequenceFinished(Samples.Frame));
        var block = Assert.Single(entries.OfType<ActivationBlock>());
        Assert.Equal("R1 · Team 1 · Battle Brothers · Hold", block.Header);
        var nested = Assert.IsType<ActivationBlock>(Assert.Single(block.Entries));
        Assert.True(nested.Nested);
        Assert.Equal($"Cast Hex (3) → {Foe}", nested.Header);
        Assert.Equal(("hex", (int?)3), (nested.Spell, nested.SpellCost));
        Assert.Equal("Casting · 4+ · ⚄ · cast", Assert.IsType<RollEntry>(Assert.Single(nested.Entries)).Collapsed(CombatLog.Pip));
        Assert.Equal("Casting cast", block.Chain);
        Assert.Equal(casting.Id, CombatLog.Latest(log, n)!.Record.Id);
        var rolls = Assert.IsType<ActivationBlock>(Assert.Single(CombatLog.Filter(entries, LogFilter.Rolls, _ => true, null)));
        Assert.IsType<RollEntry>(Assert.Single(Assert.IsType<ActivationBlock>(Assert.Single(rolls.Entries)).Entries));
    }

    [Fact]
    public void A_single_die_test_reads_its_consequence_and_the_change_draws_no_line()
    {
        var morale = Guid.CreateVersion7();
        var (entries, _, _) = Build(Round1, Hold(Samples.Frame),
            new SequenceStarted(morale, "morale-test", new SequenceContext(Samples.Unit, [], [], null)),
            new StepCompleted(morale, "test", null, [One("morale", 4, 3, false)], []),
            new StepCompleted(morale, "consequence", null, [], [new ConditionSet(new Subject(SubjectKind.Unit, Samples.Unit), "shaken", true)]),
            new SequenceFinished(morale), new SequenceFinished(Samples.Frame));
        var block = Assert.Single(entries.OfType<ActivationBlock>());
        Assert.Equal("Morale · 4+ · ⚂ · failed → Shaken", Assert.IsType<RollEntry>(Assert.Single(block.Entries)).Collapsed(CombatLog.Pip));
    }

    [Fact]
    public void A_condition_on_another_unit_names_it_and_the_actors_own_reads_bare()
    {
        var theirs = Guid.CreateVersion7();
        var (entries, _, _) = Build(Round1,
            new SequenceStarted(theirs, "hold", new SequenceContext(Enemy, [], [], "Hold")),          // a strike-back's fatigue lands on the charger
            new StepCompleted(theirs, "x", null, [], [new ConditionSet(new Subject(SubjectKind.Unit, Samples.Unit), "fatigued", true),
                new ConditionSet(new Subject(SubjectKind.Model, Samples.Model), "shaken", true)]),
            new SequenceFinished(theirs),
            Hold(Samples.Frame),
            new StepCompleted(Samples.Frame, "x", null, [], [new ConditionSet(new Subject(SubjectKind.Model, Samples.Model), "fatigued", true)]),
            new SequenceFinished(Samples.Frame));
        Assert.Equal([["Battle Brothers · Fatigued", "Battle Brothers · Shaken"], ["Fatigued"]],
            entries.OfType<ActivationBlock>().Select(b => b.Entries.OfType<LineEntry>().Select(l => l.Text).ToArray()));
        var outside = Guid.CreateVersion7();                                // a step outside any block: always names the unit
        var (loose, _, _) = Build(Round1, new SequenceStarted(outside, "x", new SequenceContext(null, [], [], null)),
            new StepCompleted(outside, "y", null, [], [new ConditionSet(new Subject(SubjectKind.Unit, Samples.Unit), "shaken", false)]));
        Assert.Equal(["Round 1", "Battle Brothers · Shaken cleared"], loose.OfType<LineEntry>().Select(l => l.Text));
    }

    [Fact]
    public void A_grant_names_the_unit_that_gets_it()
    {
        var (entries, _, _) = Build(Round1, Hold(Samples.Frame),
            new StepCompleted(Samples.Frame, "pick-rule", null, [], [new GrantAdded(Samples.Unit, Samples.Grant)]), new SequenceFinished(Samples.Frame));
        Assert.Equal(["Battle Brothers gets Regeneration"],Assert.Single(entries.OfType<ActivationBlock>()).Entries.OfType<LineEntry>().Select(l => l.Text));
    }

    [Fact]
    public void A_second_undo_to_the_same_seq_names_the_second_action()
    {
        var (entries, _, _) = Build(Round1,
            new SequenceStarted(Samples.Frame, "advance", new SequenceContext(Samples.Unit, [], [], "Advance")), new SequenceFinished(Samples.Frame),
            new Undone(1),
            new SequenceStarted(Guid.CreateVersion7(), "charge", new SequenceContext(Samples.Unit, [], [], "Charge")),
            new Undone(1));
        Assert.Equal(["Round 1", "Undone to: R1 · Team 1 · Battle Brothers · Advance", "Undone to: R1 · Team 1 · Battle Brothers · Charge"],
            entries.OfType<LineEntry>().Select(l => l.Text));
    }

    [Fact]
    public void An_undo_before_a_block_strikes_it_and_names_its_whole_header()
    {
        Guid shooting = Guid.CreateVersion7(), attack = Guid.CreateVersion7();
        var (entries, _, _) = Build(Round1, Hold(Samples.Frame), Shooting(shooting), Attack(attack),
            new StepCompleted(attack, "roll-to-hit", null, [Samples.Hit], []), new SequenceFinished(attack), new SequenceFinished(shooting),
            new SequenceFinished(Samples.Frame), new Undone(1));
        var block = Assert.Single(entries.OfType<ActivationBlock>());
        Assert.True(block.Undone);
        Assert.All(block.Entries, e => Assert.True(e.Undone));
        Assert.Equal($"Undone to: R1 · Team 1 · Battle Brothers · Hold → shoots {Foe}", entries.OfType<LineEntry>().Last().Text);
    }

    [Fact]
    public void Outside_an_action_a_players_deploy_names_the_unit_and_a_round_ends_changes_read_one_each()
    {
        var (entries, _, _) = Build(new ModelsMoved([Samples.Deploy]), new RoundEnded(1, [new ObjectiveControlSet(Samples.Token.Id, Samples.Team1)]));
        Assert.Equal(["Team 1 · Battle Brothers deployed", $"Team 1 took {new Names(Samples.Game, null).Marker(Samples.Token.Id)}"],
            entries.OfType<LineEntry>().Select(l => l.Text));
    }

    [Fact]
    public void A_roll_off_names_each_teams_die_and_the_winner_after_the_tie_breaks()
    {
        Guid first = Guid.CreateVersion7(), second = Guid.CreateVersion7();
        StepCompleted Roll(Guid frame, params RollRecord[] r) => new(frame, "roll-off", null, r, []);
        StepCompleted Order(Guid frame, int winner) => new(frame, "order", null, [], []) { Values = new Dictionary<string, int> { ["winner_ordinal"] = winner } };
        RollRecord Dice(int a, int b) => new(Guid.CreateVersion7(), "roll-off", null, [], [new Die(a, a, false), new Die(b, b, false)]);
        var (entries, _, _) = Build(
            new SequenceStarted(first, "roll-off", new SequenceContext(null, [], [], null)), Roll(first, Dice(4, 1)), Order(first, 1), new SequenceFinished(first),
            new SequenceStarted(second, "roll-off", new SequenceContext(null, [], [], null)), Roll(second, Dice(3, 3), Dice(1, 4)), Order(second, 2), new SequenceFinished(second));
        Assert.Equal(["Roll-off · Team 1 ⚃ · Team 2 ⚀ · Team 1 wins", "Roll-off · Team 1 ⚂ · Team 2 ⚂", "Roll-off · Team 1 ⚀ · Team 2 ⚃ · Team 2 wins"],
            entries.OfType<RollEntry>().Select(r => r.Collapsed(CombatLog.Pip)));
    }

    [Fact]
    public void A_roll_off_tie_edited_away_names_the_winner_on_the_edited_record()
    {
        var frame = Guid.CreateVersion7();
        RollRecord Dice(int a, int b) => new(Guid.CreateVersion7(), "roll-off", null, [], [new Die(a, a, false), new Die(b, b, false)]);
        var (tied, breaker) = (Dice(3, 3), Dice(1, 4));
        var (entries, _, _) = Build(
            new SequenceStarted(frame, "roll-off", new SequenceContext(null, [], [], null)), new StepCompleted(frame, "roll-off", null, [tied, breaker], []),
            new DiceEdited(frame, tied.Id, [new DieReplacement(0, new Die(3, 3, false), new Die(5, 5, false))]),
            new StepCompleted(frame, "order", null, [], []) { Values = new Dictionary<string, int> { ["winner_ordinal"] = 1 } }, new SequenceFinished(frame));
        Assert.Equal(["Roll-off · Team 1 ⚄ · Team 2 ⚂ · Team 1 wins · set by hand ⚂ → ⚄", "Roll-off · 2 dice: ⚀ ⚃"],
            entries.OfType<RollEntry>().Select(r => r.Collapsed(CombatLog.Pip)));
    }
}
