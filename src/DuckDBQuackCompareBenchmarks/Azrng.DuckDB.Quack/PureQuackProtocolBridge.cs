using System.Collections;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Azrng.DuckDB.Quack.Internal;

namespace Azrng.DuckDB.Quack;

/// <summary>
/// Pure C# implementation of the Quack protocol bridge.
/// No native dependencies required.
/// </summary>
public sealed class PureQuackProtocolBridge : IQuackProtocolBridge
{
    private readonly QuackProtocolBridge _bridge;
    private readonly bool _ownsBridge;
    private bool _disposed;

    /// <summary>
    /// 初始化 <see cref="PureQuackProtocolBridge"/> 的新实例。
    /// </summary>
    /// <param name="httpClient">用于发送请求的 HTTP 客户端，为 null 时使用默认客户端。</param>
    /// <param name="logger">日志记录器，为 null 时不记录日志。</param>
    /// <param name="timeout">请求超时时间，为 null 时使用默认超时。</param>
    /// <param name="metrics">协议指标收集器，为 null 时不收集指标。</param>
    /// <param name="sslOptions">SSL 配置选项，为 null 时使用默认配置。</param>
    public PureQuackProtocolBridge(HttpClient? httpClient = null, ILogger? logger = null, TimeSpan? timeout = null, QuackProtocolMetrics? metrics = null, SslOptions? sslOptions = null)
    {
        _bridge = new QuackProtocolBridge(httpClient, logger, timeout, metrics, sslOptions);
        _ownsBridge = true;
    }

