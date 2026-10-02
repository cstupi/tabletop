using Tabletop.Protocol;

namespace Tabletop.Sim.Run;

/// <summary>
/// What "the same Ask again" means. The id is server-minted and always different, so it is excluded; the
/// legal set is included, because an Ask that reopens with a *smaller* set is progress and not a hang.
/// </summary>
public sealed record AskFingerprint(string Question, string Teams, string Legal, bool AllowPass, string? WindowKind)
{
    public static AskFingerprint Of(Ask ask) => new(
        ask.Question,
        string.Join(",", ask.Teams.Select(t => t.ToString("N")).Order()),
        Describe(ask.Legal),
        ask.AllowPass,
        ask.WindowKind);

    private static string Describe(LegalSet legal) => legal switch
    {
        Options o => $"options:{string.Join(",", o.Choices.Select(c => c.Id).Order())}",
        PieceSelection p => $"pieces:{p.Min}-{p.Max}:{string.Join(",", p.Eligible.Select(i => i.ToString("N")).Order())}",
        Allocation a => $"allocation:{a.Amount}:{string.Join(",", a.Targets.Select(i => i.ToString("N")).Order())}",
        Assignment a => $"assignment:{a.MaxDistinctTargets}:{string.Join(",", a.Items.Select(i => i.Id).Order())}:{string.Join(",", a.Targets.Select(i => i.ToString("N")).Order())}",
        MoveRegion m => $"move:{m.Allowance}:{string.Join(",", m.ModelIds.Select(i => i.ToString("N")).Order())}",
        TokenSpend t => $"token_spend:{string.Join(",", t.MaxByModel.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key:N}={kv.Value}"))}",
        Confirmation => "confirmation",
        _ => legal.GetType().Name,
    };
}
