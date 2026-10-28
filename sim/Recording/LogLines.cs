using Tabletop.Protocol;

namespace Tabletop.Sim.Recording;

/// <summary>
/// One line per event, from the event and the projection after it. It reads the projection freely, and it
/// interprets nothing: every name it prints — a sequence, a step, an action, a condition, a question — is a
/// string the event already carries, and nothing here decides what any of them mean.
/// </summary>
public static class LogLines
{
    public static string Of(GameEvent e, Projection after, IReadOnlyDictionary<Guid, int> ordinals) => e.Payload switch
    {
        RoundStarted r => $"Round {r.Round} started",
        TurnStarted t => $"{Team(ordinals, t.TeamId)}'s turn",
        TurnEnded t => t.Passed ? $"{Team(ordinals, t.TeamId)} passed" : $"{Team(ordinals, t.TeamId)}'s turn ended",
        PhaseStarted p => $"Phase: {p.Phase}",
        SequenceStarted s => $"{s.Sequence} started{Actor(after, s.Context.ActorUnitId)}",
        StepCompleted s => $"{s.Step}{Dice(s.Rolls)}",
        SequenceFinished => "sequence finished",
        SequenceAbandoned => "sequence abandoned",
        AskOpened a => $"Asked {string.Join(", ", a.Ask.Teams.Select(t => Team(ordinals, t)))}: {a.Ask.Prompt}",
        AskAnswered a => $"{Team(ordinals, a.TeamId)} answered {Answer(a.Answer)}{(a.Breach is not null ? $" (breaks {a.Breach})" : a.OutsideLegalSet ? " (outside the legal set)" : "")}",
        AskClosed => "ask closed",
        DiceRerolled d => $"rerolled {d.Replaced.Count} dice",
        DiceEdited d => $"edited {d.Replaced.Count} dice",
        Undone u => $"undone to {u.ToSeq}",
        ModelsMoved m => $"{m.Moves.Count} model(s) moved",
        WoundsSet w => $"{Model(after, w.ModelId)} at {w.Wounds} wound(s)",
        ConditionSet c => $"{Subject(after, c.Subject)} {(c.Present ? "gains" : "loses")} {c.Condition}",
        ActionRecorded a => $"{Unit(after, a.UnitId)} did {a.Action}",
        ActionRecordCleared a => $"{Unit(after, a.UnitId)}'s action record cleared",
        GrantAdded g => $"{Unit(after, g.UnitId)} gains {g.Grant.EntryName}",
        GrantRemoved => "a grant was removed",
        SpellTokensSet s => $"{Model(after, s.ModelId)} holds {s.Tokens} token(s)",
        WoundsThisActivationSet w => $"{Unit(after, w.UnitId)} has taken {w.Wounds} wound(s) this activation",
        ScoreSet s => $"{Team(ordinals, s.TeamId)} scores {s.Score}",
        MarkerMoved => "marker moved",
        RoundEnded r => $"Round {r.Round} ended",
        ObjectiveControlSet { TeamId: { } team } o => $"{Marker(after, o.MarkerId)} to {Team(ordinals, team)}",
        ObjectiveControlSet o => $"{Marker(after, o.MarkerId)} neutral",
        GameEnded g => $"Game ended: {(g.Winners.Count == 0 ? g.Reason : g.Reason.TrimEnd('.'))}{Result(ordinals, g.Winners)}",   // no ".;" when a result follows
        _ => EventKinds.Of(e.Payload),
    };

    private static string Team(IReadOnlyDictionary<Guid, int> ordinals, Guid teamId) =>
        ordinals.TryGetValue(teamId, out var ordinal) ? $"Team {ordinal}" : teamId.ToString("N")[..8];

    private static string Unit(Projection p, Guid unitId) =>
        p.Units.TryGetValue(unitId, out var u) ? u.Definition.Name : unitId.ToString("N")[..8];

    private static string Model(Projection p, Guid modelId) =>
        p.Models.TryGetValue(modelId, out var m) ? m.Slot.Name : modelId.ToString("N")[..8];

    private static string Subject(Projection p, Subject s) =>
        s.Kind == SubjectKind.Unit ? Unit(p, s.Id) : Model(p, s.Id);

    private static string Marker(Projection p, Guid id) =>
        (p.Markers.GetValueOrDefault(id) ?? p.Board.Table.Markers.FirstOrDefault(m => m.Id == id))?.Label ?? id.ToString("N")[..8];

    private static string Actor(Projection p, Guid? unitId) => unitId is { } id ? $" for {Unit(p, id)}" : "";

    /// <summary>
    /// Every roll on the step, not the first: a ruleset splits one roll wherever a step's dice do not share a threshold, and OPR's
    /// roll-to-hit takes one pool per Quality value the attacking models bring (p.4, and a joined hero brings its own). The
    /// threshold is printed because it is the only thing that tells two pools of the same roll apart.
    /// </summary>
    private static string Dice(IReadOnlyList<RollRecord> rolls) =>
        rolls.Count == 0 ? "" : ": " + string.Join("; ", rolls.Select(r =>
            $"{r.Roll}{(r.Threshold is { } t ? $" {t}+" : "")} {string.Join(",", r.Dice.Select(d => d.Success ? $"{d.Modified}!" : $"{d.Modified}"))}"));

    private static string Answer(AskAnswer? answer) => answer switch
    {
        null => "nothing",
        Chosen c => c.OptionId,
        SelectedPieces s => $"{s.PieceIds.Count} piece(s)",
        Allocated a => $"{a.Counts.Values.Sum()} allocated",
        Assigned a => $"{a.Targets.Count} assignment(s)",
        Destinations d => $"{d.Moves.Count} destination(s)",
        TokensSpent t => $"{t.Counts.Values.Sum()} token(s) spent",
        Confirmed => "yes",
        Passed => "a pass",
        _ => answer.GetType().Name,
    };

    private static string Result(IReadOnlyDictionary<Guid, int> ordinals, IReadOnlyList<Guid> winners) => winners.Count switch
    {
        0 => "",
        1 => $"; {Team(ordinals, winners[0])} won",
        _ => $"; tied between {string.Join(", ", winners.Select(w => Team(ordinals, w)))}",
    };
}
