namespace Tabletop.GameServer.Effects;

/// <summary>The catalog entries that acted in one run of a step, each once, in order of first application: what StepCompleted.Applied carries.
/// A StepContext owns one, so nothing outlives the run. The effects layer adds an entry when one of its rows applies; a ruleset hook adds the rule it names.</summary>
public sealed class AppliedEntries
{
    private readonly List<string> _names = [];
    private readonly List<Tabletop.Protocol.IgnoredEffect> _ignored = [];

    /// <summary>A rule a suppression removed would have been read here (LiveSet.ReadRemoved): StepCompleted.Ignored, each pair once.</summary>
    public void Ignore(string entry, string by)
    {
        var x = new Tabletop.Protocol.IgnoredEffect(entry, by);
        if (!_ignored.Contains(x)) _ignored.Add(x);
    }

    /// <summary>Null when nothing was removed where it would have been read.</summary>
    public IReadOnlyList<Tabletop.Protocol.IgnoredEffect>? Ignored => _ignored.Count == 0 ? null : _ignored.ToList();

    public void Add(string entry)
    {
        if (!_names.Contains(entry)) _names.Add(entry);
    }

    /// <summary>Takes back an entry a step read but that did not act after all (a once grant a move that went nowhere keeps).</summary>
    public void Remove(string entry) => _names.Remove(entry);

    /// <summary>Null when nothing acted, so a step no entry changed writes no list.</summary>
    public IReadOnlyList<string>? Names => _names.Count == 0 ? null : _names.ToList();
}
