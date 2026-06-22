namespace Azrng.DuckDB.Data.Quack;

/// <summary>
/// Quack 连接字符串解析器，支持两种格式：
/// 1. Host=10.21.50.221;Port=9494;Token=change-me
/// 2. quack://10.21.50.221:9494?token=change-me
/// </summary>
public static class QuackConnectionStringParser
{
    /// <summary>
    /// 解析连接字符串为配置对象
    /// </summary>
    public static QuackConnectionConfig Parse(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("连接字符串不能为空", nameof(connectionString));

        var trimmed = connectionString.Trim();

        if (trimmed.StartsWith("jdbc:quack://", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("不支持 jdbc:quack:// 连接字符串，请使用 Host=...;Port=...;Token=... 或 quack://...");

        if (IsQuackUri(trimmed))
            return ParseUri(trimmed);

        return ParseKeyValue(trimmed);
    }

    /// <summary>
    /// 构建 Quack URI: quack://{host}:{port}
    /// </summary>
    public static string BuildQuackUri(string host, int port)
    {
        ValidateHost(host);
        ValidatePort(port);

        var trimmedHost = host.Trim();
        if (trimmedHost.StartsWith("quack:", StringComparison.OrdinalIgnoreCase))
            return ValidateDsn(trimmedHost);

        var uriHost = trimmedHost.Contains(':', StringComparison.Ordinal) &&
                      !trimmedHost.StartsWith("[", StringComparison.Ordinal)
            ? $"[{trimmedHost}]"
            : trimmedHost;

        return $"quack://{uriHost}:{port}";
    }

    /// <summary>
    /// 校验远程主机地址，禁止空值、超长值和空白字符
    /// </summary>
    public static string ValidateHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host 不能为空", nameof(host));

        var trimmedHost = host.Trim();
        if (trimmedHost.Length > 255)
            throw new ArgumentException("Host 长度不能超过 255 个字符", nameof(host));

        if (trimmedHost.Any(char.IsWhiteSpace))
            throw new ArgumentException("Host 不能包含空白字符", nameof(host));

        return trimmedHost;
    }

    /// <summary>
    /// 校验 Quack 端口范围
    /// </summary>
    public static int ValidatePort(int port)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), port, "端口必须在 1-65535 之间");

        return port;
    }

    /// <summary>
    /// 校验认证 Token，禁止空值、超长值和不可见控制字符
    /// </summary>
    public static string ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Token 不能为空", nameof(token));

        if (token.Length > 4096)
            throw new ArgumentException("Token 长度不能超过 4096 个字符", nameof(token));

        if (token.Any(c => char.IsControl(c) && c is not '\t'))
            throw new ArgumentException("Token 不能包含控制字符", nameof(token));

        return token;
    }

    /// <summary>
    /// 校验并规范化远程 DSN，仅允许 quack 协议进入 ATTACH 语句
    /// </summary>
    public static string ValidateDsn(string dsn)
    {
        if (string.IsNullOrWhiteSpace(dsn))
            throw new ArgumentException("DSN 不能为空", nameof(dsn));

        var trimmedDsn = dsn.Trim();
        if (trimmedDsn.Length > 2048)
            throw new ArgumentException("DSN 长度不能超过 2048 个字符", nameof(dsn));

        if (trimmedDsn.Any(char.IsWhiteSpace))
            throw new ArgumentException("DSN 不能包含空白字符", nameof(dsn));

        if (!IsQuackUri(trimmedDsn))
            throw new ArgumentException("DSN 必须使用 quack 协议", nameof(dsn));

        var normalized = NormalizeQuackUri(trimmedDsn);
        var (host, port) = ParseQuackUriParts(normalized);
        ValidateHost(host);
        ValidatePort(port);

        return normalized;
    }

    /// <summary>
    /// 校验标识符安全性（仅允许字母数字下划线）
    /// </summary>
    public static string ValidateIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("标识符不能为空", nameof(name));

        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                throw new ArgumentException($"标识符包含非法字符 '{c}': {name}", nameof(name));
        }

        return name;
    }

    private static bool IsQuackUri(string value)
    {
        return value.StartsWith("quack://", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("quack:", StringComparison.OrdinalIgnoreCase);
    }

    private static QuackConnectionConfig ParseUri(string uri)
    {
        var normalizedUri = NormalizeQuackUri(uri);

        var token = GetQueryValue(uri, "token") ?? GetQueryValue(uri, "password") ?? "";
        var tls = GetQueryValue(uri, "tls");
        var disableSsl = tls == null || !ParseBoolean(tls, "tls");

        // 解析 quack:host:port 格式（非标准 URI，System.Uri 无法解析）
        var (host, port) = ParseQuackUriParts(normalizedUri);

        // URI 解析后复用统一配置校验，保证三种连接字符串格式的约束一致。
        var config = new QuackConnectionConfig
        {
            Host = host,
            Port = port,
            Token = token,
            DisableSsl = disableSsl
        };

        config.Validate();
        return config;
    }

    private static (string Host, int Port) ParseQuackUriParts(string uri)
    {
        // quack://host:port、quack:host:port 或 quack:[ipv6]:port
        var withoutScheme = uri.StartsWith("quack://", StringComparison.OrdinalIgnoreCase)
            ? uri["quack://".Length..]
            : uri["quack:".Length..];

        string host;
        string portStr;

        if (withoutScheme.StartsWith("["))
        {
            // IPv6: quack:[::1]:port
            var closeBracket = withoutScheme.IndexOf(']');
            if (closeBracket < 0)
                throw new FormatException($"无效的 IPv6 URI: {uri}");

            host = withoutScheme[1..closeBracket];
            portStr = withoutScheme[(closeBracket + 2)..]; // skip ]:
        }
        else
        {
            // IPv4 or hostname: quack:host:port
            var lastColon = withoutScheme.LastIndexOf(':');
            if (lastColon < 0)
                throw new FormatException($"无效的 URI（缺少端口）: {uri}");

            host = withoutScheme[..lastColon];
            portStr = withoutScheme[(lastColon + 1)..];
        }

        if (!int.TryParse(portStr, out var port) || port is < 1 or > 65535)
            throw new FormatException($"无效的端口号: {portStr}");

        return (host, port);
    }

    private static QuackConnectionConfig ParseKeyValue(string connectionString)
    {
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2)
                props[kv[0].Trim()] = kv[1].Trim();
        }

        var host = props.GetValueOrDefault("Host", "");
        var portStr = props.GetValueOrDefault("Port", "0");
        var token = props.GetValueOrDefault("Token", props.GetValueOrDefault("Password", ""));
        var catalog = props.GetValueOrDefault("Catalog", props.GetValueOrDefault("Database", "remote"));
        var attach = ParseBoolean(props.GetValueOrDefault("Attach", "false"), "Attach");
        var disableSslStr = props.GetValueOrDefault("DisableSsl", "true");
        var extensionDirectory = props.GetValueOrDefault("ExtensionDirectory", props.GetValueOrDefault("Extensions", ""));

        if (!int.TryParse(portStr, out var port))
            throw new FormatException($"无效的端口号: {portStr}");

        // Key/Value 格式解析后复用统一配置校验，避免后续连接流程收到半合法配置。
        var config = new QuackConnectionConfig
        {
            Host = host,
            Port = port,
            Token = token,
            Catalog = catalog,
            Attach = attach,
            DisableSsl = ParseBoolean(disableSslStr, "DisableSsl"),
            ExtensionDirectory = string.IsNullOrWhiteSpace(extensionDirectory) ? null : extensionDirectory
        };

        config.Validate();
        return config;
    }

    private static string NormalizeQuackUri(string uri)
    {
        var trimmedUri = uri.Trim();

        if (trimmedUri.StartsWith("quack://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(trimmedUri, UriKind.Absolute, out var parsedUri))
                throw new FormatException($"无效的 Quack URI: {uri}");

            if (parsedUri.Port <= 0)
                throw new InvalidOperationException("缺少 Quack 端口");

            return $"quack://{FormatUriHost(parsedUri.Host)}:{parsedUri.Port}";
        }

        if (trimmedUri.StartsWith("quack:", StringComparison.OrdinalIgnoreCase))
        {
            var questionMark = trimmedUri.IndexOf('?');
            var normalizedUri = questionMark >= 0 ? trimmedUri[..questionMark] : trimmedUri;
            if (!normalizedUri.StartsWith("quack://", StringComparison.OrdinalIgnoreCase))
            {
                var (host, port) = ParseQuackUriParts(normalizedUri);
                return $"quack://{FormatUriHost(host)}:{port}";
            }

            return normalizedUri;
        }

        throw new FormatException($"不支持的连接字符串格式: {uri}");
    }

    private static string? GetQueryValue(string uri, string key)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        var trimmedUri = uri.Trim();

        // 尝试用 System.Uri 解析（适用于 quack:// 格式）
        if (Uri.TryCreate(trimmedUri, UriKind.Absolute, out var parsedUri))
        {
            return ExtractFromQueryString(parsedUri.Query, key);
        }

        // 手动解析 quack:host:port?token=xxx 格式
        var questionMark = trimmedUri.IndexOf('?');
        if (questionMark >= 0)
        {
            return ExtractFromQueryString(trimmedUri[questionMark..], key);
        }

        return null;
    }

    private static string? ExtractFromQueryString(string query, string key)
    {
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            var queryKey = Uri.UnescapeDataString(kv[0]);
            if (!queryKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            return kv.Length == 2 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
        }

        return null;
    }

    private static string FormatUriHost(string host)
    {
        return host.Contains(':', StringComparison.Ordinal) &&
               !host.StartsWith("[", StringComparison.Ordinal)
            ? $"[{host}]"
            : host;
    }

    private static bool ParseBoolean(string value, string name)
    {
        return value.Trim().ToUpperInvariant() switch
        {
            "TRUE" or "1" or "YES" => true,
            "FALSE" or "0" or "NO" => false,
            _ => throw new FormatException($"无效的布尔值 {name}: {value}")
        };
    }
}
