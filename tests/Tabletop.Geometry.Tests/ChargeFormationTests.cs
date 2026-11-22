using System.Diagnostics;
using Xunit.Abstractions;

namespace Tabletop.Geometry.Tests;

/// <summary>
/// The charge formation search (UnitFormation.InContact), the regroup search (Regroup, Closer), contact landings (Movement.ContactPoints) and
/// charge reach (Movement.ContactInReach) answer what they answered before the search was made fast, bit for bit: over the three charge move Asks of
/// ChargeFormationScene with no vertical reach, 3.6.0's 3" and 3.5.1's 4", and over CavalryRushScene's riders charging each other unit.
/// </summary>
public class ChargeFormationTests(ITestOutputHelper output)
{
    static readonly Dictionary<string, string> Expected = new()
    {
        ["contact 1109"] = "0E794C3006E1533D703AB5CF3B30B187 00000000110000000000000000000000000000000000000000",
        ["contact 1261"] = "5E9B92978E6537E64CF0D1768F6EF1DC 111111111111111111111111111100000011111111110011101000000010",
        ["contact 679"] = "64C914F2EFAC48A8DDF28008BAEAECC3 000000000000000000001000000000001100000000111100000000111100",
        ["in-contact 1109 "] = "35FC46FF3EEEEE14E8F47E776C82BBBF",
        ["in-contact 1109 3"] = "35FC46FF3EEEEE14E8F47E776C82BBBF",
        ["in-contact 1109 4"] = "35FC46FF3EEEEE14E8F47E776C82BBBF",
        ["in-contact 1261 "] = "null",
        ["in-contact 1261 3"] = "null",
        ["in-contact 1261 4"] = "null",
        ["in-contact 679 "] = "null",
        ["in-contact 679 3"] = "null",
        ["in-contact 679 4"] = "null",
        ["regroup 1109"] = "8DA24DEF4D7EBB3E42AF153B7C71B73A/6088A92C36ADD66EC1178F96CFEFD7DF E2D903BDB555AC5A65ADDCE62CF44572/9C3C67E25F0B47411A1EF17F0210DE7F 4EF58759B629E932191D2297D3C1C6BC/null 442982682D11352CB96061F8D87328F3/AFD47687AB1F122EB063DE2C6934AEF8",
        ["regroup 1261"] = "F27D1DC19BC23D13289456BA60020263/null 88B361FC321DF472D7C88F5DAD5E376F/0EE27F85E866A665BA1FE69F03E500FE 5DEA6DFD4A3B67B36657C11EA2A18CD3/190BFAF2CDE9AF71E2A2FFC2A5DA93CB 901E0AAB3757A66AE5A9531D90997791/null",
        ["regroup 679"] = "2B28BB55C8919B96328483D0D75E9C0E/0BA48980F9B580A38D0600ADAC767D6A 4A3577293212FE3FBE0409B8FA03BFB4/B7A4DAA8C4FFE83948081EC3F5F7D849 00C0566DE97560CE30B6D7BA4442E570/null 80185AAFFA7065B775936A43A54EBE7B/38F69BA19AAE54566BEA9AF4CB9431A7",
        ["rush 0 12 6 "] = "E3C092A2C7276D8D283D1799DAF3347F",
        ["rush 0 16  4"] = "2EA01776CB8421A052FD02C81F9810A0",
        ["rush 0 16 6 3"] = "E3C092A2C7276D8D283D1799DAF3347F",
        ["rush 15 12 6 "] = "null",
        ["rush 15 16  4"] = "null",
        ["rush 15 16 6 3"] = "null",
        ["rush 31 12 6 "] = "null",
        ["rush 31 16  4"] = "null",
        ["rush 31 16 6 3"] = "null",
        ["rush 41 12 6 "] = "null",
        ["rush 41 16  4"] = "null",
        ["rush 41 16 6 3"] = "null",
        ["rush 42 12 6 "] = "null",
        ["rush 42 16  4"] = "null",
        ["rush 42 16 6 3"] = "null",
        ["rush 9 12 6 "] = "null",
        ["rush 9 16  4"] = "null",
        ["rush 9 16 6 3"] = "null",
    };

    static string Digest(IReadOnlyDictionary<Guid, Point3>? formed) =>
        formed is null ? "null" : ReferenceScene.Digest(formed.OrderBy(kv => kv.Key).SelectMany(kv => new[] { BitConverter.Int64BitsToDouble(kv.Key.GetHashCode()), kv.Value.X, kv.Value.Y, kv.Value.Z }));

    static string Digest(IReadOnlyDictionary<Guid, Point2>? formed) =>
        formed is null ? "null" : ReferenceScene.Digest(formed.OrderBy(kv => kv.Key).SelectMany(kv => new[] { BitConverter.Int64BitsToDouble(kv.Key.GetHashCode()), kv.Value.X, kv.Value.Y }));

    void Check(string key, string actual)
    {
        output.WriteLine($"[\"{key}\"] = \"{actual}\",");
        Assert.Equal(Expected.GetValueOrDefault(key), actual);
    }

    public static TheoryData<long, double?> Asks()
    {
        var data = new TheoryData<long, double?>();
        foreach (var seq in ChargeFormationScene.Seqs)
            foreach (var vertical in new double?[] { null, 3, 4 })
                data.Add(seq, vertical);
        return data;
    }

