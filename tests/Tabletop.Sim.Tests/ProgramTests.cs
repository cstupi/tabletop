using Tabletop.Sim.Scenarios;
using System.Text.Json;
using Tabletop.Sim;

namespace Tabletop.Sim.Tests;

public class ProgramTests
{
    private static CancellationToken Ct => CancellationToken.None;

    [Fact]
    public void The_argument_surface_parses_a_name_a_seed_and_a_range()
    {
        var one = SimOptions.Parse(["skirmish", "--seed", "9"]);
        Assert.Equal("skirmish", one.Scenario);
        Assert.Equal([9ul], one.Seeds);
        Assert.Equal("in-process", one.Driver);

        var many = SimOptions.Parse(["skirmish", "--seeds", "3..6", "--driver", "socket", "--out", "/tmp/x", "--rounds", "2", "--no-recording"]);
        Assert.Equal([3ul, 4ul, 5ul, 6ul], many.Seeds);
        Assert.Equal("socket", many.Driver);
        Assert.Equal("/tmp/x", many.OutputDirectory);
        Assert.Equal(2, many.RoundCap);
        Assert.False(many.Recording);

        Assert.True(SimOptions.Parse(["--list"]).List);
        Assert.Throws<UsageException>(() => SimOptions.Parse([]));
        Assert.Throws<UsageException>(() => SimOptions.Parse(["skirmish", "--driver", "carrier-pigeon"]));
        Assert.Throws<UsageException>(() => SimOptions.Parse(["skirmish", "--seeds", "6..3"]));
        Assert.Throws<UsageException>(() => SimOptions.Parse(["skirmish", "--nonsense"]));
        Assert.Throws<UsageException>(() => SimOptions.Parse(["skirmish", "--rounds", "many"]));
    }

    [Fact]
    public void The_usage_text_never_says_localhost()
    {
        Assert.DoesNotContain("localhost", SimOptions.Usage);
        Assert.Contains("ws://127.0.0.1:5200/ws", SimOptions.Usage);
        Assert.Contains("http://127.0.0.1:5100", SimOptions.Usage);
    }

    [Fact]
    public async Task Listing_the_scenarios_exits_zero_and_names_them()
    {
        var output = new StringWriter();
        Assert.Equal(0, await Program.RunAsync(SimOptions.Parse(["--list"]), output, Ct));
        Assert.Contains("skirmish", output.ToString());
        Assert.Contains("stalemate", output.ToString());
    }

    [Fact]
    public async Task Running_a_seed_range_writes_a_recording_each_and_one_summary()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sim-{Guid.CreateVersion7():N}");
        var output = new StringWriter();
        try
        {
            var code = await Program.RunAsync(
                SimOptions.Parse(["stalemate", "--seeds", "1..3", "--out", directory]), output, Ct);
            Assert.Equal(0, code);
            Assert.Equal(3, Directory.GetFiles(directory, "stalemate-*.json").Length);
            Assert.True(File.Exists(Path.Combine(directory, "summary.json")));
            Assert.Contains("3 runs", output.ToString());
            Assert.Contains("stalemate", output.ToString());

            var recording = await Recording.RecordingJson.ReadAsync(Path.Combine(directory, "stalemate-1.json"), Ct);
            Assert.Equal(1ul, recording.Meta.Seed);
            Assert.Equal("in-process", recording.Meta.Driver);
            Assert.NotEmpty(recording.Frames);

            // RunLoop always leaves RunReport.RecordingPath null; this is the only code that fills it in,
            // and the path reaches summary.json by no other route.
            var text = await File.ReadAllTextAsync(Path.Combine(directory, "summary.json"), Ct);
            Assert.Contains("stalemate-1.json", text);

            // The outcome's detail, not only its kind, reaches summary.json.
            using var json = JsonDocument.Parse(text);
            var first = json.RootElement.GetProperty("reports")[0];
            Assert.Equal("round_limit", first.GetProperty("outcome").GetProperty("kind").GetString());
            Assert.Equal(3, first.GetProperty("outcome").GetProperty("rounds").GetInt32());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task An_unknown_scenario_is_a_usage_error()
    {
        var output = new StringWriter();
        Assert.Equal(2, await Program.RunAsync(SimOptions.Parse(["no-such-scenario"]), output, Ct));
        Assert.Contains("no scenario", output.ToString());
    }

    [Fact]
    public void The_casting_scenario_fields_a_real_books_casters_and_pins_the_production_set()
    {
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("casting"));
        Assert.Equal(ScenarioCompiler.CoreProperties.ContentHash, c.Info.PropertiesHash);
        var casters = c.Board.Armies.SelectMany(a => a.Snapshot.Units).SelectMany(u => u.Models).Where(m => m.Properties.Any(p => p.Name == "Caster")).ToList();
        Assert.NotEmpty(casters);
        Assert.All(casters, m => Assert.Equal("z65fgu0l29i4lnlu", m.ArmyBook));
        Assert.Equal(6, c.Properties.SpellsOf("z65fgu0l29i4lnlu").Count);
    }

    [Fact]
    public async Task No_recording_writes_nothing()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sim-{Guid.CreateVersion7():N}");
        var output = new StringWriter();
        Assert.Equal(0, await Program.RunAsync(
            SimOptions.Parse(["stalemate", "--seed", "1", "--out", directory, "--no-recording"]), output, Ct));
        Assert.False(Directory.Exists(directory));
    }
}
