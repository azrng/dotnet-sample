using System.Collections.Concurrent;
using System.Diagnostics;

namespace Azrng.DuckDB.Quack;

/// <summary>
/// Metrics collector for Quack protocol operations.
/// Tracks query counts, timings, and errors.
/// </summary>
public sealed class QuackProtocolMetrics
{
    private long _totalQueries;
    private long _successfulQueries;
    private long _failedQueries;
    private long _totalConnections;
    private long _activeConnections;
    private readonly ConcurrentQueue<double> _queryDurations = new();
    private readonly object _syncRoot = new();

    /// <summary>
    /// Gets the total number of queries executed.
    /// </summary>
    public long TotalQueries => Interlocked.Read(ref _totalQueries);

    /// <summary>
    /// Gets the number of successful queries.
    /// </summary>
    public long SuccessfulQueries => Interlocked.Read(ref _successfulQueries);

    /// <summary>
    /// Gets the number of failed queries.
    /// </summary>
    public long FailedQueries => Interlocked.Read(ref _failedQueries);

    /// <summary>
    /// Gets the total number of connections created.
    /// </summary>
    public long TotalConnections => Interlocked.Read(ref _totalConnections);

    /// <summary>
    /// Gets the number of active connections.
    /// </summary>
    public long ActiveConnections => Interlocked.Read(ref _activeConnections);

    /// <summary>
    /// Gets the average query duration in milliseconds.
    /// </summary>
    public double AverageQueryDurationMs
    {
        get
        {
            var durations = _queryDurations.ToArray();
            return durations.Length > 0 ? durations.Average() : 0;
        }
    }

    /// <summary>
    /// Gets the P99 query duration in milliseconds.
    /// </summary>
    public double P99QueryDurationMs
    {
        get
        {
            var durations = _queryDurations.ToArray();
            if (durations.Length == 0) return 0;

            Array.Sort(durations);
            var index = (int)Math.Ceiling(durations.Length * 0.99) - 1;
            return durations[Math.Max(0, index)];
        }
    }

    /// <summary>
    /// Gets the error rate (0.0 to 1.0).
    /// </summary>
    public double ErrorRate
    {
        get
        {
            var total = TotalQueries;
            return total > 0 ? (double)FailedQueries / total : 0;
        }
    }

    /// <summary>
    /// Records a query execution.
    /// </summary>
    public void RecordQuery(double durationMs, bool success)
    {
        Interlocked.Increment(ref _totalQueries);

        if (success)
            Interlocked.Increment(ref _successfulQueries);
        else
            Interlocked.Increment(ref _failedQueries);

        _queryDurations.Enqueue(durationMs);

        // Keep only the last 1000 durations
        while (_queryDurations.Count > 1000)
        {
            _queryDurations.TryDequeue(out _);
        }
    }

    /// <summary>
    /// Records a new connection.
    /// </summary>
    public void RecordConnectionCreated()
    {
        Interlocked.Increment(ref _totalConnections);
        Interlocked.Increment(ref _activeConnections);
    }

    /// <summary>
    /// Records a connection closed.
    /// </summary>
    public void RecordConnectionClosed()
    {
        Interlocked.Decrement(ref _activeConnections);
    }

    /// <summary>
    /// Gets a snapshot of all metrics.
    /// </summary>
    public MetricsSnapshot GetSnapshot()
    {
        return new MetricsSnapshot
               {
                   TotalQueries = TotalQueries,
                   SuccessfulQueries = SuccessfulQueries,
                   FailedQueries = FailedQueries,
                   TotalConnections = TotalConnections,
                   ActiveConnections = ActiveConnections,
                   AverageQueryDurationMs = AverageQueryDurationMs,
                   P99QueryDurationMs = P99QueryDurationMs,
                   ErrorRate = ErrorRate
               };
    }

    /// <summary>
    /// Resets all metrics.
    /// </summary>
    public void Reset()
    {
        lock (_syncRoot)
        {
            Interlocked.Exchange(ref _totalQueries, 0);
            Interlocked.Exchange(ref _successfulQueries, 0);
            Interlocked.Exchange(ref _failedQueries, 0);
            Interlocked.Exchange(ref _totalConnections, 0);
            Interlocked.Exchange(ref _activeConnections, 0);
            while (_queryDurations.TryDequeue(out _)) { }
        }
    }
}
