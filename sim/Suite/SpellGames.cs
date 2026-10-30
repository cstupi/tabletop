using Tabletop.GameServer.Properties;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>
/// How a book's game is played beyond the defaults. First: spells moved to the front of the casting order. Targets: per spell, the unit
/// names its casts pick, given the caster's unit name (default: SpellGames.Friends for a friendly spell; for an enemy one the target
/// units in order starting from unit r in round r, so a grant no attack uses up, which no recast can stack (p.13), still meets a fresh unit). BlueAction: the action each target unit takes, by unit number (default Rush, which never attacks).
/// </summary>
public sealed record SpellSetup(
    IReadOnlyList<string>? First = null,
    IReadOnlyDictionary<string, Func<string, IReadOnlyList<string>>>? Targets = null,
    Func<int, string>? BlueAction = null,
    int Bonus = 2);

/// <summary>
/// The spell games, generated from the pinned set's books: one game per book that lists spells. The book's army is one single-model
/// unit per spell, every model Caster(6) on the book (ModelSlot.ArmyBook; p.13: "the Caster's spells must be picked from their own
/// faction") and Tough(6). Unit k casts the k-th spell of the casting order every activation and spends the setup's Bonus on the roll.
/// Caster(6) refills to 6 each round, so the value and the bonus are always there. The script picks
/// by name (AskPicks): the book's units activate in order with Advance, each casts its own spell, and targets are named. The target
/// army Rushes and never attacks unless the setup says so; the book's army carries no ranged weapon, so every
/// block in the game comes from a spell unless the setup gives the targets a shooting action. Every unit makes for its own lane (Lane).
/// </summary>
public static class SpellGames
{
    public const string NamePrefix = "spells-", TargetArmy = "Targets";

    /// <summary>The game's name: the book's name, lower-case and hyphenated, after the prefix.</summary>
    public static string NameOf(BookRecord book) =>
        NamePrefix + string.Join('-', new string([.. book.Name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : ' ')])
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The books with spells, in uid order.</summary>
    public static IReadOnlyList<BookRecord> Books(PropertySet set) =>
        set.Books.Values.Where(b => b.Spells.Count > 0).OrderBy(b => b.Uid, StringComparer.Ordinal).ToList();

    /// <summary>The spells in the order the book's units cast them: the setup's First, then the rest in the book's order.</summary>
    public static IReadOnlyList<string> CastingOrder(BookRecord book, SpellSetup? setup) =>
        [.. (setup?.First ?? []).Where(book.Spells.Contains), .. book.Spells.Where(s => !(setup?.First ?? []).Contains(s))];

    public static string UnitName(string army, int unit) => $"{army} unit {unit}";

    static readonly ScenarioWeapon ApRifle = new("Rifle", new Dictionary<string, string> { ["Range"] = "24\"", ["Attacks"] = "1" }, [new ScenarioRule(0, "AP", 1)]);
    static readonly ScenarioWeapon HandWeapon = new("Hand Weapon", new Dictionary<string, string> { ["Attacks"] = "1" }, []);

    public static Scenario Scenario(BookRecord book, ulong seed, PropertySet set, SpellSetup? setup = null)
    {
        var order = CastingOrder(book, setup);
        var units = Enumerable.Range(1, order.Count).ToList();
        return new ScenarioBuilder(NameOf(book), seed).Rules(set.GameRules)
            .RoundCap(4).Table(36, 24)
            .Army(book.Name, order.Count, 1,
                modelRules: [.. units.SelectMany(u => new[] { new ScenarioRule(u, "Caster", 6), new ScenarioRule(u, "Tough", 6) })],
                weapons: [HandWeapon], armyBook: book.Uid)
            .Army(TargetArmy, 3, 3, modelRules: [.. Enumerable.Range(1, 3).Select(u => new ScenarioRule(u, "Tough", 6))], weapons: [ApRifle, HandWeapon])
            .Seeking((model, view) => Lane(book.Name, order.Count, view.Projection, model))
            .Scripted(Script(book.Name, order, set, setup))
            .Build();
    }

