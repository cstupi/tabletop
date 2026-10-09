namespace Tabletop.Sim.Serve;

/// <summary>`serve [--port 5180] [--bind 127.0.0.1] [--out DIR]`. Out is where kept recordings go, one folder per run.</summary>
public sealed record ServeArguments(int Port, string Bind, string Out)
{
    public const string Verb = "serve";
    public const int DefaultPort = 5180;
    public const string DefaultOut = "recordings/runner";

    public static string Usage => """
        dotnet run --project sim -- serve [options]

          --port N      the port (default 5180)
          --bind ADDR   the address to listen on (default 127.0.0.1; 0.0.0.0 inside a container)
          --out DIR     where kept recordings go (default: ./recordings/runner)
        """;

    public static ServeArguments Parse(string[] args)
    {
        var port = DefaultPort;
        var bind = "127.0.0.1";
        var output = DefaultOut;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port":
                    if (!int.TryParse(Next(args, ref i, "--port"), out port) || port is < 0 or > 65535)
                        throw new UsageException("--port is a number from 0 to 65535");
                    break;
                case "--bind": bind = Next(args, ref i, "--bind"); break;
                case "--out": output = Next(args, ref i, "--out"); break;
                default: throw new UsageException($"unknown serve argument '{args[i]}'\n\n{Usage}");
            }
        }
        return new ServeArguments(port, bind, output);
    }

    private static string Next(string[] args, ref int i, string option) =>
        ++i < args.Length ? args[i] : throw new UsageException($"{option} wants a value");
}
