# Combat log, continued: the client

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

Tasks 4 and 5 of [`02-combat-log.md`](02-combat-log.md); goal, architecture, constraints and review focus are that file's. Needs its Tasks 1–3.

## Task 4: `CombatLog`, `RollEntry`, `ActivationBlock`

**Files:** Create `client/src/Core/RollEntry.cs`, `client/src/Core/CombatLog.cs`, `tests/Tabletop.Client.Tests/CombatLogTests.cs`; modify `client/src/Core/Names.cs`.

**Interfaces:** Consumes Task 1's `RollRecord.Basis`, Task 3's `LogFilter`; `LogText.Describe`, `EventApplier.EffectiveLog`, `Names`. Produces
`LogEntry`, `LineEntry`, `DieView`, `RollAmendment`, `RollEntry`, `ActivationBlock` and `CombatLog.{Pip, Build, Filter, Latest}`
exactly as Step 3 writes them, and `Names.Weapon(Guid)`, `Names.TeamOfUnit(Guid)`.

Wording settled here: a result is `passed`/`failed` for one die, else `{successes} {roll}s` (`2 hits`, `1 hit`); a chain part is
`{Roll} {result}` for one die, `{Roll} {successes}` for several, then `{k} model(s) wounded`, `{k} model(s) removed` and each
condition set (`Shaken`) or cleared (`Shaken cleared`); a step change is `LogText`'s line without `the game `. A block opens at the
first `SequenceStarted` with `Context.Action` while none is open and closes at that frame's `SequenceFinished` or
`SequenceAbandoned` (a live block only at a live one, an undone block at its own), or at an `Undone` whose `ToSeq` is before its
start. The chain's `wounded` leaves out a model the same step sends to the shelf; that model counts as removed. A block's targets:
every target, distinct and in order, of each `SequenceStarted` in it. *My units* and *This unit* keep blocks and roll entries whose
actor or a target matches. A roll with neither `Threshold` nor `RequiredRaw` is unjudged: faces only (raw → modified), an empty
result, no collapsed result part, the roll's name alone as its chain part, neutral in the tray.

- [ ] **Step 1: Write the failing tests.** Create `tests/Tabletop.Client.Tests/CombatLogTests.cs`:

