using Xunit;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// All Integration tests share a remote server and a global namespace for tables.
/// Serialize them so DDL/DML/cleanup do not race.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationFixture>
{
    public const string Name = "Integration";
}

public sealed class IntegrationFixture
{
}
