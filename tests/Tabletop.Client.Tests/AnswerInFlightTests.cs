using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

/// <summary>A theme change draws the open Ask again unless its answer is on its way.</summary>
public class AnswerInFlightTests
{
    static readonly Guid Ask = Guid.CreateVersion7(), Next = Guid.CreateVersion7(), Cmd = Guid.CreateVersion7(), Other = Guid.CreateVersion7();

    [Fact]
    public void An_Ask_with_no_answer_sent_is_drawn_again()
    {
        var flight = new AnswerInFlight();
        Assert.True(flight.Redraws(Ask));
        Assert.False(flight.Redraws(null));
    }

    [Fact]
    public void An_Ask_whose_answer_went_is_not_drawn_again_until_that_answer_is_refused()
    {
        var flight = new AnswerInFlight();
        flight.Sent(Ask, Cmd);
        Assert.False(flight.Redraws(Ask));
        flight.Refused(Other);
        Assert.False(flight.Redraws(Ask));
        flight.Refused(Cmd);
        Assert.True(flight.Redraws(Ask));
    }

    [Fact]
    public void An_answer_to_another_Ask_leaves_this_one_drawn_again()
    {
        var flight = new AnswerInFlight();
        flight.Sent(Ask, Cmd);
        Assert.True(flight.Redraws(Next));
        flight.Sent(Next, Other);
        Assert.True(flight.Redraws(Ask));
        Assert.False(flight.Redraws(Next));
    }
}
