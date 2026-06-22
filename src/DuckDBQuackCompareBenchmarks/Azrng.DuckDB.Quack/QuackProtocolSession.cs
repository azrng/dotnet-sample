namespace Azrng.DuckDB.Quack;

/// <summary>
/// 表示一个 Quack 协议会话，包含会话标识和连接配置。
/// </summary>
/// <param name="SessionId">会话的唯一标识符。</param>
/// <param name="Config">该会话使用的协议配置。</param>
public sealed record QuackProtocolSession(string SessionId, QuackProtocolConfig Config);
