using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Azrng.DuckDB.Quack.Internal;

/// <summary>
/// Quack 协议桥的核心实现，负责序列化/反序列化协议消息并与 HTTP 客户端交互。
/// </summary>
internal sealed class QuackProtocolBridge : IDisposable
{
    private const ushort FieldTerminator = 0xFFFF;

    private readonly QuackHttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly QuackRetryPolicy _retryPolicy;
    private readonly QuackProtocolMetrics? _metrics;

    public QuackProtocolBridge(HttpClient? httpClient = null, ILogger? logger = null, TimeSpan? timeout = null, QuackProtocolMetrics? metrics = null, SslOptions? sslOptions = null)
    {
        _httpClient = new QuackHttpClient(httpClient, timeout, sslOptions);
        _logger = logger ?? NullLogger.Instance;
        _retryPolicy = new QuackRetryPolicy(_logger);
        _metrics = metrics;
    }

    public async Task<string> ConnectAsync(QuackProtocolConfig config, CancellationToken cancellationToken)
    {
        var endpoint = config.Endpoint;
        _logger.LogDebug("Connecting to {Endpoint}", endpoint);

        var writer = new QuackWriter(config.Token.Length * 3 + 64);
        WriteConnectionRequest(ref writer, config);
        byte[] response;
        try
        {
            response = await _httpClient.PostAsync(endpoint, writer.Buffer, writer.Length, cancellationToken);
        }
        finally
        {
            writer.Return();
        }
        var reader = new QuackBinaryReader(response);

        var header = ReadHeader(ref reader);
        if (header.Type == MessageType.ErrorResponse)
        {
            var error = ReadErrorResponse(ref reader);
            _logger.LogError("Connection failed: {Error}", error.Message);
            throw new QuackProtocolException($"Connection failed: {error.Message}", header.StatusCode);
        }

        if (header.Type != MessageType.ConnectionResponse)
        {
            _logger.LogError("Expected ConnectionResponse, got {MessageType}", header.Type);
            throw new QuackProtocolException($"Expected ConnectionResponse, got {header.Type}", header.StatusCode);
        }

        var connResp = ReadConnectionResponse(ref reader);

        if (!string.IsNullOrEmpty(connResp.QuackVersion))
        {
            _logger.LogDebug("Server Quack version: {Version}", connResp.QuackVersion);
        }

        _logger.LogInformation("Connected to {Endpoint}, connectionId={ConnectionId}", endpoint, header.ConnectionId);

        if (!string.IsNullOrEmpty(config.Catalog))
        {
            // 确保 catalog 在服务端存在：先尝试 USE，失败则自动 ATTACH。
            // catalog 已由 QuackProtocolConfig.Validate 做白名单校验（仅字母数字下划线），
            // 这里仍分别按双引号/单引号上下文转义，保持防御深度。
            var quotedCatalog = EscapeIdentifier(config.Catalog);
            var literalCatalog = EscapeStringLiteral(config.Catalog);
            _logger.LogDebug("Ensuring catalog {Catalog} exists on server", config.Catalog);
            try
            {
                await PrepareAndExecuteAsync(config, header.ConnectionId, $"USE \"{quotedCatalog}\"", cancellationToken, skipCatalogUse: true);
            }
            catch (QuackProtocolException ex) when (IsCatalogNotFound(ex.Message))
            {
                _logger.LogInformation("Catalog {Catalog} not found, attaching", config.Catalog);
                await PrepareAndExecuteAsync(config, header.ConnectionId, $"ATTACH '{literalCatalog}' AS \"{quotedCatalog}\"", cancellationToken, skipCatalogUse: true);
            }
            _logger.LogInformation("Catalog {Catalog} ready", config.Catalog);
        }

        return header.ConnectionId;
    }

    public async Task<ResultHandle> PrepareAndExecuteAsync(QuackProtocolConfig config, string connectionId, string sql, CancellationToken cancellationToken, bool skipCatalogUse = false)
    {
        var endpoint = config.Endpoint;

        // 当配置了 Catalog 时，每次查询前自动拼接 USE，利用 DuckDB 多语句执行能力
        // 确保查询在正确的 catalog 上下文中执行（Quack HTTP 协议不保持跨请求的 USE 状态）
        if (!skipCatalogUse && !string.IsNullOrEmpty(config.Catalog))
        {
            sql = $"USE \"{EscapeIdentifier(config.Catalog)}\"; {sql}";
        }

        _logger.LogDebug("Executing query: {Sql}", sql);

        var sw = Stopwatch.StartNew();
        var success = false;
        try
        {
            var result = await _retryPolicy.ExecuteAsync(async () =>
            {
                var writer = new QuackWriter((connectionId.Length + sql.Length) * 3 + 64);
                WritePrepareRequest(ref writer, connectionId, sql);
                byte[] response;
                try
                {
                    response = await _httpClient.PostAsync(endpoint, writer.Buffer, writer.Length, cancellationToken);
                }
                finally
                {
                    writer.Return();
                }
                var reader = new QuackBinaryReader(response);

                var header = ReadHeader(ref reader);
                if (header.Type == MessageType.ErrorResponse)
                {
                    var error = ReadErrorResponse(ref reader);
                    _logger.LogError("Query failed: {Error}", error.Message);
                    throw new QuackProtocolException($"Prepare failed: {error.Message}", header.StatusCode);
                }

                if (header.Type != MessageType.PrepareResponse)
                {
                    _logger.LogError("Expected PrepareResponse, got {MessageType}", header.Type);
                    throw new QuackProtocolException($"Expected PrepareResponse, got {header.Type}", header.StatusCode);
                }

                return ReadPrepareResponseWithResults(ref reader);
            }, cancellationToken);

            success = true;
            sw.Stop();
            _logger.LogInformation("Query executed in {ElapsedMs}ms, rows={RowCount}", sw.ElapsedMilliseconds, result.Rows.Count);

            return result;
        }
        finally
        {
            sw.Stop();
            _metrics?.RecordQuery(sw.Elapsed.TotalMilliseconds, success);
        }
    }

