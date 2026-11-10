namespace Tabletop.Client.Tests;

/// <summary>Tests that assert a time budget run alone, after the parallel tests, so a loaded machine does not fail them.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "Timing";
}
