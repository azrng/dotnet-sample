namespace Azrng.DuckDB.Quack;

/// <summary>
/// Quack private protocol connection settings.
/// </summary>
public sealed record QuackProtocolConfig
{
    /// <summary>
    /// 默认端口号。
    /// </summary>
    public const int DefaultPort = 9494;
    private const string EndpointPath = "/quack";

    /// <summary>
    /// 主机地址。
    /// </summary>
    public string Host { get; init; } = "";

    /// <summary>
    /// 端口号。
    /// </summary>
    public int Port { get; init; } = DefaultPort;

    /// <summary>
    /// 认证令牌。
    /// </summary>
    public string Token { get; init; } = "";

    /// <summary>
    /// 数据目录名称，可选。
    /// </summary>
    public string? Catalog { get; init; }

    /// <summary>
    /// 是否禁用 SSL 加密连接。
    /// </summary>
    public bool DisableSsl { get; init; } = false;

    /// <summary>
    /// 连接超时时间（秒）。
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// 根据配置构建完整的服务端点 URI。
    /// </summary>
    public Uri Endpoint => new UriBuilder(DisableSsl ? "http" : "https", Host, Port, EndpointPath).Uri;

    /// <summary>
    /// 从连接字符串解析并创建配置实例。
    /// </summary>
    /// <param name="connectionString">连接字符串。</param>
    /// <returns>解析后的配置实例。</returns>
    public static QuackProtocolConfig FromConnectionString(string connectionString)
    {
        return QuackProtocolConnectionStringParser.Parse(connectionString);
    }

    /// <summary>
    /// 验证当前配置是否合法，不合法时抛出异常。
    /// </summary>
    public void Validate()
    {
        QuackProtocolConnectionStringParser.ValidateHost(Host);
        QuackProtocolConnectionStringParser.ValidatePort(Port);
        QuackProtocolConnectionStringParser.ValidateToken(Token);

        if (TimeoutSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds), TimeoutSeconds, "TimeoutSeconds cannot be negative.");
    }

    /// <summary>
    /// 返回配置的字符串表示，令牌部分会被脱敏处理。
    /// </summary>
    /// <returns>脱敏后的配置字符串。</returns>
    public override string ToString()
    {
        var maskedToken = Token.Length <= 4 ? "****" : $"{Token[..2]}****{Token[^2..]}";
        return $"Host={Host};Port={Port};Token={maskedToken};DisableSsl={DisableSsl};TimeoutSeconds={TimeoutSeconds}";
    }
}
