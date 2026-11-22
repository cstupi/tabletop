using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

/// <summary>Entering a game is over once the table stands with every file of its loading wave in: the settling work runs once, behind the
/// curtain, and the curtain stays for a few frames more, so the new meshes' first draws are not paid after it lifts.</summary>
public class LoadSettledTests
{
    static readonly TimeSpan T0 = TimeSpan.FromSeconds(10);

    [Fact]
    public void Settles_once_when_the_table_stands_with_every_file_loaded_then_is_done_after_its_quiet_frames()
    {
        var settled = new LoadSettled(quietFrames: 2);
        Assert.False(settled.Update(standing: false, modelsDone: 0, modelsTotal: 0, T0));
        Assert.False(settled.Update(standing: true, modelsDone: 3, modelsTotal: 12, T0));
        Assert.False(settled.Done);
        Assert.True(settled.Update(standing: true, modelsDone: 12, modelsTotal: 12, T0));
        Assert.False(settled.Done);                                                         // the settling frame itself
        Assert.False(settled.Update(standing: true, modelsDone: 12, modelsTotal: 12, T0));
        Assert.False(settled.Done);
        Assert.False(settled.Update(standing: true, modelsDone: 12, modelsTotal: 12, T0));
        Assert.True(settled.Done);
        Assert.False(settled.Update(standing: true, modelsDone: 13, modelsTotal: 14, T0));   // a late army's wave: already settled
        Assert.True(settled.Done);
    }

    [Fact]
    public void A_table_with_no_files_settles_as_it_stands()
    {
        var settled = new LoadSettled();
        Assert.True(settled.Update(standing: true, modelsDone: 0, modelsTotal: 0, T0));
    }

    [Fact]
    public void A_wave_still_loading_past_the_patience_settles_anyway()
    {
        var settled = new LoadSettled(quietFrames: 0, patience: TimeSpan.FromSeconds(20));
        Assert.False(settled.Update(standing: true, modelsDone: 1, modelsTotal: 5, T0));
        Assert.False(settled.Update(standing: true, modelsDone: 2, modelsTotal: 5, T0 + TimeSpan.FromSeconds(19)));
        Assert.True(settled.Update(standing: true, modelsDone: 2, modelsTotal: 5, T0 + TimeSpan.FromSeconds(20)));
        Assert.True(settled.Done);
    }

    [Fact]
    public void Patience_counts_from_the_table_standing()
    {
        var settled = new LoadSettled(quietFrames: 0, patience: TimeSpan.FromSeconds(20));
        Assert.False(settled.Update(standing: false, modelsDone: 0, modelsTotal: 5, T0));
        Assert.False(settled.Update(standing: true, modelsDone: 0, modelsTotal: 5, T0 + TimeSpan.FromSeconds(30)));
        Assert.False(settled.Update(standing: true, modelsDone: 0, modelsTotal: 5, T0 + TimeSpan.FromSeconds(49)));
        Assert.True(settled.Update(standing: true, modelsDone: 0, modelsTotal: 5, T0 + TimeSpan.FromSeconds(50)));
    }
}
