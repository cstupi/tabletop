namespace Tabletop.Protocol.Tests;

public class CommandTests
{
    [Fact]
    public void Every_command_is_registered_once()
    {
        var registered = StateChangeTests.Registered(typeof(Command));
        foreach (var t in StateChangeTests.ConcreteSubclasses(typeof(Command)))
            Assert.True(registered.ContainsKey(t), $"{t.Name} missing");
        Assert.Equal(registered.Count, registered.Values.Distinct().Count());
    }

    [Fact]
    public void There_is_no_end_turn_or_pass_command() =>
        Assert.DoesNotContain(StateChangeTests.ConcreteSubclasses(typeof(Command)), t => t.Name is "EndTurn" or "EndActivation" or "Pass");
}
