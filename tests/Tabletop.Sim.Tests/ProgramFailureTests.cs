using System.Text.Json;
using Tabletop.Protocol;
using Tabletop.Sim;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

public class ProgramFailureTests
{
    [Fact]
    public async Task A_script_only_scenario_with_no_script_exits_one_and_still_writes_the_summary_for_every_seed()
    {
        var scenario = new ScenarioBuilder("script-only", seed: 1).Army("Red", 1, 1).Army("Blue", 1, 1)
            .Scripted(AnswerScript.Empty, only: true).Build();
        var directory = Path.Combine(Path.GetTempPath(), $"sim-{Guid.CreateVersion7():N}");
        try
        {
            var output = new StringWriter();
            var code = await Program.RunScenarioAsync(scenario, SimOptions.Parse(["script-only", "--seeds", "1..2", "--out", directory]), output, CancellationToken.None);
            Assert.Equal(1, code);

            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "summary.json"), CancellationToken.None));
            Assert.Equal(2, json.RootElement.GetProperty("runs").GetInt32());
            Assert.Equal(2, json.RootElement.GetProperty("failed").GetInt32());
            Assert.Equal("failed", json.RootElement.GetProperty("reports")[0].GetProperty("outcome").GetProperty("kind").GetString());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
