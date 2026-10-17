using Tabletop.Management.Configuration;

namespace Tabletop.Management.Persistence;

/// <summary>Brings the schema up to date (dev mode at startup, else the --migrate run), or names what is missing.</summary>
public interface IMigrator
{
    void Run();
    IReadOnlyList<string> Pending();
}

/// <summary>Migrator over DB_CONNECTION.</summary>
public sealed class PostgresMigrator(ManagementOptions options) : IMigrator
{
    public void Run() => Migrator.Run(options.DbConnection);
    public IReadOnlyList<string> Pending() => Migrator.Pending(options.DbConnection);
}
