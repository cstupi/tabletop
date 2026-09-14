namespace Tabletop.Content.Tests;

public class StatValueTests
{
    [Theory]
    [InlineData("4+", 4)]
    [InlineData("2", 2)]
    public void Target_number_reads_the_score(string text, int expected) =>
        Assert.Equal(expected, StatValue.Read(StatKind.TargetNumber, text).TargetNumber);

    [Theory]
    [InlineData("24\"", 24.0)]
    [InlineData("6", 6.0)]
    [InlineData("1.5\"", 1.5)]
    public void Distance_reads_inches(string text, double expected) =>
        Assert.Equal(expected, StatValue.Read(StatKind.Distance, text).Inches);

    [Fact]
    public void Count_reads_an_integer() => Assert.Equal(3, StatValue.Read(StatKind.Count, "3").Count);

    [Fact]
    public void Dice_expression_is_kept_as_text() => Assert.Equal("D6", StatValue.Read(StatKind.DiceExpression, "D6").Text);

    [Fact]
    public void Wrong_format_is_a_FormatException() => Assert.Throws<FormatException>(() => StatValue.Read(StatKind.Count, "4+"));

    [Fact]
    public void Wrong_kind_accessor_is_an_InvalidOperationException() =>
        Assert.Throws<InvalidOperationException>(() => StatValue.Read(StatKind.Count, "3").Inches);
}
