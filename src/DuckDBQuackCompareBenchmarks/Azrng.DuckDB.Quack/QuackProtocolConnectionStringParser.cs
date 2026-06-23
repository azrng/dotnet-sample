namespace Azrng.DuckDB.Quack;

/// <summary>
/// Quack 协议连接字符串解析器，支持 URI 格式和键值对格式的连接字符串解析与验证。
/// </summary>
public static class QuackProtocolConnectionStringParser
{
    /// <summary>
    /// 解析连接字符串并返回 Quack 协议配置对象。支持 URI 格式（如 quack://host:port）和键值对格式（如 Host=xxx;Port=xxx）。
    /// </summary>
    /// <param name="connectionString">连接字符串，不能为空或仅包含空白字符。</param>
    /// <returns>解析后的 <see cref="QuackProtocolConfig"/> 配置对象。</returns>
    public static QuackProtocolConfig Parse(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be empty.", nameof(connectionString));

        var trimmed = connectionString.Trim();
        return IsQuackUri(trimmed) ? ParseUri(trimmed) : ParseKeyValue(trimmed);
    }

    /// <summary>
    /// 验证主机名是否合法。主机名不能为空，长度不能超过 255 个字符，且不能包含空白字符。
    /// </summary>
    /// <param name="host">待验证的主机名。</param>
    /// <returns>去除首尾空白后的有效主机名。</returns>
    public static string ValidateHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host cannot be empty.", nameof(host));

        var trimmed = host.Trim();
        if (trimmed.Length > 255)
            throw new ArgumentException("Host cannot exceed 255 characters.", nameof(host));

        if (trimmed.Any(char.IsWhiteSpace))
            throw new ArgumentException("Host cannot contain whitespace.", nameof(host));

        return trimmed;
    }

    /// <summary>
    /// 验证端口号是否在有效范围内（1-65535）。
    /// </summary>
    /// <param name="port">待验证的端口号。</param>
    /// <returns>验证通过的端口号。</returns>
    public static int ValidatePort(int port)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 1 and 65535.");

        return port;
    }

    /// <summary>
    /// 验证令牌是否合法。令牌不能为空，长度不能超过 4096 个字符，且不能包含控制字符（制表符除外）。
    /// </summary>
    /// <param name="token">待验证的令牌字符串。</param>
    /// <returns>验证通过的令牌字符串。</returns>
    public static string ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Token cannot be empty.", nameof(token));

        if (token.Length > 4096)
            throw new ArgumentException("Token cannot exceed 4096 characters.", nameof(token));

        if (token.Any(c => char.IsControl(c) && c is not '\t'))
            throw new ArgumentException("Token cannot contain control characters.", nameof(token));

        return token;
    }

    /// <summary>
    /// 校验标识符安全性（仅允许字母数字下划线）。Catalog/别名会拼进 USE/ATTACH 语句，
    /// 白名单比转义更稳健——杜绝引号注入。
    /// </summary>
    /// <param name="name">待校验的标识符。</param>
    /// <returns>校验通过的原始标识符。</returns>
    public static string ValidateIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Identifier cannot be empty.", nameof(name));

        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                throw new ArgumentException($"Identifier contains an invalid character '{c}': {name}", nameof(name));
        }

        return name;
    }

    private static QuackProtocolConfig ParseKeyValue(string connectionString)
    {
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2)
                props[kv[0].Trim()] = kv[1].Trim();
        }

        var host = props.GetValueOrDefault("Host", props.GetValueOrDefault("Server", ""));
        var portText = props.GetValueOrDefault("Port", QuackProtocolConfig.DefaultPort.ToString());
        var token = props.GetValueOrDefault("Token", props.GetValueOrDefault("Password", ""));
        string? catalog = null;
        if (!props.TryGetValue("Catalog", out catalog))
            props.TryGetValue("Database", out catalog);
        // 默认禁用 SSL：Quack 默认容器为纯 HTTP，与同仓 Data.Quack 解析器及 README 保持一致。
        var disableSslText = props.GetValueOrDefault("DisableSsl", "true");
        var timeoutText = props.GetValueOrDefault("Timeout", props.GetValueOrDefault("TimeoutSeconds", "30"));

        if (!int.TryParse(portText, out var port))
            throw new FormatException($"Invalid port: {portText}");

        if (!int.TryParse(timeoutText, out var timeoutSeconds))
            throw new FormatException($"Invalid timeout: {timeoutText}");

        var config = new QuackProtocolConfig
        {
            Host = host,
            Port = port,
            Token = token,
            Catalog = catalog,
            DisableSsl = ParseBoolean(disableSslText, "DisableSsl"),
            TimeoutSeconds = timeoutSeconds
        };

        config.Validate();
        return config;
    }

    private static QuackProtocolConfig ParseUri(string uri)
    {
        var trimmed = uri.StartsWith("jdbc:", StringComparison.OrdinalIgnoreCase)
            ? uri["jdbc:".Length..]
            : uri;

        if (trimmed.StartsWith("quack:", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("quack://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "quack://" + trimmed["quack:".Length..];
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
            throw new FormatException($"Invalid Quack URI: {uri}");

        if (!parsed.Scheme.Equals("quack", StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"Unsupported URI scheme: {parsed.Scheme}");

        var token = GetQueryValue(parsed.Query, "token") ?? GetQueryValue(parsed.Query, "password") ?? "";
        var catalog = GetQueryValue(parsed.Query, "catalog") ?? GetQueryValue(parsed.Query, "database");
        // tls 缺省时默认禁用 SSL（与键值对解析的默认值一致）；仅 tls=true 才启用 SSL。
        var tls = GetQueryValue(parsed.Query, "tls");
        var disableSsl = tls == null || !ParseBoolean(tls, "tls");

        var config = new QuackProtocolConfig
        {
            Host = parsed.Host,
            Port = parsed.Port > 0 ? parsed.Port : QuackProtocolConfig.DefaultPort,
            Token = token,
            Catalog = catalog,
            DisableSsl = disableSsl
        };

        config.Validate();
        return config;
    }

    private static bool IsQuackUri(string value)
    {
        return value.StartsWith("jdbc:quack://", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("quack://", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("quack:", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetQueryValue(string query, string key)
    {
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            var name = Uri.UnescapeDataString(kv[0]);
            if (name.Equals(key, StringComparison.OrdinalIgnoreCase))
                return kv.Length == 2 ? Uri.UnescapeDataString(kv[1]) : "";
        }

        return null;
    }

    private static bool ParseBoolean(string value, string name)
    {
        return value.Trim().ToUpperInvariant() switch
        {
            "TRUE" or "1" or "YES" => true,
            "FALSE" or "0" or "NO" => false,
            _ => throw new FormatException($"Invalid boolean value for {name}: {value}")
        };
    }
}
