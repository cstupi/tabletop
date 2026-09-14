using System.Reflection;
using System.Text.Json.Serialization;

namespace Tabletop.Protocol.Tests;

public class StateChangeTests
{
    public static IEnumerable<Type> ConcreteSubclasses(Type baseType) =>
        baseType.Assembly.GetTypes().Where(t => t.IsSubclassOf(baseType) && !t.IsAbstract);

    public static IReadOnlyDictionary<Type, string> Registered(Type baseType) =>
        baseType.GetCustomAttributes<JsonDerivedTypeAttribute>().ToDictionary(a => a.DerivedType, a => (string)a.TypeDiscriminator!);

    [Fact]
    public void Every_state_change_is_registered_with_a_unique_snake_case_kind()
    {
        var registered = Registered(typeof(StateChange));
        foreach (var t in ConcreteSubclasses(typeof(StateChange)))
            Assert.True(registered.ContainsKey(t), $"{t.Name} is not a [JsonDerivedType] of StateChange");
        Assert.Equal(registered.Count, registered.Values.Distinct().Count());
        Assert.All(registered.Values, k => Assert.Matches("^[a-z][a-z0-9_]*$", k));
    }
}