```csharp
using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class CombatLogTests
{
    static readonly Guid Enemy = Guid.Parse("0198b7ac-2222-7000-8000-000000000002");
    static Guid Rifle => Samples.Army.Units[0].Models[0].Weapons[0].Id;
    static readonly RoundStarted Round1 = new(1, [Samples.Team1, Samples.Team2], []);

    static (IReadOnlyList<LogEntry> Entries, IReadOnlyList<GameEvent> Log, Names Names) Build(params EventPayload[] history)
    {
        var a = new EventApplier(Samples.Board, Samples.Game);
        a.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, Samples.P1, p, null)));
        var n = new Names(Samples.Game, a.Projection);
        return (CombatLog.Build(a.Log, n), a.Log, n);
    }

    [Fact]
    public void A_roll_reads_in_its_frames_words_with_its_basis_modifiers_dice_and_rules()
    {
        var basis = new ThresholdBasis("Quality", 5, [new ThresholdChange("Reliable", 2, 0), new ThresholdChange("Steady", null, 1)]);
        var hit = new RollRecord(Samples.Roll, "hit", 3, [new RollModifier("cover", 1), new RollModifier("player", 1)], [new Die(3, 5, true), new Die(1, 3, false), new Die(6, 8, true)], null, basis);
        var (entries, log, n) = Build(Round1, new SequenceStarted(Samples.Frame, "shooting", new SequenceContext(Samples.Unit, [Enemy], [Rifle], "Hold")),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [hit], [], ["Reliable"]));
        var roll = Assert.Single(Assert.IsType<ActivationBlock>(entries[1]).Entries.OfType<RollEntry>());
        Assert.Equal($"Battle Brothers → {Names.Short(Enemy)} · Heavy Rifle · Hit 3+ · ⚂ ⚀ ⚅ · 2 hits", roll.Collapsed(CombatLog.Pip));
        Assert.Equal(["Quality 5+", "Reliable: 2+", "Steady: +1", "Target 3+"], roll.Basis);
        Assert.Equal(["+1 cover", "+1 player"], roll.Modifiers);
        Assert.Equal(["3 → 5 pass", "1 → 3 fail", "6 → 8 pass"], roll.Dice.Select(d => d.Text));
        Assert.Equal(["Reliable"], roll.Applied);
        Assert.Equal(Samples.Roll, CombatLog.Latest(log, n)!.Record.Id);
    }

    [Fact]
    public void A_raw_face_roll_is_marked_and_a_roll_without_a_basis_shows_its_threshold_alone()
    {
        var (entries, _, _) = Build(new SequenceStarted(Samples.Frame, "melee", new SequenceContext(Samples.Unit, [], [], null)),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [new RollRecord(Samples.Roll, "hit", 6, [], [new Die(6, 6, true)], 6)], []));
        var roll = entries.OfType<RollEntry>().Single();                   // no action: a roll between blocks
        Assert.Equal(["Target 6+"], roll.Basis);
        Assert.Equal("6 pass (raw face)", roll.Dice[0].Text);
        Assert.Equal("Battle Brothers · Hit 6+ · ⚅ · passed", roll.Collapsed(CombatLog.Pip));
    }

    [Fact]
    public void Rerolls_and_edits_attach_to_the_roll_they_name_and_a_players_modifier_is_its_own_line()
    {
        var other = Samples.Hit with { Id = Guid.CreateVersion7() };
        var (entries, _, _) = Build(new SequenceStarted(Samples.Frame, "x", new SequenceContext(Samples.Unit, [], [], null)),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [Samples.Hit, other], []),
            new DiceEdited(Samples.Frame, Samples.Roll, [new DieReplacement(0, new Die(6, 6, true), new Die(1, 1, false))]),
            new DiceRerolled(Samples.Frame, Samples.Roll, [new DieReplacement(1, new Die(2, 2, false), new Die(5, 5, true))]),
            new ModifierAdded(Samples.Frame, "hit", new RollModifier("Clay", 1)));
        var rolls = entries.OfType<RollEntry>().ToList();
        Assert.Equal(["Set by hand: replaced 6", "Rerolled: replaced 2"], rolls[0].Amendments.Select(a => a.Text));
        Assert.Equal([1, 5], rolls[0].Dice.Select(d => d.Raw));
        Assert.Empty(rolls[1].Amendments);                                  // the other pool of the same step
        Assert.Equal([6, 2], rolls[1].Dice.Select(d => d.Raw));
        Assert.Equal(["Clay began x with Battle Brothers", "Clay added +1 to the hit roll"], entries.OfType<LineEntry>().Select(l => l.Text));
    }

    [Fact]
    public void An_action_frame_groups_its_entries_under_one_header_with_a_chain_summary()
    {
        var nested = Guid.CreateVersion7();
        var (entries, _, _) = Build(new RoundStarted(2, [Samples.Team1, Samples.Team2], []),
            new SequenceStarted(Samples.Frame, "advance", new SequenceContext(Samples.Unit, [Enemy], [Rifle], "Advance")),
            new SequenceStarted(nested, "attack-sequence", new SequenceContext(Samples.Unit, [Enemy], [Rifle], "Advance")),
            new StepCompleted(nested, "roll-to-hit", null, [Samples.Hit], []),
            new StepCompleted(nested, "remove", null, [], [new WoundsSet(Samples.Model, 1), new ConditionSet(new Subject(SubjectKind.Unit, Samples.Unit), "shaken", true)]),
            new SequenceFinished(nested), new SequenceFinished(Samples.Frame), new TurnEnded(Samples.Team1, false, []));
        Assert.Equal(3, entries.Count);
        var block = Assert.IsType<ActivationBlock>(entries[1]);
        Assert.Equal("Round 2 · Team 1 · Battle Brothers · Advance", block.Header);
        Assert.Equal("Hit 1 → 1 model wounded → Shaken", block.Chain);
        Assert.IsType<LineEntry>(entries[2]);
    }

    [Fact]
    public void An_undone_activation_closes_struck_through_and_an_undo_inside_one_keeps_it_open()
    {
        var second = Guid.CreateVersion7();
        var (entries, log, n) = Build(Round1,
            new SequenceStarted(Samples.Frame, "advance", new SequenceContext(Samples.Unit, [], [], "Advance")),
            new StepCompleted(Samples.Frame, "roll-to-hit", null, [Samples.Hit], []),
            new Undone(1),                                                                              // before the block: closes it
            new SequenceStarted(second, "hold", new SequenceContext(Samples.Unit, [], [], "Hold")),
            new StepCompleted(second, "roll-to-hit", null, [Samples.Hit with { Id = Guid.CreateVersion7() }], []),
            new Undone(5),                                                                              // inside the block: stays open
            new SequenceFinished(second));
        var blocks = entries.OfType<ActivationBlock>().ToList();
        Assert.Equal([true, false], blocks.Select(b => b.Undone));
        Assert.All(blocks[0].Entries, e => Assert.True(e.Undone));
        Assert.Equal(("", ""), (blocks[0].Chain, blocks[1].Chain));                                     // an undone roll is not in a chain
        Assert.True(blocks[1].Entries.OfType<RollEntry>().Single().Undone);
        Assert.Contains(blocks[1].Entries, e => e.Event.Payload is SequenceFinished);
        Assert.Contains(entries, e => e is LineEntry { Event.Payload: Undone });
        Assert.Null(CombatLog.Latest(log, n));
    }

    [Fact]
    public void Filters_keep_rolls_my_units_or_the_carded_unit()
    {
        var mine = Guid.CreateVersion7(); var theirs = Guid.CreateVersion7();
        var (entries, _, n) = Build(Round1,
            new SequenceStarted(mine, "hold", new SequenceContext(Samples.Unit, [], [], "Hold")), new SequenceFinished(mine),
            new SequenceStarted(theirs, "advance", new SequenceContext(Enemy, [], [], "Advance")),
            new StepCompleted(theirs, "roll-to-hit", null, [Samples.Hit], []), new SequenceFinished(theirs));
        bool Mine(Guid u) => n.TeamOfUnit(u) == Samples.Team1;
        Guid ActorOf(LogEntry e) => ((ActivationBlock)e).Actor!.Value;
        Assert.Equal([Samples.Unit], CombatLog.Filter(entries, LogFilter.Mine, Mine, null).Select(ActorOf));
        Assert.All(Assert.IsType<ActivationBlock>(Assert.Single(CombatLog.Filter(entries, LogFilter.Rolls, Mine, null))).Entries, e => Assert.IsType<RollEntry>(e));
        Assert.Equal([Enemy], CombatLog.Filter(entries, LogFilter.Unit, Mine, Enemy).Select(ActorOf));
        Assert.Empty(CombatLog.Filter(entries, LogFilter.Unit, Mine, null));
        Assert.Same(entries, CombatLog.Filter(entries, LogFilter.All, Mine, null));
    }
}
```

