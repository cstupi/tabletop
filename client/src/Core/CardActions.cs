using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>One action at the decision card's foot: its id, word, icon, whether it is the primary, its number key (0: none), and whether it
/// can be pressed now and, when not, why.</summary>
public sealed record CardAction(string Id, string Word, string? Icon, bool Primary, int Key = 0, bool Enabled = true, string? Reason = null)
{
    /// <summary>A button's tooltip: the word and its key cap ("Fire  5"), and the reason it is dim.</summary>
    public string Tip => (Key > 0 ? $"{Word}  {DecisionCard.KeyCap(Key)}" : Word) + (!Enabled && Reason is { Length: > 0 } r ? $" · {r}" : "");
}

/// <summary>
/// The decision card's foot actions and their keys (the owner, 2026-11-21: "if 1,2,3 decides then users should hit 1 not enter"). Every
/// action on the card is numbered in one sequence: the option tiles first, then the foot's actions, the primary first; past 9 none has a key.
/// The box, the bar over the unit and the key overlay read these. Enter and Esc stay silent aliases for the primary and Clear or Cancel.
/// </summary>
public static class CardActions
{
    public const string Confirm = "confirm", Spend = "spend", Commit = "commit", Stop = "stop", Fire = "fire", Clear = "clear", Auto = "auto",
        Done = "done", Place = "place", Pass = "pass";
    const int Keyed = 9;

    /// <summary>The foot actions of an Ask's card, the primary first and Pass last, without keys or state.</summary>
    public static IReadOnlyList<CardAction> Of(Ask ask, AskMode mode, Projection p)
    {
        var list = new List<CardAction>();
        switch (mode)
        {
            case AskMode.Confirm: list.Add(new(Confirm, AskViewModel.ConfirmLabel(ask), "ready", true)); break;
            case AskMode.TokenSpend: list.Add(new(Spend, "Spend", "cast", true)); break;
            case AskMode.Move when ask.Legal is MoveRegion m:
                list.Add(new(Commit, "Commit", "ready", true));
                if (!MoveRuler.IsDisembark(m) && DecisionView.StopLabel(p, m) is { } word)
                    list.Add(new(Stop, word, word.StartsWith("Cast", StringComparison.Ordinal) ? "cast" : "hold", false));
                break;
            case AskMode.Assign: list.Add(new(Fire, "Fire", "shoot", true)); list.Add(new(Clear, "Clear", "remove", false)); break;
            case AskMode.Allocate:
                list.Add(new(Confirm, "Confirm", "ready", true)); list.Add(new(Auto, "Auto", "refresh", false)); list.Add(new(Clear, "Clear", "remove", false));
                break;
            case AskMode.PickPieces when ask.Legal is PieceSelection { Max: > 1 }: list.Add(new(Done, "Done", "ready", true)); break;
        }
        if (ask.AllowPass) list.Add(new(Pass, ask.Question == AskQuestions.ActOrEndPhase ? "End phase" : "Pass", "skip", false));
        return list;
    }

    /// <summary>A placement's one action (a deploy pick placed on the table).</summary>
    public static IReadOnlyList<CardAction> Placing { get; } = [new(Place, "Place", "ready", true)];

    /// <summary>The actions keyed after the card's numbered tiles: tile keys run 1 to <paramref name="tiles"/>, the actions on from there.</summary>
    public static IReadOnlyList<CardAction> Numbered(IReadOnlyList<CardAction> actions, int tiles) =>
        actions.Select((a, i) => a with { Key = tiles + i + 1 <= Keyed ? tiles + i + 1 : 0 }).ToList();
}
