using Dapper;

namespace Azrng.DuckDB.Quack.Tests;

[Trait("Category", "Integration")]
[Collection(IntegrationTestCollection.Name)]
public sealed class BatchOperationTests
{
    private readonly TestOptions _options;

    public BatchOperationTests(TestOptions options)
    {
        _options = options;
    }

    [Fact]
    public async Task ExecuteBatchInsert_InsertsMultipleRows()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        // Create table
        await using var createCmd = connection.CreateCommand();
        createCmd.CommandText = "CREATE TABLE IF NOT EXISTS test_batch (id INTEGER, name VARCHAR)";
        await createCmd.ExecuteNonQueryAsync();

        // Batch insert
        var columns = new[] { "id", "name" };
        var rows = new List<object?[]>
        {
            new object?[] { 1, "Alice" },
            new object?[] { 2, "Bob" },
            new object?[] { 3, "Charlie" }
        };

        var affected = await connection.ExecuteBatchInsertAsync("test_batch", columns, rows);
        Assert.True(affected >= 0);

        // Verify
        var count = (await connection.QueryAsync<long>("SELECT COUNT(*) FROM test_batch")).First();
        Assert.Equal(3, count);

        // Cleanup
        await using var cleanup = connection.CreateCommand();
        cleanup.CommandText = "DROP TABLE IF EXISTS test_batch";
        await cleanup.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task ExecuteBatchInsert_WithDifferentTypes_InsertsCorrectly()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        // Create table
        await using var createCmd = connection.CreateCommand();
        createCmd.CommandText = "CREATE TABLE IF NOT EXISTS test_batch_types (id INTEGER, name VARCHAR, active BOOLEAN, amount DOUBLE)";
        await createCmd.ExecuteNonQueryAsync();

        // Batch insert
        var columns = new[] { "id", "name", "active", "amount" };
        var rows = new List<object?[]>
        {
            new object?[] { 1, "Alice", true, 100.50 },
            new object?[] { 2, "Bob", false, 200.75 }
        };

        var affected = await connection.ExecuteBatchInsertAsync("test_batch_types", columns, rows);
        Assert.True(affected >= 0);

        // Cleanup
        await using var cleanup = connection.CreateCommand();
        cleanup.CommandText = "DROP TABLE IF EXISTS test_batch_types";
        await cleanup.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task ExecuteBatchInsert_EmptyRows_ReturnsZero()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        var columns = new[] { "id", "name" };
        var rows = new List<object?[]>();

        var affected = await connection.ExecuteBatchInsertAsync("test_batch", columns, rows);
        Assert.Equal(0, affected);
    }

    [Fact]
    public async Task ExecuteBatchInsert_WithNullValues_InsertsCorrectly()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        // Create table
        await using var createCmd = connection.CreateCommand();
        createCmd.CommandText = "CREATE TABLE IF NOT EXISTS test_batch_null (id INTEGER, name VARCHAR)";
        await createCmd.ExecuteNonQueryAsync();

        // Batch insert with null
        var columns = new[] { "id", "name" };
        var rows = new List<object?[]>
        {
            new object?[] { 1, null },
            new object?[] { 2, "Bob" }
        };

        var affected = await connection.ExecuteBatchInsertAsync("test_batch_null", columns, rows);
        Assert.True(affected >= 0);

        // Cleanup
        await using var cleanup = connection.CreateCommand();
        cleanup.CommandText = "DROP TABLE IF EXISTS test_batch_null";
        await cleanup.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task ExecuteParameterizedBatchInsert_LargeBatch_InsertsAll()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        // Create table
        await using var createCmd = connection.CreateCommand();
        createCmd.CommandText = "CREATE TABLE IF NOT EXISTS test_batch_large (id INTEGER, name VARCHAR)";
        await createCmd.ExecuteNonQueryAsync();

        // Generate large batch
        var columns = new[] { "id", "name" };
        var rows = new List<object?[]>();
        for (int i = 0; i < 250; i++)
        {
            rows.Add(new object?[] { i, $"item_{i}" });
        }

        var affected = await connection.ExecuteParameterizedBatchInsertAsync("test_batch_large", columns, rows, batchSize: 100);
        Assert.True(affected >= 0);

        // Verify
        var count = (await connection.QueryAsync<long>("SELECT COUNT(*) FROM test_batch_large")).First();
        Assert.Equal(250, count);

        // Cleanup
        await using var cleanup = connection.CreateCommand();
        cleanup.CommandText = "DROP TABLE IF EXISTS test_batch_large";
        await cleanup.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task ExecuteBatchInsert_ClosedConnection_Throws()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);

        var columns = new[] { "id" };
        var rows = new List<object?[]> { new object?[] { 1 } };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => connection.ExecuteBatchInsertAsync("test", columns, rows));
    }
}
