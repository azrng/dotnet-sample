using System.Collections;
using System.Data;
using System.Data.Common;
using Azrng.DuckDB.Quack.Internal;

namespace Azrng.DuckDB.Quack;

/// <summary>
/// DuckDB 查询结果的数据读取器，实现 <see cref="DbDataReader"/> 接口。
/// 支持分批获取（Fetch）和列式存储（ColumnarBatch）两种数据访问模式。
/// </summary>
public sealed class QuackDataReader : DbDataReader
{
    private readonly IQuackProtocolBridge _bridge;
    private readonly QuackProtocolSession _session;
    private readonly QuackConnection? _connection;
    private IReadOnlyList<QuackColumnInfo> _columns;
    private IReadOnlyList<object?[]> _rows;
    private ColumnarBatch? _batch;
    private string? _fetchToken;
    private bool _hasMoreRows;
    private int _cursor = -1;
    private bool _closed;
    private readonly int _recordsAffected;
    private readonly CommandBehavior _behavior;

    /// <summary>
    /// 初始化 <see cref="QuackDataReader"/> 实例。
    /// </summary>
    /// <param name="bridge">协议桥接器，用于执行 Fetch 请求。</param>
    /// <param name="session">当前协议会话。</param>
    /// <param name="result">查询结果数据。</param>
    /// <param name="connection">关联的连接（可选）。</param>
    /// <param name="behavior">命令行为标志。</param>
    internal QuackDataReader(IQuackProtocolBridge bridge, QuackProtocolSession session, QuackQueryResult result, QuackConnection? connection = null, CommandBehavior behavior = CommandBehavior.Default)
    {
        _bridge = bridge;
        _session = session;
        _connection = connection;
        _behavior = behavior;
        _columns = result.Columns;
        _rows = result.Rows;
        _batch = result.Batch;
        _fetchToken = result.FetchToken;
        _hasMoreRows = result.HasMoreRows;
        _recordsAffected = TryReadAffectedCount(result);
    }

    private int CurrentRowCount => _batch is not null ? _batch.RowCount : _rows.Count;

    /// <summary>获取当前结果集中的列数。</summary>
    public override int FieldCount => _columns.Count;
    /// <summary>获取一个值，指示当前结果集中是否包含至少一行数据。</summary>
    public override bool HasRows => CurrentRowCount > 0 || _hasMoreRows;
    /// <summary>获取一个值，指示数据读取器是否已关闭。</summary>
    public override bool IsClosed => _closed;
    /// <summary>获取执行 INSERT、UPDATE 或 DELETE 语句所影响的行数；如果未执行此类语句则返回 -1。</summary>
    public override int RecordsAffected => _recordsAffected;
    /// <summary>获取当前行的嵌套深度，始终返回 0。</summary>
    public override int Depth => 0;
    /// <summary>通过列序号获取当前行中指定列的值。</summary>
    /// <param name="ordinal">从零开始的列序号。</param>
    /// <returns>当前行中指定列的值。</returns>
    public override object this[int ordinal] => GetValue(ordinal);
    /// <summary>通过列名称获取当前行中指定列的值。</summary>
    /// <param name="name">列名称。</param>
    /// <returns>当前行中指定列的值。</returns>
    public override object this[string name] => GetValue(GetOrdinal(name));

    /// <summary>使读取器前进到下一条记录。</summary>
    /// <returns>如果存在下一行则返回 true；否则返回 false。</returns>
    public override bool Read()
    {
        return ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>异步使读取器前进到下一条记录。当当前批次读取完毕时自动请求下一批次数据。</summary>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>如果存在下一行则返回 true；否则返回 false。</returns>
    public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        ThrowIfClosed();

        // SchemaOnly exposes column metadata but no data rows (ADO.NET contract). The Quack
        // protocol always materialises the result set, so we honour the contract by withholding
        // rows rather than by suppressing the fetch.
        if ((_behavior & CommandBehavior.SchemaOnly) != 0)
            return false;

        // SingleRow yields exactly one row; after the first, surface EOF.
        if ((_behavior & CommandBehavior.SingleRow) != 0 && _cursor >= 0)
            return false;

        if (_cursor + 1 < CurrentRowCount)
        {
            _cursor++;
            return true;
        }

        if (!_hasMoreRows || _fetchToken is null)
            return false;

        // Loop (not recurse) so a malicious server streaming empty batches cannot blow the stack.
        while (true)
        {
            var next = await _bridge.FetchAsync(_session, _fetchToken, cancellationToken).ConfigureAwait(false);
            if (next is null)
            {
                _hasMoreRows = false;
                return false;
            }

            _columns = next.Columns;
            _rows = next.Rows;
            _batch = next.Batch;
            _fetchToken = next.FetchToken;
            _hasMoreRows = next.HasMoreRows;
            _cursor = -1;

            if (CurrentRowCount > 0)
            {
                _cursor = 0;
                return true;
            }

            if (!_hasMoreRows || _fetchToken is null)
                return false;
        }
    }

