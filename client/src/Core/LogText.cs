using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>Every event in words, with its actor. Presence never appears here because presence is never an event.</summary>
public static class LogText
{
    public static string Describe(GameEvent e, Names n)
    {
        var who = n.Participant(e.Actor);
        var text = e.Payload switch
        {
            ModelsMoved m => Moved(m, n),
            WoundsSet w => $"set {n.Model(w.ModelId)} to {w.Wounds} wound{(w.Wounds == 1 ? "" : "s")}",
            ConditionSet c => $"{(c.Present ? "marked" : "cleared")} {(c.Subject.Kind == SubjectKind.Unit ? n.Unit(c.Subject.Id) : n.Model(c.Subject.Id))} {c.Condition}",
            ActionRecorded a => $"recorded {a.Action} for {n.Unit(a.UnitId)}",
            ActionRecordCleared a => $"cleared {n.Unit(a.UnitId)}'s action record",
            GrantAdded g => $"granted {g.Grant.EntryName} to {n.Unit(g.UnitId)}",
            GrantRemoved g => $"removed a grant from {n.Unit(g.UnitId)}",
            SpellTokensSet s => $"set {n.Model(s.ModelId)} to {s.Tokens} spell token{(s.Tokens == 1 ? "" : "s")}",
            WoundsThisActivationSet w => $"set {n.Unit(w.UnitId)}'s wounds this activation to {w.Wounds}",
            ScoreSet s => $"set {n.Team(s.TeamId)}'s score to {s.Score}",
            MarkerPlaced m => $"placed a {m.Marker.Kind.ToString().ToLowerInvariant()}{(m.Marker.Label is null ? "" : $" '{m.Marker.Label}'")}",
            MarkerMoved m => $"moved marker {n.Piece(m.MarkerId)}",
            MarkerRemoved m => $"removed marker {n.Piece(m.MarkerId)}",
            RoundStarted r => $"started round {r.Round}",
            TurnStarted t => $"started {n.Team(t.TeamId)}'s turn",
            TurnEnded t => t.Passed ? $"passed for {n.Team(t.TeamId)}" : $"ended {n.Team(t.TeamId)}'s turn",
            PhaseStarted p => $"began {p.Phase}",
            SequenceStarted s => $"began {s.Sequence}{(s.Context.ActorUnitId is { } u ? $" with {n.Unit(u)}" : "")}",
            StepCompleted s => Step(s),
            SequenceFinished => "finished the sequence",
            SequenceAbandoned => "abandoned the sequence",
            AskOpened a => $"asked {string.Join(", ", a.Ask.Teams.Select(n.Team))}: {a.Ask.Prompt}",
            // "confirmed" is what a confirm gate and the round boundary's end_round Ask both are; the round_started line under it says which round began.
            AskAnswered a => a.Answer switch
            {
                Passed => $"passed for {n.Team(a.TeamId)}",
                Confirmed => $"confirmed for {n.Team(a.TeamId)}",
                // An unsealed answer is in hand, so it is in words: a takedown's pick has to name the model it picked (p.13).
                { } answer => $"answered for {n.Team(a.TeamId)}: {Answer(answer, n)}{(a.OutsideLegalSet ? " (outside the legal set)" : "")}",
                null => $"answered for {n.Team(a.TeamId)}{(a.OutsideLegalSet ? " (outside the legal set)" : "")}",   // withheld: a sealed Ask reveals at ask_closed
            },
            AskClosed a => a.Answers.Count == 0 ? "closed the question"
                : $"closed the question: {string.Join(", ", a.Answers.Select(x => $"{n.Team(x.TeamId)} {Answer(x.Answer, n)}"))}",   // a sealed Ask's reveal
            DiceRerolled d => $"rerolled {d.Replaced.Count} dice",
            DiceEdited d => $"set {d.Replaced.Count} dice by hand",
            ModifierAdded m => $"added {m.Modifier.Amount:+#;-#;0} to the {m.Roll} roll",
            Undone u => $"undid back to #{u.ToSeq}",
            GameEnded g => $"ended the game: {g.Reason}{Result(g.Winners, n)}",
            var other => other.GetType().Name,
        };
        return $"{who} {text}" + (e.Warning is null ? "" : $" — {e.Warning}");
    }

    /// <summary>One winner, a tie, or nothing when the ruleset recorded no result.</summary>
    static string Result(IReadOnlyList<Guid> winners, Names n) => winners.Count switch
    {
        0 => "",
        1 => $"; {n.Team(winners[0])} won",
        _ => $"; tied between {string.Join(", ", winners.Select(n.Team))}",
    };

