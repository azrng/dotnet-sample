using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Dapper;
using DuckDB.NET.Data;

namespace Azrng.DuckDB.Data.Quack;

/// <summary>
/// 基于嵌入式 DuckDB + Quack 扩展的数据提供程序
/// 通过 Quack 扩展连接远程 DuckDB，充当 Quack client
/// </summary>
public sealed class QuackDataProvider : IDisposable
{
    private const string ExtensionVersion = "v1.5.3";
    private const string HttpfsExtensionFileName = "httpfs.duckdb_extension";
    private const string QuackExtensionFileName = "quack.duckdb_extension";
    private static readonly object ExtensionInstallLock = new();
    private static readonly HashSet<string> InstalledExtensions = new(StringComparer.OrdinalIgnoreCase);

    private readonly DuckDBConnection _connection;
    private readonly string _uri;
    private readonly string _token;
    private readonly bool _disableSsl;
    private readonly bool _useAttach;
    private readonly string? _extensionDirectory;
    private readonly bool _useRemoteCatalog;
    // DuckDBConnection 不是面向并发初始化/释放设计的，这里保护附加和释放状态。
    private readonly object _syncRoot = new();
    private string? _attachedCatalog;
    private bool _attached;
    private bool _disposed;

    /// <summary>
    /// 使用配置对象创建 Quack 数据提供程序
    /// </summary>
    public QuackDataProvider(QuackConnectionConfig config)
        : this(config.Host,
               config.Port,
               config.Token,
               config.Catalog,
               config.DisableSsl,
               config.Attach,
               config.ExtensionDirectory)
    {
    }

    /// <summary>
    /// 创建 Quack 数据提供程序
    /// </summary>
    /// <param name="host">远程 DuckDB 主机地址</param>
    /// <param name="port">Quack 端口</param>
    /// <param name="token">认证 token</param>
    /// <param name="catalog">远端服务端 catalog 名称；用于 ATTACH 模式的本地挂载名</param>
    /// <param name="disableSsl">是否禁用 SSL</param>
    public QuackDataProvider(
        string host,
        int port,
        string token,
        string catalog = "remote",
        bool disableSsl = true,
        bool attach = false,
        string? extensionDirectory = null)
    {
        _attachedCatalog = QuackConnectionStringParser.ValidateIdentifier(catalog);
        _uri = QuackConnectionStringParser.BuildQuackUri(host, port);
        _token = QuackConnectionStringParser.ValidateToken(token);
        _disableSsl = disableSsl;
        _useAttach = attach;
        _extensionDirectory = extensionDirectory;
        _useRemoteCatalog = !_useAttach && !string.Equals(_attachedCatalog, "remote", StringComparison.OrdinalIgnoreCase);

        _connection = new DuckDBConnection("Data Source=:memory:;allow_unsigned_extensions=true");
        _connection.Open();

        LoadExtensionResolved("httpfs", HttpfsExtensionFileName);
        LoadExtensionResolved("quack", QuackExtensionFileName);
    }

    /// <summary>
    /// 底层 DuckDB 连接（供高级场景使用）
    /// </summary>
    public DuckDBConnection Connection => _connection;

    /// <summary>
    /// 是否已附加远程数据库
    /// </summary>
    public bool IsAttached => _attached;

    /// <summary>
    /// 是否使用 ATTACH 模式
    /// </summary>
    public bool UseAttachMode() => _useAttach;

    /// <summary>
    /// 附加远程 DuckDB 数据库（别名 remote）
    /// </summary>
    public void AttachRemote()
    {
        Attach(_attachedCatalog ?? "remote");
    }

    /// <summary>
    /// 附加远程数据库并切换当前 database
    /// </summary>
    /// <param name="alias">数据库别名</param>
    /// <param name="dsn">远程 DSN（留空则使用默认值）</param>
    public void Attach(string alias, string dsn = "")
    {
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_attached)
                return;

            var safeAlias = QuackConnectionStringParser.ValidateIdentifier(alias);
            // 外部传入的 DSN 先规范化为 quack:host:port，避免任意 URI 进入 ATTACH 拼接。
            var attachDsn = string.IsNullOrWhiteSpace(dsn) ? _uri : QuackConnectionStringParser.ValidateDsn(dsn);
            var dsnPart = $"'{EscapeSqlLiteral(attachDsn)}'";
            var tokenPart = $"'{EscapeSqlLiteral(_token)}'";
            var disableSslPart = _disableSsl.ToString().ToLowerInvariant();

            Execute($"ATTACH {dsnPart} AS {safeAlias} (TYPE quack, TOKEN {tokenPart}, DISABLE_SSL {disableSslPart});");