    /// <summary>使读取器前进到下一个结果集。DuckDB 协议不支持多结果集，始终返回 false。</summary>
    /// <returns>始终返回 false。</returns>
    public override bool NextResult()
    {
        return false;
    }

    /// <summary>获取指定列序号的列名称。</summary>
    /// <param name="ordinal">从零开始的列序号。</param>
    /// <returns>列名称。</returns>
    public override string GetName(int ordinal)
    {
        return _columns[ordinal].Name;
    }

    /// <summary>获取指定列序号的数据类型名称（DuckDB 类型字符串）。</summary>
    /// <param name="ordinal">从零开始的列序号。</param>
    /// <returns>数据类型名称字符串。</returns>
    public override string GetDataTypeName(int ordinal)
    {
        return _columns[ordinal].TypeName;
    }

    /// <summary>获取指定列序号对应的 .NET 类型。</summary>
    /// <param name="ordinal">从零开始的列序号。</param>
    /// <returns>该列的 .NET <see cref="Type"/>。</returns>
    public override Type GetFieldType(int ordinal)
    {
        return _columns[ordinal].FieldType;
    }

    /// <summary>根据列名称获取其对应的序号（不区分大小写）。</summary>
    /// <param name="name">列名称。</param>
    /// <returns>列序号；如果未找到匹配的列则返回 -1。</returns>
    public override int GetOrdinal(string name)
    {
        for (var i = 0; i < _columns.Count; i++)
        {
            if (string.Equals(_columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    /// <summary>获取当前行中指定列序号的值，null 值将返回 <see cref="DBNull.Value"/>。</summary>
    /// <param name="ordinal">从零开始的列序号。</param>
    /// <returns>列的值。</returns>
    public override object GetValue(int ordinal)
    {
        ThrowIfClosed();
        EnsureOnRow();
        if (_batch is not null)
            return _batch.GetValue(ordinal, _cursor) ?? DBNull.Value;
        return _rows[_cursor][ordinal] ?? DBNull.Value;
    }

    /// <summary>将当前行的所有列值填充到指定数组中。</summary>
    /// <param name="values">用于接收列值的目标数组。</param>
    /// <returns>实际写入的列数。</returns>
    public override int GetValues(object[] values)
    {
        var count = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < count; i++)
            values[i] = GetValue(i);

        return count;
    }

    /// <summary>获取一个值，指示当前行中指定列序号的值是否为 null。</summary>
    /// <param name="ordinal">从零开始的列序号。</param>
    /// <returns>如果该列为 null 则返回 true；否则返回 false。</returns>
    public override bool IsDBNull(int ordinal)
    {
        ThrowIfClosed();
        EnsureOnRow();
        if (_batch is not null)
            return _batch.IsNull(ordinal, _cursor);
        return _rows[_cursor][ordinal] is null;
    }

    /// <summary>获取当前行中指定列的布尔值。</summary>
    /// <param name="ordinal">从零开始的列序号。</param>
    /// <returns>布尔值。</returns>
    public override bool GetBoolean(int ordinal)
    {
        ThrowIfClosed();
        EnsureOnRow();
        return _batch is not null ? _batch.GetBoolean(ordinal, _cursor) : Convert.ToBoolean(_rows[_cursor][ordinal]);
    }
    /// <summary>获取当前行中指定列的字节值。</summary>
    /// <param name="ordinal">从零开始的列序号。</param>
    /// <returns>字节值。</returns>
    public override byte GetByte(int ordinal)
    {
        ThrowIfClosed();
        EnsureOnRow();
        return _batch is not null ? _batch.GetByte(ordinal, _cursor) : Convert.ToByte(_rows[_cursor][ordinal]);
    }
    /// <summary>获取当前行中指定列的字符值。</summary>
    /// <param name="ordinal">从零开始的列序号。</param>
    /// <returns>字符值。</returns>
    public override char GetChar(int ordinal) => Convert.ToChar(GetValue(ordinal));
    /// <summary>获取当前行中指定列的字节数组。此方法不受支持，调用时将抛出异常。</summary>
    /// <param name="ordinal">从零开始的列序号。</param>
    /// <param name="dataOffset">数据偏移量。</param>
    /// <param name="buffer">接收数据的缓冲区。</param>
    /// <param name="bufferOffset">缓冲区偏移量。</param>
    /// <param name="length">要读取的字节数。</param>
    /// <returns>不适用，始终抛出 <see cref="NotSupportedException"/>。</returns>
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    /// <summary>获取当前行中指定列的字符数组。此方法不受支持，调用时将抛出异常。</summary>
    /// <param name="ordinal">从零开始的列序号。</param>
    /// <param name="dataOffset">数据偏移量。</param>
    /// <param name="buffer">接收数据的缓冲区。</param>
    /// <param name="bufferOffset">缓冲区偏移量。</param>
    /// <param name="length">要读取的字符数。</param>
    /// <returns>不适用，始终抛出 <see cref="NotSupportedException"/>。</returns>
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    /// <summary>获取当前行中指定列的 GUID 值（从字符串解析）。</summary>
    /// <param name="ordinal">从零开始的列序号。</param>
    /// <returns>GUID 值。</returns>
    public override Guid GetGuid(int ordinal) => Guid.Parse(GetString(ordinal));
    /// <summary>获取当前行中指定列的 16 位有符号整数值。</summary>
    /// <param name="ordinal">从零开始的列序号。</param>
    /// <returns>16 位有符号整数值。</returns>
    public override short GetInt16(int ordinal)
    {
        ThrowIfClosed();
        EnsureOnRow();
        return _batch is not null ? _batch.GetInt16(ordinal, _cursor) : Convert.ToInt16(_rows[_cursor][ordinal]);
    }
    /// <summary>获取当前行中指定列的 32 位有符号整数值。</summary>
    /// <param name="ordinal">从零开始的列序号。</param>
    /// <returns>32 位有符号整数值。</returns>
    public override int GetInt32(int ordinal)
    {
        ThrowIfClosed();
        EnsureOnRow();
        return _batch is not null ? _batch.GetInt32(ordinal, _cursor) : Convert.ToInt32(_rows[_cursor][ordinal]);
    }
    /// <summary>获取当前行中指定列的 64 位有符号整数值。</summary>
    /// <param name="ordinal">从零开始的列序号。</param>
    /// <returns>64 位有符号整数值。</returns>
    public override long GetInt64(int ordinal)
    {
        ThrowIfClosed();
        EnsureOnRow();
        return _batch is not null ? _batch.GetInt64(ordinal, _cursor) : Convert.ToInt64(_rows[_cursor][ordinal]);
    }
    /// <summary>获取当前行中指定列的单精度浮点值。</summary>
    /// <param name="ordinal">从零开始的列序号。</param>
    /// <returns>单精度浮点值。</returns>
    public override float GetFloat(int ordinal)
    {
        ThrowIfClosed();
        EnsureOnRow();
        return _batch is not null ? _batch.GetSingle(ordinal, _cursor) : Convert.ToSingle(_rows[_cursor][ordinal]);
    }
    /// <summary>获取当前行中指定列的双精度浮点值。</summary>
    /// <param name="ordinal">从零开始的列序号。</param>
    /// <returns>双精度浮点值。</returns>
    public override double GetDouble(int ordinal)
    {
        ThrowIfClosed();
        EnsureOnRow();
        return _batch is not null ? _batch.GetDouble(ordinal, _cursor) : Convert.ToDouble(_rows[_cursor][ordinal]);
    }
    /// <summary>获取当前行中指定列的字符串值。</summary>
    /// <param name="ordinal">从零开始的列序号。</param>
    /// <returns>字符串值，null 值返回空字符串。</returns>
    public override string GetString(int ordinal)
    {
        ThrowIfClosed();
        EnsureOnRow();
        if (_batch is not null)
            return _batch.GetString(ordinal, _cursor) ?? "";
        return Convert.ToString(_rows[_cursor][ordinal]) ?? "";
    }
    /// <summary>获取当前行中指定列的十进制值。</summary>
    /// <param name="ordinal">从零开始的列序号。</param>
    /// <returns>十进制值。</returns>
    public override decimal GetDecimal(int ordinal)
    {
        ThrowIfClosed();
        EnsureOnRow();
        return _batch is not null ? _batch.GetDecimal(ordinal, _cursor) : Convert.ToDecimal(_rows[_cursor][ordinal]);
    }
    /// <summary>获取当前行中指定列的日期时间值。</summary>
    /// <param name="ordinal">从零开始的列序号。</param>
    /// <returns>日期时间值。</returns>
    public override DateTime GetDateTime(int ordinal) => Convert.ToDateTime(GetValue(ordinal));
    /// <summary>返回一个枚举器，用于遍历结果集中的所有行。支持列式存储和行式存储两种模式。</summary>
    /// <returns>行枚举器。</returns>
    public override IEnumerator GetEnumerator()
    {
        if (_batch is not null)
        {
            for (int r = 0; r < _batch.RowCount; r++)
            {
                var row = new object?[_batch.ColumnCount];
                for (int c = 0; c < _batch.ColumnCount; c++)
                    row[c] = _batch.GetValue(c, r);
                yield return row;
            }
            yield break;
        }
        foreach (var row in _rows)
            yield return row;
    }

    /// <summary>返回包含结果集列元数据的 <see cref="DataTable"/>（SchemaTable）。</summary>
    /// <returns>包含列名称、序号、数据类型、精度和标度等信息的 DataTable。</returns>
    public override DataTable GetSchemaTable()
    {
        var table = new DataTable("SchemaTable");

        table.Columns.Add("ColumnName", typeof(string));
        table.Columns.Add("ColumnOrdinal", typeof(int));
        table.Columns.Add("ColumnSize", typeof(int));
        table.Columns.Add("DataType", typeof(Type));
        table.Columns.Add("DataTypeName", typeof(string));
        table.Columns.Add("AllowDBNull", typeof(bool));
        table.Columns.Add("IsUnique", typeof(bool));
        table.Columns.Add("IsKey", typeof(bool));
        table.Columns.Add("IsLong", typeof(bool));
        table.Columns.Add("NumericPrecision", typeof(byte));
        table.Columns.Add("NumericScale", typeof(byte));

        for (var i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i];
            var (precision, scale) = GetNumericPrecisionScale(column.TypeName);

            table.Rows.Add(
                column.Name,
                i,
                GetColumnSize(column.TypeName),
                column.FieldType,
                column.TypeName,
                true,
                false,
                false,
                IsLongType(column.TypeName),
                precision,
                scale);
        }

        return table;
    }

    /// <summary>异步使读取器前进到下一个结果集。DuckDB 协议不支持多结果集，始终返回 false。</summary>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>始终返回已完成的 false 任务。</returns>
    public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    /// <summary>关闭数据读取器，释放查询锁。如果指定了 <see cref="CommandBehavior.CloseConnection"/>，则同时关闭关联的连接。</summary>
    public override void Close()
    {
        if (!_closed)
        {
            _closed = true;
            _connection?.ReleaseQueryLock();

            // CloseConnection binds the connection's lifetime to the reader's: once the reader
            // closes, tear the underlying connection down too.
            if ((_behavior & CommandBehavior.CloseConnection) != 0)
                _connection?.Close();
        }

        base.Close();
    }

    /// <summary>释放 <see cref="QuackDataReader"/> 占用的资源。</summary>
    /// <param name="disposing">如果为 true 则同时释放托管资源。</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Close();

        base.Dispose(disposing);
    }

    private void ThrowIfClosed()
    {
        if (_closed)
            throw new InvalidOperationException("DataReader is closed.");
    }

    private static int GetColumnSize(string typeName)
    {
        return typeName.ToUpperInvariant() switch
        {
            "BOOLEAN" or "BOOL" => 1,
            "TINYINT" or "UTINYINT" => 1,
            "SMALLINT" or "USMALLINT" => 2,
            "INTEGER" or "INT" or "UINTEGER" => 4,
            "BIGINT" or "UBIGINT" => 8,
            "FLOAT" or "REAL" => 4,
            "DOUBLE" => 8,
            "DATE" => 4,
            "TIME" => 8,
            "TIMESTAMP" or "TIMESTAMP_TZ" => 8,
            "UUID" => 16,
            _ => -1
        };
    }

    private static (byte Precision, byte Scale) GetNumericPrecisionScale(string typeName)
    {
        var upper = typeName.ToUpperInvariant();
        if (upper.StartsWith("DECIMAL", StringComparison.Ordinal) || upper.StartsWith("NUMERIC", StringComparison.Ordinal))
        {
            var open = typeName.IndexOf('(');
            if (open >= 0)
            {
                var close = typeName.IndexOf(')', open);
                if (close > open)
                {
                    var parts = typeName[(open + 1)..close].Split(',');
                    if (parts.Length >= 2 && byte.TryParse(parts[0].Trim(), out var p) && byte.TryParse(parts[1].Trim(), out var s))
                        return (p, s);
                    if (parts.Length == 1 && byte.TryParse(parts[0].Trim(), out var p2))
                        return (p2, 0);
                }
            }

            return (38, 9);
        }

        return (0, 0);
    }

    private static bool IsLongType(string typeName)
    {
        return string.Equals(typeName, "BLOB", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(typeName, "BIT", StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureOnRow()
    {
        if (_cursor < 0 || _cursor >= CurrentRowCount)
            throw new InvalidOperationException("DataReader is not positioned on a row.");
    }

    private static int TryReadAffectedCount(QuackQueryResult result)
    {
        object? firstValue;
        if (result.Batch is { RowCount: > 0, ColumnCount: > 0 })
        {
            firstValue = result.Batch.GetValue(0, 0);
        }
        else if (result.Rows.Count > 0 && result.Rows[0].Length > 0)
        {
            firstValue = result.Rows[0][0];
        }
        else
        {
            return -1;
        }

        return firstValue switch
        {
            long l => (int)l,
            int i => i,
            _ => -1
        };
    }
}
