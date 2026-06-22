using System.Data;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// Edge-case coverage for ADO.NET provider contracts that are easy to regress: empty result
/// sets, NULL semantics across column types, Unicode fidelity, and over-long payloads.
/// </summary>
[Trait("Category", "Integration")]
[Collection(IntegrationTestCollection.Name)]
public sealed class EdgeCaseIntegrationTests
{
    private readonly TestOptions _options;

    public EdgeCaseIntegrationTests(TestOptions options)
    {
        _options = options;
    }

    private async Task<QuackDataReader> OpenReaderAsync(string sql)
    {
        var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return (QuackDataReader)await command.ExecuteReaderAsync();
    }

    /// <summary>
    /// A query that matches zero rows must still report its schema (FieldCount + column names)
    /// and return false from the first ReadAsync without throwing.
    /// </summary>
    [Fact]
    public async Task EmptyResultSet_ReportsSchemaAndNoRows()
    {
        await using var reader = await OpenReaderAsync("SELECT 1 AS a, 'x' AS b WHERE 1 = 0");

        Assert.Equal(2, reader.FieldCount);
        Assert.False(await reader.ReadAsync());
        // Schema is reported up front even with no rows.
        Assert.Equal("a", reader.GetName(0));
        Assert.Equal("b", reader.GetName(1));
    }

    /// <summary>
    /// Unicode text (CJK + emoji) must survive the UTF-8 wire round-trip byte-for-byte.
    /// </summary>
    [Fact]
    public async Task UnicodeString_RoundtripsIntact()
    {
        const string expected = "你好，世界！🌍 日本語 텍스트";

        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @s";
        var p = command.CreateParameter();
        p.ParameterName = "@s";
        p.Value = expected;
        command.Parameters.Add(p);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(expected, reader.GetString(0));
    }

    /// <summary>
    /// NULLs stored in typed columns must read back as DBNull via IsDBNull/GetValue across
    /// INTEGER, VARCHAR, and DOUBLE columns in the same row.
    /// </summary>
    [Fact]
    public async Task MixedNullColumns_AllReadAsDBNull()
    {
        var tableName = $"nulls_{Guid.NewGuid():N}".Substring(0, 30);

        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using (var create = connection.CreateCommand())
        {
            create.CommandText = $"CREATE TABLE {tableName} (i INTEGER, s VARCHAR, d DOUBLE)";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.CommandText = $"INSERT INTO {tableName} VALUES (NULL, NULL, NULL)";
                await insert.ExecuteNonQueryAsync();
            }

            await using var read = connection.CreateCommand();
            read.CommandText = $"SELECT i, s, d FROM {tableName}";
            await using var reader = await read.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());

            for (var i = 0; i < 3; i++)
            {
                Assert.True(reader.IsDBNull(i), $"column {i} should be NULL");
                Assert.Equal(DBNull.Value, reader.GetValue(i));
            }
        }
        finally
        {
            await using var cleanup = connection.CreateCommand();
            cleanup.CommandText = $"DROP TABLE IF EXISTS {tableName}";
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// A very long string (50k chars) must round-trip without truncation — guards against any
    /// fixed-size buffer assumption in the reader.
    /// </summary>
    [Fact]
    public async Task LongString_RoundtripsWithoutTruncation()
    {
        var expected = new string('Q', 50_000);

        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @s";
        var p = command.CreateParameter();
        p.ParameterName = "@s";
        p.Value = expected;
        command.Parameters.Add(p);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(expected.Length, reader.GetString(0).Length);
    }

    /// <summary>
    /// Committing or rolling back an already-finished transaction must surface a clear error
    /// rather than silently no-op — the ADO.NET transaction lifecycle contract.
    /// </summary>
    [Fact]
    public async Task Transaction_DoubleCommit_Throws()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        var tx = await connection.BeginTransactionAsync();
        await tx.CommitAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => tx.CommitAsync());
    }

    /// <summary>
    /// RecordsAffected must reflect the number of rows touched by a DML statement, not the
    /// hardcoded -1 placeholder.
    /// </summary>
    [Fact]
    public async Task ExecuteNonQuery_RecordsAffected_MatchesRowCount()
    {
        var tableName = $"aff_{Guid.NewGuid():N}".Substring(0, 30);

        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using (var create = connection.CreateCommand())
        {
            create.CommandText = $"CREATE TABLE {tableName} (id INTEGER)";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.CommandText = $"INSERT INTO {tableName} VALUES (1), (2), (3), (4), (5)";
                var affected = await insert.ExecuteNonQueryAsync();
                Assert.Equal(5, affected);
            }

            await using (var update = connection.CreateCommand())
            {
                update.CommandText = $"UPDATE {tableName} SET id = id + 100 WHERE id <= 2";
                var affected = await update.ExecuteNonQueryAsync();
                Assert.Equal(2, affected);
            }
        }
        finally
        {
            await using var cleanup = connection.CreateCommand();
            cleanup.CommandText = $"DROP TABLE IF EXISTS {tableName}";
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
