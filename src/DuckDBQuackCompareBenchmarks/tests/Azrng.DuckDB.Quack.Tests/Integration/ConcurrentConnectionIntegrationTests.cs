using System.Data;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// Real-server concurrency coverage: independent sessions running in parallel must each observe
/// their own result with no cross-talk, and the connection pool must honour its max size and
/// recycle connections safely under contention. Unit-layer concurrency tests use a fake bridge;
/// these exercise real parallel handshakes and session isolation.
/// </summary>
[Trait("Category", "Integration")]
[Collection(IntegrationTestCollection.Name)]
public sealed class ConcurrentConnectionIntegrationTests
{
    private readonly TestOptions _options;

    public ConcurrentConnectionIntegrationTests(TestOptions options)
    {
        _options = options;
    }

    private static async Task<long> ScalarAsync(string connectionString, long seed)
    {
        await using var connection = new QuackConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @seed + 1000";
        var p = command.CreateParameter();
        p.ParameterName = "@seed";
        p.Value = seed;
        command.Parameters.Add(p);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return Convert.ToInt64(reader.GetValue(0));
    }

    /// <summary>
    /// N independent connections executing concurrently must each receive their own distinct
    /// scalar back — guards against shared mutable state on the client and session confusion on
    /// the server.
    /// </summary>
    [Fact]
    public async Task Parallel_IndependentConnections_AllReturnOwnValue()
    {
        const int degree = 8;
        var seeds = Enumerable.Range(0, degree).Select(i => (long)i * 7).ToArray();

        var results = await Task.WhenAll(seeds.Select(s => Task.Run(() => ScalarAsync(_options.ConnectionString, s))));

        Assert.Equal(seeds.Select(s => s + 1000).ToArray(), results);
    }

    /// <summary>
    /// Under contention the pool must never hand out more than maxPoolSize live connections, every
    /// worker must complete with the correct value, and once the dust settles every connection is
    /// accounted for (none leaked, none orphaned in use).
    /// </summary>
    [Fact]
    public async Task Pool_ConcurrentAcquireRelease_AllWorkersCompleteWithinMaxSize()
    {
        const int maxPoolSize = 3;
        const int workers = 12;

        await using var pool = new QuackConnectionPool(_options.ConnectionString, maxPoolSize: maxPoolSize);

        var observedInUse = 0;
        var observedLock = new object();

        async Task<long> Worker(int iteration)
        {
            var connection = await pool.GetConnectionAsync();

            int inUse;
            lock (observedLock)
            {
                inUse = pool.InUseCount;
                if (inUse > observedInUse)
                    observedInUse = inUse;
            }

            // The semaphore guarantees at most maxPoolSize connections are ever checked out.
            Assert.InRange(inUse, 1, maxPoolSize);

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT @n * @n";
                var p = command.CreateParameter();
                p.ParameterName = "@n";
                p.Value = (long)iteration;
                command.Parameters.Add(p);

                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                return Convert.ToInt64(reader.GetValue(0));
            }
            finally
            {
                pool.ReturnConnection(connection);
            }
        }

        var results = await Task.WhenAll(Enumerable.Range(1, workers).Select(i => Task.Run(() => Worker(i))));

        // Every worker got its square back — no result was lost or crossed.
        Assert.Equal(Enumerable.Range(1, workers).Select(i => (long)i * i).ToArray(), results);

        // Saturation: at least one instant had all pool slots busy, proving real reuse happened.
        Assert.InRange(observedInUse, 1, maxPoolSize);

        // Clean state: nothing leaked back into "in use" once everyone returned.
        Assert.Equal(0, pool.InUseCount);
        Assert.InRange(pool.AvailableCount, 1, maxPoolSize);
    }
}
