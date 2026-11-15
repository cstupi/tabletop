namespace Tabletop.Client.Core;

/// <summary>
/// The Ask (or the deployment's unit) whose answer is on its way to the server: set by a send, spent by that command's refusal or failure. A theme change draws the open Ask again in
/// the new theme unless its answer is in flight: the redraw would offer the answered Ask again.
/// </summary>
public sealed class AnswerInFlight
{
    Guid? _ask, _command;

    public void Sent(Guid ask, Guid command) { _ask = ask; _command = command; }

    public void Refused(Guid command) { if (_command == command) _ask = _command = null; }

    /// <summary>Whether this rendered Ask or placement is drawn again: one is rendered and its answer is not in flight.</summary>
    public bool Redraws(Guid? rendered) => rendered is { } a && a != _ask;
}
