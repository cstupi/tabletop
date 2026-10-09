namespace Tabletop.GameServer.Effects;

/// <summary>The catalog entries that acted in one run of a step, each once, in order of first application: what StepCompleted.Applied carries.
/// A StepContext owns one, so nothing outlives the run. The effects layer adds an entry when one of its rows applies; a ruleset hook adds the rule it names.</summary>
public sealed class AppliedEntries
{
    private readonly List<string> _names = [];

    public void Add(string entry)
    {
        if (!_names.Contains(entry)) _names.Add(entry);
    }

    /// <summary>Null when nothing acted, so a step no entry changed writes no list.</summary>
    public IReadOnlyList<string>? Names => _names.Count == 0 ? null : _names.ToList();
}
