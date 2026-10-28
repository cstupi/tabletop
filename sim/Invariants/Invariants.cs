using Tabletop.GameServer.Log;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Invariants;

public sealed record InvariantResult(string Name, bool Held, string? Detail);

public sealed class InvariantViolatedException(IReadOnlyList<InvariantResult> failures)
    : Exception("invariants violated:\n" + string.Join("\n", failures.Select(f => $"  {f.Name}: {f.Detail}")))
{
    public IReadOnlyList<InvariantResult> Failures { get; } = failures;
}

/// <summary>
/// The invariants that hold for any game, whatever the ruleset (spec → What it asserts). These read state
/// freely — wounds, conditions, positions, the scheduler position — which spec → Two ways to answer an Ask allows.
/// What they contain is no second implementation of the rules: each one is a statement about the log, the
/// projection and the slab, and none of them recomputes an outcome the engine produced.
/// </summary>
public static class Invariants
{
    public static IReadOnlyList<InvariantResult> CheckAll(Projection driverProjection, IReadOnlyList<GameEvent> log, CompiledScenario scenario) =>
    [
        ReplayReproducesTheProjection(driverProjection, log, scenario),
        UndoKeepsTheEarlierDice(log, scenario),
        EveryAskAnsweredByAnAddressedTeam(log),
        NoModelInTwoPlacesOrOffTheSlab(log, scenario),
        TheRoundAdvancedOnlyThroughTheBoundaryAsk(log),
        NothingAdvancesAfterTheEnd(log),
        ControlFollowsTheMarkers(log, scenario),
        SpellsComeFromTheCastersBook(log, scenario),
    ];

    /// <summary>
    /// Invariant 8 (spell library spec → Testing). p.13: "the Caster's spells must be picked from their own faction": every closed
    /// pick-spell names a spell the caster model's own army book lists in the pinned set, and every grant a step wrote carries the name
    /// of a spell some caster's book in the scenario lists, unless the set holds that name as another kind (an army-book Buff, Mark or
    /// Debuff, a model rule picked before attacking). Grants are checked by name, not effect id: over the socket the server
    /// seeds Postgres with its own ids. Reads the log, the board and the pinned set; names no rule.
    /// </summary>
    public static InvariantResult SpellsComeFromTheCastersBook(IReadOnlyList<GameEvent> log, CompiledScenario scenario)
    {
        var slots = scenario.Board.Armies.SelectMany(a => a.Snapshot.Units).SelectMany(u => u.Models).ToDictionary(m => m.Id);
        var inBooks = slots.Values.Select(m => m.ArmyBook).Distinct()
            .SelectMany(b => scenario.Properties.SpellsOf(b)).Select(s => s.Name).ToHashSet();
        foreach (var e in EventApplier.EffectiveLog(log))
        {
            if (e.Payload is not StepCompleted step) continue;
            if (step.Step == "pick-spell" && step.Answer is Chosen { OptionId: var id })
            {
                var cut = id.LastIndexOf('|');
                if (cut <= 0 || !Guid.TryParse(id[(cut + 1)..], out var caster) || !slots.TryGetValue(caster, out var slot))
                    return new InvariantResult(nameof(SpellsComeFromTheCastersBook), false, $"seq {e.Seq}: pick '{id}' names no caster model on the board");
                if (scenario.Properties.SpellsOf(slot.ArmyBook).All(s => s.Name != id[..cut]))
                    return new InvariantResult(nameof(SpellsComeFromTheCastersBook), false, $"seq {e.Seq}: caster {caster:N} of book {slot.ArmyBook ?? "none"} picked '{id[..cut]}'");
            }
            foreach (var g in step.Changes.OfType<GrantAdded>().Where(g => !inBooks.Contains(g.Grant.EntryName)
                && scenario.Properties.Entry(g.Grant.EntryName) is null or { Kind: CatalogKind.Spell }))
                return new InvariantResult(nameof(SpellsComeFromTheCastersBook), false, $"seq {e.Seq}: grant {g.Grant.Id:N} is '{g.Grant.EntryName}', which no caster's book lists");
        }
        return new InvariantResult(nameof(SpellsComeFromTheCastersBook), true, null);
    }

