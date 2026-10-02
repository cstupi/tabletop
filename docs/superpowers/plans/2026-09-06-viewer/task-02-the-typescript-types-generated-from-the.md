# Task 2: The TypeScript types, generated from the C# and checked in

**Files:**
- Create: `tests/Tabletop.Viewer.Tests/Tabletop.Viewer.Tests.csproj`, `tests/Tabletop.Viewer.Tests/RepoPaths.cs`, `tests/Tabletop.Viewer.Tests/TypeScriptEmitter.cs`, `tests/Tabletop.Viewer.Tests/GeneratedTypesTests.cs`
- Create (generated, checked in): `viewer/src/lib/protocol/recording.generated.ts`, `viewer/src/lib/protocol/wire.generated.ts`
- Modify: `Tabletop.sln` (add `tests/Tabletop.Viewer.Tests` to the `tests` solution folder)

**Interfaces:**
- Consumes: `Tabletop.Sim.Recording.Recording` and `RecordingJson.Options`, `RecordingJson.SchemaVersion` (simulation plan Task 5); `Tabletop.Protocol.WireJson.Options`, `SocketMessage`, `Connect`, `Welcome`, `GameInfoMessage`, `BoardMessage`, `EventsMessage`, `Refused`, `Rejected` (contracts plan Task 12).
- Produces: `sealed record EmitRequest(string Module, JsonSerializerOptions Options, IReadOnlyList<Type> Roots, IReadOnlyDictionary<Type, IReadOnlyList<Type>> PrunedUnions, IReadOnlyDictionary<Type, string> Rename)`; `static class TypeScriptEmitter` with `string Emit(EmitRequest)`; `static class RepoPaths` with `string Root`, `string Viewer`, `string Fixtures`; the two generated `.ts` modules, each exporting one TypeScript declaration per reachable JSON contract.
- Produces for `viewer/`: from `recording.generated.ts` — `Point2`, `Recording`, `RecordingMeta`, `RecordedTeam`, `RecordedBoard`, `RecordedTerrain`, `RecordedVolume`, `RecordedMarker`, `RecordedZone`, `RecordedModel`, `RecordedFrame`, `RecordedModelState`, `RecordedUnitState`, `RecordedMarkerState`, `RecordedAsk`, `RecordedAskOption`, `RecordedRoll`, `RecordedModifier`, and `SCHEMA_VERSION`. From `wire.generated.ts` — `SocketMessage`, pruned to its seven members, plus every type reachable from them.
- `Point2` is `Tabletop.Geometry.Point2`, reachable only through `RecordedZone.Polygons`. It is the one Geometry type the recording schema names, and it emits as `{ readonly x: number; readonly y: number }`, which is structurally `board/transform.ts`'s `Point`.

**Field order is part of the generated file.** The emitter walks `JsonTypeInfo.Properties` in order, so each interface's members come out in the C# record's positional order and the golden file records it. `RecordedMarkerState` is `Id, Kind, Diameter, Label, X, Y, Z, Yaw` — the definition first, then the placement — and Step 1's test pins that line for line, because a reordered record is a silent one-line diff in the golden file rather than a compile error anywhere. Declarations themselves are sorted by name, not by discovery, so `Point2` is the first declaration in `recording.generated.ts`.

**`at_rest` is pruned away.** `AtRest(long Seq)`, wire kind `at_rest`, is `SocketMessage`'s thirteenth kind. It stays out of `PrunedUnions`' member list, so it is not declared in `wire.generated.ts` and the checked-in module does not depend on it. Two reasons. The frame exists to replace the simulator's 250 ms quiet window as a signal that the server has stopped emitting, and the viewer has no quiet window and no such signal to replace: it republishes on every `EventsMessage`, so its `frames` array is already current the moment an events frame is decoded, and "the server has nothing more to send" changes nothing it draws. And the generated surface is deliberately the smallest thing the live feed reads — the same rule that keeps `Command`, `Edit`, `Presence` and `Ping` out — so a kind no branch acts on would be a declaration with no reader and a golden file that moves for no behavioural reason. If a later version wants an idle indicator in the header, the change is one member in `PrunedUnions`, one `HANDLED` entry and one `case`; nothing about the emitter or the guard changes.

Spec: spec → *Two feeds* — "the TypeScript types are generated from the C# records rather than hand-written"; *Still open* — "Whether generating TypeScript types from the C# records is worth a build step, or whether a checked-in generated file is enough" (this task answers it: checked in, guarded by a test). Simulation plan → *Interfaces needed from other modules*, item 6: "It should generate its TypeScript types from these C# records … rather than hand-writing them."