- [ ] **Step 2:** `dotnet test tests/Tabletop.Client.Tests --filter CombatLogTests` — expected: compile errors, `CombatLog`, `RollEntry`, `LogEntry` do not exist.

- [ ] **Step 3: Write the model.** `Names.cs` gains:

```csharp
    /// <summary>A weapon by its snapshot name, from the slot that carries it.</summary>
    public string Weapon(Guid id) => projection?.Models.Values.SelectMany(m => m.Slot.Weapons).FirstOrDefault(w => w.Id == id)?.Name ?? Short(id);
    public Guid? TeamOfUnit(Guid unitId) => projection?.Units.GetValueOrDefault(unitId) is { } u ? game.Participants.FirstOrDefault(x => x.Id == u.ParticipantId)?.TeamId : null;
```

Create `client/src/Core/RollEntry.cs`:

```csharp
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>A combat log entry. Undone: struck through, an undone event or a block whose start was undone.</summary>
public abstract record LogEntry(long Seq, GameEvent Event, bool Undone);
public sealed record LineEntry(long Seq, GameEvent Event, bool Undone, string Text) : LogEntry(Seq, Event, Undone);

/// <summary>A die as the log shows it. RawJudged: the roll judged the raw face alone (RequiredRaw).</summary>
public sealed record DieView(int Raw, int Modified, bool Success, bool RawJudged)
{
    public string Text => $"{Raw}{(Modified == Raw ? "" : $" → {Modified}")} {(Success ? "pass" : "fail")}{(RawJudged ? " (raw face)" : "")}";
}

/// <summary>A reroll or hand edit of one roll, with the faces it replaced.</summary>
public sealed record RollAmendment(long Seq, string Kind, IReadOnlyList<int> Replaced, bool Undone)
{
    public string Text => $"{Kind}: replaced {string.Join(" ", Replaced)}";
}

/// <summary>One RollRecord of a step_completed in its frame's words (client UX spec → Combat log). Dice stand after every live amendment.</summary>
public sealed record RollEntry(long Seq, GameEvent Event, bool Undone, Guid FrameId, RollRecord Record, SequenceContext Context, string Title,
    IReadOnlyList<string> Basis, IReadOnlyList<string> Modifiers, IReadOnlyList<DieView> Dice, IReadOnlyList<RollAmendment> Amendments,
    IReadOnlyList<string> Applied) : LogEntry(Seq, Event, Undone)
{
    public int Successes => Dice.Count(d => d.Success);
    public string Result => Dice.Count == 1 ? (Dice[0].Success ? "passed" : "failed") : $"{Successes} {Record.Roll}{(Successes == 1 ? "" : "s")}";
    public string Chain => $"{Capital(Record.Roll)} {(Dice.Count == 1 ? Result : Successes.ToString())}";
    public string Collapsed(Func<int, string> face) => $"{Title} · {string.Join(" ", Dice.Select(d => face(d.Raw)))} · {Result}";
    public static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public static RollEntry Of(GameEvent e, bool undone, StepCompleted step, RollRecord r, SequenceContext c, IEnumerable<(GameEvent Event, bool Undone)> amendments, Names n)
    {
        var dice = r.Dice.ToList(); var amended = new List<RollAmendment>();
        foreach (var (a, dead) in amendments)
        {
            var (kind, replaced) = a.Payload is DiceRerolled x ? ("Rerolled", x.Replaced) : ("Set by hand", ((DiceEdited)a.Payload).Replaced);
            amended.Add(new RollAmendment(a.Seq, kind, replaced.Select(d => d.Old.Raw).ToList(), dead));
            if (!dead) foreach (var d in replaced.Where(d => d.Index >= 0 && d.Index < dice.Count)) dice[d.Index] = d.New;
        }
        var title = new List<string>();
        if (c.ActorUnitId is { } actor) title.Add(c.TargetUnitIds.Count == 0 ? n.Unit(actor) : $"{n.Unit(actor)} → {string.Join(", ", c.TargetUnitIds.Select(n.Unit))}");
        if (c.WeaponIds.Count > 0) title.Add(string.Join(", ", c.WeaponIds.Select(n.Weapon).Distinct()));
        title.Add($"{Capital(r.Roll)}{(r.Threshold is { } t ? $" {t}+" : "")}");
        return new RollEntry(e.Seq, e, undone, step.FrameId, r with { Dice = dice }, c, string.Join(" · ", title), BasisLines(r),
            r.Modifiers.Select(m => $"{m.Amount:+#;-#;0} {m.Source}").ToList(), dice.Select(d => new DieView(d.Raw, d.Modified, d.Success, r.RequiredRaw is not null)).ToList(),
            amended, step.Applied ?? []);
    }

    /// <summary>The printed value, each change with its source, the result; the threshold alone when the record has no basis.</summary>
    static IReadOnlyList<string> BasisLines(RollRecord r) => r.Threshold is not { } t ? [] : r.Basis is not { } b ? [$"Target {t}+"]
        : [$"{b.Stat} {b.Printed}+", .. b.Changes.Select(c => c.SetTo is { } to ? $"{c.Source}: {to}+" : $"{c.Source}: {c.Amount:+#;-#;0}"), $"Target {t}+"];
}
```