    [Theory]
    [MemberData(nameof(Asks))]
    public void The_charge_formation_is_what_it_was(long seq, double? vertical)
    {
        var scene = ChargeFormationScene.Load(seq);
        Check($"in-contact {seq} {vertical}", Digest(UnitFormation.InContact(scene.Movers, scene.Members, scene.Targets, scene.Terms(vertical))));
    }

    [Theory]
    [InlineData(679)]
    [InlineData(1109)]
    [InlineData(1261)]
    public void Contact_points_and_reach_are_what_they_were(long seq)
    {
        var scene = ChargeFormationScene.Load(seq);
        var t = scene.Terms(scene.ContactVertical);
        var points = new List<double>();
        var reach = new List<char>();
        foreach (var (_, start) in scene.Movers)
            foreach (var target in scene.Targets)
            {
                foreach (var (gap, bearings) in new[] { (UnitFormation.ContactGap, 32), (0.3, 16), (0.7, 16) })
                    points.AddRange(Movement.ContactPoints(start, target, gap, bearings).SelectMany(p => new[] { p.X, p.Y }));
                reach.Add(Movement.ContactInReach(start, target, scene.Allowance, t.Impassable, scene.Table, scene.Volumes, scene.ContactVertical) ? '1' : '0');
                reach.Add(Movement.ContactInReach(start, target, scene.Allowance, t.Impassable, scene.Table, scene.Volumes, scene.ContactVertical, t.Difficult, 6, t.Steep) ? '1' : '0');
            }
        Check($"contact {seq}", $"{ReferenceScene.Digest(points)} {new string([.. reach])}");
    }

    /// <summary>The unit with its last model 5" off its chain, each way: Regroup's landings and Closer's.</summary>
    [Theory]
    [InlineData(679)]
    [InlineData(1109)]
    [InlineData(1261)]
    public void The_regroup_is_what_it_was(long seq)
    {
        var scene = ChargeFormationScene.Load(seq);
        var t = scene.Terms(null) with { DifficultCap = 6 };
        var parts = new List<string>();
        foreach (var (dx, dy) in new[] { (5.0, 0.0), (-5.0, 0.0), (0.0, 5.0), (0.0, -5.0) })
        {
            var unit = scene.Movers.ToList();
            var (id, start) = unit[^1];
            var p = start.Placement.Position;
            unit[^1] = (id, start with { Placement = start.Placement with { Position = new Point3(p.X + dx, p.Y + dy, p.Z) } });
            var closer = UnitFormation.Closer(unit, scene.Members, t);
            parts.Add($"{Digest(UnitFormation.Regroup(unit, scene.Members, t))}/{(closer is { } c ? ReferenceScene.Digest([c.At.X, c.At.Y]) : "null")}");
        }
        Check($"regroup {seq}", string.Join(" ", parts));
    }

    public static TheoryData<int, double, double?, double?> Rushes()
    {
        var data = new TheoryData<int, double, double?, double?>();
        foreach (var target in new[] { 0, 9, 15, 31, 41, 42 })
            foreach (var (allowance, cap, vertical) in new (double, double?, double?)[] { (16, 6, 3), (16, null, 4), (12, 6, null) })
                data.Add(target, allowance, cap, vertical);
        return data;
    }

    /// <summary>CavalryRushScene's five riders charging the unit whose first model is Others[target] (models of a unit stand next to each other).</summary>
    [Theory]
    [MemberData(nameof(Rushes))]
    public void The_riders_formation_is_what_it_was(int target, double allowance, double? cap, double? vertical)
    {
        var unit = CavalryRushScene.Riders.Select((r, i) => (new Guid(i + 1, 0, 0, new byte[8]), r)).ToList();
        var count = target switch { 0 => 9, 9 => 6, 15 => 6, 31 => 11, 41 => 1, _ => 5 };
        var targets = CavalryRushScene.Others.Skip(target).Take(count).ToList();
        var t = new FormationTerms(allowance, [CavalryRushScene.Building], CavalryRushScene.DifficultGround, cap, [CavalryRushScene.Building],
            CavalryRushScene.Volumes, CavalryRushScene.Table, CavalryRushScene.Others, false, 1, 3, 9, CavalryRushScene.Hills, ContactVertical: vertical);
        Check($"rush {target} {allowance} {cap} {vertical}", Digest(UnitFormation.InContact(unit, [], targets, t)));
    }

    /// <summary>Each Ask's search, the first in the process (JIT) and the best of five: TABLETOP_BENCH=1 dotnet test tests/Tabletop.Geometry.Tests -c Release --filter ChargeFormationTests.Bench.</summary>
    [Fact]
    public void Bench()
    {
        if (Environment.GetEnvironmentVariable("TABLETOP_BENCH") != "1") return;
        foreach (var seq in ChargeFormationScene.Seqs)
        {
            var scene = ChargeFormationScene.Load(seq);
            var t = scene.Terms(scene.ContactVertical);
            var times = new List<double>();
            for (var i = 0; i < 5; i++)
            {
                var watch = Stopwatch.StartNew();
                UnitFormation.InContact(scene.Movers, scene.Members, scene.Targets, t);
                times.Add(watch.Elapsed.TotalMilliseconds);
            }
            output.WriteLine($"seq {seq}: first {times[0]:0.0} ms, best {times.Min():0.0} ms of five");
        }
    }
}