**Why `JsonTypeInfo` and not reflection over the records.** The emitter asks `System.Text.Json` what the JSON looks like — `JsonTypeInfo.Properties[i].Name` is the wire name, so `[JsonPropertyName("schemaVersion")]` and the camelCase policy are honoured without being reimplemented; `JsonPolymorphismOptions.DerivedTypes` gives the discriminator property and every `kind` string; and an enum's wire spelling is obtained by serialising the value rather than by guessing between `snake_case` and `kebab-case`. A reflection-over-properties emitter would be a second implementation of the JSON conventions, which is the drift this whole approach exists to remove.

- [ ] **Step 1: Write the project, `RepoPaths.cs`, and the failing test**

```xml
<!-- tests/Tabletop.Viewer.Tests/Tabletop.Viewer.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <RootNamespace>Tabletop.Viewer.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3" Version="4.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../libs/Tabletop.Protocol/Tabletop.Protocol.csproj" />
    <ProjectReference Include="../../sim/Tabletop.Sim.csproj" />
  </ItemGroup>
</Project>
```

Package versions: copy them from `tests/Tabletop.Protocol.Tests/Tabletop.Protocol.Tests.csproj` as the contracts plan Task 1 wrote it, rather than the numbers above, if they differ — one set of test package versions across the tree.

```csharp
// tests/Tabletop.Viewer.Tests/RepoPaths.cs
namespace Tabletop.Viewer.Tests;

/// <summary>The repo root, found by walking up from the test assembly until Tabletop.sln appears.</summary>
public static class RepoPaths
{
    public static string Root { get; } = Find();
    public static string Viewer => Path.Combine(Root, "viewer");
    public static string Generated => Path.Combine(Viewer, "src", "lib", "protocol");
    public static string Fixtures => Path.Combine(Viewer, "tests", "fixtures");

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tabletop.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Tabletop.sln not found above the test assembly.");
    }
}
```

