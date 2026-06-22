namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// QuackConnectionPoolConcurrencyTests 的单元测试
/// </summary>
public class QuackConnectionPoolConcurrencyTests
{
    /// <summary>
    /// Pool DisposeBeforeUse DoesNotThrow
    /// </summary>
    [Fact]
    public void Pool_DisposeBeforeUse_DoesNotThrow()
    {
        var pool = new QuackConnectionPool("Host=quack.example;Token=abc");
        pool.Dispose();
    }

    /// <summary>
    /// Pool DisposeTwice 是否Idempotent
    /// </summary>
    [Fact]
    public async Task Pool_DisposeTwice_IsIdempotent()
    {
        var pool = new QuackConnectionPool("Host=quack.example;Token=abc");
        await pool.DisposeAsync();
        await pool.DisposeAsync();
    }

    /// <summary>
    /// Pool GetAfterDispose 抛出
    /// </summary>
    [Fact]
    public async Task Pool_GetAfterDispose_Throws()
    {
        var pool = new QuackConnectionPool("Host=quack.example;Token=abc");
        await pool.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pool.GetConnectionAsync());
    }

    /// <summary>
    /// Pool RentAfterDispose 抛出
    /// </summary>
    [Fact]
    public async Task Pool_RentAfterDispose_Throws()
    {
        var pool = new QuackConnectionPool("Host=quack.example;Token=abc");
        await pool.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await pool.RentConnectionAsync());
    }

    /// <summary>
    /// ReturnConnection NullConnection 抛出
    /// </summary>
    [Fact]
    public void ReturnConnection_NullConnection_Throws()
    {
        using var pool = new QuackConnectionPool("Host=quack.example;Token=abc");

        Assert.Throws<ArgumentNullException>(() => pool.ReturnConnection(null!));
    }

    /// <summary>
    /// Pool NullConnectionString 抛出
    /// </summary>
    [Fact]
    public void Pool_NullConnectionString_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new QuackConnectionPool(null!));
    }

    /// <summary>
    /// Pool AvailableCountAndInUseCount StartAtZero
    /// </summary>
    [Fact]
    public void Pool_AvailableCountAndInUseCount_StartAtZero()
    {
        using var pool = new QuackConnectionPool("Host=quack.example;Token=abc");

        Assert.Equal(0, pool.AvailableCount);
        Assert.Equal(0, pool.InUseCount);
    }

    /// <summary>
    /// ReturnConnection UnknownConnection DisposesAndReleasesSemaphore
    /// </summary>
    [Fact]
    public async Task ReturnConnection_UnknownConnection_DisposesAndReleasesSemaphore()
    {
        await using var pool = new QuackConnectionPool("Host=quack.example;Token=abc", maxPoolSize: 2);

        // Hand-rolled QuackConnection that wasn't produced by GetConnectionAsync.
        var stray = new QuackConnection("Host=quack.example;Token=abc");
        pool.ReturnConnection(stray);

        // Semaphore must still be releasable into — pool did not lose a slot to an unknown connection.
        Assert.Equal(0, pool.InUseCount);
        Assert.Equal(0, pool.AvailableCount);
    }

    /// <summary>
    /// ReturnConnection AfterDispose DoesNotThrow
    /// </summary>
    [Fact]
    public async Task ReturnConnection_AfterDispose_DoesNotThrow()
    {
        var pool = new QuackConnectionPool("Host=quack.example;Token=abc");
        await pool.DisposeAsync();

        var stray = new QuackConnection("Host=quack.example;Token=abc");
        pool.ReturnConnection(stray);

        Assert.Equal(System.Data.ConnectionState.Closed, stray.State);
    }
}