Create `client/src/Core/CombatLog.cs`:

```csharp
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>An action frame's entries, from its sequence_started to its finish or abandonment, under one header.</summary>
public sealed record ActivationBlock(long Seq, GameEvent Event, bool Undone, string Header, Guid? Actor, IReadOnlyList<Guid> Targets,
    IReadOnlyList<LogEntry> Entries, IReadOnlyList<string> ChainParts) : LogEntry(Seq, Event, Undone)
{
    public string Chain => string.Join(" → ", ChainParts);
}

/// <summary>The play log (client UX spec → Combat log): every event, undone ones flagged; action frames grouped; one RollEntry per
/// RollRecord with its rerolls and edits attached by RollId; anything else LogText's line. Copies the log's words and names no rule.</summary>
public static class CombatLog
{
    static readonly SequenceContext NoContext = new(null, [], [], null);

    public static string Pip(int face) => face is >= 1 and <= 6 ? char.ConvertFromUtf32(0x2680 + face - 1) : face.ToString();

    public static IReadOnlyList<LogEntry> Build(IReadOnlyList<GameEvent> log, Names n)
    {
        var live = EventApplier.EffectiveLog(log).Select(e => e.Seq).ToHashSet();
        bool Dead(GameEvent e) => !live.Contains(e.Seq) && e.Payload is not Undone;
        var frames = new Dictionary<Guid, SequenceContext>();
        foreach (var s in log.Select(e => e.Payload).OfType<SequenceStarted>()) frames[s.FrameId] = s.Context;
        var rolled = log.Select(e => e.Payload).OfType<StepCompleted>().SelectMany(s => s.Rolls).Select(r => r.Id).ToHashSet();
        var amendments = log.Where(e => e.Payload is DiceRerolled or DiceEdited)
            .ToLookup(e => e.Payload is DiceRerolled r ? r.RollId : ((DiceEdited)e.Payload).RollId, e => (e, Dead(e)));
        var top = new List<LogEntry>(); Draft? open = null; var round = 0;
        foreach (var e in log)
        {
            var dead = Dead(e);
            if (e.Payload is RoundStarted r && !dead) round = r.Round;
            if (open is not null && e.Payload is Undone u && u.ToSeq < open.Start.Seq) { top.Add(open.Close(Dead(open.Start))); open = null; }
            if (open is null && e.Payload is SequenceStarted { Context.Action: { } action } started) { open = new Draft(e, started, Header(round, started.Context, action, n)); continue; }
            var into = open?.Entries ?? top;
            switch (e.Payload)
            {
                case DiceRerolled d when rolled.Contains(d.RollId): break;
                case DiceEdited d when rolled.Contains(d.RollId): break;
                case StepCompleted step:
                    foreach (var roll in step.Rolls)
                    {
                        var entry = RollEntry.Of(e, dead, step, roll, frames.GetValueOrDefault(step.FrameId) ?? NoContext, amendments[roll.Id], n);
                        into.Add(entry);
                        if (!dead) open?.Chain.Add(entry.Chain);
                    }
                    into.AddRange(step.Changes.Select(c => new LineEntry(e.Seq, e, dead, Change(c, e.Seq, n))));
                    if (step.Rolls.Count == 0 && step.Changes.Count == 0) into.Add(new LineEntry(e.Seq, e, dead, LogText.Describe(e, n)));
                    if (!dead) open?.Chain.AddRange(Changes(step.Changes));
                    break;
                default: into.Add(new LineEntry(e.Seq, e, dead, LogText.Describe(e, n))); break;
            }
            var ended = e.Payload switch { SequenceFinished x => x.FrameId, SequenceAbandoned x => x.FrameId, _ => (Guid?)null };
            if (open is not null && ended == open.S.FrameId) { top.Add(open.Close(Dead(open.Start))); open = null; }
        }
        if (open is not null) top.Add(open.Close(Dead(open.Start)));
        return top;
    }

    public static IReadOnlyList<LogEntry> Filter(IReadOnlyList<LogEntry> entries, LogFilter filter, Func<Guid, bool> mine, Guid? unitId) => filter switch
    {
        LogFilter.Rolls => entries.Select(RollsOnly).OfType<LogEntry>().ToList(),
        LogFilter.Mine => entries.Where(e => Involves(e, mine)).ToList(),
        LogFilter.Unit => unitId is { } id ? entries.Where(e => Involves(e, u => u == id)).ToList() : [],
        _ => entries,
    };

    /// <summary>The last roll still standing: what the dice tray shows.</summary>
    public static RollEntry? Latest(IReadOnlyList<GameEvent> log, Names n) =>
        Build(log, n).SelectMany(e => e is ActivationBlock b ? b.Entries : (IEnumerable<LogEntry>)[e]).OfType<RollEntry>().LastOrDefault(r => !r.Undone);

    static LogEntry? RollsOnly(LogEntry e) => e switch
    {
        RollEntry => e,
        ActivationBlock b when b.Entries.OfType<RollEntry>().Any() => b with { Entries = b.Entries.OfType<RollEntry>().ToList() },
        _ => null,
    };

    static bool Involves(LogEntry e, Func<Guid, bool> unit) => e switch
    {
        ActivationBlock b => b.Actor is { } a && unit(a) || b.Targets.Any(unit),
        RollEntry r => r.Context.ActorUnitId is { } a && unit(a) || r.Context.TargetUnitIds.Any(unit),
        _ => false,
    };

    static string Header(int round, SequenceContext c, string action, Names n) => string.Join(" · ", new[]
        { $"Round {round}", c.ActorUnitId is { } a && n.TeamOfUnit(a) is { } t ? n.Team(t) : null, c.ActorUnitId is { } u ? n.Unit(u) : null, action }.OfType<string>());

    /// <summary>LogText's words for a step's change, without the actor: the engine applied it.</summary>
    static string Change(StateChange c, long seq, Names n)
    {
        var text = LogText.Describe(new GameEvent(seq, null, c, null), n); var engine = n.Participant(null) + " ";
        return text.StartsWith(engine, StringComparison.Ordinal) ? text[engine.Length..] : text;
    }

    static IEnumerable<string> Changes(IReadOnlyList<StateChange> changes)
    {
        var wounded = changes.OfType<WoundsSet>().Select(w => w.ModelId).Distinct().Count();
        var removed = changes.OfType<ModelsMoved>().SelectMany(m => m.Moves).Count(m => m.To is OnShelf);
        if (wounded > 0) yield return $"{wounded} model{(wounded == 1 ? "" : "s")} wounded";
        if (removed > 0) yield return $"{removed} model{(removed == 1 ? "" : "s")} removed";
        foreach (var c in changes.OfType<ConditionSet>()) yield return RollEntry.Capital(c.Condition) + (c.Present ? "" : " cleared");
    }

    sealed class Draft(GameEvent start, SequenceStarted s, string header)
    {
        public GameEvent Start { get; } = start; public SequenceStarted S { get; } = s;
        public List<LogEntry> Entries { get; } = []; public List<string> Chain { get; } = [];
        public ActivationBlock Close(bool undone) => new(Start.Seq, Start, undone, header, S.Context.ActorUnitId, S.Context.TargetUnitIds, Entries, Chain);
    }
}
```

