using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// 3.6 Ravage Aura: "This model and its unit get Ravage(+1)." A granted Ravage that names no model adds its amount per living model of the unit, on top
/// of each model's own Ravage(X).
/// </summary>
public class GfRavageAura36Tests
{
    static readonly EntryDocument Aura = new("Test Ravage Aura", CatalogKind.Model, false, Aggregation.Any, null, null, "test",
        null, [new EffectDocument(EffectSubject.Self, 5, EffectKind.GrantProperty, null, null, GfNames.Ravage, null, null, null, null, 1, false, null, null, null,
            EffectDuration.Interaction, [], [])]);

    static readonly PropertySet Props = PropertiesJson.ToSet(TestProperties.CoreDocument36 with { Entries = [.. TestProperties.CoreDocument36.Entries, Aura] }, Guid.CreateVersion7);

    static readonly int[] Ones = [.. Enumerable.Repeat(1, 40)];

    static Tabletop.Content.Unit Charger(bool ownRavage)
    {
        var c = Unit("C", "4+", "4+", [], 3, [P("Tough", 6)], Melee("Axe", 1));
        return c with { Models = [
            c.Models[0] with { Properties = [.. c.Models[0].Properties, .. (ownRavage ? new[] { P(GfNames.Ravage, 2) } : [])] },
            c.Models[1] with { Properties = [.. c.Models[1].Properties, P(Aura.Name)] },
            c.Models[2]] };
    }

    static Sim Fight(Tabletop.Content.Unit charger, Tabletop.Content.Unit defender, Sim? sim = null)
    {
        sim ??= new Sim(State([charger], [defender], x1: -3, x2: 0).Under(RuleSets.Gf360), GrimdarkFutureRules.V360, Props, new ScriptedDice(Ones));
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [defender.Id], charger.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge));
        for (var i = 0; i < 8; i++)
        {
            if (sim.AskForOrNull(Samples.Team2) is { } ask) sim.Answer(Samples.Team2, ask.Question == "move" ? new Passed() : new Chosen("strike"));
            else if (sim.AskForOrNull(Samples.Team1) is { }) sim.Answer(Samples.Team1, new Passed());
            else break;
        }
        return sim;
    }

    static IReadOnlyList<RollRecord> Rolls(Sim sim) => [.. sim.Emitted.OfType<StepCompleted>().SelectMany(s => s.Rolls).Where(r => r.Roll == GfNames.Ravage)];

    [Theory]
    [InlineData(true, 5)]      // Ravage(2) on one model, +1 on each of three
    [InlineData(false, 3)]     // the grant alone
    public void A_unit_grant_adds_one_die_per_living_model(bool ownRavage, int dice)
    {
        var sim = Fight(Charger(ownRavage), Unit("D", "4+", "4+", [], 1, [P("Tough", 12)]));
        Assert.Equal(dice, Assert.Single(Rolls(sim)).Dice.Count);
        Assert.Contains(sim.Steps("roll"), s => s.Applied?.Contains(Aura.Name) ?? false);
    }

    [Fact]
    public void A_granted_ravage_rolls_once_per_round()
    {
        var charger = Charger(false); var defender = Unit("D", "4+", "4+", [], 1, [P("Tough", 12)]);
        var sim = Fight(charger, defender);
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, charger.Id), GfNames.Fatigued, false)]);
        Fight(charger, defender, sim);
        Assert.Single(Rolls(sim));
    }
}
