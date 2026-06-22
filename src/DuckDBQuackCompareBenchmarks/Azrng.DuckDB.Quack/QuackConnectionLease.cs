namespace Azrng.DuckDB.Quack;

/// <summary>
/// A pooled Quack connection lease. Dispose the lease to return the connection to its pool.
/// </summary>
public sealed class QuackConnectionLease : IDisposable, IAsyncDisposable
{
    private QuackConnectionPool? _pool;

    internal QuackConnectionLease(QuackConnectionPool pool, QuackConnection connection)
    {
        _pool = pool;
        Connection = connection;
    }

    /// <summary>
    /// Gets the leased connection.
    /// </summary>
    public QuackConnection Connection { get; }

    /// <summary>
    /// Returns the leased connection to its pool.
    /// </summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _pool, null)?.ReturnConnection(Connection);
    }

    /// <summary>
    /// Returns the leased connection to its pool.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Allows passing a lease to APIs that expect a <see cref="QuackConnection"/>.
    /// </summary>
    public static implicit operator QuackConnection(QuackConnectionLease lease)
    {
        return lease.Connection;
    }
}
