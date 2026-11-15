using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class MutedPromptsTests
{
    [Theory]
    [InlineData("before-attacking", "before attacking", "Before attacking")]
    [InlineData("activation_end", "activation end", "Activation end")]
    [InlineData("casting", "casting", "Casting")]
    public void A_window_kind_reads_as_words(string kind, string words, string label) =>
        Assert.Equal((words, label), (MutedPrompts.Words(kind), MutedPrompts.Label(kind)));

    [Fact]
    public void The_muted_prompts_are_listed_in_order_with_their_labels()
    {
        var s = ClientSettings.Default.Mute("casting").Mute("before-attacking");
        Assert.Equal([("before-attacking", "Before attacking"), ("casting", "Casting")], MutedPrompts.Of(s.MutedWindowKinds));
        Assert.Empty(MutedPrompts.Of(s.Unmute("casting").Unmute("before-attacking").MutedWindowKinds));
    }
}
