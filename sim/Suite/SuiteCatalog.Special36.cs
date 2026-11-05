using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>
/// Special games under Grimdark Future 3.6.0 alone (Edition = Gf360, named &lt;name&gt;@3.6): one per special rule whose 3.6.0 text changed and one
/// per rule 3.6.0 adds, each built by construction (sim/AGENTS.md → Writing a suite game that holds), every check quoting the 3.6.0 sentence and page.
/// </summary>
static partial class SpecialGames
{
    public static IEnumerable<TestGame> All36() =>
    [
        Regrowth36().In36(),
        Gunline36().In36(),
        Artillery36().In36(),
        Charge36().In36(),
        Command36().In36(),
        CastMidMove36().In36(),
        Aircraft36().In36(),
        IndirectOnHold36().In36(),
        HeroAura36().In36(),
        AssaultAndCumbersome36().In36(),
        ShredAndAmplifier36().In36(),
    ];

    /// <summary>Every shoot_or_not answered shoot and every strike_back strike; activations by the pick given, else the policy.</summary>
    static AnswerScript Plays(Func<Ask, Projection, AskAnswer?> activate, params (string Question, Func<Ask, Projection, AskAnswer?> Pick)[] more)
    {
        var script = AnswerScript.Build()
            .Match(AskQuestions.Activate, activate)
            .Match("shoot_or_not", (ask, _) => ask.Legal is Options o && o.Choices.Any(c => c.Id == "shoot") ? new Chosen("shoot") : null)
            .Match("strike_back", (ask, _) => new Chosen("strike"));
        foreach (var (question, pick) in more) script.Match(question, pick);
        return script.Build();
    }

    /// <summary>The charge sequence the melee was fought in: the charger's last charge frame started before it.</summary>
    static SequenceStarted ChargeOf(SuiteGameRun run, Melee m) =>
        Payloads(run).TakeWhile(p => p != m.Seq).OfType<SequenceStarted>().Last(s => s.Sequence == "charge" && s.Context.ActorUnitId is { } a && UnitName(run, a) == m.Charger);

    /// <summary>Whether the melee's defender was Fatigued when the melee started.</summary>
    static bool DefenderFatigued(SuiteGameRun run, Melee m)
    {
        var q = LogQueries.Of(run);
        return q.HasCondition(q.UnitNamed(m.Defender), "fatigued", q.FramesOf("melee").Single(f => f.Id == m.Seq.FrameId).Seq);
    }

    /// <summary>The same weapons for models 1..count of a unit (ScenarioModel.Weapons replaces the army's).</summary>
    static IEnumerable<ScenarioModel> Armed(int unit, int count, params ScenarioWeapon[] weapons) =>
        Enumerable.Range(1, count).Select(m => new ScenarioModel(unit, m, Weapons: weapons));
}
