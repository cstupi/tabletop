using System.Text.Json;

namespace Tabletop.Geometry.Tests;

/// <summary>
/// The reach area, contacts and landings over the reference game's table, against Fixtures/reference-move.expected.json: what the
/// implementation before the broad-phase culls answered, bit for bit (TABLETOP_WRITE_FIXTURES=1 rewrites it).
/// </summary>
public class ReferenceAgreementTests
{
    static readonly ReferenceScene Scene = ReferenceScene.Load();

    /// <summary>Every third unit (ten of thirty) moves; everyone else stands.</summary>
    static IEnumerable<IReadOnlyList<ReferenceScene.Model>> Movers() => Scene.Units().Where((_, i) => i % 3 == 0);

    static IEnumerable<(string Name, string Value)> ReachCases()
    {
        var difficult = Scene.DifficultVolumes;
        foreach (var unit in Movers())
        {
            var others = Scene.Others(unit);
            var units = Scene.OthersUnits(unit);
            foreach (var m in unit)
            {
                foreach (var (allowance, cap) in new[] { (6.0, Scene.DifficultCap), (12.0, (double?)6) })
                {
                    var reach = MoveReach.Build(m.Placed, allowance, [], difficult, cap, Scene.Size, others, Scene.Volumes, units: units);
                    var t = reach.Triangles();
                    yield return ($"reach {m.Id} {allowance}", $"{t.Count} {ReferenceScene.Digest(t.SelectMany(p => new[] { p.X, p.Y }))}");
                }
            }
            var lead = unit[0];
            var least = MoveReach.Build(lead.Placed, 12, [], difficult, 6, Scene.Size, others, Scene.Volumes, units: units)
                .Triangles(p => 3 + 0.1 * (p.X - lead.Placement.Position.X));
            yield return ($"reach least {lead.Id}", $"{least.Count} {ReferenceScene.Digest(least.SelectMany(p => new[] { p.X, p.Y }))}");
        }
    }

    static readonly Point2[] Offsets = [new(3, 0), new(0, 3), new(-2.5, -2.5), new(5.5, 1), new(-1, 5.8), new(0.4, 0.2)];

