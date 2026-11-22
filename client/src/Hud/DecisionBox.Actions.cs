using Godot;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Hud;

/// <summary>The box's half of the action buttons over the selected unit (spec 2026-11-21-actions-over-unit-design.md): it makes their
/// UnitActionsNode in the table's own canvas layer, and gives it the numbering, the answer and the hover the tiles use.</summary>
public partial class DecisionBox
{
    CanvasLayer? _actionsLayer;

    /// <summary>After each render of the box.</summary>
    public event Action? Rendered;

    /// <summary>The option id of the tile under the pointer; null when the pointer leaves it.</summary>
    public event Action<string?>? TileHovered;

    /// <summary>The keys the box answers to now: the full list, or a selected unit's options renumbered from 1.</summary>
    public IReadOnlyList<(int Key, string Label, string OptionId)> Numbered => _numbered;

    /// <summary>Whether this Ask is the one on the card and still unanswered: no answer sent, its tiles live.</summary>
    public bool Answering(Guid askId) => _renderedAsk == askId && _onTile is not null;

    /// <summary>Answers as the option's tile does; false once the answer is sent or the Ask is gone.</summary>
    public bool Choose(string optionId)
    {
        if (_onTile is not { } pick) return false;
        pick(optionId);
        return true;
    }

    /// <summary>Lights an option as the pointer on its tile does (its pieces on the table, the tile itself); null puts everything back.</summary>
    public void HoverOption(string? optionId)
    {
        DecisionTiles.ChoiceTile? hit = null;
        foreach (var tile in _choices.FindChildren("*", "Button", true, false).OfType<DecisionTiles.ChoiceTile>())
        {
            var lit = optionId is not null && tile.Option?.OptionId == optionId;
            if (lit) hit = tile;
            var variation = lit ? Variation.TileLit : Variation.Tile;
            if (tile.ThemeTypeVariation != variation) tile.ThemeTypeVariation = variation;
        }
        Light(hit?.Option?.Pieces);
    }

    void BindActions()
    {
        _view.AddChild(_actionsLayer = new CanvasLayer());
        var node = new UnitActionsNode();
        _actionsLayer.AddChild(node);
        node.Bind(_s, _view, _input, this);
    }

    void UnbindActions()
    {
        if (_actionsLayer is not null && IsInstanceValid(_actionsLayer)) _actionsLayer.QueueFree();
    }
}
