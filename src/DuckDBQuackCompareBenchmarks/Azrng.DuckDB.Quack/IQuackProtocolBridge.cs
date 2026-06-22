namespace Azrng.DuckDB.Quack;

/// <summary>
/// Quack 协议桥接口，用于与 DuckDB 原生协议层通信。
/// </summary>
public interface IQuackProtocolBridge : IDisposable
{
    /// <summary>
    /// 获取协议桥的版本信息，包括原生 ABI 版本、DuckDB 版本和 Quack 版本。
    /// </summary>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>包含版本信息的结果。</returns>
    Task<QuackProtocolBridgeVersion> GetVersionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 使用指定配置建立与 DuckDB 的协议会话。
    /// </summary>
    /// <param name="config">协议连接配置。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>新创建的协议会话。</returns>
    Task<QuackProtocolSession> ConnectAsync(QuackProtocolConfig config, CancellationToken cancellationToken);

    /// <summary>
    /// 在指定会话中执行 SQL 查询并返回结果。
    /// </summary>
    /// <param name="session">用于执行查询的协议会话。</param>
    /// <param name="sql">要执行的 SQL 语句。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>查询结果，包含列信息和数据行。</returns>
    Task<QuackQueryResult> ExecuteQueryAsync(
        QuackProtocolSession session,
        string sql,
        CancellationToken cancellationToken);

    /// <summary>
    /// 使用获取令牌从会话中拉取下一批查询结果数据。
    /// </summary>
    /// <param name="session">用于拉取数据的协议会话。</param>
    /// <param name="fetchToken">用于标识下一批数据的获取令牌。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>下一批查询结果，若无更多数据则返回 null。</returns>
    Task<QuackQueryResult?> FetchAsync(
        QuackProtocolSession session,
        string fetchToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// 关闭指定的协议会话并释放相关资源。
    /// </summary>
    /// <param name="session">要关闭的协议会话。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    Task CloseSessionAsync(QuackProtocolSession session, CancellationToken cancellationToken);
}
