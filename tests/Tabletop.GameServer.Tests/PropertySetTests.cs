using Tabletop.Content;
using Tabletop.GameServer.Properties;

namespace Tabletop.GameServer.Tests;

public class PropertySetTests
{
    public static Effect ModifyRoll(string roll, int amount, bool fromParameter = false, EffectSubject subject = EffectSubject.Opponent, int ordinal = 10, params EffectCondition[] conditions) =>
        new(Guid.CreateVersion7(), subject, ordinal, EffectKind.ModifyRoll, roll, null, null, null, null, null, null, amount, fromParameter, null, null, null, EffectDuration.Interaction, conditions, []);

    public static PropertySet Tiny() => new("sha256:tiny", "Tiny", "grimdark-future",
        [new StatDefinition(StatLevel.Unit, "Quality", StatKind.TargetNumber), new StatDefinition(StatLevel.Weapon, "Range", StatKind.Distance)],
        [new ActionDefinition("Hold", 0), new ActionDefinition("Advance", 6)],
        [new RollDefinition("hit"), new RollDefinition("defense")],
        [new CatalogEntry("AP", CatalogKind.Weapon, true, null, null, null, "Targets get -X to Defense rolls.", null, [ModifyRoll("defense", -1, fromParameter: true)])]);

    [Fact]
    public void Lookups_answer_by_name_and_level()
    {
        var set = Tiny();
        Assert.Equal(6, set.Action("Advance").Distance);
        Assert.Throws<KeyNotFoundException>(() => set.Action("Rush"));
        Assert.Equal(StatKind.Distance, set.Stat(StatLevel.Weapon, "Range")!.Kind);
        Assert.Null(set.Stat(StatLevel.Unit, "Range"));
        Assert.True(set.HasRoll("hit")); Assert.False(set.HasRoll("save"));
        Assert.Equal(-3, set.Entry("AP")!.Effects[0].EffectiveAmount(3));
        Assert.Equal(-1, set.Entry("AP")!.Effects[0] with { AmountFromParameter = false } is var e ? e.EffectiveAmount(3) : 0);
    }

    [Fact]
    public void Comparison_covers_the_six_operators_and_neq_tests_absence()
    {
        EffectCondition C(Comparison c) => new(Guid.CreateVersion7(), ConditionKind.Range, null, c, 9, []);
        Assert.True(C(Comparison.Gt).Compare(10)); Assert.False(C(Comparison.Gt).Compare(9));
        Assert.True(C(Comparison.Gte).Compare(9)); Assert.True(C(Comparison.Lt).Compare(8)); Assert.True(C(Comparison.Lte).Compare(9));
        Assert.True(C(Comparison.Eq).Compare(9)); Assert.True(C(Comparison.Neq).Compare(0));
    }

    [Fact]
    public void Enums_spell_as_the_schema_does()
    {
        Assert.Equal("\"modify_roll\"", ContentJson.Serialize(EffectKind.ModifyRoll));
        Assert.Equal("\"models_in_target_unit\"", ContentJson.Serialize(CapKind.ModelsInTargetUnit));
        Assert.Equal("\"to_hit_penalties\"", ContentJson.Serialize(TargetModifier.ToHitPenalties));
        Assert.Equal(ConditionKind.PositionTerrain, ContentJson.Deserialize<ConditionKind>("\"position_terrain\""));
    }
}
