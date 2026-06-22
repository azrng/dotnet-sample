using Microsoft.Extensions.Logging;

namespace Azrng.DuckDB.Quack.Tests;

[Trait("Category", "Integration")]
[Collection(IntegrationTestCollection.Name)]
public sealed class QuackConnectionPoolTests
{
    private readonly TestOptions _options;

    public QuackConnectionPoolTests(TestOptions options)
    {
        _options = options;
    }

    [Fact]
    public async Task GetConnectionAsync_ReturnsOpenConnection()
    {
        await using var pool = new QuackConnectionPool(_options.ConnectionString, maxPoolSize: 10);

        var connection = await pool.GetConnectionAsync();
        Assert.Equal(System.Data.ConnectionState.Open, connection.State);

        pool.ReturnConnection(connection);
    }

    [Fact]
    public async Task GetConnectionAsync_ReusesReturnedConnection()
    {
        await using var pool = new QuackConnectionPool(_options.ConnectionString, maxPoolSize: 10);

        var connection1 = await pool.GetConnectionAsync();
        pool.ReturnConnection(connection1);

        var connection2 = await pool.GetConnectionAsync();
        Assert.Same(connection1, connection2);

        pool.ReturnConnection(connection2);
    }

    [Fact]
    public async Task RentConnectionAsync_ReturnsConnectionWhenLeaseDisposed()
    {
        await using var pool = new QuackConnectionPool(_options.ConnectionString, maxPoolSize: 10);

        QuackConnection connection;
        await using (var lease = await pool.RentConnectionAsync())
        {
            connection = lease.Connection;
            Assert.Equal(System.Data.ConnectionState.Open, connection.State);
            Assert.Equal(1, pool.InUseCount);
        }

        Assert.Equal(0, pool.InUseCount);
        Assert.Equal(1, pool.AvailableCount);

        var reused = await pool.GetConnectionAsync();
        Assert.Same(connection, reused);

        pool.ReturnConnection(reused);
    }

    [Fact]
    public async Task RentConnectionAsync_DisposeTwice_ReturnsOnce()
    {
        await using var pool = new QuackConnectionPool(_options.ConnectionString, maxPoolSize: 10);

        var lease = await pool.RentConnectionAsync();
        lease.Dispose();
        lease.Dispose();
        await lease.DisposeAsync();

        Assert.Equal(0, pool.InUseCount);
        Assert.Equal(1, pool.AvailableCount);
    }

    [Fact]
    public async Task GetConnectionAsync_CreatesNewWhenNoneAvailable()
    {
        await using var pool = new QuackConnectionPool(_options.ConnectionString, maxPoolSize: 10);

        var connection1 = await pool.GetConnectionAsync();
        var connection2 = await pool.GetConnectionAsync();

        Assert.NotSame(connection1, connection2);

        pool.ReturnConnection(connection1);
        pool.ReturnConnection(connection2);
    }

    [Fact]
    public async Task GetConnectionAsync_RespectsMaxPoolSize()
    {
        await using var pool = new QuackConnectionPool(_options.ConnectionString, maxPoolSize: 2);

        var connection1 = await pool.GetConnectionAsync();
        var connection2 = await pool.GetConnectionAsync();

        // Pool is full, third connection should wait
        var task = pool.GetConnectionAsync();
        Assert.False(task.IsCompleted);

        // Return one connection to allow the third to proceed
        pool.ReturnConnection(connection1);
        var connection3 = await task;

        // connection3 should be valid and open
        Assert.Equal(System.Data.ConnectionState.Open, connection3.State);

        pool.ReturnConnection(connection2);
        pool.ReturnConnection(connection3);
    }

    [Fact]
    public async Task ReturnConnection_DisposesInvalidConnection()
    {
        await using var pool = new QuackConnectionPool(_options.ConnectionString);

        var connection = await pool.GetConnectionAsync();
        await connection.CloseAsync();

        pool.ReturnConnection(connection);

        Assert.Equal(0, pool.AvailableCount);
    }

    [Fact]
    public async Task DisposeAsync_DisposesAllConnections()
    {
        var pool = new QuackConnectionPool(_options.ConnectionString);

        var connection1 = await pool.GetConnectionAsync();
        var connection2 = await pool.GetConnectionAsync();

        await pool.DisposeAsync();

        Assert.Equal(System.Data.ConnectionState.Closed, connection1.State);
        Assert.Equal(System.Data.ConnectionState.Closed, connection2.State);
    }
}