    public async Task<ResultHandle> FetchAsync(QuackProtocolConfig config, string connectionId, long uuidUpper, ulong uuidLower, CancellationToken cancellationToken)
    {
        return await FetchAsync(config, connectionId, uuidUpper, uuidLower, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ResultHandle> FetchAsync(QuackProtocolConfig config, string connectionId, ReadOnlyMemory<byte> uuidWireBytes, CancellationToken cancellationToken)
    {
        return await FetchAsync(config, connectionId, 0, 0, uuidWireBytes, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ResultHandle> FetchAsync(
        QuackProtocolConfig config,
        string connectionId,
        long uuidUpper,
        ulong uuidLower,
        ReadOnlyMemory<byte>? uuidWireBytes,
        CancellationToken cancellationToken)
    {
        var endpoint = config.Endpoint;
        _logger.LogDebug("Fetching more rows");

        var writer = new QuackWriter(connectionId.Length * 3 + 64);
        WriteFetchRequest(ref writer, connectionId, uuidUpper, uuidLower, uuidWireBytes);
        byte[] response;
        try
        {
            response = await _httpClient.PostAsync(endpoint, writer.Buffer, writer.Length, cancellationToken);
        }
        finally
        {
            writer.Return();
        }
        var reader = new QuackBinaryReader(response);

        var header = ReadHeader(ref reader);
        if (header.Type == MessageType.ErrorResponse)
        {
            var error = ReadErrorResponse(ref reader);
            _logger.LogError("Fetch failed: {Error}", error.Message);
            throw new QuackProtocolException($"Fetch failed: {error.Message}", header.StatusCode);
        }

        if (header.Type != MessageType.FetchResponse)
        {
            _logger.LogError("Expected FetchResponse, got {MessageType}", header.Type);
            throw new QuackProtocolException($"Expected FetchResponse, got {header.Type}", header.StatusCode);
        }

        var result = ReadFetchResponse(ref reader);
        _logger.LogDebug("Fetched {RowCount} rows", result.Rows.Count);

        return result;
    }

    public async Task DisconnectAsync(QuackProtocolConfig config, string connectionId, CancellationToken cancellationToken)
    {
        var endpoint = config.Endpoint;
        _logger.LogDebug("Disconnecting from {Endpoint}", endpoint);

        var writer = new QuackWriter(connectionId.Length * 3 + 64);
        WriteDisconnectRequest(ref writer, connectionId);
        try
        {
            await _httpClient.PostAsync(endpoint, writer.Buffer, writer.Length, cancellationToken);
        }
        finally
        {
            writer.Return();
        }

        _logger.LogInformation("Disconnected from {Endpoint}", endpoint);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static void WriteConnectionRequest(ref QuackWriter w, QuackProtocolConfig config)
    {
        // Header — field 3 (client_query_id) is required by the server's MessageHeader::Deserialize.
        w.WriteFieldId(1);
        w.WriteByte((byte)MessageType.ConnectionRequest);
        w.WriteFieldId(3);
        w.WriteVarUInt(0);
        w.WriteFieldId(0xFFFF);

        // Body: auth_string is required by the server — without it, the server rejects with 500.
        w.WriteFieldId(1);
        w.WriteString(config.Token);
        w.WriteFieldId(2);
        w.WriteString("1.5.3");
        w.WriteFieldId(3);
        w.WriteString("win-x64");
        w.WriteFieldId(4);
        w.WriteVarUInt(1);
        w.WriteFieldId(5);
        w.WriteVarUInt(1);
        w.WriteFieldId(0xFFFF);
    }

    private static void WritePrepareRequest(ref QuackWriter w, string connectionId, string sql)
    {
        w.WriteFieldId(1);
        w.WriteByte((byte)MessageType.PrepareRequest);
        w.WriteFieldId(2);
        w.WriteString(connectionId);
        w.WriteFieldId(3);
        w.WriteVarUInt(0);
        w.WriteFieldId(0xFFFF);

        w.WriteFieldId(1);
        w.WriteString(sql);
        w.WriteFieldId(0xFFFF);
    }

    private static void WriteFetchRequest(ref QuackWriter w, string connectionId, long uuidUpper, ulong uuidLower, ReadOnlyMemory<byte>? uuidWireBytes = null)
    {
        w.WriteFieldId(1);
        w.WriteByte((byte)MessageType.FetchRequest);
        w.WriteFieldId(2);
        w.WriteString(connectionId);
        w.WriteFieldId(3);
        w.WriteVarUInt(0);
        w.WriteFieldId(0xFFFF);

        w.WriteFieldId(1);
        if (uuidWireBytes is { Length: > 0 } rawUuid)
            w.WriteBytes(rawUuid.Span);
        else
            w.WriteHugeInt(uuidUpper, uuidLower);
        w.WriteFieldId(0xFFFF);
    }

    private static void WriteDisconnectRequest(ref QuackWriter w, string connectionId)
    {
        w.WriteFieldId(1);
        w.WriteByte((byte)MessageType.DisconnectMessage);
        w.WriteFieldId(2);
        w.WriteString(connectionId);
        w.WriteFieldId(3);
        w.WriteVarUInt(0);
        w.WriteFieldId(0xFFFF);

        w.WriteFieldId(0xFFFF);
    }

    internal static MessageHeader ReadHeader(ref QuackBinaryReader reader)
    {
        var type = MessageType.INVALID;
        var connectionId = "";
        var statusCode = 0;
        ulong clientQueryId = 0;

        while (reader.HasMore)
        {
            var fieldId = reader.ReadFieldId();
            if (fieldId == FieldTerminator) break;

            switch (fieldId)
            {
                case 1: type = (MessageType)reader.ReadVarUInt(); break;
                case 2: connectionId = ReadString(ref reader); break;
                case 3:
                    {
                        var val = reader.ReadVarUInt();
                        if (val > 0) clientQueryId = val - 1;
                        break;
                    }
                default:
                    SkipUnknownField(ref reader, fieldId);
                    break;
            }
        }

        return new MessageHeader { Type = type, ConnectionId = connectionId, ClientQueryId = clientQueryId, StatusCode = statusCode };
    }

    internal static ConnectionResponse ReadConnectionResponse(ref QuackBinaryReader reader)
    {
        string serverDuckDbVersion = "";
        string serverPlatform = "";
        ulong quackVersion = 0;

        while (reader.HasMore)
        {
            var fieldId = reader.ReadFieldId();
            if (fieldId == FieldTerminator) break;

            switch (fieldId)
            {
                case 1: serverDuckDbVersion = ReadString(ref reader); break;
                case 2: serverPlatform = ReadString(ref reader); break;
                case 3: quackVersion = reader.ReadVarUInt(); break;
                default:
                    SkipUnknownField(ref reader, fieldId);
                    break;
            }
        }

        return new ConnectionResponse { ServerDuckDbVersion = serverDuckDbVersion, ServerPlatform = serverPlatform, QuackVersion = quackVersion.ToString(CultureInfo.InvariantCulture) };
    }

    private static ResultHandle ReadPrepareResponseWithResults(ref QuackBinaryReader reader)
    {
        var columnTypes = new List<string>();
        var columnNames = new List<string>();
        bool needsMoreFetch = false;
        long uuidUpper = 0;
        ulong uuidLower = 0;
        byte[]? uuidWireBytes = null;
        ColumnarBatch? batch = null;

        while (reader.HasMore)
        {
            var fieldId = reader.ReadFieldId();
            if (fieldId == FieldTerminator) break;

            switch (fieldId)
            {
                case 1: ReadTypeList(ref reader, columnTypes); break;
                case 2: ReadStringList(ref reader, columnNames); break;
                case 3: needsMoreFetch = reader.ReadBool(); break;
                case 4:
                    var chunkBatch = ReadDataChunksColumnar(ref reader, columnTypes);
                    batch = batch is null ? chunkBatch : AppendBatch(batch, chunkBatch);
                    break;
                case 5: uuidWireBytes = ReadHugeInt(ref reader, out uuidUpper, out uuidLower); break;
                default:
                    SkipUnknownField(ref reader, fieldId);
                    break;
            }
        }

        return new ResultHandle
        {
            QueryId = 0,
            HasMoreRows = needsMoreFetch,
            Rows = [],
            ColumnTypes = columnTypes,
            ColumnNames = columnNames,
            UuidUpper = uuidUpper,
            UuidLower = uuidLower,
            UuidWireBytes = uuidWireBytes,
            Batch = batch
        };
    }

    private static ResultHandle ReadFetchResponse(ref QuackBinaryReader reader)
    {
        var columnTypes = new List<string>();
        ulong batchIndex = 0;
        long uuidUpper = 0;
        ulong uuidLower = 0;
        byte[]? uuidWireBytes = null;
        ColumnarBatch? batch = null;
        while (reader.HasMore)
        {
            var fieldId = reader.ReadFieldId();
            if (fieldId == FieldTerminator) break;

            switch (fieldId)
            {
                case 1:
                    var chunkBatch = ReadDataChunksColumnar(ref reader, columnTypes);
                    batch = batch is null ? chunkBatch : AppendBatch(batch, chunkBatch);
                    break;
                case 2: batchIndex = reader.ReadVarUInt(); break;
                case 5: uuidWireBytes = ReadHugeInt(ref reader, out uuidUpper, out uuidLower); break;
                default:
                    SkipUnknownField(ref reader, fieldId);
                    break;
            }
        }

        return new ResultHandle
        {
            QueryId = batchIndex,
            HasMoreRows = batch is { RowCount: > 0 },
            Rows = [],
            ColumnTypes = columnTypes,
            UuidUpper = uuidUpper,
            UuidLower = uuidLower,
            UuidWireBytes = uuidWireBytes,
            Batch = batch
        };
    }

    internal static ErrorResponse ReadErrorResponse(ref QuackBinaryReader reader)
    {
        string message = "";

        while (reader.HasMore)
        {
            var fieldId = reader.ReadFieldId();
            if (fieldId == FieldTerminator) break;

            switch (fieldId)
            {
                case 1: message = ReadString(ref reader); break;
                default:
                    SkipUnknownField(ref reader, fieldId);
                    break;
            }
        }

        return new ErrorResponse { Message = message };
    }

    /// <summary>一个解码后的列：类型标识 + typed 数据数组 + null 位图。</summary>
    private readonly struct TypedColumn(QuackColumnKind kind, Array data, byte[]? validity)
    {
        public readonly QuackColumnKind Kind = kind;
        public readonly Array Data = data;
        public readonly byte[]? Validity = validity;
    }

    private static ColumnarBatch ReadDataChunksColumnar(ref QuackBinaryReader reader, List<string> columnTypes)
    {
        var batches = new List<ColumnarBatch>();
        var count = reader.ReadVarUInt();
        for (ulong i = 0; i < count; i++)
        {
            var isPresent = reader.ReadBool();
            if (isPresent)
            {
                var wrapperField = reader.ReadFieldId();
                if (wrapperField == 300)
                {
                    var chunk = ReadDataChunkColumnar(ref reader, columnTypes);
                    if (chunk.RowCount > 0) batches.Add(chunk);
                    var wrapperTerminator = reader.ReadFieldId();
                    if (wrapperTerminator != FieldTerminator)
                    {
                        throw new QuackProtocolException(
                            $"Expected DataChunkWrapper terminator, got field {wrapperTerminator}.");
                    }
                }
                else
                {
                    SkipUnknownField(ref reader, wrapperField);
                }
            }
        }

        return batches.Count == 1 ? batches[0] : MergeBatches(batches, columnTypes.Count);
    }

    private static ColumnarBatch ReadDataChunkColumnar(ref QuackBinaryReader reader, List<string> columnTypes)
    {
        ulong rowCount = 0;
        var localColumnTypes = new List<string>();
        var vectors = new List<TypedColumn>();

        while (reader.HasMore)
        {
            var fieldId = reader.ReadFieldId();
            if (fieldId == FieldTerminator) break;

            switch (fieldId)
            {
                case 100: rowCount = reader.ReadVarUInt(); break;
                case 101: ReadTypeList(ref reader, localColumnTypes); break;
                case 102: ReadVectorList(ref reader, vectors, columnTypes.Count > 0 ? columnTypes : localColumnTypes, rowCount); break;
                default:
                    SkipUnknownField(ref reader, fieldId);
                    break;
            }
        }

        if (columnTypes.Count == 0 && localColumnTypes.Count > 0)
        {
            columnTypes.AddRange(localColumnTypes);
        }

        int n = (int)rowCount;
        int colCount = vectors.Count;
        var kinds = new QuackColumnKind[colCount];
        var columns = new Array?[colCount];
        var validity = new byte[]?[colCount];
        for (int c = 0; c < colCount; c++)
        {
            kinds[c] = vectors[c].Kind;
            columns[c] = vectors[c].Data;
            validity[c] = vectors[c].Validity;
        }

        return new ColumnarBatch(kinds, columns, validity, n);
    }

    private static ColumnarBatch MergeBatches(List<ColumnarBatch> batches, int columnCount)
    {
        var totalRows = 0;
        foreach (var b in batches) totalRows += b.RowCount;

        var kinds = new QuackColumnKind[columnCount];
        var columns = new Array?[columnCount];
        var validity = new byte[]?[columnCount];

        for (int c = 0; c < columnCount; c++)
        {
            // Kind from the first batch that defines this column; default to the Object fallback.
            var kind = QuackColumnKind.Object;
            foreach (var b in batches)
            {
                if (c < b.ColumnKinds.Length) { kind = b.ColumnKinds[c]; break; }
            }
            kinds[c] = kind;

            var dest = AllocateColumn(kind, totalRows);
            columns[c] = dest;

            var offset = 0;
            foreach (var b in batches)
            {
                if (c < b.Columns.Length && b.Columns[c] is { } src && b.RowCount > 0)
                {
                    Array.Copy(src, 0, dest, offset, Math.Min(src.Length, b.RowCount));
                }
                offset += b.RowCount;
            }

            validity[c] = MergeValidity(batches, c, totalRows);
        }

        return new ColumnarBatch(kinds, columns, validity, totalRows);
    }

    /// <summary>合并多批的同列 null 位图；仅在某批存在 null 时才分配结果位图。</summary>
    private static byte[]? MergeValidity(List<ColumnarBatch> batches, int col, int totalRows)
    {
        byte[]? result = null;
        var offset = 0;
        foreach (var b in batches)
        {
            var src = col < b.Validity.Length ? b.Validity[col] : null;
            if (src is null || src.Length == 0)
            {
                offset += b.RowCount;
                continue;
            }

            result ??= new byte[(totalRows + 7) >> 3];
            for (int i = 0; i < b.RowCount; i++)
            {
                if (((src[i >> 3] >> (i & 7)) & 1) != 0)
                {
                    int dst = offset + i;
                    result[dst >> 3] |= (byte)(1 << (dst & 7));
                }
            }
            offset += b.RowCount;
        }
        return result;
    }

    /// <summary>按列类型分配原生 typed 数组。</summary>
    private static Array AllocateColumn(QuackColumnKind kind, int length)
    {
        return kind switch
        {
            QuackColumnKind.Int64 => new long[length],
            QuackColumnKind.Double => new double[length],
            QuackColumnKind.Single => new float[length],
            QuackColumnKind.Boolean => new bool[length],
            QuackColumnKind.String => new string?[length],
            QuackColumnKind.Blob => new byte[]?[length],
            QuackColumnKind.Decimal => new decimal[length],
            QuackColumnKind.Guid => new Guid[length],
            QuackColumnKind.DateTime => new DateTime[length],
            QuackColumnKind.Date => new DateOnly[length],
            _ => new object?[length],
        };
    }

    private static ColumnarBatch AppendBatch(ColumnarBatch existing, ColumnarBatch extra)
    {
        return MergeBatches([existing, extra], Math.Max(existing.ColumnCount, extra.ColumnCount));
    }

    private static void ReadVectorList(ref QuackBinaryReader reader, List<TypedColumn> vectors, List<string> columnTypes, ulong rowCount)
    {
        var count = reader.ReadVarUInt();
        for (ulong i = 0; i < count; i++)
        {
            vectors.Add(ReadVector(ref reader, (int)i < columnTypes.Count ? columnTypes[(int)i] : "UNKNOWN", rowCount));
        }
    }

    private static TypedColumn ReadVector(ref QuackBinaryReader reader, string columnType, ulong rowCount)
    {
        byte[]? validity = null;
        (QuackColumnKind Kind, Array Data) data = (QuackColumnKind.Object, Array.Empty<object?>());

        while (reader.HasMore)
        {
            var fieldId = reader.ReadFieldId();
            if (fieldId == FieldTerminator) break;

            switch (fieldId)
            {
                case 100: reader.ReadVarUInt(); break;
                case 101: validity = ReadValidityBitmap(ref reader, rowCount); break;
                case 102: data = ReadVectorData(ref reader, columnType, (int)rowCount); break;
                default:
                    SkipUnknownField(ref reader, fieldId);
                    break;
            }
        }

        // Nulls are represented by the per-column validity bitmap (read above), not by overwriting
        // typed array elements — value-type arrays cannot hold null. The reader consults
        // ColumnarBatch.IsNull(col,row) before interpreting a cell.
        return new TypedColumn(data.Kind, data.Data, validity);
    }

    private static byte[] ReadValidityBitmap(ref QuackBinaryReader reader, ulong rowCount)
    {
        var length = (int)reader.ReadVarUInt();
        if (length > 0)
        {
            var bytes = reader.ReadBytes(length);
            return bytes.Span.ToArray();
        }
        return [];
    }

    private static (QuackColumnKind Kind, Array Data) ReadVectorData(ref QuackBinaryReader reader, string columnType, int rowCount)
    {
        // Normalise "DECIMAL(10,2)" → "DECIMAL" for dispatch, but keep the scale for decoding.
        var baseType = columnType.IndexOf('(') is var open && open >= 0
            ? columnType[..open]
            : columnType;

        return baseType switch
        {
            "INTEGER" => (QuackColumnKind.Int64, ReadIntVector(ref reader, rowCount)),
            "BIGINT" => (QuackColumnKind.Int64, ReadBigIntVector(ref reader, rowCount)),
            "HUGEINT" => (QuackColumnKind.Object, ReadHugeIntVector(ref reader, rowCount)),
            "VARCHAR" or "CHAR" => (QuackColumnKind.String, ReadStringVector(ref reader, rowCount)),
            "BLOB" => (QuackColumnKind.Blob, ReadBlobVector(ref reader, rowCount)),
            "BOOLEAN" => (QuackColumnKind.Boolean, ReadBoolVector(ref reader, rowCount)),
            "FLOAT" => (QuackColumnKind.Single, ReadFloatVector(ref reader, rowCount)),
            "DOUBLE" => (QuackColumnKind.Double, ReadDoubleVector(ref reader, rowCount)),
            "DECIMAL" => (QuackColumnKind.Decimal, ReadDecimalVector(ref reader, rowCount, ExtractScale(columnType))),
            "UUID" => (QuackColumnKind.Guid, ReadUuidVector(ref reader, rowCount)),
            "TIMESTAMP" or "TIMESTAMP_TZ" => (QuackColumnKind.DateTime, ReadTimestampVector(ref reader, rowCount)),
            "TIMESTAMP_SEC" => (QuackColumnKind.DateTime, ReadTimestampVector(ref reader, rowCount, TimestampUnit.Second)),
            "TIMESTAMP_MS" => (QuackColumnKind.DateTime, ReadTimestampVector(ref reader, rowCount, TimestampUnit.Millisecond)),
            "TIMESTAMP_NS" => (QuackColumnKind.DateTime, ReadTimestampVector(ref reader, rowCount, TimestampUnit.Nanosecond)),
            "DATE" => (QuackColumnKind.Date, ReadDateVector(ref reader, rowCount)),
            _ => SkipUnknownVectorData(ref reader),
        };
    }

    /// <summary>未知类型：跳过其数据负载，返回空 object 列。</summary>
    private static (QuackColumnKind, Array) SkipUnknownVectorData(ref QuackBinaryReader reader)
    {
        var length = (int)reader.ReadVarUInt();
        if (length > 0) reader.Skip(length);
        return (QuackColumnKind.Object, Array.Empty<object?>());
    }

    private static long[] ReadIntVector(ref QuackBinaryReader reader, int rowCount)
    {
        var length = (int)reader.ReadVarUInt();
        var bytes = reader.ReadBytes(length);
        var count = Math.Min(rowCount, length / 4);
        var data = new long[rowCount];
        for (int i = 0; i < count; i++)
            data[i] = BinaryPrimitives.ReadInt32LittleEndian(bytes.Span[(i * 4)..]);
        return data;
    }

    private static long[] ReadBigIntVector(ref QuackBinaryReader reader, int rowCount)
    {
        var length = (int)reader.ReadVarUInt();
        var bytes = reader.ReadBytes(length);
        var count = Math.Min(rowCount, length / 8);
        var data = new long[rowCount];
        for (int i = 0; i < count; i++)
            data[i] = BinaryPrimitives.ReadInt64LittleEndian(bytes.Span[(i * 8)..]);
        return data;
    }

    private static string?[] ReadStringVector(ref QuackBinaryReader reader, int rowCount)
    {
        var count = reader.ReadVarUInt();
        var readCount = Math.Min(rowCount, (int)count);
        var data = new string?[rowCount];
        for (int i = 0; i < readCount; i++)
        {
            var length = (int)reader.ReadVarUInt();
            // Preserve legacy semantics: a zero-length VARCHAR cell is stored as null
            // (GetString coerces back to "" on read).
            data[i] = length == 0 ? null : Encoding.UTF8.GetString(reader.ReadBytes(length).Span);
        }
        return data;
    }

    private static byte[]?[] ReadBlobVector(ref QuackBinaryReader reader, int rowCount)
    {
        var count = reader.ReadVarUInt();
        var readCount = Math.Min(rowCount, (int)count);
        var data = new byte[]?[rowCount];
        for (int i = 0; i < readCount; i++)
        {
            var length = (int)reader.ReadVarUInt();
            // Copy out of the reader's buffer — BLOB values must outlive the read span.
            data[i] = reader.ReadBytes(length).Span.ToArray();
        }
        return data;
    }

    private static bool[] ReadBoolVector(ref QuackBinaryReader reader, int rowCount)
    {
        var length = (int)reader.ReadVarUInt();
        var bytes = reader.ReadBytes(length);
        var count = Math.Min(rowCount, length);
        var data = new bool[rowCount];
        for (int i = 0; i < count; i++)
            data[i] = bytes.Span[i] != 0;
        return data;
    }

    private static float[] ReadFloatVector(ref QuackBinaryReader reader, int rowCount)
    {
        var length = (int)reader.ReadVarUInt();
        var bytes = reader.ReadBytes(length);
        var count = Math.Min(rowCount, length / 4);
        var data = new float[rowCount];
        for (int i = 0; i < count; i++)
            data[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.Span[(i * 4)..]);
        return data;
    }

    private static double[] ReadDoubleVector(ref QuackBinaryReader reader, int rowCount)
    {
        var length = (int)reader.ReadVarUInt();
        var bytes = reader.ReadBytes(length);
        var count = Math.Min(rowCount, length / 8);
        var data = new double[rowCount];
        for (int i = 0; i < count; i++)
            data[i] = BinaryPrimitives.ReadDoubleLittleEndian(bytes.Span[(i * 8)..]);
        return data;
    }

    private static decimal[] ReadDecimalVector(ref QuackBinaryReader reader, int rowCount, int scale)
    {
        var length = (int)reader.ReadVarUInt();
        var bytes = reader.ReadBytes(length);
        if (rowCount == 0) return Array.Empty<decimal>();
        var elemSize = length / rowCount;
        // DuckDB stores DECIMAL as a scaled two's-complement integer; divide by 10^scale to
        // recover the real value. Supports up to 38 digits (HUGEINT).
        var divisor = scale > 0 ? (decimal)Math.Pow(10, scale) : 1m;
        var count = Math.Min(rowCount, length / elemSize);
        var data = new decimal[rowCount];

        for (int i = 0; i < count; i++)
        {
            var offset = i * elemSize;
            decimal raw = elemSize switch
            {
                <= 2 => BinaryPrimitives.ReadInt16LittleEndian(bytes.Span[offset..]),
                <= 4 => BinaryPrimitives.ReadInt32LittleEndian(bytes.Span[offset..]),
                <= 8 => BinaryPrimitives.ReadInt64LittleEndian(bytes.Span[offset..]),
                // int128 (DECIMAL width 19..38): little-endian 16 bytes. The decimal(lo, mid, hi)
                // mantissa maps to bytes [0..3]/[4..7]/[8..11] respectively. lo and mid must not be
                // swapped, or the value is shifted left by 32 bits (the SUM(DECIMAL) regression).
                _ => new decimal(
                    BinaryPrimitives.ReadInt32LittleEndian(bytes.Span[offset..]),
                    BinaryPrimitives.ReadInt32LittleEndian(bytes.Span[(offset + 4)..]),
                    BinaryPrimitives.ReadInt32LittleEndian(bytes.Span[(offset + 8)..]),
                    false,
                    0)
            };
            data[i] = raw / divisor;
        }
        return data;
    }

    private static Guid[] ReadUuidVector(ref QuackBinaryReader reader, int rowCount)
    {
        var count = reader.ReadVarUInt();
        var readCount = Math.Min(rowCount, (int)count);
        var data = new Guid[rowCount];
        Span<byte> raw = stackalloc byte[16];
        Span<byte> rfc = stackalloc byte[16];
        for (int i = 0; i < readCount; i++)
        {
            // DuckDB stores a UUID as a 128-bit hugeint optimised for lexicographic comparison:
            // the RFC 4122 byte order is reversed, and the top bit is flipped so signed
            // ordering matches string ordering. Reverse the bytes back and flip the high bit.
            var src = reader.ReadBytes(16);
            src.Span.CopyTo(raw);
            for (int j = 0; j < 16; j++)
                rfc[j] = raw[15 - j];
            rfc[0] ^= 0x80;

            data[i] = new Guid(
                (rfc[0] << 24) | (rfc[1] << 16) | (rfc[2] << 8) | rfc[3],
                (short)((rfc[4] << 8) | rfc[5]),
                (short)((rfc[6] << 8) | rfc[7]),
                rfc[8], rfc[9], rfc[10], rfc[11], rfc[12], rfc[13], rfc[14], rfc[15]);
        }
        return data;
    }

    /// <summary>
    /// DuckDB 时间戳的存储精度。所有精度变体在 wire 上都是 8 字节有符号整数,
    /// 差别仅在物理值的单位(秒/毫秒/微秒/纳秒)。此处用于 <see cref="ReadTimestampVector"/> 换算。
    /// </summary>
    private enum TimestampUnit : byte
    {
        /// <summary>TIMESTAMP_SEC(TIMESTAMP_S):整数 = 自 epoch 的秒数。</summary>
        Second,
        /// <summary>TIMESTAMP_MS(TIMESTAMP_MS):整数 = 自 epoch 的毫秒数。</summary>
        Millisecond,
        /// <summary>TIMESTAMP / TIMESTAMP_TZ:整数 = 自 epoch 的微秒数(默认精度)。</summary>
        Microsecond,
        /// <summary>TIMESTAMP_NS(TIMESTAMP_NS):整数 = 自 epoch 的纳秒数。</summary>
        Nanosecond,
    }

    private static DateTime[] ReadTimestampVector(ref QuackBinaryReader reader, int rowCount)
        => ReadTimestampVector(ref reader, rowCount, TimestampUnit.Microsecond);

    /// <summary>
    /// 解码 DuckDB 时间戳列。所有精度变体物理布局相同(8 字节 LE 有符号整数),
    /// 仅单位不同,统一换算到 ticks 后构造 <see cref="DateTime"/>。
    /// NULL 行的物理值是 <see cref="long.MinValue"/> 哨兵(0x8000000000000000),
    /// 直接换算会溢出,这里保留 default(DateTime),由列的 validity 位图在 GetValue/IsNull 阶段判定为 null。
    /// </summary>
    private static DateTime[] ReadTimestampVector(ref QuackBinaryReader reader, int rowCount, TimestampUnit unit)
    {
        var length = (int)reader.ReadVarUInt();
        var bytes = reader.ReadBytes(length);
        var count = Math.Min(rowCount, length / 8);
        var data = new DateTime[rowCount];
        // 自 0001-01-01(epoch 之前)的 ticks。DuckDB 的整数为自 1970-01-01 的单位数。
        const long epochTicks = 621355968000000000L;
        for (int i = 0; i < count; i++)
        {
            var raw = BinaryPrimitives.ReadInt64LittleEndian(bytes.Span[(i * 8)..]);
            if (raw == long.MinValue)
                continue;

            // 将各单位的整数换算为 ticks(自 epoch)。
            long ticks = unit switch
            {
                TimestampUnit.Second => raw * TimeSpan.TicksPerSecond,
                TimestampUnit.Millisecond => raw * TimeSpan.TicksPerMillisecond,
                TimestampUnit.Microsecond => raw * (TimeSpan.TicksPerMillisecond / 1000), // 10 ticks/μs
                // 1 tick = 100ns。raw 是纳秒,ticks = raw / 100,余数(0..99ns)丢弃。
                // .NET DateTime 精度上限即 100ns,亚 100ns 部分无法表达——这是 DateTime 的固有约束,
                // 非桥接 bug;与 μs→ms 路径的整数截断处理一致。
                TimestampUnit.Nanosecond => raw / 100,
                _ => throw new InvalidOperationException($"Unsupported timestamp unit: {unit}")
            };
            data[i] = new DateTime(epochTicks + ticks, DateTimeKind.Utc);
        }
        return data;
    }

    // HUGEINT is heterogeneous: a value may be long (fits) or decimal (overflow), so it stays
    // object?[] and boxes — the only type that does. Rare enough to be an acceptable fallback.
    private static object?[] ReadHugeIntVector(ref QuackBinaryReader reader, int rowCount)
    {
        var length = (int)reader.ReadVarUInt();
        var bytes = reader.ReadBytes(length);
        var count = Math.Min(rowCount, length / 16);
        var data = new object?[rowCount];

        for (int i = 0; i < count; i++)
        {
            var offset = i * 16;
            var lower = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Span[offset..]);
            var upper = BinaryPrimitives.ReadInt64LittleEndian(bytes.Span[(offset + 8)..]);
            if (upper == 0 && lower <= long.MaxValue)
            {
                data[i] = (long)lower;
            }
            else if (upper == -1 && lower > (ulong)long.MaxValue)
            {
                data[i] = unchecked((long)lower);
            }
            else
            {
                data[i] = (decimal)upper * 18446744073709551616m + lower;
            }
        }
        return data;
    }

    private static DateOnly[] ReadDateVector(ref QuackBinaryReader reader, int rowCount)
    {
        var length = (int)reader.ReadVarUInt();
        var bytes = reader.ReadBytes(length);
        var count = Math.Min(rowCount, length / 4);
        var data = new DateOnly[rowCount];
        for (int i = 0; i < count; i++)
        {
            var days = BinaryPrimitives.ReadInt32LittleEndian(bytes.Span[(i * 4)..]);
            // NULL 行的物理值是 Int32.MinValue 哨兵(0x80000000);若 +719162 后交给
            // DateOnly.FromDayNumber 会溢出。这里保留 default(DateOnly),由列的 validity
            // 位图在 GetValue/IsNull 阶段判定为 null(与其它值类型列一致的约定)。
            if (days == int.MinValue)
                continue;
            data[i] = DateOnly.FromDayNumber(days + 719162);
        }
        return data;
    }

    private static void ReadTypeList(ref QuackBinaryReader reader, List<string> types)
    {
        var count = reader.ReadVarUInt();
        for (ulong i = 0; i < count; i++)
        {
            var typeName = "";
            while (reader.HasMore)
            {
                var fieldId = reader.ReadFieldId();
                if (fieldId == FieldTerminator) break;

                if (fieldId == 100)
                {
                    typeName = LogicalTypeIdToString(reader.ReadVarUInt());
                }
                else if (fieldId == 101)
                {
                    // type_info carries Decimal width/scale, List/Array child_type, etc.
                    // For DECIMAL we need the scale to decode the scaled-int storage.
                    var info = ReadTypeInfo(ref reader);
                    if (typeName == "DECIMAL" && info.Scale.HasValue)
                    {
                        typeName = $"DECIMAL({info.Width ?? 0},{info.Scale.Value})";
                    }
                }
                else
                {
                    SkipUnknownField(ref reader, fieldId);
                }
            }
            types.Add(typeName);
        }
    }

    private sealed record TypeInfo(int? Width, int? Scale);

    /// <summary>
    /// Reads a DuckDB nullable type_info envelope and extracts the fields we care about
    /// (Decimal width=200, scale=201). Other ExtraTypeInfo subclasses are walked past safely.
    /// </summary>
    private static TypeInfo ReadTypeInfo(ref QuackBinaryReader reader)
    {
        var present = reader.ReadBool();
        if (!present) return new TypeInfo(null, null);

        int? width = null;
        int? scale = null;
        while (reader.HasMore)
        {
            var fieldId = reader.ReadFieldId();
            if (fieldId == FieldTerminator) break;

            switch (fieldId)
            {
                case 100: reader.ReadVarUInt(); break; // ExtraTypeInfoType enum
                case 101: ReadString(ref reader); break; // alias
                case 103: SkipNullableObject(ref reader); break; // extension_info
                case 200: width = (int)reader.ReadVarUInt(); break;
                case 201: scale = (int)reader.ReadVarUInt(); break;
                case 202: reader.ReadVarUInt(); break;
                default:
                    throw new QuackProtocolException(
                        $"Unknown field {fieldId} inside type_info; cannot skip safely.");
            }
        }

        return new TypeInfo(width, scale);
    }

    private static int ExtractScale(string columnType)
    {
        // columnType is like "DECIMAL(10,2)" — pull the scale out of the second parameter.
        var open = columnType.IndexOf('(');
        if (open < 0) return 0;
        var close = columnType.IndexOf(')', open);
        if (close < 0) return 0;
        var args = columnType.Substring(open + 1, close - open - 1).Split(',');
        return args.Length >= 2 && int.TryParse(args[1].Trim(), out var s) ? s : 0;
    }

    /// <summary>
    /// Skips a DuckDB nullable envelope: 1 present byte, then if present, a nested object
    /// (sequence of field-id/value pairs terminated by 0xFFFF).
    /// Used for ExtraTypeInfo (Decimal width/scale, List child_type, etc.) where we don't
    /// need the contents — only the type enum from the outer LogicalType matters to us.
    /// </summary>
    private static void SkipNullableObject(ref QuackBinaryReader reader)
    {
        var present = reader.ReadBool();
        if (!present) return;

        while (reader.HasMore)
        {
            var fieldId = reader.ReadFieldId();
            if (fieldId == FieldTerminator) return;

            switch (fieldId)
            {
                // Common ExtraTypeInfo fields observed across Decimal/List/Array/Map subclasses.
                // 100/200/201/202 are varuint in most subclasses; 101 is a string alias;
                // 103 is a recursive nullable object. 200/201 can also be nested LogicalType
                // objects (List/Array child_type) — handle that case by peeking for an object.
                case 100:
                case 200:
                case 201:
                case 202:
                    reader.ReadVarUInt();
                    break;
                case 101:
                    ReadString(ref reader);
                    break;
                case 103:
                    SkipNullableObject(ref reader);
                    break;
                default:
                    throw new QuackProtocolException(
                        $"Unknown field {fieldId} inside nullable type_info; cannot skip safely.");
            }
        }
    }

    private static void ReadStringList(ref QuackBinaryReader reader, List<string> strings)
    {
        var count = reader.ReadVarUInt();
        for (ulong i = 0; i < count; i++)
        {
            strings.Add(ReadString(ref reader));
        }
    }

    private static string ReadString(ref QuackBinaryReader reader)
    {
        var length = (int)reader.ReadVarUInt();
        if (length == 0) return "";

        var bytes = reader.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes.Span);
    }

    private static byte[] ReadHugeInt(ref QuackBinaryReader reader, out long upper, out ulong lower)
    {
        var start = reader.Position;
        lower = reader.ReadVarUInt();
        upper = reader.ReadVarInt();
        return reader.Data.Slice(start, reader.Position - start).ToArray();
    }

    /// <summary>
    /// Raises a protocol error for an unknown field. The wire format is not self-describing for leaf
    /// values, so silently skipping an unknown field would misalign every subsequent parse — we surface
    /// the divergence instead and let the caller decide whether to update the parser.
    /// </summary>
    private static void SkipUnknownField(ref QuackBinaryReader reader, ushort fieldId)
    {
        throw new QuackProtocolException($"Unknown protocol field {fieldId} encountered; parser cannot skip safely.");
    }

    private static string LogicalTypeIdToString(ulong typeId)
    {
        return typeId switch
        {
            10 => "BOOLEAN",
            11 => "TINYINT",
            12 => "SMALLINT",
            13 => "INTEGER",
            14 => "BIGINT",
            15 => "DATE",
            16 => "TIME",
            17 => "TIMESTAMP_SEC",
            18 => "TIMESTAMP_MS",
            19 => "TIMESTAMP",
            20 => "TIMESTAMP_NS",
            21 => "DECIMAL",
            22 => "FLOAT",
            23 => "DOUBLE",
            24 => "CHAR",
            25 => "VARCHAR",
            26 => "BLOB",
            27 => "INTERVAL",
            28 => "UTINYINT",
            29 => "USMALLINT",
            30 => "UINTEGER",
            31 => "UBIGINT",
            32 => "TIMESTAMP_TZ",
            34 => "TIME_TZ",
            36 => "BIT",
            50 => "HUGEINT",
            54 => "UUID",
            _ => $"UNKNOWN_{typeId}"
        };
    }

    /// <summary>
    /// 零分配的协议写入器：写入到从 <see cref="ArrayPool{T}"/> 租借的可增长缓冲区，
    /// 替代每请求 <c>new MemoryStream() + new BinaryWriter() + ToArray()</c> 的分配链。
    /// 编码与旧 BinaryWriter 实现逐字节一致（fieldId=ushort LE、字符串=VarUInt 长度前缀+UTF8、
    /// 整数=LEB128）。调用方在请求发送完成后必须调用 <see cref="Return"/> 归还缓冲区。
    /// </summary>
    private struct QuackWriter
    {
        private byte[] _buffer;
        private int _position;

        public QuackWriter(int initialCapacity)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity <= 0 ? 64 : initialCapacity);
            _position = 0;
        }

        public byte[] Buffer => _buffer;
        public int Length => _position;

        public void Return()
        {
            if (_buffer is not null)
                ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null!;
        }

        private void Ensure(int needed)
        {
            if (_position + needed <= _buffer.Length) return;
            var size = _buffer.Length;
            while (size < _position + needed) size <<= 1;
            var grown = ArrayPool<byte>.Shared.Rent(size);
            _buffer.AsSpan(0, _position).CopyTo(grown);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = grown;
        }

        public void WriteFieldId(ushort fieldId)
        {
            Ensure(2);
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_position), fieldId);
            _position += 2;
        }

