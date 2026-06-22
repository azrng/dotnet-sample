using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Azrng.DuckDB.Quack;

/// <summary>
/// DuckDB 数据库命令的实现，封装了 SQL 语句的执行逻辑。
/// </summary>
public sealed class QuackCommand : DbCommand
{
    private readonly QuackParameterCollection _parameters = new();
    private QuackConnection? _connection;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// 初始化 <see cref="QuackCommand"/> 的新实例。
    /// </summary>
    /// <param name="connection">关联的 DuckDB 数据库连接。</param>
    internal QuackCommand(QuackConnection connection)
    {
        _connection = connection;
    }

    /// <summary>
    /// 获取或设置要执行的 SQL 语句文本。
    /// </summary>
    [AllowNull]
    public override string CommandText { get; set; } = "";

    /// <summary>
    /// 获取或设置命令执行的超时时间（秒）。默认值为 0，表示不设置超时。
    /// </summary>
    public override int CommandTimeout { get; set; }

    /// <summary>
    /// 获取或设置命令的类型。当前仅支持 <see cref="CommandType.Text"/>。
    /// </summary>
    public override CommandType CommandType { get; set; } = CommandType.Text;

    /// <summary>
    /// 获取或设置命令在设计时是否可见。
    /// </summary>
    public override bool DesignTimeVisible { get; set; }

    /// <summary>
    /// 获取或设置命令在运行 Update 后如何将结果应用于 <see cref="DataRow"/>。
    /// </summary>
    public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.None;

    protected override DbConnection? DbConnection
    {
        get => _connection;
        set => _connection = (QuackConnection?)value;
    }

    protected override DbParameterCollection DbParameterCollection => _parameters;
    protected override DbTransaction? DbTransaction { get; set; }

    /// <summary>
    /// 尝试取消正在执行的命令。
    /// </summary>
    public override void Cancel()
    {
        _cts?.Cancel();
    }

    /// <summary>
    /// 同步执行 SQL 语句并返回受影响的行数。
    /// </summary>
    /// <returns>受影响的行数。</returns>
    public override int ExecuteNonQuery()
    {
        return ExecuteNonQueryAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 异步执行 SQL 语句并返回受影响的行数。
    /// </summary>
    /// <param name="cancellationToken">用于取消异步操作的令牌。</param>
    /// <returns>受影响的行数。</returns>
    public new async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is null || _connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Connection is not open.");

        if (CommandType != CommandType.Text)
            throw new NotSupportedException("Quack private protocol v1 only supports text commands.");

        if (string.IsNullOrWhiteSpace(CommandText))
            throw new InvalidOperationException("CommandText cannot be empty.");

        _connection.AcquireQueryLock();
        try
        {
            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            if (CommandTimeout > 0)
                _cts.CancelAfter(TimeSpan.FromSeconds(CommandTimeout));

            var sql = QuackParameterSqlRenderer.Render(CommandText, _parameters);
            var result = await _connection.Bridge
                .ExecuteQueryAsync(_connection.Session, sql, _cts.Token)
                .ConfigureAwait(false);

            // For DDL/DML, return the number of affected rows
            // DuckDB returns affected row count in the result
            return result.Rows.Count > 0 && result.Rows[0].Length > 0
                ? Convert.ToInt32(result.Rows[0][0])
                : 0;
        }
        finally
        {
            _connection.ReleaseQueryLock();
        }
    }

    /// <summary>
    /// 执行查询并返回结果集中第一行第一列的值。
    /// </summary>
    /// <returns>查询结果的第一行第一列值，如果结果集为空则返回 <c>null</c>。</returns>
    public override object? ExecuteScalar()
    {
        using var reader = ExecuteReader();
        return reader.Read() ? reader.GetValue(0) : null;
    }

    /// <summary>
    /// 预编译命令以提高执行效率。当前协议不支持此操作，调用将抛出异常。
    /// </summary>
    public override void Prepare()
    {
        throw new NotSupportedException("Quack private protocol v1 does not support prepared statements.");
    }

    protected override DbParameter CreateDbParameter()
    {
        return new QuackParameter();
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        return ExecuteDbDataReaderAsync(behavior, CancellationToken.None).GetAwaiter().GetResult();
    }

    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        if (_connection is null || _connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Connection is not open.");

        if (CommandType != CommandType.Text)
            throw new NotSupportedException("Quack private protocol v1 only supports text commands.");

        if (string.IsNullOrWhiteSpace(CommandText))
            throw new InvalidOperationException("CommandText cannot be empty.");

        _connection.AcquireQueryLock();
        try
        {
            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            if (CommandTimeout > 0)
                _cts.CancelAfter(TimeSpan.FromSeconds(CommandTimeout));

            var sql = QuackParameterSqlRenderer.Render(CommandText, _parameters);
            var result = await _connection.Bridge
                .ExecuteQueryAsync(_connection.Session, sql, _cts.Token)
                .ConfigureAwait(false);

            return new QuackDataReader(_connection.Bridge, _connection.Session, result, _connection, behavior);
        }
        catch
        {
            _connection.ReleaseQueryLock();
            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts?.Dispose();
            _cts = null;
        }

        base.Dispose(disposing);
    }
}