    /// <summary>
    /// Every objective_control_set, alone or inside another event's changes, names a setup marker of the game and, when it names a
    /// team, a team of the game (objectives spec → sim).
    /// </summary>
    public static InvariantResult ControlFollowsTheMarkers(IReadOnlyList<GameEvent> log, CompiledScenario scenario)
    {
        var markers = scenario.Board.Table.Markers.Select(m => m.Id).ToHashSet();
        var teams = scenario.Info.Teams.Select(t => t.Id).ToHashSet();
        foreach (var e in EventApplier.EffectiveLog(log))
            foreach (var change in ChangesOf(e.Payload))
            {
                switch (change)
                {
                    case ObjectiveControlSet o when !markers.Contains(o.MarkerId):
                        return new InvariantResult(nameof(ControlFollowsTheMarkers), false, $"seq {e.Seq} gives marker {o.MarkerId:N}, which the game does not hold");
                    case ObjectiveControlSet { TeamId: { } team } when !teams.Contains(team):
                        return new InvariantResult(nameof(ControlFollowsTheMarkers), false, $"seq {e.Seq} gives a marker to {team:N}, which is not a team of this game");
                }
            }
        return new InvariantResult(nameof(ControlFollowsTheMarkers), true, null);
    }

    private static IEnumerable<StateChange> ChangesOf(EventPayload payload) => payload switch
    {
        StateChange c => [c],
        RoundStarted r => r.Changes,
        RoundEnded r => r.Changes,
        TurnEnded t => t.Changes,
        StepCompleted s => s.Changes,
        _ => [],
    };

    /// <summary>
    /// After game_ended the engine is silent until a round is started by hand (enforcement spec → The game_ended event): every event
    /// between a live game_ended and the next round_started has an actor. Read from the effective log, so an undone end ends nothing.
    /// </summary>
    public static InvariantResult NothingAdvancesAfterTheEnd(IReadOnlyList<GameEvent> log)
    {
        GameEvent? end = null;
        foreach (var e in EventApplier.EffectiveLog(log))
        {
            if (end is not null && e.Actor is null)
                return new InvariantResult(nameof(NothingAdvancesAfterTheEnd), false,
                    $"seq {e.Seq} ({EventKinds.Of(e.Payload)}) has no actor, after the game ended at seq {end.Seq}");
            if (e.Payload is GameEnded) end = e;
            else if (e.Payload is RoundStarted) end = null;
        }
        return new InvariantResult(nameof(NothingAdvancesAfterTheEnd), true, null);
    }

    public static InvariantResult ReplayReproducesTheProjection(Projection driverProjection, IReadOnlyList<GameEvent> log, CompiledScenario scenario)
    {
        if (log.Count == 0) return new InvariantResult(nameof(ReplayReproducesTheProjection), true, null);
        var cold = Replay.To(scenario.Board, scenario.Info, log, log[^1].Seq);
        var differences = ProjectionComparer.Differences(cold, driverProjection);
        return new InvariantResult(nameof(ReplayReproducesTheProjection), differences.Count == 0, string.Join("; ", differences.Take(10)));
    }

    /// <summary>
    /// The dice are counter-based on (RngSeed, cursor), and RngSeed is the seed stored on the game row, so it
    /// never changes for a game: die k is a pure function of that seed and k, and Undone(N) shortens the
    /// effective log rather than rewriting it. Checked at every sequence that carries dice, which is
    /// "an arbitrary sequence" exhaustively. This checks the effective-log prefix only: that dice after an undo are
    /// fresh, because the undone dice stay counted, is the socket test DriverAgreementTests.
    /// </summary>
    public static InvariantResult UndoKeepsTheEarlierDice(IReadOnlyList<GameEvent> log, CompiledScenario scenario)
    {
        // The effective log: an undone range's dice are no longer the game's, so a log that already holds an
        // Undone is measured as it stands, not as every event ever appended.
        var live = EventApplier.EffectiveLog(log);
        var original = DiceOf(live);
        foreach (var target in live.Where(e => e.Payload is StepCompleted { Rolls.Count: > 0 }).Select(e => e.Seq))
        {
            var undone = log.Append(new GameEvent(log[^1].Seq + 1, null, new Undone(target), null)).ToList();
            var effective = EventApplier.EffectiveLog(undone);
            var kept = DiceOf(effective);
            var expected = original.Take(kept.Count).ToList();
            if (!kept.SequenceEqual(expected))
                return new InvariantResult(nameof(UndoKeepsTheEarlierDice), false,
                    $"undoing to {target} changed the dice: kept [{string.Join(",", kept)}] against [{string.Join(",", expected)}]");
        }
        return new InvariantResult(nameof(UndoKeepsTheEarlierDice), true, null);
    }

    private static IReadOnlyList<int> DiceOf(IReadOnlyList<GameEvent> log) => log
        .SelectMany(e => e.Payload switch
        {
            StepCompleted s => s.Rolls.SelectMany(r => r.Dice.Select(d => d.Raw)),
            DiceRerolled d => d.Replaced.Select(r => r.New.Raw),
            _ => [],
        })
        .ToList();

