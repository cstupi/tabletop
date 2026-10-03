using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public partial class ArmyRuleHookTests
{
    static readonly PropertySet Core = TestProperties.Core;
    static ConditionDocument At(string point) => new(ConditionKind.AtPoint, null, Comparison.Eq, null, [point]);
    static EffectDocument Move(int inches, params ConditionDocument[] when) =>
        new(EffectSubject.Self, 10, EffectKind.Move, null, null, null, null, null, null, null, inches, false, null, null, null, EffectDuration.Interaction, when, []);

    [Fact]
    public void An_at_point_row_is_returned_only_at_its_point_and_never_by_an_interaction_query()
    {
        var hop = new EntryDocument("Hop", CatalogKind.Model, false, Aggregation.All, null, null, "Hop.", null, [Move(3, At(GfNames.AfterShooting))]);
        var props = TestProperties.WithSpells(hop);
        var a = Unit("A", "4+", "4+", [], 1, [P("Hop")]);
        var s = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
        LiveSet Live(string? point) => LiveProperties.Settle(new Interaction(s, props, Party.Whole(s, a.Id), null, null, null, null, [], point));
        Assert.Single(Live(GfNames.AfterShooting).AtPoint(Side.Attacker, GfNames.AfterShooting));
        Assert.Empty(Live(GfNames.AfterMelee).AtPoint(Side.Attacker, GfNames.AfterMelee));
        Assert.Empty(Live(null).Effects(Side.Attacker, EffectKind.Move));
    }

    [Fact]
    public void A_round_scoped_usage_counts_this_rules_sequences_since_the_round_started()
    {
        var once = new EntryDocument("Once", CatalogKind.Model, false, Aggregation.All, null, null, "Once per round.", null,
            [Move(3, At(GfNames.AfterShooting), new ConditionDocument(ConditionKind.UsageCount, null, Comparison.Lt, 1, ["round"]))]);
        var props = TestProperties.WithSpells(once);
        var a = Unit("A", "4+", "4+", [], 1, [P("Once")]);
        var s = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
        LiveSet Now() => LiveProperties.Settle(new Interaction(s, props, Party.Whole(s, a.Id), null, null, null, null, [], GfNames.AfterShooting));
        Assert.Single(Now().AtPoint(Side.Attacker, GfNames.AfterShooting));
        s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new SequenceStarted(Guid.CreateVersion7(), GfNames.RepositionSeq, new SequenceContext(a.Id, [], [], "Once")), null));
        Assert.Empty(Now().AtPoint(Side.Attacker, GfNames.AfterShooting));
        s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new RoundStarted(2, [], []), null));
        Assert.Single(Now().AtPoint(Side.Attacker, GfNames.AfterShooting));
    }

    static EffectDocument Row(EffectKind kind, string? entry = null) =>
        new(EffectSubject.Self, 10, kind, null, null, entry, null, null, null, null, 1, false, null, null, null, EffectDuration.Once, [], []);

    [Fact]
    public void A_granted_rules_own_pass_one_rows_run_so_a_granted_boost_suppresses_its_base()
    {
        var grantBoost = new EntryDocument("Boost It", CatalogKind.Spell, false, null, 1, null, "Boost it.", new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 18), [Row(EffectKind.GrantProperty, "Boosted")]);
        var boosted = new EntryDocument("Boosted", CatalogKind.Model, false, Aggregation.All, null, null, "Boosted.", null, [Row(EffectKind.SuppressProperty, "Base")]);
        var baseRule = new EntryDocument("Base", CatalogKind.Model, false, Aggregation.All, null, null, "Base.", null, []);
        var props = TestProperties.WithSpells(grantBoost, boosted, baseRule);
        var a = Unit("A", "4+", "4+", [], 1, [P("Base")]);
        var s = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
        LiveSet Live() => LiveProperties.Settle(new Interaction(s, props, Party.Whole(s, a.Id), null, null, null, null, []));
        Assert.True(Live().Has(Side.Attacker, "Base"));
        var row = props.Entry("Boost It")!.Effects.Single();
        s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new GrantAdded(a.Id, new Grant(Guid.CreateVersion7(), row.Id, "Boost It", a.Id, 1)), null));
        var live = Live();
        Assert.True(live.Has(Side.Attacker, "Boosted"));
        Assert.False(live.Has(Side.Attacker, "Base"));
    }

    [Fact]
    public void Pass_one_stops_when_a_rule_grants_itself()
    {
        var loop = new EntryDocument("Loop", CatalogKind.Model, false, Aggregation.All, null, null, "Loop.", null, [Row(EffectKind.GrantProperty, "Loop")]);
        var props = TestProperties.WithSpells(loop);
        var a = Unit("A", "4+", "4+", [], 1, [P("Loop")]);
        var s = State([a], [Unit("B", "4+", "4+", [], 1, [])]);
        var live = LiveProperties.Settle(new Interaction(s, props, Party.Whole(s, a.Id), null, null, null, null, []));
        Assert.True(live.Has(Side.Attacker, "Loop"));
    }
}
