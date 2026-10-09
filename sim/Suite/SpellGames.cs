using Tabletop.GameServer.Properties;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>
/// The spell games, generated from the pinned set's books: one game per book that lists spells. Every model of the book's army is a
/// Caster(6) whose slot carries the book (p.13: "the Caster's spells must be picked from their own faction"), so the cast Ask offers
/// exactly the book's spells. The policy picks which spell each caster tries, at whom and with how many tokens; a game's seed is one
/// under which every spell of the book is cast and resolved at least once.
/// </summary>
public static class SpellGames
{
    public const string NamePrefix = "spells-";

    /// <summary>The game's name: the book's name, lower-case and hyphenated, after the prefix.</summary>
    public static string NameOf(BookRecord book) =>
        NamePrefix + string.Join('-', new string([.. book.Name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : ' ')])
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The books with spells, in uid order.</summary>
    public static IReadOnlyList<BookRecord> Books(PropertySet set) =>
        set.Books.Values.Where(b => b.Spells.Count > 0).OrderBy(b => b.Uid, StringComparer.Ordinal).ToList();

    static readonly ScenarioWeapon Rifle = new("Rifle", new Dictionary<string, string> { ["Range"] = "24\"", ["Attacks"] = "1" }, []);
    static readonly ScenarioWeapon ApRifle = new("Rifle", new Dictionary<string, string> { ["Range"] = "24\"", ["Attacks"] = "1" }, [new ScenarioRule(0, "AP", 1)]);
    static readonly ScenarioWeapon HandWeapon = new("Hand Weapon", new Dictionary<string, string> { ["Attacks"] = "1" }, []);

    /// <summary>
    /// The book's army: two units of three, every model Caster(6) on the book. The targets: three units of three with AP(1) rifles, so a
    /// spell that takes AP away has some to take. Every model is Tough(6), so the targets outlast four rounds of spells, and every model
    /// makes for the table's centre, so the 6" spells find their targets.
    /// </summary>
    public static Scenario Scenario(BookRecord book, ulong seed) => new ScenarioBuilder(NameOf(book), seed)
        .RoundCap(4).Table(36, 24)
        .Army(book.Name, 2, 3,
            modelRules: [new ScenarioRule(1, "Caster", 6), new ScenarioRule(2, "Caster", 6), new ScenarioRule(1, "Tough", 6), new ScenarioRule(2, "Tough", 6)],
            weapons: [Rifle, HandWeapon], armyBook: book.Uid)
        .Army("Targets", 3, 3,
            modelRules: [new ScenarioRule(1, "Tough", 6), new ScenarioRule(2, "Tough", 6), new ScenarioRule(3, "Tough", 6)],
            weapons: [ApRifle, HandWeapon])
        .Seeking((_, view) => new Point2(view.Table.Width / 2, view.Table.Length / 2))
        .Build();

    public static TestGame Game(BookRecord book, ulong seed, IReadOnlyList<DiceFix>? dice = null, IReadOnlyList<GameCheck>? checks = null) =>
        new(NameOf(book), SuiteCatalog.SpellsPrefix + book.Uid, Scenario(book, seed), book.Spells, dice ?? [],
            [Checks.Ended(), Checks.Invariants(), Checks.FiredAll(), .. checks ?? []]);
}

/// <summary>One try at a spell, as the casting frame logged it: who tried it, the casting roll, and the targets when it resolved.</summary>
public sealed record SpellCast(Guid Frame, Guid Unit, string Spell, Guid Caster, RollRecord? Roll, bool Passed, IReadOnlyList<Guid> Targets);

/// <summary>
/// The effective log read as frames: which frame each frame was started under (the stack, rebuilt from sequence_started and
/// sequence_finished/abandoned), each frame's steps, and the casts. Reading only; it decides nothing a rule decides.
/// </summary>
public sealed class SpellLog
{
    public IReadOnlyList<EventPayload> Payloads { get; }
    readonly Dictionary<Guid, Guid?> _parent = [];
    readonly Dictionary<Guid, SequenceStarted> _started = [];
    readonly Dictionary<Guid, int> _at = [];
    readonly Dictionary<Guid, List<Die>> _dice = [];

