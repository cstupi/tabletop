namespace Tabletop.Sim.Scenarios;

/// <summary>A scenario's own assertion, checked at the end of a run beside the universal invariants.</summary>
public abstract record Expectation
{
    public sealed record EventOfKind(string Kind, int AtLeast = 1) : Expectation;
    public sealed record NoEventOfKind(string Kind) : Expectation;
    public sealed record ReachedRound(int Round) : Expectation;
    public sealed record TeamWiped(int TeamOrdinal) : Expectation;
    public sealed record ModelsRemaining(int TeamOrdinal, int AtLeast) : Expectation;
    public sealed record AskAsked(string Question, int AtLeast = 1) : Expectation;
    public sealed record Outcome(string Kind) : Expectation;
}