            _attachedCatalog = safeAlias;
            _attached = true;
        }
    }

    #region ExecuteQuery - 返回 List<object[]>

    /// <summary>
    /// 执行查询，返回二维数组（含表头）
    /// </summary>
    public List<object[]> ExecuteQuery(string sql, bool includeHeader = true, int maxRows = 100)
    {
        EnsureReady();
        var normalized = BuildExecutableSql(sql);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = normalized;
        using var reader = cmd.ExecuteReader();

        return ReadResults(reader, includeHeader, maxRows);
    }

    /// <summary>
    /// 执行带命名参数的查询，返回二维数组（含表头）
    /// SQL 中使用 @paramName 形式，内部自动转换为 DuckDB 位置参数
    /// 示例: ExecuteQuery("SELECT * FROM orders WHERE status = @status", new { status = "completed" })
    /// </summary>
    public List<object[]> ExecuteQuery(string sql, object parameters, bool includeHeader = true, int maxRows = 100)
    {
        if (_useAttach)
        {
            var (convertedSql, dbParams) = ParameterConverter.ConvertNamedToQuestionMark(sql, parameters);
            return ExecuteQuery(convertedSql, dbParams, includeHeader, maxRows);
        }

        return ExecuteQuery(ParameterConverter.ResolveNamedParameters(sql, parameters), includeHeader, maxRows);
    }

    /// <summary>
    /// 执行带位置参数的查询，返回二维数组（含表头）
    /// DuckDB 使用 ? 作为位置参数占位符，参数按顺序绑定
    /// </summary>
    public List<object[]> ExecuteQuery(string sql, DuckDBParameter[] parameters, bool includeHeader = true, int maxRows = 100)
    {
        EnsureReady();
        var querySql = _useAttach ? sql : ParameterConverter.ResolveQuestionMarkParameters(sql, parameters);
        var normalized = BuildExecutableSql(querySql);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = normalized;
        if (_useAttach)
        {
            foreach (var p in parameters)
                cmd.Parameters.Add(p);
        }

        using var reader = cmd.ExecuteReader();
        return ReadResults(reader, includeHeader, maxRows);
    }

    #endregion

    #region ExecuteQuery<T> - Dapper 强类型

    /// <summary>
    /// 执行查询，返回强类型列表（Dapper）
    /// </summary>
    public List<T> ExecuteQuery<T>(string sql) where T : new()
    {
        EnsureReady();
        return _connection.Query<T>(BuildExecutableSql(sql)).ToList();
    }

    /// <summary>
    /// 执行带命名参数的查询，返回强类型列表
    /// SQL 中使用 @paramName 形式，内部自动转换为 $1,$2 位置参数
    /// 示例: ExecuteQuery&lt;OrderDto&gt;("SELECT * FROM orders WHERE status = @status", new { status = "completed" })
    /// </summary>
    public List<T> ExecuteQuery<T>(string sql, object parameters) where T : new()
    {
        EnsureReady();
        if (_useAttach)
        {
            var (convertedSql, dynamicParams) = ParameterConverter.ConvertNamedToPositional(sql, parameters);
            return _connection.Query<T>(BuildExecutableSql(convertedSql), dynamicParams).ToList();
        }

        return _connection.Query<T>(BuildExecutableSql(ParameterConverter.ResolveNamedParameters(sql, parameters))).ToList();
    }

    #endregion

    #region ExecuteNonQuery

    /// <summary>
    /// 执行非查询语句（DDL/DML）
    /// </summary>
    public int ExecuteNonQuery(string sql)
    {
        EnsureReady();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = BuildExecutableSql(sql);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 执行带命名参数的非查询语句
    /// SQL 中使用 @paramName 形式，内部自动转换为 DuckDB 位置参数
    /// </summary>
    public int ExecuteNonQuery(string sql, object parameters)
    {
        if (_useAttach)
        {
            var (convertedSql, dbParams) = ParameterConverter.ConvertNamedToQuestionMark(sql, parameters);
            return ExecuteNonQuery(convertedSql, dbParams);
        }

        return ExecuteNonQuery(ParameterConverter.ResolveNamedParameters(sql, parameters));
    }

    /// <summary>
    /// 执行带位置参数的非查询语句
    /// </summary>
    public int ExecuteNonQuery(string sql, DuckDBParameter[] parameters)
    {
        EnsureReady();
        using var cmd = _connection.CreateCommand();
        var querySql = _useAttach ? sql : ParameterConverter.ResolveQuestionMarkParameters(sql, parameters);
        cmd.CommandText = BuildExecutableSql(querySql);
        if (_useAttach)
        {
            foreach (var p in parameters)
                cmd.Parameters.Add(p);
        }
        return cmd.ExecuteNonQuery();
    }

    #endregion

    #region ExecuteScalar

    /// <summary>
    /// 执行标量查询
    /// </summary>
    public object? ExecuteScalar(string sql)
    {
        EnsureReady();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = BuildExecutableSql(sql);
        return cmd.ExecuteScalar();
    }

    /// <summary>
    /// 执行带命名参数的标量查询
    /// SQL 中使用 @paramName 形式，内部自动转换为 DuckDB 位置参数
    /// </summary>
    public object? ExecuteScalar(string sql, object parameters)
    {
        if (_useAttach)
        {
            var (convertedSql, dbParams) = ParameterConverter.ConvertNamedToQuestionMark(sql, parameters);
            return ExecuteScalar(convertedSql, dbParams);
        }

        return ExecuteScalar(ParameterConverter.ResolveNamedParameters(sql, parameters));
    }

    /// <summary>
    /// 执行带位置参数的标量查询
    /// </summary>
    public object? ExecuteScalar(string sql, DuckDBParameter[] parameters)
    {
        EnsureReady();
        using var cmd = _connection.CreateCommand();
        var querySql = _useAttach ? sql : ParameterConverter.ResolveQuestionMarkParameters(sql, parameters);
        cmd.CommandText = BuildExecutableSql(querySql);
        if (_useAttach)
        {
            foreach (var p in parameters)
                cmd.Parameters.Add(p);
        }
        return cmd.ExecuteScalar();
    }

    #endregion

    #region quack_query_by_name 包装（解决下推降级问题）

    /// <summary>
    /// 使用 quack_query_by_name 包装 SQL，让远端 Quack 自行解析
    /// 解决 attached-table 下推时 schema 丢失的问题
    /// </summary>
    public List<T> ExecuteQueryViaQuack<T>(string sql) where T : new()
    {
        EnsureReady();
        var wrappedSql = BuildQuackQueryByNameSql(sql);
        return _connection.Query<T>(wrappedSql).ToList();
    }

    /// <summary>
    /// 使用 quack_query_by_name 包装 SQL，返回二维数组（含表头）
    /// </summary>
    public List<object[]> ExecuteQueryViaQuack(string sql, bool includeHeader = true, int maxRows = 100)
    {
        EnsureReady();
        var wrappedSql = BuildQuackQueryByNameSql(sql);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = wrappedSql;
        using var reader = cmd.ExecuteReader();

        return ReadResults(reader, includeHeader, maxRows);
    }

    /// <summary>
    /// 构建 quack_query_by_name 包装 SQL
    /// </summary>
    public string BuildQuackQueryByNameSql(string sql)
    {
        var normalized = SqlNormalizer.Normalize(sql);
        if (!_useAttach)
            return BuildQuackQuerySql(normalized);

        var alias = _attachedCatalog ?? "remote";
        return $"select * from quack_query_by_name('{EscapeSqlLiteral(alias)}', '{EscapeSqlLiteral(normalized)}')";
    }

    /// <summary>
    /// 构建默认 quack_query 包装 SQL
    /// </summary>
    public string BuildQuackQuerySql(string sql)
    {
        var normalized = SqlNormalizer.Normalize(sql);
        if (_useRemoteCatalog)
        {
            normalized = $"USE \"{EscapeIdentifier(_attachedCatalog ?? "remote")}\"; {normalized}";
        }

        return $"SELECT * FROM quack_query('{EscapeSqlLiteral(_uri)}', '{EscapeSqlLiteral(normalized)}', token := '{EscapeSqlLiteral(_token)}', disable_ssl := {ToDuckDbBoolean(_disableSsl)})";
    }

    #endregion

    /// <summary>
    /// SQL 规范化（静态便捷方法）
    /// </summary>
    public static string NormalizeSql(string sql) => SqlNormalizer.Normalize(sql);

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
                return;

            _disposed = true;
            _connection.Dispose();
        }
    }

    private void Execute(string sql)
    {
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException("执行 DuckDB 命令失败。", ex);
        }
    }

    internal void EnsureReady()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_useAttach && !_attached)
            AttachRemote();
    }

    private string BuildExecutableSql(string sql)
    {
        return _useAttach ? SqlNormalizer.Normalize(sql) : BuildQuackQuerySql(sql);
    }

    private static List<object[]> ReadResults(DuckDBDataReader reader, bool includeHeader, int maxRows)
    {
        var results = new List<object[]>();
        var fieldCount = reader.FieldCount;

        if (includeHeader)
        {
            var header = new object[fieldCount];
            for (var i = 0; i < fieldCount; i++)
                header[i] = reader.GetName(i);
            results.Add(header);
        }

        while (reader.Read() && results.Count < maxRows + (includeHeader ? 1 : 0))
        {
            var row = new object[fieldCount];
            for (var i = 0; i < fieldCount; i++)
                row[i] = reader.IsDBNull(i) ? null! : reader.GetValue(i);
            results.Add(row);
        }

        return results;
    }

    private void LoadExtension(string extensionPath)
    {
        // 每个 DuckDB 连接都必须 LOAD；FORCE INSTALL 只在进程内首次 LOAD 失败时执行。
        try
        {
            Execute($"LOAD '{extensionPath}';");
            MarkExtensionInstalled($"path:{extensionPath}");
        }
        catch
        {
            var loadedInCurrentConnection = EnsureExtensionInstalled(
                $"path:{extensionPath}",
                $"FORCE INSTALL '{extensionPath}';",
                $"LOAD '{extensionPath}';");
            if (!loadedInCurrentConnection)
                Execute($"LOAD '{extensionPath}';");
        }
    }

    /// <summary>
    /// 按扩展名加载（自动下载）：放行未签名扩展后优先 LOAD，失败则 FORCE INSTALL（DuckDB 从其扩展仓库下载）再 LOAD。
    /// httpfs 为官方扩展可正常下载；quack 为第三方未签名扩展，仅当运行环境的 DuckDB 仓库支持时才能下载，
    /// 否则会抛出异常——此时应改用 <see cref="QuackConnectionConfig.ExtensionDirectory"/> 指向已下载的本地文件。
    /// </summary>
    private void LoadExtensionByName(string extensionName)
    {
        // allow_unsigned_extensions 已在打开连接时通过连接字符串设置（运行期无法再改）。
        try
        {
            Execute($"LOAD {extensionName};");
            MarkExtensionInstalled($"name:{extensionName}");
        }
        catch
        {
            var loadedInCurrentConnection = EnsureExtensionInstalled(
                $"name:{extensionName}",
                $"FORCE INSTALL {extensionName};",
                $"LOAD {extensionName};");
            if (!loadedInCurrentConnection)
                Execute($"LOAD {extensionName};");
        }
    }

    private bool EnsureExtensionInstalled(string cacheKey, string installSql, string loadSql)
    {
        lock (ExtensionInstallLock)
        {
            if (InstalledExtensions.Contains(cacheKey))
                return false;

            // 其他连接可能已经完成安装；在锁内先重试 LOAD，避免不必要的 FORCE INSTALL。
            try
            {
                Execute(loadSql);
                InstalledExtensions.Add(cacheKey);
                return true;
            }
            catch
            {
                Execute(installSql);
                InstalledExtensions.Add(cacheKey);
                return false;
            }
        }
    }

    private static void MarkExtensionInstalled(string cacheKey)
    {
        lock (ExtensionInstallLock)
        {
            InstalledExtensions.Add(cacheKey);
        }
    }

    /// <summary>
    /// 解析并加载单个扩展：优先本地文件（配置目录 → 包内 extensions/），找不到则按扩展名自动下载。
    /// </summary>
    private void LoadExtensionResolved(string extensionName, string extensionFileName)
    {
        var path = TryFindExtensionFile(extensionFileName);
        if (path is not null)
        {
            LoadExtension(path);
            return;
        }

        LoadExtensionByName(extensionName);
    }

    private string? TryFindExtensionFile(string extensionFileName)
    {
        // 1. 配置目录（扁平结构）：用户把 .duckdb_extension 直接放在该目录下。
        if (!string.IsNullOrWhiteSpace(_extensionDirectory))
        {
            var configuredPath = Path.Combine(_extensionDirectory, extensionFileName);
            if (File.Exists(configuredPath))
                return NormalizeExtensionPath(configuredPath);
        }

        // 2. 包内扩展目录 / 源码调试目录（结构化：extensions/{version}/{platform}/{file}）。
        //    NuGet 输出目录和源码调试目录结构不同，两类路径都需要尝试探测。
        foreach (var rootFolder in new[] { "extensions", "Extensions" })
        {
            var relativePath = Path.Combine(rootFolder, ExtensionVersion, GetPlatformFolder(), extensionFileName);
            var outputPath = Path.Combine(AppContext.BaseDirectory, relativePath);
            if (File.Exists(outputPath))
                return NormalizeExtensionPath(outputPath);

            var sourcePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", relativePath));
            if (File.Exists(sourcePath))
                return NormalizeExtensionPath(sourcePath);
        }

        return null;
    }

    private static string NormalizeExtensionPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Quack DuckDB 扩展文件未找到", fullPath);

        return fullPath.Replace("\\", "/").Replace("'", "''");
    }

    private static string GetPlatformFolder()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            _ => "amd64"
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return $"windows_{architecture}";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return $"linux_{architecture}";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return $"osx_{architecture}";

        throw new PlatformNotSupportedException("不支持的平台");
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("\\", "\\\\").Replace("'", "''");
    }

    private static string EscapeIdentifier(string value)
    {
        return value.Replace("\"", "\"\"");
    }

    private static string ToDuckDbBoolean(bool value) => value ? "true" : "false";
}
