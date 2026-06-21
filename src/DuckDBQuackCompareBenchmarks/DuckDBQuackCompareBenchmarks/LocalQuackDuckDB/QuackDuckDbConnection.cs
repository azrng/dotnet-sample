using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using DuckDB.NET.Data;

namespace Quack.DuckDB;

/// <summary>
/// Quack DuckDB 连接，继承 DbConnection 以兼容 ADO.NET 体系
/// 主要供 synyi-manhattan-sim-webapi 的 IDataProvider 体系使用
/// </summary>
public sealed class QuackDuckDbConnection : DbConnection
{
    // 保护 _provider 与 _state 的转换，避免 Open/Close/ConnectionString setter 交叉执行。
    private readonly object _syncRoot = new();
    private QuackConnectionConfig _config;
    private QuackDataProvider? _provider;
    private ConnectionState _state = ConnectionState.Closed;

    public QuackDuckDbConnection(string connectionString)
    {
        _config = QuackConnectionStringParser.Parse(connectionString);
    }

    public QuackDuckDbConnection(QuackConnectionConfig config)
    {
        config.Validate();
        _config = config;
    }

    [AllowNull]
    public override string ConnectionString
    {
        get => $"{_config.Host}:{_config.Port}";
        set
        {
            lock (_syncRoot)
            {
                if (_state != ConnectionState.Closed)
                    throw new InvalidOperationException("连接打开后不能修改连接字符串。");

                _config = QuackConnectionStringParser.Parse(value ?? "");
            }
        }
    }

    public override string Database => _config.Catalog;
    public override string DataSource => QuackConnectionStringParser.BuildQuackUri(_config.Host, _config.Port);
    public override string ServerVersion => "quack";
    public override ConnectionState State => _state;
    public override int ConnectionTimeout => 0;

    /// <summary>
    /// 获取底层 QuackDataProvider（供高级场景使用）
    /// </summary>
    public QuackDataProvider GetProvider()
    {
        EnsureOpen();
        return _provider!;
    }

    public override void Open()
    {
        lock (_syncRoot)
        {
            if (_state == ConnectionState.Open)
                return;

            if (_state == ConnectionState.Connecting)
                throw new InvalidOperationException("连接正在打开。");

            _state = ConnectionState.Connecting;

            try
            {
                var provider = new QuackDataProvider(_config);
                if (_config.Attach)
                    provider.AttachRemote();
                _provider = provider;
                _state = ConnectionState.Open;
            }
            catch
            {
                _provider?.Dispose();
                _provider = null;
                _state = ConnectionState.Closed;
                throw;
            }
        }
    }

    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        // 连接初始化包含本地扩展加载和远端 ATTACH，放到线程池避免阻塞调用线程。
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(Open, cancellationToken);
    }

    public override void Close()
    {
        lock (_syncRoot)
        {
            _provider?.Dispose();
            _provider = null;
            _state = ConnectionState.Closed;
        }
    }

    public override void ChangeDatabase(string databaseName)
    {
        throw new NotSupportedException("Quack 远程数据库切换不支持");
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        EnsureOpen();
        return _provider!.Connection.BeginTransaction(isolationLevel);
    }

    protected override DbCommand CreateDbCommand()
    {
        EnsureOpen();
        if (_config.Attach)
            return _provider!.Connection.CreateCommand();

        return new QuackDbCommand(_provider!.Connection, _provider);
    }

    internal string BuildQuackQuerySql(string innerSql)
    {
        return _provider?.BuildQuackQuerySql(innerSql)
               ?? throw new InvalidOperationException("连接未打开，请先调用 Open 或 OpenAsync。");
    }

    internal string BuildQuackQuerySqlForTest(string innerSql)
    {
        using var provider = new QuackDataProvider(_config);
        return provider.BuildQuackQuerySql(innerSql);
    }

    internal bool UseAttachMode()
    {
        return _config.Attach;
    }

    internal string BuildAttachSql()
    {
        return $"ATTACH '{QuackConnectionStringParser.BuildQuackUri(_config.Host, _config.Port)}' AS {_config.Catalog} (TYPE quack, TOKEN '{EscapeSqlLiteral(_config.Token)}', DISABLE_SSL {_config.DisableSsl.ToString().ToLowerInvariant()});";
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("\\", "\\\\").Replace("'", "''");
    }

    private void EnsureOpen()
    {
        // ADO.NET 调用方应显式打开连接，避免 CreateCommand 等 API 隐式触发网络连接。
        if (_state != ConnectionState.Open)
            throw new InvalidOperationException("连接未打开，请先调用 Open 或 OpenAsync。");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Close();

        base.Dispose(disposing);
    }
}