- [ ] **Step 4:** `dotnet test tests/Tabletop.Client.Tests` — expected: PASS, the six `CombatLogTests` included.
- [ ] **Step 5: Commit** `git add client/src/Core tests/Tabletop.Client.Tests && git commit -m "Build the combat log from the event log"` with the two trailer lines.

---

## Task 5: `CombatLogPanel` in the log drawer

**Files:** Create `client/src/Hud/CombatLogPanel.cs`; modify `client/src/Hud/LogDrawer.cs`, `client/src/Hud/StatSheet.cs`, `client/src/Screens/GameScreen.Play.cs`.

**Interfaces:** Consumes Tasks 3–4; `Session.{Log, Projection, Game, MyTeamId, PendingUndoTo, Rules, Settings, UpdateSettings, UndoToAsync}`,
`Guarded.{Run, Command}`, `PieceMenu.AskInteger`, `TableView.{Pieces, PieceAt}`. Produces `CombatLogPanel.Bind(Session, TableView, Action<string> report)`,
`CombatLogPanel.FocusUnit { get; set; }` (the *This unit* filter's unit), `CombatLogPanel.Reveal(Guid rollId)`, `LogDrawer.CombatLog` (the panel),
`LogDrawer.OpenAt(Guid rollId)`; `StatSheet.UnitId` and `StatSheet.FocusChanged`, the names plan 03's `UnitCard` keeps when it replaces `StatSheet`.

- [ ] **Step 1: Write the panel.** Create `client/src/Hud/CombatLogPanel.cs`:

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>The play log (client UX spec → Combat log): a filter over CombatLog's blocks, rolls and lines. Renders only; every word is CombatLog's.</summary>
public partial class CombatLogPanel : VBoxContainer
{
    static readonly Color Struck = new(0.5f, 0.5f, 0.5f), Pending = new(0.6f, 0.6f, 0.6f), Lit = Colors.Yellow, Plain = Colors.White,
        Pass = new(0.35f, 0.8f, 0.4f), Fail = new(0.85f, 0.4f, 0.4f);
    static readonly string[] FilterLabels = ["All", "Rolls", "My units", "This unit"];   // LogFilter's order
    Session _s = null!; TableView _view = null!; Action<string> _report = _ => { };
    readonly OptionButton _filter = new();
    readonly ScrollContainer _scroll = new() { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    readonly VBoxContainer _rows = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    readonly HashSet<string> _open = [];
    Guid? _reveal, _focus; Control? _revealed; bool _toEnd;
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onBoard;

    /// <summary>The unit on the card, which the This unit filter keeps; set by the screen when the card changes.</summary>
    public Guid? FocusUnit
    {
        get => _focus;
        set { if (_focus == value) return; _focus = value; if (_s is not null && _s.Settings.LogFilter == LogFilter.Unit) Render(false); }
    }

    public void Bind(Session s, TableView view, Action<string> report)
    {
        _s = s; _view = view; _report = report;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        for (var i = 0; i < FilterLabels.Length; i++) _filter.AddItem(FilterLabels[i], i);
        _filter.Selected = (int)s.Settings.LogFilter;
        _filter.ItemSelected += i => { _s.UpdateSettings(x => x with { LogFilter = (LogFilter)(int)i }); Render(false); };
        AddChild(_filter); AddChild(_scroll); _scroll.AddChild(_rows);
        _onEvents = _ => Render(true); _onBoard = () => Render(true);
        s.EventsApplied += _onEvents; s.BoardChanged += _onBoard;
        Render(true);
    }

    public override void _ExitTree() { if (_s is not null) { _s.EventsApplied -= _onEvents; _s.BoardChanged -= _onBoard; } }

    /// <summary>Expand the block and the roll holding this RollRecord and scroll to it.</summary>
    public void Reveal(Guid rollId) { _reveal = rollId; Render(false); }

    void Render(bool toEnd)
    {
        foreach (var c in _rows.GetChildren()) { _rows.RemoveChild(c); c.QueueFree(); }
        _revealed = null; _toEnd = toEnd;
        if (_s.Projection is not { } p || _s.Game is not { } game) return;
        var n = new Names(game, p);
        foreach (var e in CombatLog.Filter(CombatLog.Build(_s.Log, n), _s.Settings.LogFilter, u => n.TeamOfUnit(u) is { } t && t == _s.MyTeamId, _focus))
            Add(_rows, e);
        _reveal = null;
        CallDeferred(nameof(Scroll));
    }

    static string Key(LogEntry e) => e is RollEntry r ? r.Record.Id.ToString() : e.Seq.ToString();
    bool Holds(LogEntry e) => _reveal is { } id && (e is RollEntry r && r.Record.Id == id || e is ActivationBlock b && b.Entries.Any(Holds));

    void Add(VBoxContainer into, LogEntry e)
    {
        if (Holds(e)) _open.Add(Key(e));
        var open = _open.Contains(Key(e));
        var text = e switch
        {
            ActivationBlock b => $"{(open ? "▾" : "▸")} {b.Header}{(open || b.Chain.Length == 0 ? "" : $"  ·  {b.Chain}")}",
            RollEntry r => $"{(open ? "▾" : "▸")} {r.Collapsed(CombatLog.Pip)}",
            LineEntry l => $"#{l.Seq}  {l.Text}",
            _ => "",
        };
        var row = new HBoxContainer(); into.AddChild(row);
        var button = new Button { Text = e.Undone ? Strike(text) : text, Flat = true, Alignment = HorizontalAlignment.Left, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
        if (e.Undone) button.Modulate = Struck; else if (_s.PendingUndoTo is { } to && e.Seq > to) button.Modulate = Pending;
        button.Pressed += () => { Highlight(e); if (e is LineEntry) return; if (!_open.Remove(Key(e))) _open.Add(Key(e)); Render(false); };
        row.AddChild(button);
        if (e is RollEntry shown && shown.Record.Id == _reveal) _revealed = button;
        if (!e.Undone && e.Event.Payload is not Undone) AddButton(row, "Undo to here", () => Guarded.Run(() => _s.UndoToAsync(e.Seq), _report));
        if (!open) return;
        var inner = new VBoxContainer(); var indent = new MarginContainer(); indent.AddThemeConstantOverride("margin_left", 16);
        indent.AddChild(inner); into.AddChild(indent);
        if (e is ActivationBlock block) foreach (var x in block.Entries) Add(inner, x);
        if (e is RollEntry roll) Details(inner, roll);
    }

    void Details(VBoxContainer into, RollEntry r)
    {
        foreach (var line in r.Basis.Concat(r.Modifiers)) into.AddChild(new Label { Text = line });
        foreach (var d in r.Dice) into.AddChild(new Label { Text = $"{CombatLog.Pip(d.Raw)}  {d.Text}", Modulate = d.Success ? Pass : Fail });
        foreach (var a in r.Amendments) into.AddChild(new Label { Text = a.Undone ? Strike(a.Text) : a.Text, Modulate = a.Undone ? Struck : Plain });
        var chips = new HFlowContainer(); into.AddChild(chips);   // the rules that acted in the step, with their catalog text
        foreach (var name in r.Applied) chips.AddChild(new Button { Text = name, Flat = true, TooltipText = _s.Rules.TextOf(name) ?? name });
        if (r.Undone) return;
        var actions = new HBoxContainer(); into.AddChild(actions);
        AddButton(actions, "Reroll all", () => Send(new RerollDice(r.FrameId, r.Record.Id, [])));
        AddButton(actions, "Set die 1…", () => PieceMenu.AskInteger(this, "Raw value for die 1", v => Send(new SetDice(r.FrameId, r.Record.Id, [new DieValue(0, v)]))));
        AddButton(actions, "+1", () => Send(new AddModifier(r.FrameId, r.Record.Roll, 1)));
    }

    static void AddButton(Container row, string text, Action then) { var b = new Button { Text = text, Flat = true }; b.Pressed += then; row.AddChild(b); }

    void Highlight(LogEntry e)
    {
        if (!IsInstanceValid(_view)) return;
        foreach (var piece in _view.Pieces.OfType<PieceNode>()) piece.SetHighlight(null);
        foreach (var id in LogText.Touched(e.Event)) _view.PieceAt(id)?.SetHighlight(Lit);
    }

    void Send(Command command) => Guarded.Command(_s, command, _report);
    /// <summary>A Button has no strike style: U+0336 after each character.</summary>
    static string Strike(string s) => string.Concat(s.Select(c => $"{c}̶"));

    void Scroll()
    {
        if (!IsInstanceValid(_scroll)) return;
        if (_revealed is not null && IsInstanceValid(_revealed)) _scroll.EnsureControlVisible(_revealed);
        else if (_toEnd) _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
    }
}
```

- [ ] **Step 2: Put it in the drawer.** In `LogDrawer.cs`: `_drawer` becomes `new() { CustomMinimumSize = new Vector2(640, 420) }`;
  a field `readonly CombatLogPanel _log = new();` with `public CombatLogPanel CombatLog => _log;`; `Bind` keeps its signature, keeps the
  Animate checkbox with the comment `// Animate stays here until plan 04's Settings dialog takes it.`, puts `inner.AddChild(_log);
  _log.Bind(session, view, message => Reported?.Invoke(message));` in place of the `LogPanel`, and ends `SetOpen(session.Settings.LogOpen);`.
  Replace `Toggle` and add:

```csharp
    public void Toggle() { SetOpen(!_drawer.Visible); _s.UpdateSettings(x => x with { LogOpen = _drawer.Visible }); }

    /// <summary>The dice tray's click: open the log at that roll.</summary>
    public void OpenAt(Guid rollId) { if (!_drawer.Visible) Toggle(); _log.Reveal(rollId); }

    void SetOpen(bool open) { _drawer.Visible = open; _toggle.Text = open ? "Close log (G)" : "Log (G)"; }
```

  `StatSheet.cs` gains `public Guid? UnitId => _focus.UnitId;`, `public event Action<Guid?>? FocusChanged;`, a field `Guid? _told;`, and as
  `Render`'s first line `if (_told != _focus.UnitId) { _told = _focus.UnitId; FocusChanged?.Invoke(_told); }`. In `GameScreen.Play.cs`, after
  `_logDrawer.Bind(...)`: `_sheet.FocusChanged += id => { if (_logDrawer is { } d) d.CombatLog.FocusUnit = id; };` and
  `_logDrawer.CombatLog.FocusUnit = _sheet.UnitId;` (the sheet told its first unit in its own `Bind`).
- [ ] **Step 3:** `dotnet build client/Tabletop.csproj && ./scripts/check-client` — expected: build clean; check-client prints nothing.
- [ ] **Step 4: Run it.** `./scripts/client`, join a game the sim keeps (Task 7's `--keep` run) or play a shooting activation:
  the drawer is 640 px wide; an activation is one `Round · Team · Unit · Action` row with its chain; expanding a roll shows the
  basis, modifiers, dice, amendments and rule chips; Undo to here, Reroll all, Set die 1 and +1 act; the filter and open state
  survive a restart.
- [ ] **Step 5: Commit** `git add client/src/Hud client/src/Screens/GameScreen.Play.cs && git commit -m "Show the combat log in the log drawer"` with the two trailer lines.
