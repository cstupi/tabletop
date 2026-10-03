using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.GameServer.Tests.Infrastructure;

public static class TestArmies
{
    /// <summary>
    /// units × modelsPerUnit, fresh ids. unitRules go on the unit; modelRules on every model of that unit (1-based unit numbers).
    /// Put a rule on the side its catalog entry is declared for, or Lookup.UnitHas silently answers false: in TestProperties.Core
    /// Aircraft, Scout and Ambush are kind unit (no aggregation, so the model path can never match), Immobile is kind model with
    /// aggregation all, and Caster and Tough are read per model by Lookup.ModelParameter — so the first four go in unitRules and the
    /// last two in modelRules, which is what every fixture below does.
    /// </summary>
    public static Army Build(string name, int units, int modelsPerUnit,
        (int Unit, string Rule, int? Parameter)[]? unitRules = null, (int Unit, string Rule, int? Parameter)[]? modelRules = null)
    {
        var list = new List<Unit>();
        for (var u = 1; u <= units; u++)
        {
            var mr = (modelRules ?? []).Where(r => r.Unit == u).Select(r => new PropertyInstance(r.Rule, r.Parameter)).ToList();
            var models = Enumerable.Range(1, modelsPerUnit).Select(m => new ModelSlot(Guid.CreateVersion7(), m, $"{name} {u}.{m}",
                new Dictionary<string, string>(), mr, Base.Circle(1.26), 1.5, null, [], TestProperties.TestBook)).ToList();
            list.Add(new Unit(Guid.CreateVersion7(), u, $"{name} unit {u}", new Dictionary<string, string> { ["Quality"] = "4+", ["Defense"] = "4+" },
                (unitRules ?? []).Where(r => r.Unit == u).Select(r => new PropertyInstance(r.Rule, r.Parameter)).ToList(), models));
        }
        return new Army(Guid.CreateVersion7(), name, "grimdark-future", "test", list);
    }
}
