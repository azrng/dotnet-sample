using Dapper;

namespace Azrng.DuckDB.Quack.Tests;

[Trait("Category", "Integration")]
[Collection(IntegrationTestCollection.Name)]
public sealed class DdlDmlTests
{
    private readonly TestOptions _options;

    public DdlDmlTests(TestOptions options)
    {
        _options = options;
    }

    [Fact]
    public async Task CreateTable_CreatesTable()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS test_create_table (id INTEGER, name VARCHAR)";
        var affected = await command.ExecuteNonQueryAsync();

        Assert.True(affected >= 0);

        // Cleanup
        await using var cleanup = connection.CreateCommand();
        cleanup.CommandText = "DROP TABLE IF EXISTS test_create_table";
        await cleanup.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Insert_InsertsRows()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        // Create table
        await using var createCmd = connection.CreateCommand();
        createCmd.CommandText = "CREATE TABLE IF NOT EXISTS test_insert (id INTEGER, name VARCHAR)";
        await createCmd.ExecuteNonQueryAsync();

        // Insert
        await using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO test_insert VALUES (1, 'Alice'), (2, 'Bob')";
        var affected = await insertCmd.ExecuteNonQueryAsync();

        Assert.True(affected >= 0);

        // Verify
        var rows = (await connection.QueryAsync<dynamic>("SELECT COUNT(*) AS cnt FROM test_insert")).ToList();
        Assert.Equal(2L, rows[0].cnt);

        // Cleanup
        await using var cleanup = connection.CreateCommand();
        cleanup.CommandText = "DROP TABLE IF EXISTS test_insert";
        await cleanup.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Update_UpdatesRows()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        // Create and insert
        await using var createCmd = connection.CreateCommand();
        createCmd.CommandText = "CREATE TABLE IF NOT EXISTS test_update (id INTEGER, name VARCHAR)";
        await createCmd.ExecuteNonQueryAsync();

        await using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO test_update VALUES (1, 'Alice'), (2, 'Bob')";
        await insertCmd.ExecuteNonQueryAsync();

        // Update
        await using var updateCmd = connection.CreateCommand();
        updateCmd.CommandText = "UPDATE test_update SET name = 'Charlie' WHERE id = 1";
        var affected = await updateCmd.ExecuteNonQueryAsync();

        Assert.True(affected >= 0);

        // Verify
        var rows = (await connection.QueryAsync<dynamic>("SELECT name FROM test_update WHERE id = 1")).ToList();
        Assert.Equal("Charlie", (string)rows[0].name);

        // Cleanup
        await using var cleanup = connection.CreateCommand();
        cleanup.CommandText = "DROP TABLE IF EXISTS test_update";
        await cleanup.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Delete_DeletesRows()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        // Create and insert
        await using var createCmd = connection.CreateCommand();
        createCmd.CommandText = "CREATE TABLE IF NOT EXISTS test_delete (id INTEGER, name VARCHAR)";
        await createCmd.ExecuteNonQueryAsync();

        await using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO test_delete VALUES (1, 'Alice'), (2, 'Bob')";
        await insertCmd.ExecuteNonQueryAsync();

        // Delete
        await using var deleteCmd = connection.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM test_delete WHERE id = 1";
        var affected = await deleteCmd.ExecuteNonQueryAsync();

        Assert.True(affected >= 0);

        // Verify
        var rows = (await connection.QueryAsync<dynamic>("SELECT COUNT(*) AS cnt FROM test_delete")).ToList();
        Assert.Equal(1L, rows[0].cnt);

        // Cleanup
        await using var cleanup = connection.CreateCommand();
        cleanup.CommandText = "DROP TABLE IF EXISTS test_delete";
        await cleanup.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task DropTable_DropsTable()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        // Create
        await using var createCmd = connection.CreateCommand();
        createCmd.CommandText = "CREATE TABLE IF NOT EXISTS test_drop (id INTEGER)";
        await createCmd.ExecuteNonQueryAsync();

        // Drop
        await using var dropCmd = connection.CreateCommand();
        dropCmd.CommandText = "DROP TABLE IF EXISTS test_drop";
        var affected = await dropCmd.ExecuteNonQueryAsync();

        Assert.True(affected >= 0);
    }

    [Fact]
    public async Task ExecuteNonQuery_WithParameters_Works()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        // Create table
        await using var createCmd = connection.CreateCommand();
        createCmd.CommandText = "CREATE TABLE IF NOT EXISTS test_params (id INTEGER, name VARCHAR)";
        await createCmd.ExecuteNonQueryAsync();

        // Insert with parameters
        await using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO test_params VALUES (@id, @name)";
        var param1 = insertCmd.CreateParameter();
        param1.ParameterName = "@id";
        param1.Value = 1;
        insertCmd.Parameters.Add(param1);

        var param2 = insertCmd.CreateParameter();
        param2.ParameterName = "@name";
        param2.Value = "Test";
        insertCmd.Parameters.Add(param2);

        var affected = await insertCmd.ExecuteNonQueryAsync();
        Assert.True(affected >= 0);

        // Cleanup
        await using var cleanup = connection.CreateCommand();
        cleanup.CommandText = "DROP TABLE IF EXISTS test_params";
        await cleanup.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task ExecuteNonQuery_ClosedConnection_Throws()
    {
        var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();
        await connection.CloseAsync();

        // Reopen and create command, then close
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE test_fail (id INTEGER)";
        connection.Close();

        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteNonQueryAsync());
    }
}