    static IEnumerable<(string Name, string Value)> ContactCases()
    {
        var bodies = Scene.Models.Select(m => new ContactBody(m.Id, m.UnitId, m.Body, m.Placement)).ToList();
        var impassableRuin = Scene.WithImpassableRuin();
        var ruinId = Scene.Volumes.Single(v => v.Volume.Kind == VolumeKind.Solid).Id;
        foreach (var unit in Movers())
        {
            for (var k = 0; k < Offsets.Length; k++)
            {
                var proposed = unit.ToDictionary(m => m.Id, m => m.Placement with { Position = m.Placement.Position with
                {
                    X = m.Placement.Position.X + Offsets[k].X, Y = m.Placement.Position.Y + Offsets[k].Y,
                } });
                // The unit's nearest enemy: lands one model beside, on and through it.
                var lead = unit[0];
                var enemy = Scene.Models.Where(o => o.UnitId != lead.UnitId).MinBy(o => o.Placement.Position.Horizontal.DistanceTo(lead.Placement.Position.Horizontal))!;
                proposed[lead.Id] = lead.Placement with { Position = enemy.Placement.Position with { X = enemy.Placement.Position.X + 0.4 * k } };
                // A second model on the ruin (in impassable terrain where it is declared so).
                proposed[unit[1].Id] = unit[1].Placement with { Position = new Point3(33 + 0.9 * k, 24, 0) };
                foreach (var (name, volumes, terms) in new[]
                {
                    ("move", Scene.Volumes, new ContactTerms(Scene.Separation, false, true, new ContactMove(6, [], Scene.Difficult, Scene.DifficultCap), Scene.Size)),
                    ("move-capped", Scene.Volumes, new ContactTerms(1, false, true, new ContactMove(12, [], Scene.Difficult, 6), Scene.Size, 6)),
                    ("move-ruin", impassableRuin, new ContactTerms(1, false, true, new ContactMove(12, [ruinId], Scene.Difficult, 6), Scene.Size)),
                    ("passes", Scene.Volumes, new ContactTerms(1, true, false, new ContactMove(6, [], Scene.Difficult, Scene.DifficultCap), Scene.Size)),
                    ("placement", Scene.Volumes, new ContactTerms(null, false, false)),
                })
                {
                    var found = Contacts.Of(bodies, proposed, volumes, terms);
                    yield return ($"contacts {lead.Id} {k} {name}", string.Join(";", found.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value.Kind}:{kv.Value.UnitId}")));
                }
            }
        }
    }

    static IEnumerable<(string Name, string Value)> LandCases()
    {
        var impassableRuin = Scene.WithImpassableRuin();
        var walls = impassableRuin.Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).ToList();
        foreach (var unit in Movers())
        {
            var stationary = Scene.Others(unit);
            for (var k = 0; k < Offsets.Length; k++)
            {
                var lead = unit[0];
                var enemy = Scene.Models.Where(o => o.UnitId != lead.UnitId).MinBy(o => o.Placement.Position.Horizontal.DistanceTo(lead.Placement.Position.Horizontal))!;
                var held = unit.Select((m, i) => (m.Placed, To: i == 0 ? enemy.Placement.Position.Horizontal + new Point2(0.3 * k, 0) : m.Placement.Position.Horizontal + Offsets[k],
                    Within: k % 2 == 0 ? null : (Func<Point2, bool>)(p => p.DistanceTo(m.Placement.Position.Horizontal) <= 6))).ToList();
                foreach (var (name, w, volumes) in new[] { ("open", new List<WorldVolume>(), Scene.Volumes), ("ruin", walls, impassableRuin) })
                {
                    var ends = Collision.Land(held, stationary, w, volumes);
                    yield return ($"land {lead.Id} {k} {name}", string.Join(";", ends.Select(e => $"{e.X:R},{e.Y:R},{e.Z:R}")));
                }
            }
        }
    }

    static Dictionary<string, string> Expected() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reference-move.expected.json")))!;

    static void Check(string prefix, IEnumerable<(string Name, string Value)> cases)
    {
        var actual = cases.ToList();
        if (Environment.GetEnvironmentVariable("TABLETOP_WRITE_FIXTURES") == "1")
        {
            var path = ReferenceScene.SourceFixture("reference-move.expected.json");
            var all = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))! : [];
            foreach (var key in all.Keys.Where(k => k.StartsWith(prefix + " ")).ToList()) all.Remove(key);
            foreach (var (name, value) in actual) all[name] = value;
            File.WriteAllText(path, JsonSerializer.Serialize(new SortedDictionary<string, string>(all, StringComparer.Ordinal), ReferenceScene.Json) + "\n");
            return;
        }
        var expected = Expected().Where(kv => kv.Key.StartsWith(prefix + " ")).ToDictionary();
        Assert.Equal(expected.Count, actual.Count);
        foreach (var (name, value) in actual) Assert.True(expected.TryGetValue(name, out var e) && e == value, $"{name}: expected {e}, got {value}");
    }

    [Fact]
    public void The_reach_area_is_what_it_was() => Check("reach", ReachCases());

    [Fact]
    public void Contacts_are_what_they_were() => Check("contacts", ContactCases());

    [Fact]
    public void Landings_are_what_they_were() => Check("land", LandCases());

    [Fact]
    public void The_cases_cover_every_contact_kind()
    {
        var kinds = Expected().Where(kv => kv.Key.StartsWith("contacts ")).SelectMany(kv => kv.Value.Split(';', StringSplitOptions.RemoveEmptyEntries)).Select(s => s.Split(':')[1]).ToHashSet();
        Assert.Equal(Enum.GetNames<ContactKind>().Order(), kinds.Order());
    }
}
