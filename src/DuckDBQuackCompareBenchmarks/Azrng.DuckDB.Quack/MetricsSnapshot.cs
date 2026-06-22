namespace Azrng.DuckDB.Quack;

/// <summary>
/// 指标快照，记录某一时刻的指标状态。
/// </summary>
public sealed class MetricsSnapshot
{
    /// <summary>获取已执行的查询总数。</summary>
    public long TotalQueries { get; init; }

    /// <summary>获取成功的查询数。</summary>
    public long SuccessfulQueries { get; init; }

    /// <summary>获取失败的查询数。</summary>
    public long FailedQueries { get; init; }

    /// <summary>获取创建的连接总数。</summary>
    public long TotalConnections { get; init; }

    /// <summary>获取当前活跃的连接数。</summary>
    public long ActiveConnections { get; init; }

    /// <summary>获取平均查询耗时（毫秒）。</summary>
    public double AverageQueryDurationMs { get; init; }

    /// <summary>获取 P99 查询耗时（毫秒）。</summary>
    public double P99QueryDurationMs { get; init; }

    /// <summary>获取错误率（0.0 到 1.0）。</summary>
    public double ErrorRate { get; init; }

    /// <summary>返回指标快照的格式化字符串表示。</summary>
    /// <returns>包含所有指标摘要信息的字符串。</returns>
    public override string ToString()
    {
        return $"Queries: {TotalQueries} (Success: {SuccessfulQueries}, Failed: {FailedQueries}), " +
               $"Connections: {ActiveConnections}/{TotalConnections}, " +
               $"Avg Duration: {AverageQueryDurationMs:F2}ms, P99: {P99QueryDurationMs:F2}ms, " +
               $"Error Rate: {ErrorRate:P2}";
    }
}