        public void WriteByte(byte value)
        {
            Ensure(1);
            _buffer[_position++] = value;
        }

        public void WriteBytes(ReadOnlySpan<byte> value)
        {
            Ensure(value.Length);
            value.CopyTo(_buffer.AsSpan(_position));
            _position += value.Length;
        }

        public void WriteVarUInt(ulong value)
        {
            Ensure(10); // 64-bit LEB128 worst case
            while (value > 0x7F)
            {
                _buffer[_position++] = (byte)(value & 0x7F | 0x80);
                value >>= 7;
            }
            _buffer[_position++] = (byte)value;
        }

        public void WriteVarInt(long value)
        {
            Ensure(10); // 64-bit signed LEB128 worst case
            var more = true;
            while (more)
            {
                var current = (byte)(value & 0x7F);
                var signBitSet = (current & 0x40) != 0;
                value >>= 7;

                more = !((value == 0 && !signBitSet) || (value == -1 && signBitSet));
                if (more)
                    current |= 0x80;

                _buffer[_position++] = current;
            }
        }

        public void WriteString(string value)
        {
            var byteCount = Encoding.UTF8.GetByteCount(value);
            WriteVarUInt((ulong)byteCount);
            Ensure(byteCount);
            Encoding.UTF8.GetBytes(value, _buffer.AsSpan(_position));
            _position += byteCount;
        }

