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
        Assert.Contains("readonly polygons: readonly (readonly RecordedPoint[])[];", ts);
        Assert.Contains("export interface RecordedPoint {\n  readonly x: number;\n  readonly y: number;\n}", ts.Replace("\r\n", "\n"));
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
        Assert.Contains("export interface VisualRef {\n  readonly builtin?: string;\n  readonly assetId?: string;\n}", ts.Replace("\r\n", "\n"));
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
