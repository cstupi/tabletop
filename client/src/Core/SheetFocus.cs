namespace Tabletop.Client.Core;

/// <summary>
/// Which unit the stat sheet shows (in-game UX spec → Stat sheet, Which unit): the activating unit when an activation begins; a
/// clicked unit until the next activation begins; with no activation running and nothing selected, none, and the sheet hides.
/// </summary>
public sealed class SheetFocus
{
    Guid? _activating, _clicked;

    public Guid? UnitId => _clicked ?? _activating;

    public void OnActivationStarted(Guid unitId) { _activating = unitId; _clicked = null; }
    public void OnActivationEnded() => _activating = null;
    public void OnClicked(Guid unitId) => _clicked = unitId;
    public void OnCleared() => _clicked = null;
}
