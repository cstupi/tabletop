namespace Tabletop.Sim;

public sealed class UsageException(string message) : Exception(message);

public sealed record SimOptions(
    string? Scenario, ulong FromSeed, ulong ToSeed, string Driver,
    string OutputDirectory, int? RoundCap, bool Recording, bool List)
{
    public const string InProcess = "in-process";
    public const string Socket = "socket";
    public const string DefaultWebSocket = "ws://127.0.0.1:5200/ws";
    public const string DefaultManagement = "http://127.0.0.1:5100";

    public static string Usage => $"""
        dotnet run --project sim -- <scenario> [options]
        dotnet run --project sim -- suite [options]       the rules suite
        dotnet run --project sim -- lifecycle [options]   the lifecycle suite (the stack up)

          --seed N              one seed (default: the scenario's own)
          --seeds A..B          a closed range of seeds, one game each
          --driver in-process   the default; or socket
          --out DIR             where recordings and summary.json go (default: ./recordings)
          --rounds N            override the scenario's round cap
          --no-recording        run and report, write no files
          --list                list the scenario names and exit

        For --driver socket: TABLETOP_DB_CONNECTION, TABLETOP_SIM_WS (default {DefaultWebSocket})
        and TABLETOP_SIM_MANAGEMENT (default {DefaultManagement}), from the environment or the
        nearest .env. Session tokens are minted from POST /v1/dev/sessions, which needs the
        management API run as a development environment.
        """;

    public IEnumerable<ulong> Seeds
    {
        get { for (var seed = FromSeed; seed <= ToSeed; seed++) { yield return seed; if (seed == ulong.MaxValue) yield break; } }
    }

    public static SimOptions Parse(string[] args)
    {
        string? scenario = null, driver = InProcess, output = "recordings";
        ulong? from = null, to = null;
        int? rounds = null;
        var recording = true;
        var list = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--list": list = true; break;
                case "--no-recording": recording = false; break;
                case "--seed": from = to = ULong(Next(args, ref i, "--seed")); break;
                case "--seeds":
                    var parts = Next(args, ref i, "--seeds").Split("..", StringSplitOptions.TrimEntries);
                    if (parts.Length != 2) throw new UsageException("--seeds wants A..B");
                    from = ULong(parts[0]); to = ULong(parts[1]);
                    if (from > to) throw new UsageException($"--seeds {from}..{to} runs backwards");
                    break;
                case "--driver":
                    driver = Next(args, ref i, "--driver");
                    if (driver is not (InProcess or Socket)) throw new UsageException($"--driver is {InProcess} or {Socket}, not '{driver}'");
                    break;
                case "--out": output = Next(args, ref i, "--out"); break;
                case "--rounds":
                    var text = Next(args, ref i, "--rounds");
                    rounds = int.TryParse(text, out var n) && n > 0 ? n : throw new UsageException($"'{text}' is not a round count");
                    break;
                default:
                    if (args[i].StartsWith('-')) throw new UsageException($"unknown option '{args[i]}'\n\n{Usage}");
                    if (scenario is not null) throw new UsageException($"one scenario at a time; got '{scenario}' and '{args[i]}'");
                    scenario = args[i];
                    break;
            }
        }

        if (!list && scenario is null) throw new UsageException(Usage);
        var seed = from ?? (scenario is not null && Scenarios.ScenarioCatalog.TryGet(scenario, out var s) ? s.Seed : 1);
        return new SimOptions(scenario, seed, to ?? seed, driver, output, rounds, recording, list);
    }

    private static string Next(string[] args, ref int i, string option) =>
        ++i < args.Length ? args[i] : throw new UsageException($"{option} wants a value");

    private static ulong ULong(string text) =>
        ulong.TryParse(text, out var value) ? value : throw new UsageException($"'{text}' is not a seed");
}

/// <summary>`suite [--only a,b] [--driver in-process|socket] [--keep] [--list] [--out DIR] [--verbose]`. Out is null unless given.</summary>
public sealed record SuiteArguments(IReadOnlyList<string> Only, Suite.DriverKind Driver, bool Keep, bool List, string? Out, bool Verbose = false)
{
    public const string Verb = "suite";
    public const string DefaultOut = "recordings";

    public static string Usage => """
        dotnet run --project sim -- suite [options]

          --only A,B            game names (name, name@3.6) or groups (core, special, army:3.5:<book>, spells:3.5:<book>); repeatable; default every game
          --driver in-process   the default; or socket (the stack up, as for a scenario's socket run)
          --keep                keep the games: recordings in --out, and a socket run's rows
          --out DIR             where the report (suite-<run id>.json and .md, copied to suite-latest.*) and kept recordings go (default: ./recordings)
          --verbose             print a line per game and the coverage table as well as writing them to the report
          --list                list the games and what each covers, and exit
        """;

    public static SuiteArguments Parse(string[] args)
    {
        var only = new List<string>();
        var driver = Suite.DriverKind.InProcess;
        var keep = false;
        var list = false;
        var verbose = false;
        string? output = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--only":
                    only.AddRange(Next(args, ref i, "--only").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                    break;
                case "--driver":
                    driver = Next(args, ref i, "--driver") switch
                    {
                        SimOptions.InProcess => Suite.DriverKind.InProcess,
                        SimOptions.Socket => Suite.DriverKind.Socket,
                        var other => throw new UsageException($"--driver is {SimOptions.InProcess} or {SimOptions.Socket}, not '{other}'"),
                    };
                    break;
                case "--keep": keep = true; break;
                case "--list": list = true; break;
                case "--verbose": verbose = true; break;
                case "--out": output = Next(args, ref i, "--out"); break;
                default: throw new UsageException($"unknown suite argument '{args[i]}'\n\n{Usage}");
            }
        }
        return new SuiteArguments(only, driver, keep, list, output, verbose);
    }

    private static string Next(string[] args, ref int i, string option) =>
        ++i < args.Length ? args[i] : throw new UsageException($"{option} wants a value");
}

/// <summary>The lifecycle suite's verb (lifecycle suite spec): the shapes to play, or the list of them.</summary>
public sealed record LifecycleArguments(IReadOnlyList<string> Only, bool List)
{
    public const string Verb = "lifecycle";

    public static string Usage => $"""
        dotnet run --project sim -- lifecycle [options]

          --only A,B            shape names; repeatable; default every shape
          --list                list the shapes and exit

        Plays each shape against the running stack by the client's path: management over HTTP, the game over the socket.
        TABLETOP_SIM_MANAGEMENT (default {SimOptions.DefaultManagement}) and TABLETOP_SIM_WS (default {SimOptions.DefaultWebSocket}),
        from the environment or the nearest .env; both servers in dev mode.
        """;

    public static LifecycleArguments Parse(string[] args)
    {
        var only = new List<string>();
        var list = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--only":
                    if (++i >= args.Length) throw new UsageException("--only wants a value");
                    only.AddRange(args[i].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                    break;
                case "--list": list = true; break;
                default: throw new UsageException($"unknown lifecycle argument '{args[i]}'\n\n{Usage}");
            }
        }
        return new LifecycleArguments(only, list);
    }
}