    public SpellLog(IReadOnlyList<GameEvent> effectiveLog)
    {
        Payloads = effectiveLog.Select(e => e.Payload).ToList();
        var stack = new List<Guid>();
        for (var i = 0; i < Payloads.Count; i++)
            switch (Payloads[i])
            {
                case SequenceStarted s:
                    _parent[s.FrameId] = stack.Count == 0 ? null : stack[^1];
                    _started[s.FrameId] = s; _at[s.FrameId] = i;
                    stack.Add(s.FrameId);
                    break;
                case SequenceFinished f: Pop(stack, f.FrameId); break;
                case SequenceAbandoned a: Pop(stack, a.FrameId); break;
                case StepCompleted c: foreach (var r in c.Rolls) _dice[r.Id] = [.. r.Dice]; break;
                case DiceEdited d: Replace(d.RollId, d.Replaced); break;
                case DiceRerolled d: Replace(d.RollId, d.Replaced); break;
            }
    }

    void Replace(Guid roll, IReadOnlyList<DieReplacement> replaced)
    {
        if (_dice.TryGetValue(roll, out var dice)) foreach (var r in replaced) dice[r.Index] = r.New;
    }

    /// <summary>A roll's dice as they stand after every edit and reroll the log holds (a step_completed keeps the dice as first rolled).</summary>
    public IReadOnlyList<Die> Dice(RollRecord roll) => _dice.TryGetValue(roll.Id, out var dice) ? dice : roll.Dice;

    static void Pop(List<Guid> stack, Guid frame)
    {
        var at = stack.LastIndexOf(frame);
        if (at >= 0) stack.RemoveRange(at, stack.Count - at);
    }

    public SequenceStarted Frame(Guid frame) => _started[frame];
    public int StartOf(Guid frame) => _at[frame];
    public Guid? ParentOf(Guid frame) => _parent.GetValueOrDefault(frame);

    /// <summary>The frames started directly under this one, in log order.</summary>
    public IReadOnlyList<SequenceStarted> Children(Guid frame) =>
        _started.Values.Where(s => _parent[s.FrameId] == frame).OrderBy(s => _at[s.FrameId]).ToList();

    /// <summary>The frames started anywhere under this one, in log order.</summary>
    public IReadOnlyList<SequenceStarted> Descendants(Guid frame) =>
        _started.Values.Where(s => IsUnder(s.FrameId, frame)).OrderBy(s => _at[s.FrameId]).ToList();

    public bool IsUnder(Guid frame, Guid ancestor)
    {
        for (var f = ParentOf(frame); f is not null; f = ParentOf(f.Value)) if (f == ancestor) return true;
        return false;
    }

    public IReadOnlyList<StepCompleted> Steps(Guid frame) => Payloads.OfType<StepCompleted>().Where(s => s.FrameId == frame).ToList();

    /// <summary>Every frame of a sequence, in log order.</summary>
    public IReadOnlyList<SequenceStarted> Frames(string sequence) =>
        _started.Values.Where(s => s.Sequence == sequence).OrderBy(s => _at[s.FrameId]).ToList();

    /// <summary>Every try at a spell: a casting frame whose pick-spell chose it ("&lt;spell&gt;|&lt;caster model&gt;", GfCasting's option ids).</summary>
    public IReadOnlyList<SpellCast> Casts(string spell) => Frames("casting").Select(Cast).OfType<SpellCast>().Where(c => c.Spell == spell).ToList();

    SpellCast? Cast(SequenceStarted frame)
    {
        var steps = Steps(frame.FrameId);
        if (steps.FirstOrDefault(s => s.Step == "pick-spell")?.Answer is not Chosen { OptionId: var option }) return null;
        var cut = option.LastIndexOf('|');
        if (cut <= 0 || !Guid.TryParse(option.AsSpan(cut + 1), out var caster)) return null;
        var roll = steps.FirstOrDefault(s => s.Step == "roll");
        var targets = steps.FirstOrDefault(s => s.Step == "targets")?.Answer is SelectedPieces p ? p.PieceIds : [];
        return new SpellCast(frame.FrameId, frame.Context.ActorUnitId!.Value, option[..cut], caster,
            roll?.Rolls.FirstOrDefault(r => r.Roll == "casting"), roll?.Values?.GetValueOrDefault("passed") == 1, targets);
    }

    /// <summary>The grants a cast's effects steps wrote.</summary>
    public IReadOnlyList<GrantAdded> GrantsOf(SpellCast cast) =>
        Steps(cast.Frame).Where(s => s.Step == "effects").SelectMany(s => s.Changes).OfType<GrantAdded>().ToList();
}
