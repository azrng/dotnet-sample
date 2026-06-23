using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Azrng.DuckDB.Quack;

/// <summary>
/// Connection pool for Quack protocol connections.
/// Reuses connections to reduce handshake overhead.
/// </summary>
public sealed class QuackConnectionPool : IAsyncDisposable, IDisposable
{
    private readonly ConcurrentBag<PooledConnection> _available = new();
    private readonly ConcurrentDictionary<int, PooledConnection> _inUseById = new();
    private ConditionalWeakTable<QuackConnection, PooledConnection> _inUseByConnection = new();
    private readonly string _connectionString;
    private readonly ILogger<QuackConnectionPool> _logger;
    private readonly int _maxPoolSize;
    private readonly TimeSpan _connectionLifetime;
    private readonly TimeSpan? _idleTimeout;
    private readonly SemaphoreSlim _semaphore;
    private int _nextId;
    private bool _disposed;

    /// <summary>
    /// 初始化 <see cref="QuackConnectionPool"/> 的新实例。
    /// </summary>
    /// <param name="connectionString">用于创建新连接的连接字符串。</param>
    /// <param name="logger">可选的日志记录器，为 null 时使用空日志实现。</param>
    /// <param name="maxPoolSize">连接池允许的最大连接数，默认为 10。</param>
    /// <param name="connectionLifetime">连接的最大生存时间，超过后将被丢弃，默认为 5 分钟。</param>
    /// <param name="idleTimeout">
    /// 连接的最大空闲时间（自上次使用起），超过后从池中淘汰，默认为 1 分钟。
    /// 传 <c>null</c> 可禁用空闲淘汰，仅按 <paramref name="connectionLifetime"/> 控制；适用于低 QPS 场景下希望连接长期复用的情形。
    /// </param>
    public QuackConnectionPool(
        string connectionString,
        ILogger<QuackConnectionPool>? logger = null,
        int maxPoolSize = 10,
        TimeSpan? connectionLifetime = null,
        TimeSpan? idleTimeout = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? NullLogger<QuackConnectionPool>.Instance;
        _maxPoolSize = maxPoolSize;
        _connectionLifetime = connectionLifetime ?? TimeSpan.FromMinutes(5);
        _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(1);
        _semaphore = new SemaphoreSlim(maxPoolSize, maxPoolSize);
    }

    /// <summary>
    /// Gets the number of available connections in the pool.
    /// </summary>
    public int AvailableCount => _available.Count;

    /// <summary>
    /// Gets the number of connections currently in use.
    /// </summary>
    public int InUseCount => _inUseById.Count;

    /// <summary>
    /// 预热连接池：提前建立至多 <paramref name="count"/> 个连接并归还到空闲队列，
    /// 把首次查询的握手开销（~2ms/连接）前移到启动阶段，避免冷启动抖动。
    /// </summary>
    /// <param name="count">预热的连接数量，自动钳制到不超过池的最大连接数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task WarmUpAsync(int count, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        count = Math.Min(Math.Max(count, 0), _maxPoolSize);

        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            ReturnConnection(connection);
        }
    }

    /// <summary>
    /// Gets a connection from the pool.
    /// </summary>
    public async Task<QuackConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            while (_available.TryTake(out var pooled))
            {
                if (IsValid(pooled) && await pooled.Connection.IsHealthyAsync(cancellationToken).ConfigureAwait(false))
                {
                    _inUseById[pooled.Id] = pooled;
                    _inUseByConnection.GetValue(pooled.Connection, _ => pooled);
                    _logger.LogDebug("Reusing connection {ConnectionId}", pooled.Id);
                    return pooled.Connection;
                }

                _logger.LogDebug("Disposing invalid connection {ConnectionId}", pooled.Id);
                pooled.Connection.Dispose();
            }

            var id = Interlocked.Increment(ref _nextId);
            var connection = new QuackConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var newPooled = new PooledConnection
            {
                Id = id,
                Connection = connection,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUsedAt = DateTimeOffset.UtcNow
            };

            _inUseById[id] = newPooled;
            _inUseByConnection.GetValue(connection, _ => newPooled);
            _logger.LogDebug("Created new connection {ConnectionId}", id);

            return connection;
        }
        catch
        {
            _semaphore.Release();
            throw;
        }
    }

    /// <summary>
    /// Gets a connection lease from the pool. Disposing the lease returns the connection automatically.
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async ValueTask<QuackConnectionLease> RentConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        return new QuackConnectionLease(this, connection);
    }

    /// <summary>
    /// Returns a connection to the pool.
    /// </summary>
    public void ReturnConnection(QuackConnection connection)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));

        if (_disposed)
        {
            connection.Dispose();
            return;
        }

        if (!_inUseByConnection.TryGetValue(connection, out var entry))
        {
            _logger.LogWarning("Connection not found in pool, disposing");
            connection.Dispose();
            // No matching acquire — leave the semaphore untouched so it can never overflow.
            return;
        }

        _inUseById.TryRemove(entry.Id, out _);
        _inUseByConnection.Remove(connection);

        if (IsValid(entry) && connection.State == System.Data.ConnectionState.Open)
        {
            entry.LastUsedAt = DateTimeOffset.UtcNow;
            _available.Add(entry);
            _logger.LogDebug("Returned connection {ConnectionId} to pool", entry.Id);
        }
        else
        {
            _logger.LogDebug("Disposing invalid connection {ConnectionId}", entry.Id);
            connection.Dispose();
        }

        _semaphore.Release();
    }

    /// <summary>
    /// Clears the pool and disposes all connections.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        while (_available.TryTake(out var pooled))
        {
            pooled.Connection.Dispose();
        }

        foreach (var entry in _inUseById.Values)
        {
            entry.Connection.Dispose();
        }

        _inUseById.Clear();
        _inUseByConnection = null!;
        _semaphore.Dispose();

        await ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private bool IsValid(PooledConnection pooled)
    {
        if (DateTimeOffset.UtcNow - pooled.CreatedAt > _connectionLifetime)
            return false;

        // 双重淘汰：连接绝对寿命（connectionLifetime）之上再叠加空闲淘汰（idleTimeout）。
        // idleTimeout 为 null 时仅按绝对寿命淘汰，便于低 QPS 场景长期复用连接。
        if (_idleTimeout is { } idle && DateTimeOffset.UtcNow - pooled.LastUsedAt > idle)
            return false;

        return true;
    }

    private sealed class PooledConnection
    {
        public int Id { get; init; }
        public required QuackConnection Connection { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset LastUsedAt { get; set; }
    }
}
