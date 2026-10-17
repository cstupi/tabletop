using Tabletop.Management.Configuration;

namespace Tabletop.Management.Persistence;

/// <summary>`Tabletop.Management --migrate`: applies the migrations over TABLETOP_DB_CONNECTION (the owner role) and exits. Outside dev mode
/// it is the only way the schema changes; the server itself connects as a runtime role that cannot create tables.</summary>
public static class MigrateCommand
{
    public const string Flag = "--migrate";

    public static int Run(string environment, IConfiguration c)
    {
        var connection = c["DB_CONNECTION"];
        if (string.IsNullOrEmpty(connection)) { Console.Error.WriteLine("Missing configuration TABLETOP_DB_CONNECTION."); return 2; }
        StartupGuard.Throw(environment, StartupGuard.DatabaseProblems(environment, connection));
        var pending = Migrator.Pending(connection);
        Migrator.Run(connection);
        Console.WriteLine(pending.Count == 0 ? "Schema up to date." : $"Applied {string.Join(", ", pending)}.");
        return 0;
    }
}
