namespace Tabletop.Protocol.Tests;

public class AskTests
{
    public static Ask Activate(Guid team, Guid unit) => new(
        Guid.CreateVersion7(), AskQuestions.Activate, "Activate a unit", [team],
        new Options([new AskOption($"{unit}:Advance", "Advance", [unit]), new AskOption($"{unit}:Hold", "Hold", [unit])]),
        AllowPass: true, Sealed: false, WindowKind: null, FrameId: null);

    [Fact]
    public void Every_legal_set_and_answer_kind_is_registered()
    {
        foreach (var baseType in new[] { typeof(LegalSet), typeof(AskAnswer) })
        {
            var registered = StateChangeTests.Registered(baseType);
            foreach (var t in StateChangeTests.ConcreteSubclasses(baseType))
                Assert.True(registered.ContainsKey(t), $"{t.Name} missing on {baseType.Name}");
            Assert.Equal(registered.Count, registered.Values.Distinct().Count());
        }
    }

    [Fact]
    public void A_sealed_token_spend_caps_every_spender_at_its_own_tokens()
    {
        // p.13 Caster(X): each model spends any number of the tokens it holds, so the cap is per model and
        // a team's maximum is the sum of its own entries. One legal set serves both addressed teams.
        Guid a1 = Guid.CreateVersion7(), a2 = Guid.CreateVersion7(), b1 = Guid.CreateVersion7();
        var teamA = Guid.CreateVersion7(); var teamB = Guid.CreateVersion7();
        var spend = new TokenSpend(new Dictionary<Guid, int> { [a1] = 2, [a2] = 3, [b1] = 1 });
        var ask = new Ask(Guid.CreateVersion7(), AskQuestions.SpendTokens, "Spend tokens", [teamA, teamB],
            spend, AllowPass: true, Sealed: true, WindowKind: "casting", FrameId: Guid.CreateVersion7());
        Assert.Equal(2, ask.Teams.Count);
        Assert.True(ask.Sealed);
        Assert.Equal(5, new[] { a1, a2 }.Sum(m => spend.MaxByModel[m]));                                    // team A's maximum, carried nowhere else
        Assert.Equal(1, spend.MaxByModel[b1]);
        var answer = new TokensSpent(new Dictionary<Guid, int> { [a2] = 3 });                                // a1 kept its two for its own spell
        Assert.False(answer.Counts.ContainsKey(a1));
        Assert.Equal(3, answer.Counts.Values.Sum());
    }

    [Fact]
    public void An_assignment_item_takes_only_its_own_targets_when_the_set_lists_them()
    {
        // p.8: "Each group may be fired at a different target" — and a target is valid for a group only with a weapon of it in range.
        Guid near = Guid.CreateVersion7(), far = Guid.CreateVersion7();
        var items = new[] { new AskOption("Rifles", "Rifles", []), new AskOption("Pistols", "Pistols", []) };
        var split = new Assignment(items, [near, far], 2, new Dictionary<string, IReadOnlyList<Guid>> { ["Rifles"] = [near, far], ["Pistols"] = [near] });
        Assert.Equal([near, far], split.TargetsOf("Rifles"));
        Assert.Equal([near], split.TargetsOf("Pistols"));
        Assert.Empty(split.TargetsOf("Grenades"));                                                          // an item the map does not name takes none
        var open = new Assignment(items, [near, far], 2);
        Assert.Null(open.TargetsByItem);
        Assert.Equal([near, far], open.TargetsOf("Pistols"));                                               // no map: every item takes any target
    }

    [Fact]
    public void An_assignment_without_per_item_targets_reads_from_json_that_omits_them()
    {
        var unit = Guid.CreateVersion7();
        var json = $$"""{"kind":"assignment","items":[{"id":"g1","label":"Rifles","pieces":[]}],"targets":["{{unit}}"],"maxDistinctTargets":2}""";
        var legal = Assert.IsType<Assignment>(WireJson.Deserialize<LegalSet>(json));
        Assert.Null(legal.TargetsByItem);
        Assert.Equal([unit], legal.TargetsOf("g1"));
    }

    [Fact]
    public void A_piece_selection_is_answered_at_either_grain()
    {
        // Unit ids for deploy and a charge target; model ids for a model-grain spell and for a Takedown's
        // target model (p.14). Nothing in the type or its answer distinguishes the two.
        Guid unit = Guid.CreateVersion7(), model = Guid.CreateVersion7();
        Assert.Equal([unit], new PieceSelection([unit], 1, 1).Eligible);
        var takedown = new PieceSelection([model, Guid.CreateVersion7()], 1, 1);
        Assert.Equal(1, takedown.Max);
        Assert.Equal([model], new SelectedPieces([model]).PieceIds);
    }

    [Fact]
    public void A_move_says_nothing_about_containers_unless_one_is_in_play()
    {
        var model = Guid.CreateVersion7(); var bus = Guid.CreateVersion7();
        Assert.Null(new MoveRegion([model], 6, [], [], 6).Containment);                                     // the ordinary move: five arguments, Containment null
        var embark = new MoveRegion([model], 6, [], [], 6, new MoveContainment([bus], null, null));
        Assert.Equal([bus], embark.Containment!.Enterable);
        Assert.Null(embark.Containment.ExitingId);
        var disembark = new MoveRegion([model], 6, [], [], 6, new MoveContainment([], bus, 6));
        Assert.Equal((bus, 6.0), (disembark.Containment!.ExitingId!.Value, disembark.Containment.ExitWithin!.Value));
        Assert.Empty(disembark.Containment.Enterable);                                                      // never both in one activation
    }

    [Fact]
    public void A_move_region_carries_its_separation_and_whether_it_passes_models()
    {
        var plain = new MoveRegion([Guid.CreateVersion7()], 6, [], [], 6);
        Assert.Equal(((double?)null, false), (plain.Separation, plain.PassesModels));
        var back = Assert.IsType<MoveRegion>(WireJson.Deserialize<LegalSet>(WireJson.Serialize<LegalSet>(plain with { Separation = 1, PassesModels = true })));
        Assert.Equal(((double?)1, true), (back.Separation, back.PassesModels));
    }
}
