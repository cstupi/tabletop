using Tabletop.Protocol;

namespace Tabletop.GameServer.Dispatch;

/// <summary>Set membership over the closed LegalSet shapes. Rules-free: the set was computed by the engine; this only reads it.</summary>
public static class LegalSetMembership
{
    public static bool Contains(Ask ask, AskAnswer answer) => answer switch
    {
        Passed => ask.AllowPass,
        Chosen c => ask.Legal is Options o && o.Choices.Any(x => x.Id == c.OptionId),
        SelectedPieces s => ask.Legal is PieceSelection p && s.PieceIds.Count >= p.Min && s.PieceIds.Count <= p.Max
                            && s.PieceIds.Distinct().Count() == s.PieceIds.Count && s.PieceIds.All(p.Eligible.Contains),
        Allocated a => ask.Legal is Allocation al && a.Counts.Keys.All(al.Targets.Contains) && a.Counts.Values.All(n => n >= 0) && a.Counts.Values.Sum() == al.Amount,
        Assigned g => ask.Legal is Assignment asg && asg.Items.All(i => g.Targets.ContainsKey(i.Id)) && g.Targets.Keys.All(k => asg.Items.Any(i => i.Id == k))
                      && g.Targets.Values.All(asg.Targets.Contains) && g.Targets.Values.Distinct().Count() <= asg.MaxDistinctTargets,
        Destinations d => ask.Legal is MoveRegion m && d.Moves.All(x => m.ModelIds.Contains(x.ModelId)),
        // Keys and per-key caps, and no total: a spend is any number up to a maximum (p.13). Whether the answering
        // team owns a key is the spend step's, as MoveRegion's distance is the move step's — no state answers it here.
        TokensSpent t => ask.Legal is TokenSpend ts && t.Counts.All(kv => ts.MaxByModel.TryGetValue(kv.Key, out var max) && kv.Value >= 0 && kv.Value <= max),
        Confirmed => ask.Legal is Confirmation,
        _ => false,
    };
}
