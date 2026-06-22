namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// QuackProtocolMetrics 协议指标记录与查询的单元测试
/// </summary>
public class QuackProtocolMetricsTests
{
    /// <summary>
    /// 验证 RecordQuery 能正确递增总查询计数
    /// </summary>
    [Fact]
    public void RecordQuery_IncrementsTotalQueries()
    {
        var metrics = new QuackProtocolMetrics();

        metrics.RecordQuery(10.0, true);
        metrics.RecordQuery(20.0, false);

        Assert.Equal(2, metrics.TotalQueries);
    }

    /// <summary>
    /// 验证 RecordQuery 能正确区分并记录成功和失败的查询
    /// </summary>
    [Fact]
    public void RecordQuery_TracksSuccessfulAndFailed()
    {
        var metrics = new QuackProtocolMetrics();

        metrics.RecordQuery(10.0, true);
        metrics.RecordQuery(20.0, true);
        metrics.RecordQuery(30.0, false);

        Assert.Equal(2, metrics.SuccessfulQueries);
        Assert.Equal(1, metrics.FailedQueries);
    }

    /// <summary>
    /// 验证平均查询耗时计算正确
    /// </summary>
    [Fact]
    public void AverageQueryDurationMs_ReturnsCorrectValue()
    {
        var metrics = new QuackProtocolMetrics();

        metrics.RecordQuery(10.0, true);
        metrics.RecordQuery(20.0, true);
        metrics.RecordQuery(30.0, true);

        Assert.Equal(20.0, metrics.AverageQueryDurationMs, 2);
    }

    /// <summary>
    /// 验证无查询记录时平均耗时返回零
    /// </summary>
    [Fact]
    public void AverageQueryDurationMs_NoQueries_ReturnsZero()
    {
        var metrics = new QuackProtocolMetrics();

        Assert.Equal(0, metrics.AverageQueryDurationMs);
    }

    /// <summary>
    /// 验证错误率计算正确
    /// </summary>
    [Fact]
    public void ErrorRate_ReturnsCorrectValue()
    {
        var metrics = new QuackProtocolMetrics();

        metrics.RecordQuery(10.0, true);
        metrics.RecordQuery(20.0, true);
        metrics.RecordQuery(30.0, false);
        metrics.RecordQuery(40.0, false);

        Assert.Equal(0.5, metrics.ErrorRate, 2);
    }

    /// <summary>
    /// 验证无查询记录时错误率返回零
    /// </summary>
    [Fact]
    public void ErrorRate_NoQueries_ReturnsZero()
    {
        var metrics = new QuackProtocolMetrics();

        Assert.Equal(0, metrics.ErrorRate);
    }

    /// <summary>
    /// 验证连接创建和关闭计数正确追踪
    /// </summary>
    [Fact]
    public void ConnectionMetrics_TracksCreatedAndClosed()
    {
        var metrics = new QuackProtocolMetrics();

        metrics.RecordConnectionCreated();
        metrics.RecordConnectionCreated();
        metrics.RecordConnectionClosed();

        Assert.Equal(2, metrics.TotalConnections);
        Assert.Equal(1, metrics.ActiveConnections);
    }

    /// <summary>
    /// 验证 GetSnapshot 返回当前指标的正确快照
    /// </summary>
    [Fact]
    public void GetSnapshot_ReturnsCurrentState()
    {
        var metrics = new QuackProtocolMetrics();

        metrics.RecordQuery(10.0, true);
        metrics.RecordQuery(20.0, false);
        metrics.RecordConnectionCreated();

        var snapshot = metrics.GetSnapshot();

        Assert.Equal(2, snapshot.TotalQueries);
        Assert.Equal(1, snapshot.SuccessfulQueries);
        Assert.Equal(1, snapshot.FailedQueries);
        Assert.Equal(1, snapshot.TotalConnections);
        Assert.Equal(1, snapshot.ActiveConnections);
        Assert.Equal(0.5, snapshot.ErrorRate, 2);
    }

    /// <summary>
    /// 验证 Reset 能清除所有指标数据
    /// </summary>
    [Fact]
    public void Reset_ClearsAllMetrics()
    {
        var metrics = new QuackProtocolMetrics();

        metrics.RecordQuery(10.0, true);
        metrics.RecordConnectionCreated();
        metrics.Reset();

        Assert.Equal(0, metrics.TotalQueries);
        Assert.Equal(0, metrics.SuccessfulQueries);
        Assert.Equal(0, metrics.FailedQueries);
        Assert.Equal(0, metrics.TotalConnections);
        Assert.Equal(0, metrics.ActiveConnections);
    }

    /// <summary>
    /// 验证 P99 查询耗时百分位计算正确
    /// </summary>
    [Fact]
    public void P99QueryDurationMs_ReturnsCorrectValue()
    {
        var metrics = new QuackProtocolMetrics();

        // Add 100 queries with different durations
        for (int i = 1; i <= 100; i++)
        {
            metrics.RecordQuery(i, true);
        }

        // P99 should be around 99
        Assert.True(metrics.P99QueryDurationMs >= 90);
    }

    /// <summary>
    /// 验证无查询记录时 P99 耗时返回零
    /// </summary>
    [Fact]
    public void P99QueryDurationMs_NoQueries_ReturnsZero()
    {
        var metrics = new QuackProtocolMetrics();

        Assert.Equal(0, metrics.P99QueryDurationMs);
    }

    /// <summary>
    /// 验证 MetricsSnapshot 的 ToString 返回格式化的指标字符串
    /// </summary>
    [Fact]
    public void MetricsSnapshot_ToString_ReturnsFormattedString()
    {
        var snapshot = new MetricsSnapshot
        {
            TotalQueries = 100,
            SuccessfulQueries = 95,
            FailedQueries = 5,
            TotalConnections = 10,
            ActiveConnections = 3,
            AverageQueryDurationMs = 25.5,
            P99QueryDurationMs = 150.0,
            ErrorRate = 0.05
        };

        var result = snapshot.ToString();

        Assert.Contains("100", result);
        Assert.Contains("95", result);
        Assert.Contains("Failed: 5", result);
        Assert.Contains("3/10", result);
        Assert.Contains("25", result);
        Assert.Contains("150", result);
    }
}
