using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

/// <summary>The models of a drag or a placement in breach and why: the client's own measure, or the server's reason for a commit it refused.</summary>
public class ViolationsTests
{
    static readonly Guid A = Guid.CreateVersion7(), B = Guid.CreateVersion7(), C = Guid.CreateVersion7();

    [Fact]
    public void A_measure_replaces_only_the_models_it_was_taken_over()
    {
        var v = new Violations();
        v.Measure([A, B], new Dictionary<Guid, string> { [A] = "overlaps a base", [B] = "path crosses a model" });
        v.Measure([C], new Dictionary<Guid, string> { [C] = "within 1\" of Orcs" });
        v.Measure([B], new Dictionary<Guid, string>());
        Assert.Equal(new Dictionary<Guid, string> { [A] = "overlaps a base", [C] = "within 1\" of Orcs" }, v.Marked);
    }

    [Fact]
    public void Changed_is_raised_only_when_the_marks_change()
    {
        var v = new Violations(); var raised = 0; v.Changed += () => raised++;
        v.Measure([A], new Dictionary<Guid, string> { [A] = "overlaps a base" });
        v.Measure([A], new Dictionary<Guid, string> { [A] = "overlaps a base" });
        Assert.Equal(1, raised);
        v.Measure([A], new Dictionary<Guid, string>());
        Assert.Equal(2, raised);
        v.Clear(); Assert.Equal(2, raised);
    }

    [Fact]
    public void A_refusal_marks_its_models_with_the_servers_reason_until_one_of_them_moves()
    {
        var v = new Violations();
        v.Refused([A, B], "Squad (M1) would move through another model");
        Assert.Equal("Squad (M1) would move through another model", v.Marked[A]);
        Assert.Equal("Squad (M1) would move through another model", v.Marked[B]);
        v.Measure([A, B], new Dictionary<Guid, string>());                       // the client's measure finds nothing: the server's word stands
        Assert.Equal(2, v.Marked.Count);
        v.Moved([C]); Assert.Equal(2, v.Marked.Count);
        v.Moved([B]); Assert.Empty(v.Marked);
    }

    [Fact]
    public void The_clients_own_reason_for_a_model_comes_before_the_servers()
    {
        var v = new Violations();
        v.Refused([A], "refused");
        v.Measure([A], new Dictionary<Guid, string> { [A] = "overlaps a base" });
        Assert.Equal("overlaps a base", v.Marked[A]);
    }

    [Fact]
    public void Only_strict_waits_on_a_mark()
    {
        var v = new Violations();
        Assert.False(v.Blocks(Enforcement.Strict));
        v.Measure([A], new Dictionary<Guid, string> { [A] = "overlaps a base" });
        Assert.True(v.Blocks(Enforcement.Strict));
        Assert.False(v.Blocks(Enforcement.Warn));
        Assert.False(v.Blocks(Enforcement.Silent));
    }

    [Fact]
    public void With_a_subject_a_mark_reads_as_a_sentence_naming_the_first_model_with_it_and_the_labels_keep_the_words()
    {
        var v = new Violations { Subject = id => id == A ? "APC 1" : id == B ? "APC 2" : null };
        v.Measure([A, B, C], new Dictionary<Guid, string> { [A] = "within 1\" of Orcs", [B] = "within 1\" of Orcs", [C] = "overlaps a base" });
        Assert.Equal("APC 1 would end within 1\" of Orcs", v.Marked[A]);
        Assert.Equal("APC 1 would end within 1\" of Orcs", v.Marked[B]);
        Assert.Equal("overlaps a base", v.Marked[C]);
        Assert.Equal([(B, "within 1\" of Orcs"), (C, "overlaps a base")], v.Labels([B, A, C]));
        v.Refused([C], "C would be placed outside its deployment zone");
        v.Measure([C], new Dictionary<Guid, string>());
        Assert.Equal("C would be placed outside its deployment zone", v.Marked[C]);
    }

    [Fact]
    public void Labels_name_each_reason_once_over_its_first_model()
    {
        var v = new Violations();
        v.Measure([A, B, C], new Dictionary<Guid, string> { [A] = "overlaps a base", [B] = "overlaps a base", [C] = "within 1\" of Orcs" });
        Assert.Equal([(A, "overlaps a base"), (C, "within 1\" of Orcs")], v.Labels([A, B, C]));
        Assert.Equal([(B, "overlaps a base"), (C, "within 1\" of Orcs")], v.Labels([B, C, A]));
    }

    [Fact]
    public void Clear_drops_the_measure_and_the_refusal()
    {
        var v = new Violations();
        v.Measure([A], new Dictionary<Guid, string> { [A] = "overlaps a base" }); v.Refused([B], "refused");
        v.Clear();
        Assert.Empty(v.Marked);
    }

    [Fact]
    public void Gate_names_each_contact_reason_and_strict_waits()
    {
        var marked = new Dictionary<Guid, string> { [A] = "within 1\" of Orcs", [B] = "within 1\" of Orcs", [C] = "path crosses a model" };
        Assert.Equal(new MoveGate(false, "within 1\" of Orcs (2 models) · path crosses a model."), MoveRuler.Gate([], Enforcement.Strict, marked));
        Assert.Equal(new MoveGate(true, "within 1\" of Orcs (2 models) · path crosses a model."), MoveRuler.Gate([], Enforcement.Warn, marked));
        var over = MoveRuler.Read(new Tabletop.Geometry.Point2(0, 0), new Tabletop.Geometry.Point2(8, 0), 6);
        Assert.Equal(new MoveGate(false, "1 model past its allowance · path crosses a model."), MoveRuler.Gate([over], Enforcement.Strict, new Dictionary<Guid, string> { [C] = "path crosses a model" }));
    }
}