```csharp
// tests/Tabletop.Viewer.Tests/GeneratedTypesTests.cs
using Tabletop.Protocol;
using Tabletop.Sim.Recording;

namespace Tabletop.Viewer.Tests;

/// <summary>
/// The drift guard. `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests` rewrites the two
/// checked-in modules; every other run asserts they are byte for byte what the C# would emit today.
/// </summary>
public class GeneratedTypesTests
{
    public static EmitRequest RecordingModule => new(
        "recording.generated.ts", RecordingJson.Options, [typeof(Recording)],
        new Dictionary<Type, IReadOnlyList<Type>>(), new Dictionary<Type, string>());

    public static EmitRequest WireModule => new(
        "wire.generated.ts", WireJson.Options,
        [typeof(SocketMessage)],
        // Only what the live feed reads. No Command, no Edit, no Presence, no Ping, no AtRest (spec →
        // Two feeds: "Keep the generated surface as small as the live feed actually needs"). Adding a
        // thirteenth kind to SocketMessage in C# therefore leaves this module byte for byte unchanged.
        new Dictionary<Type, IReadOnlyList<Type>>
        {
            [typeof(SocketMessage)] =
            [
                typeof(Connect), typeof(Welcome), typeof(GameInfoMessage), typeof(BoardMessage),
                typeof(EventsMessage), typeof(Refused), typeof(Rejected),
            ],
        },
        new Dictionary<Type, string>());

    public static TheoryData<string> Modules() => new("recording.generated.ts", "wire.generated.ts");

    static EmitRequest RequestFor(string module) => module == "recording.generated.ts" ? RecordingModule : WireModule;

    [Theory]
    [MemberData(nameof(Modules))]
    public void The_checked_in_module_is_what_the_csharp_emits_today(string module)
    {
        var emitted = TypeScriptEmitter.Emit(RequestFor(module));
        var path = Path.Combine(RepoPaths.Generated, module);
        if (Environment.GetEnvironmentVariable("TABLETOP_WRITE_GENERATED") == "1")
        {
            Directory.CreateDirectory(RepoPaths.Generated);
            File.WriteAllText(path, emitted);
        }
        Assert.True(File.Exists(path), $"{path} is missing; run TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests");
        Assert.Equal(emitted, File.ReadAllText(path));
    }

    [Fact]
    public void The_recording_module_carries_the_schema_version_and_its_own_property_names()
    {
        var ts = TypeScriptEmitter.Emit(RecordingModule);
        Assert.Contains($"export const SCHEMA_VERSION = {RecordingJson.SchemaVersion};", ts);
        Assert.Contains("readonly schemaVersion: number;", ts);          // [JsonPropertyName], honoured by the contract
        Assert.Contains("export interface RecordedFrame {", ts);
        Assert.Contains("readonly openAsks: readonly RecordedAsk[];", ts);
        Assert.Contains("readonly legalKind: string;", ts);
        Assert.Contains("readonly raw: readonly number[];", ts);
        Assert.Contains("readonly modified: readonly number[];", ts);
        Assert.DoesNotContain("SocketMessage", ts);                      // the recording schema is self-contained

        // polygons, scores and passed are pinned here; the marker's Kind, Diameter and Label
        // are pinned by the RecordedMarkerState block below. Scores is the only dictionary in the whole
        // schema: its Guid keys are canonical strings because neither option set sets a key policy.
        Assert.Contains("readonly polygons: readonly (readonly Point2[])[];", ts);
        Assert.Contains("export interface Point2 {\n  readonly x: number;\n  readonly y: number;\n}", ts.Replace("\r\n", "\n"));
        Assert.Contains("readonly scores: { readonly [key: string]: number };", ts);
        Assert.Contains("readonly passed: readonly string[];", ts);

        // Positional order, not just presence: the emitter follows JsonTypeInfo.Properties, so a
        // reordered C# record shows up here rather than as a silent golden-file diff.
        Assert.Contains(
            """
            export interface RecordedMarkerState {
              readonly id: string;
              readonly kind: string;
              readonly diameter: number;
              readonly label?: string;
              readonly x: number;
              readonly y: number;
              readonly z: number;
              readonly yaw: number;
            }
            """.ReplaceLineEndings("\n"),
            ts.Replace("\r\n", "\n"));
    }

    [Fact]
    public void The_wire_module_carries_the_frames_the_live_feed_reads_and_nothing_more()
    {
        var ts = TypeScriptEmitter.Emit(WireModule);
        Assert.Contains("export interface EventsMessage {\n  readonly kind: 'events';", ts.Replace("\r\n", "\n"));
        Assert.Contains("export type EventPayload =", ts);
        Assert.Contains("export type StateChange =", ts);
        Assert.Contains("export type LegalSet =", ts);
        Assert.Contains("export type Location =", ts);
        Assert.Contains("export interface ModelsMoved {\n  readonly kind: 'models_moved';", ts.Replace("\r\n", "\n"));
        foreach (var absent in new[] { "AnswerAsk", "MoveModels", "PickTeam", "SetTableSize", "PresenceMessage", "PingMessage", "Cursor", "Measurement", "AtRest", "at_rest" })
            Assert.DoesNotContain(absent, ts);
    }

    [Fact]
    public void Every_union_member_is_discriminated_by_a_literal_kind()
    {
        var ts = TypeScriptEmitter.Emit(WireModule).Replace("\r\n", "\n");
        // `export type X = A | B;` is emitted for two different things: a discriminated union, whose
        // members are interface names, and an enum, whose members are quoted literals — plus a [Flags]
        // enum, emitted as `export type X = string;`. Only the first kind is what this test is for, so
        // skip any declaration whose members are quoted or are the bare `string`.
        var unions = 0;
        foreach (var line in ts.Split('\n').Where(l => l.StartsWith("export type ") && l.Contains(" = ")))
        {
            var members = line[(line.IndexOf(" = ", StringComparison.Ordinal) + 3)..].TrimEnd(';').Split(" | ");
            if (members.Any(m => m.StartsWith('\'') || m == "string")) continue;
            unions++;
            // A union alias names only interfaces, and each of those interfaces opens with a literal kind.
            foreach (var m in members)
                Assert.Contains($"export interface {m} {{\n  readonly kind: '", ts);
        }
        // SocketMessage, EventPayload, StateChange, LegalSet, Location, AskAnswer — if the skip rule
        // ever swallows one of those, the loop above passes by checking nothing.
        Assert.True(unions >= 6, $"only {unions} discriminated unions were checked; the skip rule is too broad");
    }
}
```

- [ ] **Step 2: Run; expect failure**

```bash
dotnet test tests/Tabletop.Viewer.Tests
```
Expected: `CS0246: The type or namespace name 'TypeScriptEmitter' could not be found`.

- [ ] **Step 3: Write `TypeScriptEmitter.cs`**

