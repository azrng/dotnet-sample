using System.Data;
using Dapper;

namespace Azrng.DuckDB.Quack.Tests;

[Trait("Category", "Integration")]
[Collection(IntegrationTestCollection.Name)]
public sealed class TransactionTests
{
    private readonly TestOptions _options;

    public TransactionTests(TestOptions options)
    {
        _options = options;
    }

    [Fact]
    public async Task BeginTransaction_CreatesTransaction()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var transaction = await connection.BeginTransactionAsync();

        Assert.NotNull(transaction);
        Assert.Equal(IsolationLevel.ReadCommitted, transaction.IsolationLevel);
    }

    [Fact]
    public async Task BeginTransaction_WithIsolationLevel_SetsLevel()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);

        Assert.Equal(IsolationLevel.Serializable, transaction.IsolationLevel);
    }

    [Fact]
    public async Task Commit_CommitsChanges()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        // Create table
        await using var createCmd = connection.CreateCommand();
        createCmd.CommandText = "CREATE TABLE IF NOT EXISTS test_tx_commit (id INTEGER, name VARCHAR)";
        await createCmd.ExecuteNonQueryAsync();

        // Begin transaction and insert
        await using var transaction = await connection.BeginTransactionAsync();
        await using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO test_tx_commit VALUES (1, 'Alice')";
        await insertCmd.ExecuteNonQueryAsync();

        // Commit
        await transaction.CommitAsync();

        // Verify
        var rows = (await connection.QueryAsync<dynamic>("SELECT COUNT(*) AS cnt FROM test_tx_commit")).ToList();
        Assert.Equal(1L, rows[0].cnt);

        // Cleanup
        await using var cleanup = connection.CreateCommand();
        cleanup.CommandText = "DROP TABLE IF EXISTS test_tx_commit";
        await cleanup.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Rollback_RollsBackChanges()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        // Create table
        await using var createCmd = connection.CreateCommand();
        createCmd.CommandText = "CREATE TABLE IF NOT EXISTS test_tx_rollback (id INTEGER, name VARCHAR)";
        await createCmd.ExecuteNonQueryAsync();

        // Begin transaction and insert
        await using var transaction = await connection.BeginTransactionAsync();
        await using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO test_tx_rollback VALUES (1, 'Alice')";
        await insertCmd.ExecuteNonQueryAsync();

        // Rollback
        await transaction.RollbackAsync();

        // Verify - should be empty
        var rows = (await connection.QueryAsync<dynamic>("SELECT COUNT(*) AS cnt FROM test_tx_rollback")).ToList();
        Assert.Equal(0L, rows[0].cnt);

        // Cleanup
        await using var cleanup = connection.CreateCommand();
        cleanup.CommandText = "DROP TABLE IF EXISTS test_tx_rollback";
        await cleanup.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task CommitThenRollback_ThrowsInvalidOperation()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        var transaction = await connection.BeginTransactionAsync();
        await transaction.CommitAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.RollbackAsync());
    }

    [Fact]
    public async Task BeginTransaction_WhenAlreadyInTransaction_Throws()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var transaction = await connection.BeginTransactionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.BeginTransactionAsync());
    }

    [Fact]
    public async Task BeginTransaction_WhenClosed_Throws()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.BeginTransactionAsync());
    }

    [Fact]
    public async Task Commit_AfterRollback_Throws()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var transaction = await connection.BeginTransactionAsync();
        await transaction.RollbackAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync());
    }
}