internal sealed class QuackDbCommand : DbCommand
{
    private readonly DuckDBConnection _innerConnection;
    private readonly QuackDataProvider _provider;
    private readonly QuackDbParameterCollection _parameters = new();
    private string _commandText = string.Empty;
    private int _commandTimeout = 30;

    public QuackDbCommand(DuckDBConnection innerConnection, QuackDataProvider provider)
    {
        _innerConnection = innerConnection;
        _provider = provider;
    }

    [AllowNull]
    public override string CommandText
    {
        get => _commandText;
        set => _commandText = value ?? string.Empty;
    }

    public override int CommandTimeout
    {
        get => _commandTimeout;
        set => _commandTimeout = value;
    }

    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    protected override DbTransaction? DbTransaction { get; set; }
    protected override DbParameterCollection DbParameterCollection => _parameters;

    public override void Cancel() { }

    public override int ExecuteNonQuery()
    {
        using var command = CreateInnerCommand();
        return command.ExecuteNonQuery();
    }

    public override object? ExecuteScalar()
    {
        using var command = CreateInnerCommand();
        return command.ExecuteScalar();
    }

    public override void Prepare() { }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        var command = CreateInnerCommand();
        try
        {
            var reader = command.ExecuteReader(behavior);
            return new QuackDbDataReaderWrapper(reader, command);
        }
        catch
        {
            command.Dispose();
            throw;
        }
    }

    protected override DbParameter CreateDbParameter()
    {
        return new QuackDbParameter();
    }

    private DbCommand CreateInnerCommand()
    {
        var command = _innerConnection.CreateCommand();
        SetCommandText(command, _provider.BuildQuackQuerySql(BuildResolvedSql()));
        command.CommandTimeout = _commandTimeout;
        return command;
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Quack table function requires SQL literals; connection and parameter values are escaped before assignment.")]
    private static void SetCommandText(DbCommand command, string commandText)
    {
        command.CommandText = commandText;
    }

    private string BuildResolvedSql()
    {
        var sql = _commandText;
        foreach (QuackDbParameter parameter in _parameters)
        {
            foreach (var placeholder in GetPlaceholders(parameter.ParameterName))
            {
                var pattern = Regex.Escape(placeholder) + @"(?!\w)";
                sql = Regex.Replace(sql, pattern, FormatParameterValue(parameter.Value), RegexOptions.IgnoreCase);
            }
        }

        return sql;
    }

    private static IEnumerable<string> GetPlaceholders(string parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
            yield break;

        if (parameterName.StartsWith("@", StringComparison.Ordinal) ||
            parameterName.StartsWith("$", StringComparison.Ordinal))
        {
            yield return parameterName;
            yield break;
        }

        yield return $"@{parameterName}";

        if (parameterName.All(char.IsDigit))
            yield return $"${parameterName}";
    }

    private static string FormatParameterValue(object? value)
    {
        return value switch
        {
            null => "NULL",
            DBNull => "NULL",
            string s => $"'{EscapeSqlLiteral(s)}'",
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
            DateTimeOffset dto => $"'{dto:yyyy-MM-dd HH:mm:ss}'",
            DateOnly d => $"'{d:yyyy-MM-dd}'",
            TimeOnly t => $"'{t:HH:mm:ss}'",
            bool b => b ? "TRUE" : "FALSE",
            byte or sbyte or short or ushort or int or uint or long or ulong => value.ToString() ?? "NULL",
            float f => f.ToString("G", CultureInfo.InvariantCulture),
            double d => d.ToString("G", CultureInfo.InvariantCulture),
            decimal m => m.ToString("G", CultureInfo.InvariantCulture),
            Guid g => $"'{g:D}'",
            byte[] bytes => $"'{Convert.ToHexString(bytes)}'",
            _ => $"'{EscapeSqlLiteral(value.ToString() ?? string.Empty)}'"
        };
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("\\", "\\\\").Replace("'", "''");
    }
}

internal sealed class QuackDbDataReaderWrapper : DbDataReader
{
    private readonly DbDataReader _inner;
    private readonly DbCommand _command;
    private bool _disposed;

    public QuackDbDataReaderWrapper(DbDataReader inner, DbCommand command)
    {
        _inner = inner;
        _command = command;
    }

