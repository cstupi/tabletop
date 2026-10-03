namespace Tabletop.Sim.Run;

/// <param name="MaxSteps">
/// A hard stop, so a bug in this loop cannot spin forever — and the only guard against an engine that makes
/// no real progress while emitting one non-Ask event per pass, which clears the AskReopenedUnchanged check.
/// </param>
/// <param name="Record">
/// False builds no frames: a loop attached to a game already past its first event (the redo after an Undo) has
/// no projector state for the events that refer back to the kept prefix. Pair it with CheckInvariants: false, since
/// the invariants replay the loop's own log from its first event.
/// </param>
public sealed record RunSettings(int MaxSteps = 20_000, int FrameDumpOnFailure = 12, bool CheckInvariants = true, bool Record = true);

/// <param name="RecordingPath">
/// Where the recording was written, or null. <b><c>RunLoop</c> never sets this and always leaves it null.</b>
/// The loop builds a <c>Recording</c> on demand through <c>BuildRecording</c> but writes no file and knows no
/// output directory; whoever writes the file owns the path, which is <c>Program.PlayAsync</c> (Task 10)
/// returning <c>report with { RecordingPath = path }</c> after <c>RecordingJson.WriteAsync</c>.
/// </param>
public sealed record RunReport(
    string Scenario, ulong Seed, string Driver, RunOutcome Outcome,
    int Rounds, long LastSeq, int Frames, int AsksAnswered, double DurationMs,
    IReadOnlyList<string> Failures, string? RecordingPath)
{
    public bool Passed => Failures.Count == 0 && Outcome is RunOutcome.Ended or RunOutcome.RoundLimit or RunOutcome.Paused;
}
