using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// weapon_range on a spell's hits (owner ruling): a spell's printed range is "this weapon's range", and the target's distance is the caster model's to
/// the target unit's nearest model, as a spell's range is measured (p.5 base to base, never rounded). Smash: "When attacking units over half of this
/// weapon's range away, the target must re-roll unmodified Defense results of 6." Pierce: "Ignores all positive Defense modifiers on the target, and
/// when attacking units over half of this weapon's range away, this weapon gets AP(+1)." Battle Brothers' Cerebral Trauma ("Pick one enemy unit within
/// 24" in line of sight, which takes 6 hits with AP(1) and Smash.") and Prime Brothers' Mind Wound ("Pick one enemy unit within 18" in line of sight,
/// which takes 6 hits with AP(1) and Pierce."), shipped 3.6.
/// </summary>
public class SpellWeaponRange36Tests
{
    const string BattleBrothers = "78qp9l5alslt6yj8", PrimeBrothers = "oqnnu0gk8q6hyyny", Shield = "Test Shield";

    /// <summary>No shipped rule gives a positive Defense modifier against a spell's hits, so the test adds one: +1 to Defense rolls.</summary>
    static readonly EntryDocument ShieldRule = new(Shield, CatalogKind.Unit, false, null, null, null, "+1 to Defense rolls.", null,
        [new EffectDocument(EffectSubject.Self, 10, EffectKind.ModifyRoll, GfNames.DefenseRoll, null, null, null, null, null, null, 1, false, null, null, null, EffectDuration.Interaction, [], [])]);
    static readonly PropertySet Props = PropertiesJson.ToSet(TestProperties.CoreDocument36 with { Entries = [.. TestProperties.CoreDocument36.Entries, ShieldRule] }, Guid.CreateVersion7);

    static void Put(Sim sim, Guid model, double x, double y) =>
        sim.Append([new ModelsMoved([new ModelRelocation(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))))])]);

    /// <summary>The caster's first model at (0, -20) and its second at <paramref name="mateX"/>; the target's one model <paramref name="centres"/>" east of the caster
    /// (1.26" bases, so base to base is 1.26" less). Casts the spell on it and returns the roll-to-block step.</summary>
    static (StepCompleted Block, Sim Sim) Cast(string book, string spell, double centres, PropertyInstance[] targetRules, int[] dice, double mateX = -10)
    {
        var wizard = InBook(Unit("Wizard", "4+", "4+", [], 2, [P(GfNames.Caster, 3)]), book);
        var target = Unit("T", "4+", "4+", targetRules, 1, []);
        var sim = new Sim(State([wizard], [target], x1: -20, x2: 20).Under(RuleSets.Gf360), GrimdarkFutureRules.V360, Props, new ScriptedDice(dice));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 3)]);
        Put(sim, wizard.Models[0].Id, 0, -20); Put(sim, wizard.Models[1].Id, mateX, -20); Put(sim, target.Models[0].Id, centres, -20);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"{spell}|{wizard.Models[0].Id}"));
        for (var i = 0; i < 6 && sim.AskForOrNull(Samples.Team1) is { } ask && ask.Legal is not PieceSelection; i++) sim.Answer(Samples.Team1, new Passed());
        sim.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        return (sim.Steps("roll-to-block").Single(), sim);
    }

    /// <summary>The defense dice a forced re-roll replaced.</summary>
    static int Rerolled((StepCompleted Block, Sim Sim) cast) =>
        cast.Sim.Emitted.OfType<DiceRerolled>().Where(r => r.RollId == cast.Block.Rolls.Single().Id).Sum(r => r.Replaced.Count);

    [Theory]
    [InlineData(14.0, 6)]   // 12.74" base to base: over half of 24"
    [InlineData(13.0, 0)]   // 11.74": half or under
    public void A_smash_spell_rerolls_raw_sixes_only_on_a_target_over_half_its_range_away(double centres, int rerolled)
    {
        // The cast's 6, then six defense 6s, then the re-rolls.
        Assert.Equal(rerolled, Rerolled(Cast(BattleBrothers, "Cerebral Trauma", centres, [], [6, 6, 6, 6, 6, 6, 6, 2, 2, 2, 2, 2, 2])));
    }

    [Fact]
    public void The_distance_is_the_caster_models_not_its_units()
    {
        // The caster model is 12.74" from the target; its unit-mate is 3.74" from it.
        Assert.Equal(6, Rerolled(Cast(BattleBrothers, "Cerebral Trauma", 14.0, [], [6, 6, 6, 6, 6, 6, 6, 2, 2, 2, 2, 2, 2], mateX: 9)));
    }

    [Theory]
    [InlineData(11.0, -2)]   // 9.74" base to base: over half of 18", AP(1) and Pierce's AP(+1)
    [InlineData(10.0, -1)]   // 8.74": AP(1) alone
    public void A_pierce_spell_gets_ap_plus_one_over_half_its_range_and_ignores_positive_defense_at_any_range(double centres, int ap)
    {
        var block = Cast(PrimeBrothers, "Mind Wound", centres, [P(Shield)], [6, 4, 4, 4, 4, 4, 4]).Block.Rolls.Single();
        Assert.Equal(ap, block.Modifiers.Where(m => m.Source == "AP").Sum(m => m.Amount));
        Assert.DoesNotContain(block.Modifiers, m => m.Amount > 0);
        Assert.Contains(new IgnoredEffect(Shield, "Pierce", 1), block.Ignored!);
    }

    static LiveSet Shot(GameState s, Unit a, Unit b, double range) =>
        LiveProperties.Settle(new Interaction(s, TestProperties.Core36, Party.Of(s, a.Id, [a.Models[0].Id], a.Models[0].Weapons.Select(w => w.Id)), Party.Whole(s, b.Id), AttackKind.Shooting, null, range, []));

    [Theory]
    [InlineData(13, 2, 1)]
    [InlineData(12, 1, 0)]
    public void A_weapons_pierce_and_smash_read_the_weapons_range(double range, int ap, int smashRows)
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, P("AP", 1), P("Pierce"), P("Smash"))); var b = Unit("B", "4+", "4+", [], 1, []);
        var live = Shot(State([a], [b]).Under(RuleSets.Gf360), a, b, range);
        Assert.Equal(ap, live.Parameter(Side.Attacker, "AP"));
        Assert.Equal(smashRows, live.Effects(Side.Defender, EffectKind.ForceReroll, new DieContext(GfNames.DefenseRoll, 6, 6, true)).Count(x => x.Source.Entry.Name == "Smash"));
    }
}
