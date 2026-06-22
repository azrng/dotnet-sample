namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// 用于单元测试的假协议桥实现。
/// 模拟 IQuackProtocolBridge 接口的行为，便于测试。
/// </summary>
internal sealed class FakeQuackProtocolBridge : IQuackProtocolBridge
{
    /// <summary>获取或设置要返回的查询结果队列。</summary>
    public Queue<QuackQueryResult> Results { get; } = new();
    /// <summary>获取或设置要返回的 Fetch 结果队列。</summary>
    public Queue<QuackQueryResult?> FetchResults { get; } = new();
    /// <summary>获取连接调用的次数。</summary>
    public int ConnectCount { get; private set; }
    /// <summary>获取关闭会话调用的次数。</summary>
    public int CloseCount { get; private set; }
    /// <summary>获取 Fetch 调用的次数。</summary>
    public int FetchCount { get; private set; }
    /// <summary>获取一个值，指示是否已调用 Dispose 方法。</summary>
    public bool Disposed { get; private set; }
    /// <summary>获取最后一次执行的 SQL 语句。</summary>
    public string? LastSql { get; private set; }
    /// <summary>获取或设置执行查询时的模拟延迟。</summary>
    public TimeSpan ExecuteDelay { get; set; }
    /// <summary>获取或设置执行门控，用于控制查询执行的时机。</summary>
    public TaskCompletionSource? ExecuteGate { get; set; }
    /// <summary>获取执行已进入的信号源。</summary>
    public TaskCompletionSource? ExecuteEntered { get; private set; }
    /// <summary>获取或设置协议桥版本信息。</summary>
    public QuackProtocolBridgeVersion Version { get; set; } = new(
        QuackProtocolVersions.NativeAbiVersion,
        QuackProtocolVersions.DuckDbVersion,
        QuackProtocolVersions.QuackVersion);

    /// <summary>
    /// 获取协议桥版本信息。
    /// </summary>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>版本信息。</returns>
    public Task<QuackProtocolBridgeVersion> GetVersionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Version);
    }

    /// <summary>
    /// 建立协议会话（模拟实现）。
    /// </summary>
    /// <param name="config">协议连接配置。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>新创建的协议会话。</returns>
    public Task<QuackProtocolSession> ConnectAsync(QuackProtocolConfig config, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConnectCount++;
        return Task.FromResult(new QuackProtocolSession("1", config));
    }

    /// <summary>
    /// 执行 SQL 查询（模拟实现）。
    /// </summary>
    /// <param name="session">协议会话。</param>
    /// <param name="sql">要执行的 SQL 语句。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>查询结果。</returns>
    public async Task<QuackQueryResult> ExecuteQueryAsync(
        QuackProtocolSession session,
        string sql,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastSql = sql;

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ExecuteEntered = entered;
        entered.SetResult();

        if (ExecuteGate is { } gate)
            await gate.Task.WaitAsync(cancellationToken);

        if (ExecuteDelay > TimeSpan.Zero)
            await Task.Delay(ExecuteDelay, cancellationToken);

        if (Results.Count == 0)
            throw new InvalidOperationException("No fake result queued.");

        return Results.Dequeue();
    }

    /// <summary>
    /// 获取下一批查询结果（模拟实现）。
    /// </summary>
    /// <param name="session">协议会话。</param>
    /// <param name="queryId">查询标识。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>下一批查询结果，若无更多数据则返回 null。</returns>
    public Task<QuackQueryResult?> FetchAsync(
        QuackProtocolSession session,
        string queryId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FetchCount++;
        return Task.FromResult(FetchResults.Count == 0 ? null : FetchResults.Dequeue());
    }

    /// <summary>
    /// 关闭协议会话（模拟实现）。
    /// </summary>
    /// <param name="session">要关闭的协议会话。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public Task CloseSessionAsync(QuackProtocolSession session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CloseCount++;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 释放资源（模拟实现）。
    /// </summary>
    public void Dispose()
    {
        Disposed = true;
    }
}
