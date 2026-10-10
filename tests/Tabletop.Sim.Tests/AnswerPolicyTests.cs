using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Tests;

public class AnswerPolicyTests
{
    static readonly Guid A = Guid.CreateVersion7(), B = Guid.CreateVersion7(), C = Guid.CreateVersion7();
    static Ask With(LegalSet legal, bool allowPass = false, bool sealedAsk = false) =>
        new(Guid.CreateVersion7(), "q", "p", [A], legal, allowPass, sealedAsk, null, null);
    static AnswerPolicy Policy(ulong seed = 1, PolicyOptions? o = null) => new(new Chooser(seed), o);
    internal static PolicyView EmptyViewForOtherTests() => new(Projection.Initial(new Board(Bare(), []), Info()), new Board(Bare(), []));
    static TableState Bare() => new(Guid.CreateVersion7(), null, new TableSize(72, 48), VisualRef.OfBuiltin("grass"), [], [], []);
    static GameInfo Info() => new(Guid.CreateVersion7(), "g", GameStatus.Playing, A, "grimdark-future", "sha256:x",
        SchedulerShape.AlternatingActivation, Enforcement.Strict, false, RngSeed: 1, [new Team(A, 1)], []);

    [Fact]
    public void Options_picks_one_of_the_choices_by_id()
    {
        var ask = With(new Options([new AskOption("x", "X", []), new AskOption("y", "Y", [])]));
        var chosen = Assert.IsType<Chosen>(Policy().Answer(ask, EmptyViewForOtherTests()));
        Assert.Contains(chosen.OptionId, (string[])["x", "y"]);
    }

    [Fact]
    public void An_empty_options_set_passes_when_it_may_and_throws_when_it_may_not()
    {
        Assert.IsType<Passed>(Policy().Answer(With(new Options([]), allowPass: true), EmptyViewForOtherTests()));
        Assert.Throws<NoLegalAnswerException>(() => Policy().Answer(With(new Options([])), EmptyViewForOtherTests()));
    }

    [Fact]
    public void Pieces_respects_min_and_max_and_picks_only_eligible_ones()
    {
        var answer = Assert.IsType<SelectedPieces>(Policy().Answer(With(new PieceSelection([A, B, C], 2, 2)), EmptyViewForOtherTests()));
        Assert.Equal(2, answer.PieceIds.Count);
        Assert.All(answer.PieceIds, id => Assert.Contains(id, (Guid[])[A, B, C]));
        Assert.Equal(2, answer.PieceIds.Distinct().Count());
    }

    [Fact]
    public void Allocation_spends_the_whole_amount_over_the_targets()
    {
        var answer = Assert.IsType<Allocated>(Policy().Answer(With(new Allocation([A, B], 5)), EmptyViewForOtherTests()));
        Assert.Equal(5, answer.Counts.Values.Sum());
        Assert.All(answer.Counts.Keys, k => Assert.Contains(k, (Guid[])[A, B]));
    }

    [Fact]
    public void Assignment_covers_every_item_within_the_distinct_target_cap()
    {
        var ask = With(new Assignment([new AskOption("g1", "", []), new AskOption("g2", "", []), new AskOption("g3", "", [])], [A, B, C], 2));
        var answer = Assert.IsType<Assigned>(Policy().Answer(ask, EmptyViewForOtherTests()));
        Assert.Equal(["g1", "g2", "g3"], answer.Targets.Keys.Order());
        Assert.True(answer.Targets.Values.Distinct().Count() <= 2);
    }

    [Fact]
    public void Assignment_aims_every_item_within_its_own_targets()
    {
        // p.8: each group only at a target it can shoot. g3 reaches only C and g2 only B, so the two targets must be B and C.
        var byItem = new Dictionary<string, IReadOnlyList<Guid>> { ["g1"] = [A, B, C], ["g2"] = [B], ["g3"] = [C] };
        var ask = With(new Assignment([new AskOption("g1", "", []), new AskOption("g2", "", []), new AskOption("g3", "", [])], [A, B, C], 2, byItem));
        for (var seed = 0UL; seed < 20; seed++)
        {
            var answer = Assert.IsType<Assigned>(new AnswerPolicy(new Chooser(seed)).Answer(ask, EmptyViewForOtherTests()));
            Assert.Equal(["g1", "g2", "g3"], answer.Targets.Keys.Order());
            Assert.All(answer.Targets, kv => Assert.Contains(kv.Value, byItem[kv.Key]));
            Assert.True(answer.Targets.Values.Distinct().Count() <= 2);
        }
    }

    [Fact]
    public void A_token_spend_stays_within_every_model_own_cap_and_confirmation_confirms()
    {
        // p.13: any number of tokens up to what each model holds, so the bound is per model and not a total.
        var spend = new TokenSpend(new Dictionary<Guid, int> { [A] = 1, [B] = 4 });
        var answer = Assert.IsType<TokensSpent>(Policy().Answer(With(spend), EmptyViewForOtherTests()));
        Assert.Equal(new HashSet<Guid> { A, B }, answer.Counts.Keys.ToHashSet());   // one entry per model the map named, whatever order Guid comparison puts them in
        Assert.InRange(answer.Counts[A], 0, 1);
        Assert.InRange(answer.Counts[B], 0, 4);
        Assert.IsType<Confirmed>(Policy().Answer(With(new Confirmation()), EmptyViewForOtherTests()));
    }

    [Fact]
    public void Pass_is_taken_only_when_the_ask_allows_it_and_the_probability_says_so()
    {
        var ask = With(new Options([new AskOption("x", "X", [])]), allowPass: true);
        Assert.IsType<Passed>(Policy(1, new PolicyOptions(PassProbability: 1)).Answer(ask, EmptyViewForOtherTests()));
        Assert.IsType<Chosen>(Policy(1, new PolicyOptions(PassProbability: 0)).Answer(ask, EmptyViewForOtherTests()));
        Assert.IsType<Chosen>(Policy(1, new PolicyOptions(PassProbability: 1))
            .Answer(With(new Options([new AskOption("x", "X", [])])), EmptyViewForOtherTests()));
    }

    [Fact]
    public void The_same_seed_answers_a_run_of_asks_identically()
    {
        var asks = Enumerable.Range(0, 30).Select(i => With(new PieceSelection([A, B, C], 1, 3))).ToList();
        var one = Policy(77); var two = Policy(77);
        foreach (var ask in asks)
        {
            // SelectedPieces holds a List, so record equality is by reference: compare the ids.
            var a = Assert.IsType<SelectedPieces>(one.Answer(ask, EmptyViewForOtherTests()));
            var b = Assert.IsType<SelectedPieces>(two.Answer(ask, EmptyViewForOtherTests()));
            Assert.Equal(a.PieceIds, b.PieceIds);
        }
    }

    [Fact]
    public void Every_legal_set_shape_is_answered()
    {
        Assert.Equal(["allocation", "assignment", "confirmation", "move_region", "options", "pieces", "token_spend"], AnswerPolicy.ShapeNames.Order());
        var policy = Policy();
        LegalSet[] all =
        [
            new Options([new AskOption("x", "X", [])]),
            new PieceSelection([A], 1, 1),
            new Allocation([A], 1),
            new Assignment([new AskOption("g", "", [])], [A], 1),
            new TokenSpend(new Dictionary<Guid, int> { [A] = 1 }),
            new Confirmation(),
        ];
        foreach (var legal in all) Assert.NotNull(policy.Answer(With(legal), EmptyViewForOtherTests()));
    }
}