    public override object this[int ordinal] => _inner[ordinal];
    public override object this[string name] => _inner[name];
    public override int Depth => _inner.Depth;
    public override int FieldCount => _inner.FieldCount;
    public override bool HasRows => _inner.HasRows;
    public override bool IsClosed => _inner.IsClosed;
    public override int RecordsAffected => _inner.RecordsAffected;
    public override bool GetBoolean(int ordinal) => _inner.GetBoolean(ordinal);
    public override byte GetByte(int ordinal) => _inner.GetByte(ordinal);
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => _inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
    public override char GetChar(int ordinal) => _inner.GetChar(ordinal);
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => _inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
    public override DateTime GetDateTime(int ordinal) => _inner.GetDateTime(ordinal);
    public override decimal GetDecimal(int ordinal) => _inner.GetDecimal(ordinal);
    public override double GetDouble(int ordinal) => _inner.GetDouble(ordinal);
    public override Type GetFieldType(int ordinal) => _inner.GetFieldType(ordinal);
    public override float GetFloat(int ordinal) => _inner.GetFloat(ordinal);
    public override Guid GetGuid(int ordinal) => _inner.GetGuid(ordinal);
    public override short GetInt16(int ordinal) => _inner.GetInt16(ordinal);
    public override int GetInt32(int ordinal) => _inner.GetInt32(ordinal);
    public override long GetInt64(int ordinal) => _inner.GetInt64(ordinal);
    public override string GetName(int ordinal) => _inner.GetName(ordinal);
    public override int GetOrdinal(string name) => _inner.GetOrdinal(name);
    public override string GetString(int ordinal) => _inner.GetString(ordinal);
    public override object GetValue(int ordinal) => _inner.GetValue(ordinal);
    public override int GetValues(object[] values) => _inner.GetValues(values);
    public override bool IsDBNull(int ordinal) => _inner.IsDBNull(ordinal);
    public override bool NextResult() => _inner.NextResult();
    public override bool Read() => _inner.Read();
    public override Task<bool> ReadAsync(CancellationToken cancellationToken) => _inner.ReadAsync(cancellationToken);
    public override Type GetProviderSpecificFieldType(int ordinal) => _inner.GetProviderSpecificFieldType(ordinal);
    public override object GetProviderSpecificValue(int ordinal) => _inner.GetProviderSpecificValue(ordinal);
    public override int GetProviderSpecificValues(object[] values) => _inner.GetProviderSpecificValues(values);
    public override string GetDataTypeName(int ordinal) => _inner.GetDataTypeName(ordinal);
    public override System.Collections.IEnumerator GetEnumerator() => _inner.GetEnumerator();

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _inner.Dispose();
                _command.Dispose();
            }

            _disposed = true;
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
            await _command.DisposeAsync().ConfigureAwait(false);
            _disposed = true;
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class QuackDbParameter : DbParameter
{
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }
    [AllowNull]
    public override string ParameterName { get; set; } = string.Empty;
    public override int Size { get; set; }
    [AllowNull]
    public override string SourceColumn { get; set; } = string.Empty;
    public override bool SourceColumnNullMapping { get; set; }
    [AllowNull]
    public override object? Value { get; set; }

    public override void ResetDbType()
    {
        DbType = DbType.String;
    }
}

internal sealed class QuackDbParameterCollection : DbParameterCollection
{
    private readonly List<QuackDbParameter> _parameters = new();

    public override int Count => _parameters.Count;
    public override object SyncRoot => ((System.Collections.ICollection)_parameters).SyncRoot;

    public override int Add(object value)
    {
        _parameters.Add((QuackDbParameter)value);
        return _parameters.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (QuackDbParameter parameter in values)
            _parameters.Add(parameter);
    }

    public override void Clear() => _parameters.Clear();
    public override bool Contains(object value) => _parameters.Contains((QuackDbParameter)value);
    public override bool Contains(string value) => _parameters.Any(p => p.ParameterName == value);
    public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)_parameters).CopyTo(array, index);
    public override System.Collections.IEnumerator GetEnumerator() => _parameters.GetEnumerator();
    public override int IndexOf(object value) => _parameters.IndexOf((QuackDbParameter)value);
    public override int IndexOf(string parameterName) => _parameters.FindIndex(p => p.ParameterName == parameterName);
    public override void Insert(int index, object value) => _parameters.Insert(index, (QuackDbParameter)value);
    public override void Remove(object value) => _parameters.Remove((QuackDbParameter)value);
    public override void RemoveAt(int index) => _parameters.RemoveAt(index);
    public override void RemoveAt(string parameterName) => _parameters.RemoveAll(p => p.ParameterName == parameterName);
    protected override DbParameter GetParameter(int index) => _parameters[index];
    protected override DbParameter GetParameter(string parameterName) => _parameters.First(p => p.ParameterName == parameterName);
    protected override void SetParameter(int index, DbParameter value) => _parameters[index] = (QuackDbParameter)value;

    protected override void SetParameter(string parameterName, DbParameter value)
    {
        var index = IndexOf(parameterName);
        if (index >= 0)
        {
            _parameters[index] = (QuackDbParameter)value;
        }
        else
        {
            _parameters.Add((QuackDbParameter)value);
        }
    }
}
