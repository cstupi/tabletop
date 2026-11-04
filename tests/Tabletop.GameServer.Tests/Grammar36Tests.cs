using Tabletop.Content;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>The 3.6.0 text-only rules' predicates: weapon_range (Pierce, Smash: "over half of this weapon's range away") and remaining_size
/// (Destructive, Decimate: "over half of their starting size"; 3.6.0 p.10 counts starting size at the beginning of the game).</summary>
public class Grammar36Tests
{
    static EffectDocument HitMod(int amount, params ConditionDocument[] when) =>
        new(EffectSubject.Self, 10, EffectKind.ModifyRoll, "hit", null, null, null, null, null, null, amount, false, null, null, null, EffectDuration.Interaction, when, []);
    static readonly ConditionDocument OverHalfRange = new(ConditionKind.WeaponRange, null, Comparison.Gt, 50, []);
    static readonly ConditionDocument OverHalfSize = new(ConditionKind.RemainingSize, EffectSubject.Opponent, Comparison.Gt, 50, [GfNames.Tough]);

    static readonly PropertySet Props = TestProperties.WithSpells(
        new EntryDocument("Far Shot", CatalogKind.Weapon, false, null, null, null, "Far Shot.", null, [HitMod(1, OverHalfRange)]),
        new EntryDocument("Far Sight", CatalogKind.Model, false, Aggregation.All, null, null, "Far Sight.", null, [HitMod(1, OverHalfRange)]),
        new EntryDocument("Long Barrel", CatalogKind.Unit, false, null, null, null, "Long Barrel.", null,
            [new(EffectSubject.Self, 10, EffectKind.ModifyStat, null, new StatRef(StatLevel.Weapon, "Range"), null, null, null, null, null, 12, false, null, null, null, EffectDuration.Interaction, [], [])]),
        new EntryDocument("Big Game", CatalogKind.Model, false, Aggregation.All, null, null, "Big Game.", null, [HitMod(1, OverHalfSize)]));

