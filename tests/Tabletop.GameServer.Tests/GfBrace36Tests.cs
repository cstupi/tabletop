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
/// Brace (Army Forge's common rules): "Enemies charging this unit get -X total Impact rolls, unless this unit is Fatigued." Read off the defender's
/// melee weapons; instances of one weapon type collapse, so it is X per weapon type, not per model. A Shaken unit counts as Fatigued (3.6.0 p.10).
/// </summary>
public class GfBrace36Tests
{
    static readonly EntryDocument Brace = new(GfNames.Brace, CatalogKind.Weapon, true, null, null, null,
        "Enemies charging this unit get -X total Impact rolls, unless this unit is Fatigued.", null, []);

    static readonly PropertySet Props = PropertiesJson.ToSet(TestProperties.CoreDocument36 with
        { Entries = [.. TestProperties.CoreDocument36.Entries.Where(e => e.Name != GfNames.Brace), Brace] }, Guid.CreateVersion7);

    static readonly int[] Ones = [.. Enumerable.Repeat(1, 40)];

    static Tabletop.Content.Unit Riders() => Unit("Riders", "4+", "4+", [], 3, [P("Impact", 2), P("Tough", 3)], Melee("Thrust", 1));

    static int ImpactDice(string rules, Tabletop.Content.Unit riders, Tabletop.Content.Unit defender, string? condition = null)
    {
        var state = State([riders], [defender], x1: -3, x2: 0).Under(rules);
        var sim = rules == RuleSets.Gf360 ? new Sim(state, GrimdarkFutureRules.V360, Props, new ScriptedDice(Ones)) : Sim.Gf(state, Ones);
        if (condition is not null) sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, defender.Id), condition, true)]);
        sim.Start(GfNames.Melee, new SequenceContext(riders.Id, [defender.Id], riders.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge));
        var impact = sim.Steps("impact").First();
        if (rules == RuleSets.Gf360) Assert.Equal(impact.Rolls.Count > 0 && impact.Rolls.Single().Dice.Count < 6, impact.Applied?.Contains(GfNames.Brace) ?? false);
        return impact.Rolls.Sum(r => r.Dice.Count);
    }

    /// <summary>Five Spears (Counter, Brace(1)): "-X total" with one weapon type takes one die under 3.6; 3.5.1's Counter takes one die per model.</summary>
    [Theory]
    [InlineData(RuleSets.Gf351, 1)]
    [InlineData(RuleSets.Gf360, 5)]
    public void Five_spears_take_one_die(string rules, int dice) =>
        Assert.Equal(dice, ImpactDice(rules, Riders(), Unit("Spears", "4+", "4+", [], 5, [], Melee("Spear", 1, P("Counter"), P(GfNames.Brace, 1)))));

    [Fact]
    public void Two_weapon_types_take_their_sum()
    {
        var d = Unit("Guard", "4+", "4+", [], 2, []);
        d = d with { Models = [d.Models[0] with { Weapons = [Melee("Spear", 1, P(GfNames.Brace, 1))] }, d.Models[1] with { Weapons = [Melee("Halberd", 1, P(GfNames.Brace, 3))] }] };
        Assert.Equal(2, ImpactDice(RuleSets.Gf360, Riders(), d));
    }

    [Theory]
    [InlineData(GfNames.Fatigued)]
    [InlineData(GfNames.Shaken)]
    public void A_fatigued_or_shaken_defender_takes_none(string condition) =>
        Assert.Equal(6, ImpactDice(RuleSets.Gf360, Riders(), Unit("Spears", "4+", "4+", [], 5, [], Melee("Spear", 1, P(GfNames.Brace, 1))), condition));
}
