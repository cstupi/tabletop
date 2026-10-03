using System.Diagnostics;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Dispatch;

/// <summary>AlwaysAccepted: the override path (menu, hand moves, markers, undo). Legal / OutsideLegalSet: an Ask answer against its set. Illegal: the engine's word for a command the rules cannot execute.</summary>
public enum Legality { AlwaysAccepted, Legal, OutsideLegalSet, Illegal }

public abstract record Verdict;
public sealed record Accept(string? Warning) : Verdict;
public sealed record Refuse(RefusalReason Reason, string Detail) : Verdict;
/// <summary>Strict's answer to a breach whose consequence is the end: game_ended with these winners in place of what was proposed.</summary>
public sealed record EndGame(string Reason, IReadOnlyList<Guid> Winners) : Verdict;

/// <summary>What was broken (a snake_case rule name, never on the wire), the sentence players read, and what strict does about it (enforcement spec → The breach).</summary>
public sealed record Breach(string Rule, string Detail, StrictConsequence Strict);
public abstract record StrictConsequence;
public sealed record StrictRefuses(RefusalReason Reason) : StrictConsequence;
/// <summary>One winner is a win, several a tie between them, none no result.</summary>
public sealed record StrictEndsGame(IReadOnlyList<Guid> Winners) : StrictConsequence;

/// <summary>silent | warn | strict as a decision over a breach (architecture.md → Rules enforcement; enforcement spec → The policy).</summary>
public static class EnforcementPolicy
{
    public const string OutsideLegalSetRule = "outside_legal_set", IllegalRule = "illegal";

    public static Verdict Decide(Enforcement enforcement, Breach? breach) => (breach, enforcement) switch
    {
        (null, _) => new Accept(null),
        (_, Enforcement.Silent) => new Accept(null),
        (_, Enforcement.Warn) => new Accept(breach.Detail),
        ({ Strict: StrictRefuses r }, Enforcement.Strict) => new Refuse(r.Reason, breach.Detail),
        ({ Strict: StrictEndsGame e }, Enforcement.Strict) => new EndGame(breach.Detail, e.Winners),
        _ => throw new UnreachableException($"no verdict for {breach.Strict.GetType().Name} under {enforcement}"),
    };

    /// <summary>A command's legality as a breach. Always StrictRefuses: command legality never ends the game.</summary>
    public static Breach? BreachOf(Legality legality, string detail) => legality switch
    {
        Legality.OutsideLegalSet => new Breach(OutsideLegalSetRule, detail, new StrictRefuses(RefusalReason.OutsideLegalSet)),
        Legality.Illegal => new Breach(IllegalRule, detail, new StrictRefuses(RefusalReason.Illegal)),
        _ => null,
    };

    public static Verdict Decide(Enforcement enforcement, Legality legality, string detail) => Decide(enforcement, BreachOf(legality, detail));

    /// <summary>
    /// The one place an EndGame verdict becomes payloads: game_ended alone. Only at rest — an empty stack and no open Ask — which the
    /// round boundary always is; a breach that could end the game mid-sequence would need a closing this does not provide.
    /// </summary>
    public static IReadOnlyList<EventPayload> EndGamePayloads(Projection projection, string reason, IReadOnlyList<Guid> winners)
    {
        if (projection.Stack.Count > 0) throw new InvalidOperationException($"the game cannot end with {projection.Stack.Count} frame(s) on the stack");
        if (projection.OpenAsks.Count > 0) throw new InvalidOperationException($"the game cannot end with {projection.OpenAsks.Count} open Ask(s)");
        return [new GameEnded(reason, winners)];
    }
}
