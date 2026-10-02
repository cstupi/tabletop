using Tabletop.GameServer.Tests.Infrastructure;

namespace Tabletop.Sim.Tests;

/// <summary>
/// xUnit resolves [CollectionDefinition] in the test assembly only, so the collection is declared here
/// over the fixture type imported from tests/Tabletop.GameServer.Tests.
/// </summary>
[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;