```csharp
using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Tabletop.Viewer.Tests;

/// <param name="PrunedUnions">Unions to emit with fewer members than the C# declares — the live feed's subset.</param>
/// <param name="Rename">The escape hatch for two C# types with one name; empty until one collides.</param>
public sealed record EmitRequest(
    string Module,
    JsonSerializerOptions Options,
    IReadOnlyList<Type> Roots,
    IReadOnlyDictionary<Type, IReadOnlyList<Type>> PrunedUnions,
    IReadOnlyDictionary<Type, string> Rename);

/// <summary>
/// Emits TypeScript from System.Text.Json's own contracts, so no JSON convention is reimplemented here:
/// property names come from JsonPropertyInfo.Name, unions and their `kind` strings from
/// JsonPolymorphismOptions, and an enum's spelling from serialising the value. A computed C# property that
/// System.Text.Json includes appears in the output, because the output must describe the bytes, not the records.
/// </summary>
public static class TypeScriptEmitter
{
    public static string Emit(EmitRequest request)
    {
        var declarations = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var names = new Dictionary<Type, string>();
        var discriminators = new Dictionary<Type, string>();
        var queue = new Queue<Type>(request.Roots);
        var seen = new HashSet<Type>(request.Roots);

        // First pass: names and discriminators, so a declaration can refer to a type queued after it.
        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            names[type] = Name(request, type);
            foreach (var next in Referenced(request, type, discriminators))
                if (seen.Add(next)) queue.Enqueue(next);
        }

        foreach (var type in names.Keys.ToList())
            declarations[names[type]] = Declare(request, type, names, discriminators);

        var body = new StringBuilder();
        body.Append("// GENERATED by tests/Tabletop.Viewer.Tests/TypeScriptEmitter.cs. Do not edit.\n");
        body.Append("// Regenerate: TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests\n");
        if (request.Module.StartsWith("recording", StringComparison.Ordinal))
            body.Append($"\nexport const SCHEMA_VERSION = {Tabletop.Sim.Recording.RecordingJson.SchemaVersion};\n");
        foreach (var (_, text) in declarations) body.Append('\n').Append(text);
        return body.ToString();
    }

    private static string Name(EmitRequest r, Type t) => r.Rename.TryGetValue(t, out var n) ? n : t.Name;

    /// <summary>Types this one's declaration will mention, and (for a union) each member's `kind`.</summary>
    private static IEnumerable<Type> Referenced(EmitRequest r, Type type, Dictionary<Type, string> discriminators)
    {
        if (type.IsEnum) yield break;
        var info = r.Options.GetTypeInfo(type);
        if (info.PolymorphismOptions is { } poly)
        {
            foreach (var derived in Members(r, type, poly))
            {
                var kind = poly.DerivedTypes.Single(d => d.DerivedType == derived).TypeDiscriminator?.ToString()
                    ?? throw new InvalidOperationException($"{derived.Name} has no string discriminator.");
                if (discriminators.TryGetValue(derived, out var already) && already != kind)
                    throw new InvalidOperationException($"{derived.Name} is '{already}' in one union and '{kind}' in another.");
                discriminators[derived] = kind;
                yield return derived;
            }
            yield break;
        }
        foreach (var property in info.Properties)
            foreach (var referenced in Leaves(property.PropertyType))
                yield return referenced;
    }

    private static IReadOnlyList<Type> Members(EmitRequest r, Type union, JsonPolymorphismOptions poly) =>
        r.PrunedUnions.TryGetValue(union, out var subset) ? subset : poly.DerivedTypes.Select(d => d.DerivedType).ToList();

    /// <summary>Every non-primitive type inside a property type: unwraps Nullable, arrays, lists and dictionaries.</summary>
    private static IEnumerable<Type> Leaves(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (Element(t) is { } element) { foreach (var leaf in Leaves(element)) yield return leaf; yield break; }
        if (Primitive(t) is not null) yield break;
        yield return t;
    }

    private static Type? Element(Type t)
    {
        if (t == typeof(string) || !t.IsGenericType) return t.IsArray ? t.GetElementType() : null;
        var args = t.GetGenericArguments();
        var open = t.GetGenericTypeDefinition();
        if (open == typeof(IReadOnlyDictionary<,>) || open == typeof(Dictionary<,>)) return args[1];
        return typeof(IEnumerable).IsAssignableFrom(t) ? args[0] : null;
    }

    private static string? Primitive(Type t) => t switch
    {
        _ when t == typeof(string) || t == typeof(Guid) || t == typeof(DateTimeOffset) || t == typeof(DateTime) => "string",
        _ when t == typeof(bool) => "boolean",
        _ when t == typeof(int) || t == typeof(long) || t == typeof(ulong) || t == typeof(uint)
            || t == typeof(short) || t == typeof(double) || t == typeof(float) || t == typeof(decimal) => "number",
        _ => null,
    };

    private static string Declare(EmitRequest r, Type type, Dictionary<Type, string> names, Dictionary<Type, string> kinds)
    {
        var name = names[type];
        if (type.IsEnum) return DeclareEnum(r, type, name);

        var info = r.Options.GetTypeInfo(type);
        if (info.PolymorphismOptions is { } poly)
        {
            var members = string.Join(" | ", Members(r, type, poly).Select(m => names[m]));
            return $"export type {name} = {members};\n";
        }

        var text = new StringBuilder($"export interface {name} {{\n");
        if (kinds.TryGetValue(type, out var kind)) text.Append($"  readonly kind: '{kind}';\n");
        var nullability = new NullabilityInfoContext();
        foreach (var property in info.Properties)
        {
            var optional = IsOptional(property, nullability);
            text.Append($"  readonly {property.Name}{(optional ? "?" : "")}: {Reference(r, property.PropertyType, names)};\n");
        }
        return text.Append("}\n").ToString();
    }

    /// <summary>Serialise every member so the emitter never guesses between snake_case and kebab-case.</summary>
    private static string DeclareEnum(EmitRequest r, Type type, string name)
    {
        if (type.GetCustomAttribute<FlagsAttribute>() is not null)
            return $"/** [Flags] {type.Name}: a comma-joined list of set names, or 'none'. */\nexport type {name} = string;\n";
        var literals = Enum.GetValues(type).Cast<object>()
            .Select(v => JsonSerializer.Serialize(v, type, r.Options).Trim('"'))
            .Select(s => $"'{s}'");
        return $"export type {name} = {string.Join(" | ", literals)};\n";
    }

    private static string Reference(EmitRequest r, Type type, Dictionary<Type, string> names)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (Primitive(t) is { } primitive) return primitive;
        if (t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>) || t.GetGenericTypeDefinition() == typeof(Dictionary<,>)))
            return $"{{ readonly [key: string]: {Reference(r, t.GetGenericArguments()[1], names)} }}";
        if (Element(t) is { } element) return $"readonly {Reference(r, element, names)}[]";
        return names.TryGetValue(t, out var name) ? name
            : throw new InvalidOperationException($"{t.FullName} was referenced but never queued.");
    }

    private static bool IsOptional(JsonPropertyInfo property, NullabilityInfoContext context)
    {
        if (Nullable.GetUnderlyingType(property.PropertyType) is not null) return true;
        return property.AttributeProvider switch
        {
            PropertyInfo p => context.Create(p).ReadState is NullabilityState.Nullable,
            FieldInfo f => context.Create(f).ReadState is NullabilityState.Nullable,
            _ => false,
        };
    }
}
```