    public static InvariantResult EveryAskAnsweredByAnAddressedTeam(IReadOnlyList<GameEvent> log)
    {
        var asked = log.Where(e => e.Payload is AskOpened).ToDictionary(e => ((AskOpened)e.Payload).Ask.Id, e => ((AskOpened)e.Payload).Ask);
        foreach (var e in log)
        {
            if (e.Payload is not AskAnswered answered) continue;
            if (!asked.TryGetValue(answered.AskId, out var ask))
                return new InvariantResult(nameof(EveryAskAnsweredByAnAddressedTeam), false, $"seq {e.Seq} answers ask {answered.AskId:N}, which was never opened");
            if (!ask.Teams.Contains(answered.TeamId))
                return new InvariantResult(nameof(EveryAskAnsweredByAnAddressedTeam), false,
                    $"seq {e.Seq}: team {answered.TeamId:N} answered '{ask.Question}', addressed to {string.Join(",", ask.Teams.Select(t => t.ToString("N")))}");
        }
        return new InvariantResult(nameof(EveryAskAnsweredByAnAddressedTeam), true, null);
    }

    public static InvariantResult NoModelInTwoPlacesOrOffTheSlab(IReadOnlyList<GameEvent> log, CompiledScenario scenario)
    {
        var where = new Dictionary<Guid, Location>();
        foreach (var model in scenario.Board.Armies.SelectMany(a => a.Snapshot.Units).SelectMany(u => u.Models)) where[model.Id] = new OnShelf();
        var size = scenario.Board.Table.Size;

        foreach (var e in EventApplier.EffectiveLog(log))
        {
            foreach (var move in Relocations(e.Payload))
            {
                if (!where.TryGetValue(move.ModelId, out var was))
                    return Fail($"seq {e.Seq} moves model {move.ModelId:N}, which is not in any army");
                if (!Equals(was, move.From))
                    return Fail($"seq {e.Seq} moves model {move.ModelId:N} from {move.From}, but it was at {was}");
                where[move.ModelId] = move.To;
                if (move.To is OnTable table)
                {
                    var p = table.Placement.Position;
                    if (p.X < 0 || p.X > size.Width || p.Y < 0 || p.Y > size.Length)
                        return Fail($"seq {e.Seq} puts model {move.ModelId:N} at {p.X},{p.Y}, off a {size.Width}x{size.Length} slab");
                }
            }
        }
        return new InvariantResult(nameof(NoModelInTwoPlacesOrOffTheSlab), true, null);

        InvariantResult Fail(string detail) => new(nameof(NoModelInTwoPlacesOrOffTheSlab), false, detail);
    }

    private static IEnumerable<ModelRelocation> Relocations(EventPayload payload) => payload switch
    {
        ModelsMoved m => m.Moves,
        RoundStarted r => r.Changes.SelectMany(Relocations),
        TurnEnded t => t.Changes.SelectMany(Relocations),
        StepCompleted s => s.Changes.SelectMany(Relocations),
        _ => [],
    };

    /// <summary>The round does not advance on its own: the next round's RoundStarted follows the first answer to the end_round Ask (and, under warn past the last round, the game_over Ask's Continue), or carries the actor of a menu override (scheduler design → There is no round limit).</summary>
    public static InvariantResult TheRoundAdvancedOnlyThroughTheBoundaryAsk(IReadOnlyList<GameEvent> log)
    {
        var effective = EventApplier.EffectiveLog(log);
        var asked = effective.Where(e => e.Payload is AskOpened).ToDictionary(e => ((AskOpened)e.Payload).Ask.Id, e => ((AskOpened)e.Payload).Ask);
        for (var i = 0; i < effective.Count; i++)
        {
            if (effective[i].Payload is not RoundStarted { Round: >= 2 } started) continue;
            if (effective[i].Actor is not null) continue;                                   // the menu's set_round: an override, and logged
            var closed = effective.Take(i).LastOrDefault(e => e.Payload is AskClosed c && asked.GetValueOrDefault(c.AskId)?.Question != AskQuestions.GameOver);   // warn's game_over follows end_round
            var question = closed?.Payload is AskClosed c && asked.TryGetValue(c.AskId, out var ask) ? ask.Question : null;
            if (question != AskQuestions.EndRound)
                return new InvariantResult(nameof(TheRoundAdvancedOnlyThroughTheBoundaryAsk), false,
                    $"seq {effective[i].Seq} started round {started.Round} without a closed '{AskQuestions.EndRound}' ask (nearest closed ask: {question ?? "none"})");
        }
        return new InvariantResult(nameof(TheRoundAdvancedOnlyThroughTheBoundaryAsk), true, null);
    }
}

/// <summary>The mid-run entry point the module prompt asks for: assert now, and throw with every failure named.</summary>
public static class MidRun
{
    public static void Assert(Projection projection, IReadOnlyList<GameEvent> log, CompiledScenario scenario)
    {
        var failures = Invariants.CheckAll(projection, log, scenario).Where(r => !r.Held).ToList();
        if (failures.Count > 0) throw new InvariantViolatedException(failures);
    }
}
