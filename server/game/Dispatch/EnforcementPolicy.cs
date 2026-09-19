using Tabletop.Protocol;

namespace Tabletop.GameServer.Dispatch;

/// <summary>AlwaysAccepted: the override path (menu, hand moves, markers, undo). Legal / OutsideLegalSet: an Ask answer against its set. Illegal: the engine's word for a command the rules cannot execute.</summary>
public enum Legality { AlwaysAccepted, Legal, OutsideLegalSet, Illegal }

public abstract record Verdict;
public sealed record Accept(string? Warning) : Verdict;
public sealed record Refuse(RefusalReason Reason, string Detail) : Verdict;

/// <summary>silent | warn | strict as a decision (architecture.md → Rules enforcement; whole-design review ruling 1).</summary>
public static class EnforcementPolicy
{
    public static Verdict Decide(Enforcement enforcement, Legality legality, string detail) => (legality, enforcement) switch
    {
        (Legality.AlwaysAccepted or Legality.Legal, _) => new Accept(null),
        (Legality.OutsideLegalSet, Enforcement.Strict) => new Refuse(RefusalReason.OutsideLegalSet, detail),
        (Legality.Illegal, Enforcement.Strict) => new Refuse(RefusalReason.Illegal, detail),
        (_, Enforcement.Warn) => new Accept(detail),
        _ => new Accept(null),
    };
}