    /// <summary>
    /// Where a model makes for: each unit its own lane across the 36" table, the book's units on the line y = 15 and the targets on y = 10, so
    /// every caster stands within 6" of a target unit with no other model between them, and no two units crowd one spot.
    /// </summary>
    static Point2? Lane(string army, int casters, Projection p, Guid model)
    {
        var name = AskPicks.UnitNameOf(p, model);
        if (name is null) return null;
        if (name.StartsWith(army + " unit ", StringComparison.Ordinal) && int.TryParse(name[(army.Length + 6)..], out var k))
            return new Point2(36.0 * (k - 0.5) / casters, 15);
        if (name.StartsWith(TargetArmy + " unit ", StringComparison.Ordinal) && int.TryParse(name[(TargetArmy.Length + 6)..], out var j))
            return new Point2(12.0 * j - 6, 10);
        return null;
    }

    /// <summary>
    /// A friendly spell's targets: a different unit each round (the caster's own, then its neighbours, within 12" on the line), then the
    /// caster's own. A once-grant lasts until its effect applies, and no spell stacks (p.13), so a unit already holding the grant takes
    /// nothing from a recast; moving round to round gives each round's cast a unit to write to.
    /// </summary>
    static IReadOnlyList<string> Friends(string army, int units, string caster, int round)
    {
        var k = int.Parse(caster[(army.Length + 6)..]);
        var offset = new[] { 0, 1, -1, 2 }[Math.Clamp(round, 1, 4) - 1];
        var other = k + offset < 1 || k + offset > units ? k - offset : k + offset;
        return [UnitName(army, Math.Clamp(other, 1, units)), caster];
    }

    static AnswerScript Script(string army, IReadOnlyList<string> order, PropertySet set, SpellSetup? setup)
    {
        // The unit casting now: the actor of the casting frame on top of the stack.
        static Guid? Casting(Projection p) => p.Stack.LastOrDefault(f => f.Sequence == "casting")?.Context.ActorUnitId;
        string? SpellOf(Projection p) =>
            Casting(p) is { } unit && AskPicks.UnitNameOf(p, unit) is { } name && name.StartsWith(army + " unit ", StringComparison.Ordinal)
            && int.TryParse(name[(army.Length + 6)..], out var k) && k >= 1 && k <= order.Count ? order[k - 1] : null;
        var blue = setup?.BlueAction ?? (_ => "Rush");
        return AnswerScript.Build()
            .Match(AskQuestions.Activate, (ask, _) =>
                Enumerable.Range(1, order.Count).Select(k => AskPicks.Activate(ask, UnitName(army, k), "Advance"))
                    .Concat(Enumerable.Range(1, 3).Select(j => AskPicks.Activate(ask, UnitName(TargetArmy, j), blue(j)))).FirstOrDefault(a => a is not null))
            .Match(AskQuestions.React, (ask, _) => AskPicks.Labeled(ask, "Cast a spell"))
            .Match("cast", (ask, p) => SpellOf(p) is { } spell ? AskPicks.Spell(ask, p, spell) : null)
            .Match(AskQuestions.SpendTokens, (ask, p) => ask.Legal is TokenSpend t && Casting(p) is { } unit
                ? new TokensSpent(t.MaxByModel.ToDictionary(kv => kv.Key, kv => p.Models[kv.Key].UnitId == unit ? Math.Min(kv.Value, setup?.Bonus ?? 2) : 0)) : null)
            .Match("spell_targets", (ask, p) =>
            {
                if (SpellOf(p) is not { } spell || Casting(p) is not { } unit || AskPicks.UnitNameOf(p, unit) is not { } caster) return null;
                var names = setup?.Targets?.GetValueOrDefault(spell)?.Invoke(caster)
                    ?? (set.Entry(spell)?.Targeting?.Side == TargetSide.Friendly ? Friends(army, order.Count, caster, p.Scheduler.Round)
                        : [.. Enumerable.Range(0, 3).Select(j => UnitName(TargetArmy, (p.Scheduler.Round - 1 + j) % 3 + 1))]);
                return AskPicks.Pieces(ask, p, [.. names]);
            })
            .Build();
    }

