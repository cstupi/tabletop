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
        // What a playing client sends and reads: Command, Edit, AtRest, Reauth, sight and model facts join the
        // live feed's frames. No Presence, no Ping (presence is not ported). A new kind added to SocketMessage in C#
        // leaves this module byte for byte unchanged until it is listed here.
        new Dictionary<Type, IReadOnlyList<Type>>
        {
            [typeof(SocketMessage)] =
            [
                typeof(Connect), typeof(Welcome), typeof(GameInfoMessage), typeof(BoardMessage),
                typeof(EventsMessage), typeof(Refused), typeof(Rejected),
                typeof(CommandMessage), typeof(EditMessage), typeof(AtRest), typeof(Reauth),
                typeof(SightQuery), typeof(SightAnswer), typeof(ModelFacts),
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
              readonly controlledBy?: string;
            }
            """.ReplaceLineEndings("\n"),
            ts.Replace("\r\n", "\n"));
    }

    [Fact]
    public void The_wire_module_carries_the_frames_a_playing_client_sends_and_reads()
    {
        var ts = TypeScriptEmitter.Emit(WireModule);
        Assert.Contains("export interface EventsMessage {\n  readonly kind: 'events';", ts.Replace("\r\n", "\n"));
        Assert.Contains("export type EventPayload =", ts);
        Assert.Contains("export type StateChange =", ts);
        Assert.Contains("export type LegalSet =", ts);
        Assert.Contains("export type Location =", ts);
        Assert.Contains("export interface ModelsMoved {\n  readonly kind: 'models_moved';", ts.Replace("\r\n", "\n"));
        Assert.Contains("export interface VisualRef {\n  readonly builtin?: string;\n  readonly assetId?: string;\n}", ts.Replace("\r\n", "\n"));
        var n = ts.Replace("\r\n", "\n");
        foreach (var present in new[]
        {
            "export type Command =", "export type Edit =",
            "export interface CommandMessage {\n  readonly kind: 'command';",
            "export interface EditMessage {\n  readonly kind: 'edit';",
            "export interface AtRest {\n  readonly kind: 'at_rest';",
            "export interface PickTeam {\n  readonly kind: 'pick_team';",
            "export interface ReplaceDraft {\n  readonly kind: 'replace_draft';",
            "export interface Reauth", "export interface ModelFacts",
        })
            Assert.Contains(present, n);
        foreach (var absent in new[] { "PresenceMessage", "PingMessage", "presence_ended", "Cursor", "Measurement" })
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
        Assert.True(unions >= 8, $"only {unions} discriminated unions were checked; the skip rule is too broad");
    }
}
