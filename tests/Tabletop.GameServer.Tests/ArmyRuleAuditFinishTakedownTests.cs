using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// 3.6.0 p.15 Takedown: "This model may pick any model in the target unit as its individual target for this weapon's attacks, which is resolved as if it
/// was a unit of [1]." An Aura worded "This model and its unit get Stealth" gives the rule to the unit's models, so the picked model keeps it while the
/// Aura's carrier lives; the rule's majority is then judged over the picked model alone.
/// </summary>
public class ArmyRuleAuditFinishTakedownTests
{
    [Theory]
    [InlineData(false, -1)]   // the carrier stands: the picked model has Stealth from the Aura, and is the whole "unit of [1]"
    [InlineData(true, 0)]     // the carrier is gone: no Aura
    public void A_picked_model_keeps_what_another_models_aura_gives_it_while_the_carrier_lives(bool carrierDead, int stealth)
    {
        var sniper = Unit("Sniper", "4+", "4+", [], 1, [], Gun("Long Rifle", "30\"", 1, P("Takedown")));
        var squad = Unit("Squad", "4+", "6+", [], 3, []);
        squad = squad with { Models = [.. squad.Models.Select((m, i) => i == 1 ? m with { Properties = [P("Stealth Aura")] } : m)] };
        var sim = Sim.Gf36(State([sniper], [squad], x1: -6, x2: 6), 1);
        if (carrierDead) sim.Append([new ModelsMoved([new ModelRelocation(squad.Models[1].Id, sim.Model(squad.Models[1].Id).Location, new OnShelf())])]);
        sim.Start(GfNames.AttackSequence, new SequenceContext(sniper.Id, [squad.Id], sniper.Models[0].Weapons.Select(w => w.Id).ToList(), GfNames.Hold));
        sim.Answer(Samples.Team1, new SelectedPieces([squad.Models[0].Id]));
        Assert.Equal(stealth, sim.Steps("roll-to-hit").Single().Rolls.Single().Modifiers.Where(m => m.Source == "Stealth").Sum(m => m.Amount));
    }
}