    public static TestGame Game(BookRecord book, ulong seed, PropertySet set, SpellSetup? setup = null, IReadOnlyList<DiceFix>? dice = null,
        IReadOnlyList<GameCheck>? checks = null) =>
        new(NameOf(book), SuiteCatalog.SpellsGroup(set.GameRules, book.Uid), Scenario(book, seed, set, setup), book.Spells, dice ?? [],
            [Checks.Ended(), Checks.Invariants(), Checks.FiredAll(), ResolvedUnderP5(book.Spells), .. checks ?? []]);

    public const string ResolvedCheck = "resolved on a legal cast (p.13: \"Roll one die, on 4+ resolve the effect\"; p.5: \"a roll of 1 always counts as a fail\")";

    /// <summary>
    /// Every spell of the book resolved at least once on a cast whose die was not an unmodified 1, so its coverage never rests on a cast
    /// p.5 says fails.
    /// </summary>
    public static GameCheck ResolvedUnderP5(IReadOnlyList<string> spells) => new(ResolvedCheck, run =>
    {
        var q = LogQueries.Of(run);
        var unresolved = spells.Where(s => !Casts(q, s).Any(c => c.Passed && c.Die.Raw != 1 && Resolved(q, c, s))).ToList();
        return unresolved.Count == 0 ? null : $"resolved only on an unmodified 1, or never: {string.Join(", ", unresolved)}";
    });

    /// <summary>The spell named itself in Applied under the cast (GfCasting: the effects, hit or consequence step).</summary>
    public static bool Resolved(LogQueries q, SpellCast cast, string spell) =>
        q.Descendants(cast.Frame).Prepend(cast.Frame).SelectMany(f => f.Steps).Any(s => s.Lists(spell));

    /// <summary>Every try at a spell: a casting frame whose pick-spell chose it (GfCasting's option ids, "&lt;spell&gt;|&lt;caster model&gt;").</summary>
    public static IReadOnlyList<SpellCast> Casts(LogQueries q, string spell) =>
        q.FramesOf("casting").Select(f =>
        {
            var pick = f.Steps.FirstOrDefault(s => s.Name == "pick-spell")?.Step.Answer as Chosen;
            var roll = f.Steps.FirstOrDefault(s => s.Name == "roll");
            if (pick is null || roll?.Rolls("casting").FirstOrDefault() is not { } r) return null;
            var targets = f.Steps.FirstOrDefault(s => s.Name == "targets")?.Step.Answer is SelectedPieces p ? p.PieceIds : [];
            return pick.OptionId.StartsWith(spell + "|", StringComparison.Ordinal)
                ? new SpellCast(f, f.Context.ActorUnitId!.Value, r, r.Dice[0], f.Steps.FirstOrDefault(s => s.Name == "result")?.Value("passed") == 1, targets) : null;
        }).OfType<SpellCast>().ToList();
}

/// <summary>One try at a spell: its casting frame, the casting unit, the roll and its die as they stand, whether it passed, its targets.</summary>
public sealed record SpellCast(LoggedFrame Frame, Guid Unit, LoggedRoll Roll, Die Die, bool Passed, IReadOnlyList<Guid> Targets)
{
    /// <summary>The grants the cast's effects steps wrote.</summary>
    public IReadOnlyList<GrantAdded> Grants => Frame.Steps.Where(s => s.Name == "effects").SelectMany(s => s.Step.Changes).OfType<GrantAdded>().ToList();
}
