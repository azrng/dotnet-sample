using System.Data;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// Broader SQL feature coverage exercising richer result shapes than the type/edge suites:
/// aggregates, GROUP BY/HAVING, joins, subqueries, pattern matching, window functions, and
/// expressions. Each guards a distinct wire-decoding path (multi-row, computed columns, mixed
/// types in one result set).
/// </summary>
[Trait("Category", "Integration")]
[Collection(IntegrationTestCollection.Name)]
public sealed class SqlFeatureIntegrationTests
{
    private readonly TestOptions _options;

    public SqlFeatureIntegrationTests(TestOptions options)
    {
        _options = options;
    }

    private async Task<QuackConnection> OpenAsync()
    {
        var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static string TableName(string prefix) => $"{prefix}_{Guid.NewGuid():N}".Substring(0, 28);

    /// <summary>
    /// COUNT/SUM/AVG/MIN/MAX over a typed column must decode to the expected CLR scalars. SUM/AVG
    /// over DECIMAL widen to DECIMAL(38,2) (int128 storage) — this guards the 16-byte decimal path.
    /// </summary>
    [Fact]
    public async Task Aggregates_ComputeAcrossRows()
    {
        var table = TableName("agg");
        await using var connection = await OpenAsync();

        await using (var create = connection.CreateCommand())
        {
            create.CommandText = $"CREATE TABLE {table} (id INTEGER, amt DECIMAL(10,2))";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.CommandText = $"INSERT INTO {table} VALUES (1, 10.50), (2, 20.00), (3, 30.00)";
                await insert.ExecuteNonQueryAsync();
            }

            await using var query = connection.CreateCommand();
            query.CommandText = $"SELECT COUNT(*), SUM(amt), AVG(amt), MIN(amt), MAX(amt) FROM {table}";
            await using var reader = await query.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());

            Assert.Equal(3L, Convert.ToInt64(reader.GetValue(0)));
            Assert.Equal(60.50m, Convert.ToDecimal(reader.GetValue(1)));
            Assert.Equal(20.166667m, Math.Round(Convert.ToDecimal(reader.GetValue(2)), 6));
            Assert.Equal(10.50m, Convert.ToDecimal(reader.GetValue(3)));
            Assert.Equal(30.00m, Convert.ToDecimal(reader.GetValue(4)));
        }
        finally
        {
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"DROP TABLE IF EXISTS {table}";
            await drop.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// GROUP BY with a HAVING filter must return one aggregated row per surviving group, ordered.
    /// </summary>
    [Fact]
    public async Task GroupBy_WithHaving_FiltersGroups()
    {
        var table = TableName("grp");
        await using var connection = await OpenAsync();

        await using (var create = connection.CreateCommand())
        {
            create.CommandText = $"CREATE TABLE {table} (dept VARCHAR, amt INTEGER)";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.CommandText = $"INSERT INTO {table} VALUES ('a', 1), ('a', 2), ('b', 5), ('c', 1)";
                await insert.ExecuteNonQueryAsync();
            }

            await using var query = connection.CreateCommand();
            query.CommandText = $"SELECT dept, SUM(amt) AS total FROM {table} GROUP BY dept HAVING SUM(amt) > 1 ORDER BY dept";
            await using var reader = await query.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());
            Assert.Equal("a", reader.GetString(0));
            Assert.Equal(3L, Convert.ToInt64(reader.GetValue(1)));

            Assert.True(await reader.ReadAsync());
            Assert.Equal("b", reader.GetString(0));
            Assert.Equal(5L, Convert.ToInt64(reader.GetValue(1)));

            // Group 'c' sums to 1, filtered out by HAVING.
            Assert.False(await reader.ReadAsync());
        }
        finally
        {
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"DROP TABLE IF EXISTS {table}";
            await drop.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// An INNER JOIN must align rows across two tables on the join key, projecting columns from
    /// both in column order.
    /// </summary>
    [Fact]
    public async Task InnerJoin_AlignsRowsOnKey()
    {
        var orders = TableName("ord");
        var users = TableName("usr");
        await using var connection = await OpenAsync();

        await using (var ddl = connection.CreateCommand())
        {
            ddl.CommandText = $"CREATE TABLE {users} (uid INTEGER, name VARCHAR); " +
                              $"CREATE TABLE {orders} (oid INTEGER, uid INTEGER, amt INTEGER)";
            await ddl.ExecuteNonQueryAsync();
        }

        try
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.CommandText = $"INSERT INTO {users} VALUES (1, 'alice'), (2, 'bob'); " +
                                     $"INSERT INTO {orders} VALUES (10, 1, 100), (11, 1, 200), (12, 2, 50)";
                await insert.ExecuteNonQueryAsync();
            }

            await using var query = connection.CreateCommand();
            query.CommandText = $"SELECT u.name, o.amt FROM {orders} o JOIN {users} u ON o.uid = u.uid ORDER BY o.oid";
            await using var reader = await query.ExecuteReaderAsync();

            var rows = new List<(string, long)>();
            while (await reader.ReadAsync())
                rows.Add((reader.GetString(0), Convert.ToInt64(reader.GetValue(1))));

            Assert.Equal(new[] { ("alice", 100L), ("alice", 200L), ("bob", 50L) }, rows);
        }
        finally
        {
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"DROP TABLE IF EXISTS {orders}; DROP TABLE IF EXISTS {users}";
            await drop.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// A correlated scalar subquery in the SELECT list must resolve per row.
    /// </summary>
    [Fact]
    public async Task ScalarSubquery_ResolvesPerRow()
    {
        await using var connection = await OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT n, (SELECT MAX(m) FROM range(1, n + 1) t(m)) AS mx FROM range(1, 4) s(n) ORDER BY n";
        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<(long, long)>();
        while (await reader.ReadAsync())
            rows.Add((reader.GetInt64(0), reader.GetInt64(1)));

        Assert.Equal(new[] { (1L, 1L), (2L, 2L), (3L, 3L) }, rows);
    }

    /// <summary>
    /// LIKE pattern matching with wildcards and a CASE expression in the same projection.
    /// </summary>
    [Fact]
    public async Task LikeAndCase_ProjectConditionalValues()
    {
        var table = TableName("pat");
        await using var connection = await OpenAsync();

        await using (var create = connection.CreateCommand())
        {
            create.CommandText = $"CREATE TABLE {table} (s VARCHAR)";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.CommandText = $"INSERT INTO {table} VALUES ('apple'), ('apricot'), ('banana'), ('cherry')";
                await insert.ExecuteNonQueryAsync();
            }

            await using var query = connection.CreateCommand();
            query.CommandText = $"SELECT s, CASE WHEN s LIKE 'ap%' THEN 'A' ELSE 'other' END AS cls FROM {table} WHERE s LIKE '%a%' ORDER BY s";
            await using var reader = await query.ExecuteReaderAsync();

            var rows = new List<(string, string)>();
            while (await reader.ReadAsync())
                rows.Add((reader.GetString(0), reader.GetString(1)));

            Assert.Equal(new[] { ("apple", "A"), ("apricot", "A"), ("banana", "other") }, rows);
        }
        finally
        {
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"DROP TABLE IF EXISTS {table}";
            await drop.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// A window function (ROW_NUMBER OVER) must assign sequential ranks without collapsing rows.
    /// </summary>
    [Fact]
    public async Task WindowFunction_RanksRows()
    {
        var table = TableName("win");
        await using var connection = await OpenAsync();

        await using (var create = connection.CreateCommand())
        {
            create.CommandText = $"CREATE TABLE {table} (grp VARCHAR, score INTEGER)";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.CommandText = $"INSERT INTO {table} VALUES ('a', 30), ('a', 10), ('b', 50)";
                await insert.ExecuteNonQueryAsync();
            }

            await using var query = connection.CreateCommand();
            query.CommandText = $"SELECT grp, score, ROW_NUMBER() OVER (PARTITION BY grp ORDER BY score DESC) AS rn FROM {table} ORDER BY grp, rn";
            await using var reader = await query.ExecuteReaderAsync();

            var rows = new List<(string, long, long)>();
            while (await reader.ReadAsync())
                rows.Add((reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));

            Assert.Equal(new[] { ("a", 30L, 1L), ("a", 10L, 2L), ("b", 50L, 1L) }, rows);
        }
        finally
        {
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"DROP TABLE IF EXISTS {table}";
            await drop.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// DISTINCT collapses duplicate tuples; ORDER BY + LIMIT/OFFSET paginate the surviving set.
    /// </summary>
    [Fact]
    public async Task DistinctAndLimit_PaginateCorrectly()
    {
        await using var connection = await OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT i FROM range(0, 10) t(i) WHERE i % 2 = 0 ORDER BY i LIMIT 2 OFFSET 1";
        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<long>();
        while (await reader.ReadAsync())
            rows.Add(reader.GetInt64(0));

        // Evens 0,2,4,6,8; offset 1 limit 2 → 2, 4.
        Assert.Equal(new[] { 2L, 4L }, rows);
    }

    /// <summary>
    /// date/time and string functions exercise typed-function result decoding (year via
    /// date_part, dayname, length/upper).
    /// </summary>
    [Fact]
    public async Task BuiltInFunctions_DecodeTypedResults()
    {
        await using var connection = await OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT length(upper('hello')), date_part('year', DATE '2026-06-20'), dayname(DATE '2026-06-20')";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        Assert.Equal(5L, Convert.ToInt64(reader.GetValue(0)));
        Assert.Equal(2026L, Convert.ToInt64(reader.GetValue(1)));
        Assert.Equal("Saturday", reader.GetString(2));
    }
}
