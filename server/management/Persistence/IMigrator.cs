using Tabletop.Management.Configuration;

namespace Tabletop.Management.Persistence;

/// <summary>Brings the schema up to date, once, at startup.</summary>
public interface IMigrator
{
    void Run();
}

/// <summary>Migrator over DB_CONNECTION.</summary>
public sealed class PostgresMigrator(ManagementOptions options) : IMigrator
{
    public void Run() => Migrator.Run(options.DbConnection);
}
