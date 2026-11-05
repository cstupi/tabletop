using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Scourge on a spell's hits counts as shooting's does (owner ruling): "For each unmodified result of 1 to block hits, enemies count as having taken 2
/// extra wounds when checking who won melee, or if shooting wounds would trigger Morale Tests." Dwarf Guilds' Smiting Rune, shipped 3.6: "Pick one enemy
/// unit within 12" in line of sight, which takes 4 hits with Scourge." 3.6.0 p.10: "At the end of an activation in which a unit takes wounds that leave
/// it with half or less of its starting size or Tough value (for units with a single model), it must take a Morale Test."
/// </summary>
public class SpellScourge36Tests
{
    const string DwarfGuilds = "fk1mkbp8apvltu0z";

    /// <summary>A Hold that casts Smiting Rune on a five-model, one-wound unit 6" away and shoots nothing: the cast's 6, then the four blocks.</summary>
    static (StepCompleted Block, Sim Sim, Guid Wizard, Guid Target) Cast(params int[] blocks)
    {
        var wizard = InBook(Unit("Wizard", "4+", "4+", [], 1, [P(GfNames.Caster, 3)]), DwarfGuilds);
        var target = Unit("T", "4+", "4+", [], 5, []);
        var sim = Sim.Gf36(State([wizard], [target], x1: -3, x2: 3), [6, .. blocks]);
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 3)]);
        sim.Start(GfNames.HoldSeq, new SequenceContext(wizard.Id, [], [], GfNames.Hold));
        for (var i = 0; i < 20 && sim.State.Projection.OpenAsks.Values.FirstOrDefault(a => a.Answers.Count == 0)?.Ask is { } ask; i++)
            sim.Answer(ask.Teams[0], ask.Legal switch
            {
                Options o when o.Choices.Any(c => c.Id == GfCasting.CastOption) => new Chosen(GfCasting.CastOption),
                Options o when o.Choices.Any(c => c.Id.StartsWith("Smiting Rune|")) => new Chosen(o.Choices.First(c => c.Id.StartsWith("Smiting Rune|")).Id),
                PieceSelection => new SelectedPieces([target.Id]),
                Allocation a => new Allocated(a.Targets.Take(a.Amount).ToDictionary(m => m, _ => 1)),
                _ when ask.Question == "shoot_or_not" => new Chosen("hold"),
                _ => new Passed(),
            });
        return (sim.Steps("roll-to-block").Single(), sim, wizard.Id, target.Id);
    }

    static bool Tests((StepCompleted Block, Sim Sim, Guid Wizard, Guid Target) cast) =>
        GfHooks.UnitsToTestAtEndOfActivation(cast.Sim.State, TestProperties.Core36, cast.Wizard).Contains(cast.Target);

    [Fact]
    public void One_unmodified_one_counts_two_wounds_toward_the_end_of_activation_morale_test()
    {
        var cast = Cast(1, 6, 6, 6);   // one wound: 4 of 5 left, less 2 counted is 2, half or less
        Assert.Equal(2, cast.Block.Values!["counted_wounds"]);
        Assert.Equal(4, cast.Sim.Alive(cast.Target));
        Assert.True(Tests(cast));
    }

    [Fact]
    public void A_failed_block_that_is_no_one_counts_nothing()
    {
        var cast = Cast(2, 6, 6, 6);   // one wound: 4 of 5 left
        Assert.False(cast.Block.Values!.ContainsKey("counted_wounds"));
        Assert.Equal(4, cast.Sim.Alive(cast.Target));
        Assert.False(Tests(cast));
    }

    [Fact]
    public void No_one_means_no_extra_wounds()
    {
        var cast = Cast(6, 6, 6, 6);
        Assert.False(cast.Block.Values!.ContainsKey("counted_wounds"));
        Assert.Equal(5, cast.Sim.Alive(cast.Target));
        Assert.False(Tests(cast));
    }
}