        public void WriteHugeInt(long upper, ulong lower)
        {
            WriteVarUInt(lower);
            WriteVarInt(upper);
        }
    }

    private static string EscapeIdentifier(string identifier)
    {
        return identifier.Replace("\"", "\"\"");
    }

    /// <summary>
    /// 单引号字符串字面量转义：SQL 标准用 '' 表示一个字面量单引号。
    /// 用于 ATTACH '...' 这类被单引号包围的上下文（区别于双引号标识符上下文）。
    /// </summary>
    private static string EscapeStringLiteral(string value)
    {
        return value.Replace("'", "''");
    }

    /// <summary>
    /// Checks whether a DuckDB error message indicates the target catalog/schema was not found,
    /// which is the expected failure mode when the client requests a catalog that hasn't been
    /// ATTACHed on the server yet.
    /// </summary>
    private static bool IsCatalogNotFound(string message)
    {
        return message.Contains("No catalog", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
    }
}

internal enum MessageType : byte
{
    INVALID = 0,
    ConnectionRequest = 1,
    ConnectionResponse = 2,
    PrepareRequest = 3,
    PrepareResponse = 4,
    FetchRequest = 7,
    FetchResponse = 8,
    AppendRequest = 9,
    SuccessResponse = 10,
    DisconnectMessage = 11,
    ErrorResponse = 100,
}

internal sealed class MessageHeader
{
    public MessageType Type { get; init; }
    public string ConnectionId { get; init; } = "";
    public ulong ClientQueryId { get; init; }
    public int StatusCode { get; init; }
}

internal sealed class ConnectionResponse
{
    public string ServerDuckDbVersion { get; init; } = "";
    public string ServerPlatform { get; init; } = "";
    public string QuackVersion { get; init; } = "";
}

internal sealed class ErrorResponse
{
    public string Message { get; init; } = "";
}

internal sealed class ResultHandle
{
    public ulong QueryId { get; init; }
    public bool HasMoreRows { get; init; }
    public List<List<object?>> Rows { get; init; } = [];
    public List<string> ColumnTypes { get; init; } = [];
    public List<string> ColumnNames { get; init; } = [];
    public long UuidUpper { get; init; }
    public ulong UuidLower { get; init; }
    public byte[]? UuidWireBytes { get; init; }
    public ColumnarBatch? Batch { get; init; }
}