    static string Moved(ModelsMoved m, Names n)
    {
        var first = m.Moves[0];
        var unit = n.UnitOfModel(first.ModelId) is { } unitId ? n.Unit(unitId) : Names.Short(first.ModelId);
        return (first.From, first.To) switch
        {
            (OnTable a, OnTable b) => $"moved {unit} {a.Placement.Position.Horizontal.DistanceTo(b.Placement.Position.Horizontal):0.0}\"",
            (OnShelf, OnTable) => $"deployed {unit}",
            (Inside i, OnTable) => $"took {unit} out of {n.Piece(i.ContainerId)}",
            (_, OnShelf) => $"moved {unit} to the shelf",
            (_, Inside i) => $"put {unit} inside {n.Piece(i.ContainerId)}",
            _ => $"moved {unit}",
        };
    }
    /// <summary>
    /// An answer in words. A sealed Ask's answers reach the client only inside <c>ask_closed</c>, which is the reveal.
    /// Names is taken because two answers have to name pieces (p.13 and p.14): a one-piece selection is
    /// a Takedown's chosen model, and a token spend is a count per model rather than one number.
    /// </summary>
    public static string Answer(AskAnswer a, Names n) => a switch
    {
        Chosen c => $"chose {c.OptionId}",
        SelectedPieces { PieceIds.Count: 1 } s => $"picked {n.Piece(s.PieceIds[0])}",
        SelectedPieces s => $"picked {s.PieceIds.Count} piece{(s.PieceIds.Count == 1 ? "" : "s")}",
        Allocated al => $"allocated {al.Counts.Values.Sum()}",
        Assigned => "assigned targets",
        Destinations d => $"moved {d.Moves.Count} model{(d.Moves.Count == 1 ? "" : "s")}",
        TokensSpent t => $"spent {t.Counts.Values.Sum()} token{(t.Counts.Values.Sum() == 1 ? "" : "s")} ({Parts(t.Counts, n)})",
        Confirmed => "confirmed",
        Passed => "passed",
        _ => "answered",
    };

    /// <summary>Which model gave what, ordered by the name so the words do not depend on a dictionary's order.</summary>
    static string Parts(IReadOnlyDictionary<Guid, int> counts, Names n) =>
        string.Join(", ", counts.Where(kv => kv.Value > 0).Select(kv => $"{n.Piece(kv.Key)} {kv.Value}").Order(StringComparer.Ordinal));

    static string Step(StepCompleted s)
    {
        if (s.Rolls.Count == 0) return $"completed {s.Step}";
        var dice = s.Rolls.SelectMany(r => r.Dice).ToList();
        return $"completed {s.Step}: {dice.Count(d => d.Success)} of {dice.Count} succeeded";
    }

    /// <summary>What clicking the entry highlights on the table.</summary>
    public static IReadOnlyList<Guid> Touched(GameEvent e) => e.Payload switch
    {
        ModelsMoved m => m.Moves.Select(x => x.ModelId).ToList(),
        WoundsSet w => [w.ModelId], SpellTokensSet s => [s.ModelId],
        ConditionSet c => [c.Subject.Id], ActionRecorded a => [a.UnitId], ActionRecordCleared a => [a.UnitId],
        GrantAdded g => [g.UnitId], GrantRemoved g => [g.UnitId], WoundsThisActivationSet w => [w.UnitId],
        MarkerPlaced m => [m.Marker.Id], MarkerMoved m => [m.MarkerId], MarkerRemoved m => [m.MarkerId],
        SequenceStarted s => new[] { s.Context.ActorUnitId }.Where(x => x is not null).Select(x => x!.Value).Concat(s.Context.TargetUnitIds).ToList(),
        StepCompleted s => s.Changes.SelectMany(c => Touched(new GameEvent(0, null, c, null))).ToList(),
        AskOpened a => a.Ask.Legal is Options o ? o.Choices.SelectMany(c => c.Pieces).Distinct().ToList() : a.Ask.Legal is PieceSelection p ? p.Eligible : [],
        // An answer names pieces too: the model a Takedown picked, and the models a team spent its tokens from (p.14).
        AskAnswered a => a.Answer is null ? [] : AnswerPieces(a.Answer),
        AskClosed a => a.Answers.SelectMany(x => AnswerPieces(x.Answer)).Distinct().ToList(),
        _ => [],
    };

    static IReadOnlyList<Guid> AnswerPieces(AskAnswer a) => a switch
    {
        SelectedPieces s => s.PieceIds,
        Allocated al => al.Counts.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList(),
        TokensSpent t => t.Counts.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList(),
        Assigned a2 => a2.Targets.Values.Distinct().ToList(),
        Destinations d => d.Moves.Select(m => m.ModelId).ToList(),
        _ => [],
    };
}