A property emitted with `?` and no `| null` is deliberate: both option sets set `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull` (`ContentJson.Configure`, `RecordingJson.Options`), so a null field is absent rather than `null`.

If two reachable C# types share a `Name`, `declarations[name]` is overwritten and the emitted module is wrong; add the second one to `EmitRequest.Rename` with a qualified TypeScript name. To make that loud rather than silent, `Declare`'s caller asserts distinctness:

```csharp
// in Emit, replacing the second foreach
foreach (var type in names.Keys.ToList())
{
    var text = Declare(request, type, names, discriminators);
    if (declarations.TryGetValue(names[type], out var clash) && clash != text)
        throw new InvalidOperationException($"Two types are both called {names[type]}; add one to EmitRequest.Rename.");
    declarations[names[type]] = text;
}
```

- [ ] **Step 4: Generate, then run the guard**

```bash
cd /home/cstupi/src/tabletop
TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests
dotnet test tests/Tabletop.Viewer.Tests
head -40 viewer/src/lib/protocol/recording.generated.ts
grep -c '^export ' viewer/src/lib/protocol/wire.generated.ts
cd viewer && npx tsc --noEmit -p tsconfig.json
```

Expected: 5 tests passed on the second run (the `Modules` theory is two cases, plus three facts); `recording.generated.ts` opens with the two generated-by lines, `SCHEMA_VERSION = 1`, and then `export interface Point2` — declarations are sorted by name and `Point2` precedes `Recorded*` — followed by `export interface RecordedAsk`; `wire.generated.ts` has roughly 90 declarations and neither `AnswerAsk` nor `AtRest`; `tsc` reports no errors. That count and that file are unaffected by `at_rest` joining `SocketMessage`, because the kind is pruned away — nothing in this task's output moves when the contracts plan lands ruling 4.

- [ ] **Step 5: Add the project to the solution and commit**

```bash
cd /home/cstupi/src/tabletop
dotnet sln add tests/Tabletop.Viewer.Tests/Tabletop.Viewer.Tests.csproj --solution-folder tests
dotnet build
git add tests/Tabletop.Viewer.Tests viewer/src/lib/protocol Tabletop.sln
git commit -m "Generate the viewer's TypeScript types from the C# JSON contracts"
```
