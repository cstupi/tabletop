using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Protocol;
using Projection = Tabletop.Protocol.Projection;

namespace Tabletop.Client.Hud;

public partial class DecisionBox
{
    readonly List<Label3D> _marks = new();

    /// <summary>The weapon groups and their targets while declare_targets is open; null otherwise. TargetLines draws it.</summary>
    public TargetAssignment? Targets { get; private set; }
    public event Action? TargetsChanged;

    partial void RenderPicks(AskView mine, Projection p, Names names)
    {
        switch (mine.Mode)
        {
            case AskMode.PickPieces: RenderPick(mine.Ask, (PieceSelection)mine.Ask.Legal, p, names); break;
            case AskMode.Allocate: RenderCasualties(mine.Ask, (Allocation)mine.Ask.Legal, p); break;
            case AskMode.Assign: RenderTargets(mine.Ask, (Assignment)mine.Ask.Legal, names); break;
        }
    }

    partial void UnhookPicks()
    {
        if (Targets is not null) { Targets = null; TargetsChanged?.Invoke(); }
        ClearMarks();
    }

    /// <summary>Pick Min–Max pieces: a click on an eligible piece toggles it, a shelved one is a numbered entry (a deploy). One pick of one answers at once.</summary>
    void RenderPick(Ask ask, PieceSelection sel, Projection p, Names names)
    {
        var eligible = DeployView.Pickable(p, ask);                                  // a deploy pick: units with a model on the shelf (passengers spec)
        var pick = new PiecePick(sel, eligible);
        var count = new Label(); Add(count);
        var done = Btn("Done (Enter)", Done);
        void Done() { if (pick.Answer() is { } answer) Send(ask, answer); }
        void Refresh() { count.Text = pick.Count(names); done.Disabled = !pick.Ready; }
        void Toggle(Guid id)
        {
            switch (pick.Toggle(id))
            {
                case PickChange.Added: _view.PieceAt(id)?.SetHighlight(Palette.Ghost); break;
                case PickChange.Removed: _view.PieceAt(id)?.SetHighlight(Palette.Selected); break;
                default: return;
            }
            if (pick.AnswersAtOnce) { Done(); return; }
            Refresh();
        }
        foreach (var shelved in DeployView.OffTable(p, eligible))
        {
            var key = _numbered.FirstOrDefault(n => n.OptionId == shelved.ToString()).Key;
            Add(Btn(key > 0 ? $"{key}. {names.Piece(shelved)}" : names.Piece(shelved), () => Toggle(shelved)));
        }
        _onNumber = option => { if (Guid.TryParse(option, out var id) && eligible.Contains(id)) Toggle(id); };
        _input.Eligible = eligible.ToHashSet();
        _onTarget = clicked => { if (AskAnswers.Resolve(p, clicked, eligible) is { } id) { _input.CancelDrag(); Toggle(id); } };
        _onEnter = Done;
        Add(done); Refresh();
    }

    /// <summary>declare_targets: weapon groups as chips, all picked to start (p.8: one group per weapon type). A click on an enemy aims
    /// every picked group that can shoot it (p.8); a chip picks its group alone. Enter fires once a group has a target (an unaimed group holds fire),
    /// Clear targets (Esc) removes every target.</summary>
    void RenderTargets(Ask ask, Assignment legal, Names names)
    {
        var t = Targets = new TargetAssignment(legal);
        var chips = new HFlowContainer(); Add(chips);
        var fire = Btn("Fire (Enter)", Fire);
        var clear = new Button { Text = "Clear targets (Esc)", FocusMode = FocusModeEnum.None };
        void Fire() { if (t.Answer() is { } answer) Send(ask, answer); }
        bool ClearTargets() { if (!t.CanClear) return false; t.Clear(); Show(); return true; }
        void Show()
        {
            if (Targets != t || !IsInstanceValid(chips)) return;                       // the Ask ended before a deferred refresh ran
            foreach (var c in chips.GetChildren()) { chips.RemoveChild(c); c.QueueFree(); }
            foreach (var g in t.Groups)
            {
                var id = g.Id;
                var chip = new Button { Text = TargetAssignment.Chip(g, t.Assigned.TryGetValue(id, out var unit) ? names.Unit(unit) : null),
                    ToggleMode = true, ButtonPressed = t.Picked.Contains(id), FocusMode = FocusModeEnum.None };
                chip.Pressed += () => { t.PickOnly(id); Callable.From(Show).CallDeferred(); };   // not inside the chip's own signal: Show frees it
                chips.AddChild(chip);
            }
            _input.Eligible = t.Candidates.ToHashSet(); Glow(t.Candidates);          // only units a picked group can shoot; two targets taken (p.8): nothing else glows
            fire.Disabled = t.Answer() is null;
            clear.Disabled = !t.CanClear;
            TargetsChanged?.Invoke();
        }
        _onTarget = clicked => { if (_s.Projection is { } p && AskAnswers.Resolve(p, clicked, legal.Targets) is { } unit && t.Aim(unit)) Show(); };
        _onEnter = Fire;
        clear.Pressed += () => ClearTargets();
        _onCancel = ClearTargets;
        Add(clear); Add(fire); Show();
    }

    /// <summary>remove_casualties: a click marks a model, each further click on a many-wound model adds a wound, a right-click takes one
    /// away. Auto fills the rest in the Ask's default order. Enter confirms once the count is exact.</summary>
    void RenderCasualties(Ask ask, Allocation legal, Projection p)
    {
        var left = legal.Targets.ToDictionary(id => id, id => Math.Max(0, _s.MaxWounds.GetValueOrDefault(id, 1) - (p.Models.GetValueOrDefault(id)?.Wounds ?? 0)));
        var pick = new CasualtyPick(legal, left);
        _title.Text = pick.Title; _hint.Text = pick.Hint;
        var count = new Label(); Add(count);
        var confirm = Btn("Confirm (Enter)", Confirm);
        void Confirm() { if (pick.Answer() is { } answer) Send(ask, answer); }
        void Show()
        {
            count.Text = pick.Status;
            confirm.Disabled = !pick.Complete;
            ClearMarks();
            foreach (var (id, n) in pick.Counts)
                if (n > 0 && _view.PieceAt(id) is { } node && IsInstanceValid(node) && p.Models.GetValueOrDefault(id) is { } model)
                {
                    var mark = new Label3D { Text = left[id] > 1 ? $"✕ {n}/{left[id]}" : "✕", Modulate = Ui.Failure, FontSize = 64, PixelSize = 0.01f,
                        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, (float)model.Slot.Height + 0.5f, 0) };
                    node.AddChild(mark); _marks.Add(mark);
                }
        }
        _input.Eligible = legal.Targets.ToHashSet();
        _onModel = (id, right) => { if (!left.ContainsKey(id)) return; if (right) pick.Remove(id); else pick.Click(id); Show(); };
        _onEnter = Confirm;
        Add(Btn("Auto", () => { pick.Auto(); Show(); })); Add(Btn("Clear", () => { pick.Clear(); Show(); })); Add(confirm); Show();
    }

    void ClearMarks() { foreach (var m in _marks) if (IsInstanceValid(m)) m.QueueFree(); _marks.Clear(); }
}