    static int HitBonus(LiveSet live) => live.Effects(Side.Attacker, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == "hit").Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));

    static LiveSet Shot(GameState s, Unit a, Unit b, double range) =>
        LiveProperties.Settle(new Interaction(s, Props, Party.Of(s, a.Id, [a.Models[0].Id], a.Models[0].Weapons.Select(w => w.Id)), Party.Whole(s, b.Id), AttackKind.Shooting, null, range, []));

    [Theory]
    [InlineData(13, 1)]
    [InlineData(12, 0)]
    public void Weapon_range_compares_the_measured_range_with_a_percentage_of_the_source_weapons_range(double range, int bonus)
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, P("Far Shot"))); var b = Unit("B", "4+", "4+", [], 1, []);
        Assert.Equal(bonus, HitBonus(Shot(State([a], [b]), a, b, range)));
    }

    [Fact]
    public void Weapon_range_fails_on_a_source_with_no_weapon_and_on_an_interaction_with_no_range()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Far Sight")], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 1, []);
        Assert.Equal(0, HitBonus(Shot(State([a], [b]), a, b, 13)));
        var c = Unit("C", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, P("Far Shot")));
        var s = State([c], [b]);
        Assert.Equal(0, HitBonus(LiveProperties.Settle(new Interaction(s, Props, Party.Of(s, c.Id, [c.Models[0].Id], c.Models[0].Weapons.Select(w => w.Id)), Party.Whole(s, b.Id), AttackKind.Shooting, null, null, []))));
    }

    [Fact]
    public void Weapon_range_reads_the_weapons_range_as_the_live_set_leaves_it()
    {
        var a = Unit("A", "4+", "4+", [P("Long Barrel")], 1, [], Gun("Rifle", "24\"", 1, P("Far Shot"))); var b = Unit("B", "4+", "4+", [], 1, []);
        var s = State([a], [b]);
        Assert.Equal(0, HitBonus(Shot(s, a, b, 13)));   // 36": half is 18
        Assert.Equal(1, HitBonus(Shot(s, a, b, 19)));
    }

    static void Shelve(GameState s, IEnumerable<Guid> models) =>
        s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new ModelsMoved([.. models.Select(m => new ModelRelocation(m, s.Projection.Models[m].Location, new OnShelf()))]), null));
    static void Wound(GameState s, Guid model, int wounds) => s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, new WoundsSet(model, wounds), null));

    /// <summary>Each case: the target as built, and whether it is still over half of its starting size.</summary>
    public static TheoryData<string, string, bool> Sizes() => new()
    {
        { RuleSets.Gf351, "ten with six alive", true }, { RuleSets.Gf351, "ten with five alive", false },
        { RuleSets.Gf351, "tough six with two wounds", true }, { RuleSets.Gf351, "tough six with three wounds", false }, { RuleSets.Gf351, "a single model on the shelf", false },
        { RuleSets.Gf360, "ten with six alive", true }, { RuleSets.Gf360, "ten with five alive", false },
        { RuleSets.Gf360, "tough six with two wounds", true }, { RuleSets.Gf360, "tough six with three wounds", false }, { RuleSets.Gf360, "a single model on the shelf", false },
    };

    static (GameState State, Unit Attacker, Unit Target) Target(string rulesId, string shape)
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Big Game")]);
        var t = shape.StartsWith("ten") ? Unit("T", "4+", "4+", [], 10, []) : Unit("T", "4+", "4+", [], 1, [P(GfNames.Tough, 6)]);
        var s = State([a], [t]).Under(rulesId);
        switch (shape)
        {
            case "ten with six alive": Shelve(s, t.Models.Take(4).Select(m => m.Id)); break;
            case "ten with five alive": Shelve(s, t.Models.Take(5).Select(m => m.Id)); break;
            case "tough six with two wounds": Wound(s, t.Models[0].Id, 2); break;
            case "tough six with three wounds": Wound(s, t.Models[0].Id, 3); break;
            case "a single model on the shelf": Shelve(s, [t.Models[0].Id]); break;
        }
        return (s, a, t);
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Remaining_size_compares_living_models_or_a_single_models_remaining_tough_with_its_starting_size(string rulesId, string shape, bool overHalf)
    {
        var (s, a, t) = Target(rulesId, shape);
        Assert.Equal(overHalf ? 1 : 0, HitBonus(LiveProperties.Settle(new Interaction(s, Props, Party.Whole(s, a.Id), Party.Whole(s, t.Id), AttackKind.Melee, null, 1, []))));
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void At_half_or_less_is_the_negation_of_remaining_size_over_half(string rulesId, string shape, bool overHalf)
    {
        var (s, _, t) = Target(rulesId, shape);
        Assert.Equal(!overHalf, GfHooks.AtHalfOrLess(s, Props, t.Id));
        var (remaining, starting) = Lookup.RemainingSize(s, t.Id, GfNames.Tough);
        Assert.Equal(!overHalf, remaining * 2 <= starting);
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void Extra_wounds_are_laid_in_tough_order_before_the_half_test(string rulesId)
    {
        var ten = Unit("T", "4+", "4+", [], 10, []);
        var single = Unit("S", "4+", "4+", [], 1, [P(GfNames.Tough, 6)]);
        var three = Unit("Three", "4+", "4+", [], 3, [P(GfNames.Tough, 3)]);
        var s = State([ten, single], [three]).Under(rulesId);
        Shelve(s, ten.Models.Take(4).Select(m => m.Id));
        Wound(s, single.Models[0].Id, 2);
        Assert.False(GfHooks.AtHalfOrLess(s, Props, ten.Id));
        Assert.True(GfHooks.AtHalfOrLess(s, Props, ten.Id, extraWounds: 1));
        Assert.True(GfHooks.AtHalfOrLess(s, Props, single.Id, extraWounds: 1));
        // The most-wounded Tough model takes wounds until it is killed: five wounds kill one of three Tough(3) models, six kill two.
        Assert.False(GfHooks.AtHalfOrLess(s, Props, three.Id, extraWounds: 5));
        Assert.True(GfHooks.AtHalfOrLess(s, Props, three.Id, extraWounds: 6));
        Assert.Equal(0, s.Projection.Models[three.Models[0].Id].Wounds);   // laid on a copy, not on the models
    }

    static EffectDocument Row(EffectKind kind, EffectDuration duration, int? amount = 1, TargetModifier? modifier = null, bool optional = false, TargetQuantity? quantity = null, params ConditionDocument[] when) =>
        new(EffectSubject.Opponent, 10, kind, null, null, null, null, quantity, null, modifier, amount, false, null, null, null, duration, when, [], Optional: optional);
    static readonly ConditionDocument BeforeAttacking = new(ConditionKind.AtPoint, null, Comparison.Eq, null, ["before-attacking"]);
    static readonly ConditionDocument BlockOne = new(ConditionKind.RollResult, null, Comparison.Eq, 1, ["defense", "raw"]);

    static PropertySetDocument Document(params EntryDocument[] entries) =>
        TestProperties.RulebookDocument with { Name = "Grammar 3.6", Entries = [.. TestProperties.RulebookDocument.Entries, .. entries] };

    static readonly EntryDocument[] EveryNewValue =
    [
        new("Tagger", CatalogKind.Model, false, Aggregation.Any, null, null, "Tagger.", new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 18, OncePerRound: true, CountFromParameter: true),
            [Row(EffectKind.ModifyRoll, EffectDuration.OneMarker, when: BeforeAttacking) with { TargetRoll = "defense" }]),
        new("Ranged", CatalogKind.Weapon, false, null, null, null, "Ranged.", null,
            [Row(EffectKind.SuppressModifiers, EffectDuration.Interaction, null, TargetModifier.PositiveDefense, when: OverHalfRange), Row(EffectKind.CountWounds, EffectDuration.Interaction, 2, when: BlockOne),
             Row(EffectKind.RemoveSpellTokens, EffectDuration.Interaction, 2), Row(EffectKind.AddHit, EffectDuration.Interaction, 1, optional: true, quantity: TargetQuantity.Attacks, when: BlockOne) with { Subject = EffectSubject.Self },
             Row(EffectKind.ModifyRoll, EffectDuration.Interaction, when: OverHalfSize) with { TargetRoll = "hit", Subject = EffectSubject.Self }]),
    ];

    [Fact]
    public void Every_new_value_round_trips_through_the_document_and_an_absent_one_writes_nothing()
    {
        var doc = Document(EveryNewValue);
        Assert.Empty(PropertiesJson.Validate(doc));
        var back = PropertiesJson.Parse(System.Text.Json.JsonSerializer.Serialize(doc, PropertiesJson.Options));
        Assert.Equal(PropertiesJson.Hash(doc), PropertiesJson.Hash(back));
        var set = PropertiesJson.ToSet(back, Guid.CreateVersion7);
        var tagger = set.Entry("Tagger")!;
        Assert.Equal((true, true, EffectDuration.OneMarker), (tagger.Targeting!.OncePerRound, tagger.Targeting.CountFromParameter, tagger.Effects[0].Duration));
        var ranged = set.Entry("Ranged")!.Effects;
        Assert.Equal([EffectKind.SuppressModifiers, EffectKind.CountWounds, EffectKind.RemoveSpellTokens, EffectKind.AddHit, EffectKind.ModifyRoll], ranged.Select(e => e.Kind));
        Assert.Equal(TargetModifier.PositiveDefense, ranged[0].TargetModifier);
        Assert.Equal(ConditionKind.WeaponRange, ranged[0].Conditions[0].Kind);
        Assert.Equal([false, false, false, true, false], ranged.Select(e => e.Optional));
        Assert.Equal((ConditionKind.RemainingSize, (EffectSubject?)EffectSubject.Opponent), (ranged[4].Conditions[0].Kind, ranged[4].Conditions[0].Subject));
        Assert.Equal([GfNames.Tough], ranged[4].Conditions[0].Values);
        var plain = PropertiesJson.Canonical(TestProperties.RulebookDocument);
        Assert.DoesNotContain("\"optional\"", plain);
        Assert.DoesNotContain("\"oncePerRound\"", plain);
        Assert.DoesNotContain("\"countFromParameter\"", plain);
    }

    [Fact]
    public void Validation_holds_the_new_values_to_their_shapes()
    {
        var ranged = EveryNewValue[1];
        var tagger = EveryNewValue[0];
        var problems = PropertiesJson.Validate(Document(
            ranged with { Name = "Subject On Range", Effects = [HitMod(1, OverHalfRange with { Subject = EffectSubject.Opponent })] },
            ranged with { Name = "No Number On Range", Effects = [HitMod(1, OverHalfRange with { Number = null })] },
            ranged with { Name = "No Value On Size", Effects = [HitMod(1, OverHalfSize with { Values = [] })] },
            ranged with { Name = "No Subject On Size", Effects = [HitMod(1, OverHalfSize with { Subject = null })] },
            ranged with { Name = "No Number On Size", Effects = [HitMod(1, OverHalfSize with { Number = null })] },
            ranged with { Name = "Optional Off Add Hit", Effects = [HitMod(1) with { Optional = true }] },
            tagger with { Name = "Token Without Targeting", Kind = CatalogKind.Model, Targeting = null }));
        Assert.Contains(problems, p => p.StartsWith("Subject On Range:") && p.Contains("takes no subject"));
        Assert.Contains(problems, p => p.StartsWith("No Number On Range:") && p.Contains("weapon_range needs a number"));
        Assert.Contains(problems, p => p.StartsWith("No Value On Size:") && p.Contains("names exactly one value"));
        Assert.Contains(problems, p => p.StartsWith("No Subject On Size:") && p.Contains("needs a subject"));
        Assert.Contains(problems, p => p.StartsWith("No Number On Size:") && p.Contains("remaining_size needs a number"));
        Assert.Contains(problems, p => p.StartsWith("Optional Off Add Hit:") && p.Contains("optional is add_hit only"));
        Assert.Contains(problems, p => p.StartsWith("Token Without Targeting:") && p.Contains("one_marker"));
    }
}
