using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum AskMode { Choose, PickPieces, Allocate, Assign, Move, TokenSpend, Confirm }

/// <summary>What the decision box renders. Highlights are the pieces the legal set names.</summary>
public sealed record AskView(Ask Ask, AskMode Mode, IReadOnlyList<Guid> Highlights, IReadOnlyList<string> AnsweredTeams, bool AutoPass);

public static class AskViewModel
{
    public static AskMode ModeOf(LegalSet legal) => legal switch
    {
        Options => AskMode.Choose, PieceSelection => AskMode.PickPieces, Allocation => AskMode.Allocate,
        Assignment => AskMode.Assign, MoveRegion => AskMode.Move, TokenSpend => AskMode.TokenSpend, Confirmation => AskMode.Confirm,
        _ => throw new ArgumentOutOfRangeException(nameof(legal)),
    };

    /// <summary>
    /// The spend map narrowed to one team's own models. p.13's window addresses both teams — the caster's side spends for
    /// +1 and the other side for -1 — so <c>TokenSpend.MaxByModel</c> names models this player may not spend, and a spin box
    /// over one of them would be a control the player cannot use. The map itself discloses nothing: every projection already
    /// carries each model's <c>SpellTokens</c>. A model whose unit or participant this projection cannot resolve is not mine.
    /// </summary>
    public static IReadOnlyDictionary<Guid, int> SpendableBy(Projection p, GameInfo game, Guid team, TokenSpend legal) =>
        legal.MaxByModel.Where(kv => TeamOfModel(p, game, kv.Key) == team).ToDictionary(kv => kv.Key, kv => kv.Value);

    static Guid? TeamOfModel(Projection p, GameInfo game, Guid modelId) =>
        p.Models.GetValueOrDefault(modelId) is { } m && p.Units.GetValueOrDefault(m.UnitId) is { } unit
            ? game.Participants.FirstOrDefault(x => x.Id == unit.ParticipantId)?.TeamId
            : null;

    /// <summary>The first open Ask addressed to my team that my team has not answered. Null for a watcher: a participant with no team is never Asked. `myTeam` comes from the participant's row by way of `Welcome`, so a player who reconnects mid-Ask is addressed by it again (game flow → Teams are frozen at the flip); callers pass null only for a real watcher, never for a client that is mid-reconnect.</summary>
    public static AskView? Mine(Projection p, GameInfo game, Guid? myTeam, IReadOnlySet<string> muted)
    {
        if (myTeam is not { } team) return null;
        var open = p.OpenAsks.Values.OrderBy(a => a.Ask.Id).FirstOrDefault(a => a.Ask.Teams.Contains(team) && !a.Answers.ContainsKey(team));
        if (open is null) return null;
        var names = new Names(game, p);
        var ask = open.Ask;
        var highlights = ask.Legal switch
        {
            Options o => o.Choices.SelectMany(c => c.Pieces).Distinct().ToList(),
            PieceSelection s => s.Eligible, Allocation a => a.Targets, Assignment a => a.Targets, MoveRegion m => MoveHighlights(m),
            TokenSpend t => SpendableBy(p, game, team, t).Keys.ToList(),          // my own spenders only (p.13)
            _ => (IReadOnlyList<Guid>)[],
        };
        var autoPass = ask.WindowKind is { } kind && muted.Contains(kind) && ask.AllowPass;
        return new AskView(ask, ModeOf(ask.Legal), highlights, open.Answers.Keys.Select(names.Team).ToList(), autoPass);
    }

    /// <summary>
    /// The pieces a move Ask names: its models, plus the containers containment offers. An enterable container is a click
    /// target for the answer, and the container a unit is exiting anchors the region, so both are highlights — "the legal set
    /// from the server becomes the panel's choices and the table's highlights" (client shell → Asks and windows). An occupant
    /// has no node, so highlighting its model id is a no-op; the container's node is the one the player can see and click.
    /// </summary>
    static IReadOnlyList<Guid> MoveHighlights(MoveRegion m)
    {
        var ids = m.ModelIds.ToList();
        if (m.Containment is { } c) { ids.AddRange(c.Enterable); if (c.ExitingId is { } exiting) ids.Add(exiting); }
        return ids.Distinct().ToList();
    }

    /// <summary>The waiting indicator: which teams the other open Asks are addressed to.</summary>
    public static string? WaitingFor(Projection p, GameInfo game, Guid? myTeam)
    {
        var names = new Names(game, p);
        var teams = p.OpenAsks.Values.SelectMany(a => a.Ask.Teams.Where(t => !a.Answers.ContainsKey(t) && t != myTeam)).Distinct().Select(names.Team).ToList();
        return teams.Count == 0 ? null : string.Join(", ", teams);
    }

    /// <summary>
    /// What the one button of a Confirm Ask says. A step gate says Continue; the round boundary's end_round Ask (unframed, addressed to
    /// every team that has a participant) says what it does, because the round advances only when a player answers it (scheduler → There is no round limit).
    /// </summary>
    public static string ConfirmLabel(Ask ask) => ask.Question == AskQuestions.EndRound ? "End the round" : "Continue";

    /// <summary>Who on the team answered: the ask_answered event's actor.</summary>
    public static string? AnsweredBy(IReadOnlyList<GameEvent> log, Guid askId, Names names) =>
        log.LastOrDefault(e => e.Payload is AskAnswered a && a.AskId == askId) is { } e ? names.Participant(e.Actor) : null;
}
