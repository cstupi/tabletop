using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

/// <summary>Entering a game leaves tens of megabytes of loading garbage; the client collects it once, when the table stands with every model
/// loaded, rather than in a later hover's frame.</summary>
public class LoadSettledTests
{
    [Fact]
    public void Settles_once_when_entered_with_every_model_loaded()
    {
        var settled = new LoadSettled();
        Assert.False(settled.Update(entered: false, modelsDone: 0, modelsTotal: 0));
        Assert.False(settled.Update(entered: true, modelsDone: 3, modelsTotal: 12));
        Assert.True(settled.Update(entered: true, modelsDone: 12, modelsTotal: 12));
        Assert.False(settled.Update(entered: true, modelsDone: 12, modelsTotal: 12));
        Assert.False(settled.Update(entered: true, modelsDone: 13, modelsTotal: 14));   // a late army's wave: already settled
        Assert.False(settled.Update(entered: true, modelsDone: 14, modelsTotal: 14));
    }

    [Fact]
    public void A_table_with_no_models_settles_on_entry()
    {
        var settled = new LoadSettled();
        Assert.True(settled.Update(entered: true, modelsDone: 0, modelsTotal: 0));
    }
}
