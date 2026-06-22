using System.Data;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// Integration coverage for protocol-level behaviors that are 0% covered by unit tests:
/// catalog switching, multi-chunk result fetching, post-disconnect rejection, and
/// transaction isolation level propagation.
/// </summary>
[Trait("Category", "Integration")]
[Collection(IntegrationTestCollection.Name)]
public sealed class ProtocolFeatureIntegrationTests
{
    private readonly TestOptions _options;

    public ProtocolFeatureIntegrationTests(TestOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// A connection string with Catalog set must auto-switch to that catalog on connect,
    /// so an unqualified table reference resolves inside it.
    /// </summary>
    [Fact]
    public async Task Catalog_ConnectionString_AutoSwitchesCatalog()
    {
        // Strip any existing Catalog and force memory — the default in-memory catalog.
        var baseCs = _options.ConnectionString.Split(';')
            .Where(p => !p.Trim().StartsWith("Catalog", StringComparison.OrdinalIgnoreCase));
        var catalogCs = string.Join(';', baseCs.Append("Catalog=memory"));

        await using var connection = new QuackConnection(catalogCs);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        // current_catalog should be "memory" after the implicit USE.
        command.CommandText = "SELECT current_catalog()";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("memory", reader.GetString(0));
    }

    /// <summary>
    /// A result set larger than DuckDB's vector size forces multiple FetchAsync round-trips.
    /// The reader must stitch them together and surface every row.
    /// </summary>
    [Fact]
    public async Task LargeResultSet_FetchesAcrossMultipleChunks()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        // 100000 rows reproduces the benchmark path where the final non-empty FetchResponse
        // no longer carries a next UUID. The reader must return that final batch and stop.
        command.CommandText = "SELECT i FROM range(0, 100000) t(i) ORDER BY i";

        await using var reader = await command.ExecuteReaderAsync();
        long expected = 0;
        var lastSeen = -1L;
        var sum = 0L;
        while (await reader.ReadAsync())
        {
            var value = reader.GetInt64(0);
            Assert.Equal(expected, value);
            lastSeen = value;
            sum += value;
            expected++;
        }

        Assert.Equal(100000, expected);
        Assert.Equal(99999, lastSeen);
        Assert.Equal(4_999_950_000L, sum);
    }

    /// <summary>
    /// The benchmark path does not set Catalog and reuses the server UUID across Fetch calls.
    /// The UUID must be replayed byte-for-byte, otherwise some server encodings fail after
    /// the first full 12-chunk response with "Result has been closed".
    /// </summary>
    [Fact]
    public async Task LargeResultSet_WithoutCatalog_ReplaysFetchUuidBytes()
    {
        var noCatalogCs = string.Join(';', _options.ConnectionString.Split(';')
            .Where(p => !p.Trim().StartsWith("Catalog", StringComparison.OrdinalIgnoreCase)));

        await using var connection = new QuackConnection(noCatalogCs);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT i, CAST(i AS VARCHAR) FROM range(0, 100000) t(i)";

        await using var reader = await command.ExecuteReaderAsync();
        long expected = 0;
        while (await reader.ReadAsync())
        {
            Assert.Equal(expected, reader.GetInt64(0));
            Assert.Equal(expected.ToString(System.Globalization.CultureInfo.InvariantCulture), reader.GetString(1));
            expected++;
        }

        Assert.Equal(100000, expected);
    }

    /// <summary>
    /// After Dispose, the underlying connection is torn down; any subsequent command on it
    /// (or the server's view of the session) must reject rather than silently succeed.
    /// </summary>
    [Fact]
    public async Task Dispose_TearsDownConnectionAndRejectsReuse()
    {
        var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var warmup = connection.CreateCommand();
        warmup.CommandText = "SELECT 1";
        await warmup.ExecuteReaderAsync();

        await connection.DisposeAsync();

        // Any attempt to use the torn-down connection — even creating a command — must fail
        // rather than silently proceed against a dead session.
        await Assert.ThrowsAnyAsync<Exception>(() =>
        {
            var afterDispose = connection.CreateCommand();
            afterDispose.CommandText = "SELECT 1";
            return afterDispose.ExecuteReaderAsync();
        });
    }

    /// <summary>
    /// BeginTransaction with an explicit IsolationLevel must propagate it onto the transaction
    /// object, and a committed transaction's writes must be visible to a fresh reader.
    /// </summary>
    [Fact]
    public async Task Transaction_CommitMakesWritesVisibleAndReportsIsolation()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        var tableName = $"iso_{Guid.NewGuid():N}".Substring(0, 30);
        await using (var create = connection.CreateCommand())
        {
            create.CommandText = $"CREATE TABLE {tableName} (id INTEGER)";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            await using (var tx = await connection.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                Assert.Equal(IsolationLevel.Serializable, tx.IsolationLevel);

                await using var insert = connection.CreateCommand();
                insert.CommandText = $"INSERT INTO {tableName} VALUES (1)";
                await insert.ExecuteNonQueryAsync();

                await tx.CommitAsync();
            }

            await using var read = connection.CreateCommand();
            read.CommandText = $"SELECT COUNT(*) FROM {tableName}";
            await using var reader = await read.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1L, Convert.ToInt64(reader.GetValue(0)));
        }
        finally
        {
            await using var cleanup = connection.CreateCommand();
            cleanup.CommandText = $"DROP TABLE IF EXISTS {tableName}";
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// A rolled-back transaction must leave the table untouched — the canonical rollback contract.
    /// </summary>
    [Fact]
    public async Task Transaction_RollbackDiscardsWrites()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        var tableName = $"rb_{Guid.NewGuid():N}".Substring(0, 30);
        await using (var create = connection.CreateCommand())
        {
            create.CommandText = $"CREATE TABLE {tableName} (id INTEGER)";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            await using (var tx = await connection.BeginTransactionAsync())
            {
                Assert.Equal(IsolationLevel.ReadCommitted, tx.IsolationLevel);

                await using var insert = connection.CreateCommand();
                insert.CommandText = $"INSERT INTO {tableName} VALUES (1)";
                await insert.ExecuteNonQueryAsync();

                await tx.RollbackAsync();
            }

            await using var read = connection.CreateCommand();
            read.CommandText = $"SELECT COUNT(*) FROM {tableName}";
            await using var reader = await read.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(0L, Convert.ToInt64(reader.GetValue(0)));
        }
        finally
        {
            await using var cleanup = connection.CreateCommand();
            cleanup.CommandText = $"DROP TABLE IF EXISTS {tableName}";
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Calling BeginTransaction twice on the same connection without disposing the first
    /// must surface a clear error rather than silently nest.
    /// </summary>
    [Fact]
    public async Task Transaction_BeginTwice_Throws()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var tx = await connection.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.BeginTransactionAsync());
    }
}
