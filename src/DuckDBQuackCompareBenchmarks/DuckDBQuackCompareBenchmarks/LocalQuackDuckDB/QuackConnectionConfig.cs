namespace Quack.DuckDB;

/// <summary>
/// Quack 远程连接配置
/// </summary>
public sealed record QuackConnectionConfig
{
    /// <summary>远程 DuckDB 主机地址</summary>
    public string Host { get; init; } = "";

    /// <summary>Quack 端口</summary>
    public int Port { get; init; }

    /// <summary>认证 Token</summary>
    public string Token { get; init; } = "";

    /// <summary>远端服务端 catalog 名称；Attach 模式下优先作为本地挂载名</summary>
    public string Catalog { get; init; } = "remote";

    /// <summary>是否使用 ATTACH 连接模式；默认 false，使用 quack_query 直连查询</summary>
    public bool Attach { get; init; }

    /// <summary>是否禁用 SSL</summary>
    public bool DisableSsl { get; init; } = true;

    /// <summary>
    /// 从连接字符串创建配置
    /// </summary>
    public static QuackConnectionConfig FromConnectionString(string connectionString)
    {
        return QuackConnectionStringParser.Parse(connectionString);
    }

    /// <summary>
    /// 校验连接配置中的外部输入，避免无效配置进入连接和 SQL 拼接流程
    /// </summary>
    public void Validate()
    {
        QuackConnectionStringParser.ValidateHost(Host);
        QuackConnectionStringParser.ValidatePort(Port);
        QuackConnectionStringParser.ValidateToken(Token);
        QuackConnectionStringParser.ValidateIdentifier(Catalog);
    }
}