    /// <summary>
    /// 获取 Quack 协议桥的版本信息。
    /// </summary>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>包含原生 ABI 版本、DuckDB 版本和 Quack 版本的版本信息。</returns>
    public Task<QuackProtocolBridgeVersion> GetVersionAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new QuackProtocolBridgeVersion(
            QuackProtocolVersions.NativeAbiVersion,
            QuackProtocolVersions.DuckDbVersion,
            QuackProtocolVersions.QuackVersion));
    }

    /// <summary>
    /// 使用指定配置建立与 DuckDB 的协议会话。
    /// </summary>
    /// <param name="config">协议连接配置。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>表示已建立会话的 <see cref="QuackProtocolSession"/> 实例。</returns>
    public async Task<QuackProtocolSession> ConnectAsync(QuackProtocolConfig config, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var connectionId = await _bridge.ConnectAsync(config, cancellationToken);
        return new QuackProtocolSession(connectionId, config);
    }

    /// <summary>
    /// 在指定会话中执行 SQL 查询并返回结果。
    /// </summary>
    /// <param name="session">用于执行查询的协议会话。</param>
    /// <param name="sql">要执行的 SQL 语句。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>查询执行结果。</returns>
    public async Task<QuackQueryResult> ExecuteQueryAsync(
        QuackProtocolSession session,
        string sql,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _bridge.PrepareAndExecuteAsync(session.Config, session.SessionId, sql, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// 使用获取令牌从会话中获取下一批查询结果。
    /// </summary>
    /// <param name="session">用于获取结果的协议会话。</param>
    /// <param name="fetchToken">标识结果集的获取令牌。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>下一批查询结果，如果没有更多数据则返回 null。</returns>
    public async Task<QuackQueryResult?> FetchAsync(
        QuackProtocolSession session,
        string fetchToken,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        ResultHandle result;
        if (TryParseWireFetchToken(fetchToken, out var uuidWireBytes))
        {
            result = await _bridge.FetchAsync(session.Config, session.SessionId, uuidWireBytes, cancellationToken);
        }
        else
        {
            if (!TryParseFetchToken(fetchToken, out var upper, out var lower))
                return null;

            result = await _bridge.FetchAsync(session.Config, session.SessionId, upper, lower, cancellationToken);
        }

        var isEmpty = result.Batch is null ? result.Rows.Count == 0 : result.Batch.RowCount == 0;
        if (isEmpty)
            return null;

        if (result.UuidUpper == 0 && result.UuidLower == 0)
        {
            return MapResult(result) with { FetchToken = fetchToken };
        }

        return MapResult(result);
    }

    /// <summary>
    /// 关闭指定的协议会话并释放相关资源。
    /// </summary>
    /// <param name="session">要关闭的协议会话。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public Task CloseSessionAsync(QuackProtocolSession session, CancellationToken cancellationToken)
    {
        if (_disposed)
            return Task.CompletedTask;

        cancellationToken.ThrowIfCancellationRequested();
        return _bridge.DisconnectAsync(session.Config, session.SessionId, cancellationToken);
    }

    /// <summary>
    /// 释放 <see cref="PureQuackProtocolBridge"/> 使用的资源。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_ownsBridge)
            _bridge.Dispose();
    }

    /// <summary>
    /// 对 <see cref="ColumnarBatch"/> 的惰性行视图。仅在被访问时才把对应行装箱为 <c>object?[]</c>，
    /// 避免在数据读取器直接读 batch 时仍把整批转置成行数组（旧实现的重复装箱+分配）。
    /// </summary>
    private sealed class BatchRowListView : IReadOnlyList<object?[]>
    {
        private readonly ColumnarBatch _batch;

        public BatchRowListView(ColumnarBatch batch) => _batch = batch;

        public int Count => _batch.RowCount;

        public object?[] this[int index]
        {
            get
            {
                var row = new object?[_batch.ColumnCount];
                for (int c = 0; c < _batch.ColumnCount; c++)
                    row[c] = _batch.GetValue(c, index);
                return row;
            }
        }

        public IEnumerator<object?[]> GetEnumerator()
        {
            for (int r = 0; r < _batch.RowCount; r++)
                yield return this[r];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static QuackQueryResult MapResult(ResultHandle result)
    {
        var columns = new List<QuackColumnInfo>();
        for (int i = 0; i < result.ColumnTypes.Count; i++)
        {
            var typeName = result.ColumnTypes[i];
            var name = i < result.ColumnNames.Count ? result.ColumnNames[i] : $"col_{i}";
            columns.Add(new QuackColumnInfo(name, typeName, MapType(typeName)));
        }

        IReadOnlyList<object?[]> rows;
        if (result.Batch is not null)
        {
            // Columnar batch available — expose Rows as a lazy view over the typed batch.
            // We no longer eagerly transpose the batch into row arrays; a row materialises only
            // if a caller actually enumerates Rows. The data reader reads the typed batch directly.
            rows = new BatchRowListView(result.Batch);
        }
        else
        {
            var rowList = new object?[result.Rows.Count][];
            for (int r = 0; r < result.Rows.Count; r++)
            {
                var src = result.Rows[r];
                var values = new object?[src.Count];
                for (int i = 0; i < src.Count; i++)
                {
                    values[i] = src[i];
                }
                rowList[r] = values;
            }
            rows = rowList;
        }

        return new QuackQueryResult(
            result.QueryId.ToString(CultureInfo.InvariantCulture),
            columns,
            rows,
            result.HasMoreRows)
        {
            FetchToken = result.HasMoreRows ? BuildFetchToken(result) : null,
            Batch = result.Batch
        };
    }

    private static Type MapType(string typeName)
    {
        return typeName.ToUpperInvariant() switch
        {
            "BOOLEAN" or "BOOL" => typeof(bool),
            "TINYINT" or "SMALLINT" or "INTEGER" or "INT" or "BIGINT" or "UTINYINT" or "USMALLINT" or "UINTEGER" or "UBIGINT" => typeof(long),
            "HUGEINT" => typeof(long),
            "FLOAT" or "DOUBLE" or "REAL" => typeof(double),
            var t when t.StartsWith("DECIMAL", StringComparison.Ordinal) => typeof(decimal),
            "DATE" or "TIME" or "TIMESTAMP" or "TIMESTAMP_TZ" or "TIMESTAMP_SEC" or "TIMESTAMP_MS" or "TIMESTAMP_NS" => typeof(DateTime),
            _ => typeof(string)
        };
    }

    private static string BuildFetchToken(long upper, ulong lower)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{upper}:{lower}");
    }

    private static string BuildFetchToken(ResultHandle result)
    {
        if (result.UuidWireBytes is { Length: > 0 } wireBytes)
            return "wire:" + Base64UrlEncode(wireBytes);

        return BuildFetchToken(result.UuidUpper, result.UuidLower);
    }

    private static bool TryParseFetchToken(string? token, out long upper, out ulong lower)
    {
        upper = 0;
        lower = 0;
        if (string.IsNullOrEmpty(token))
            return false;

        var parts = token.Split(':', 2);
        if (parts.Length != 2)
            return false;

        return long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out upper)
            && ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out lower);
    }

    private static bool TryParseWireFetchToken(string token, out byte[] uuidWireBytes)
    {
        uuidWireBytes = [];
        const string prefix = "wire:";
        if (!token.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var payload = token[prefix.Length..];
        if (payload.Length == 0)
            return false;

        try
        {
            uuidWireBytes = Base64UrlDecode(payload);
            return uuidWireBytes.Length > 0;
        }
        catch (FormatException)
        {
            uuidWireBytes = [];
            return false;
        }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        var padding = base64.Length % 4;
        if (padding > 0)
            base64 = base64.PadRight(base64.Length + 4 - padding, '=');

        return Convert.FromBase64String(base64);
    }
}
