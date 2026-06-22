using System.Data;
using System.Data.Common;

namespace Azrng.DuckDB.Quack;

/// <summary>
/// Transaction implementation for Quack protocol.
/// Uses BEGIN, COMMIT, ROLLBACK SQL statements.
/// </summary>
public sealed class QuackTransaction : DbTransaction
{
    private readonly QuackConnection _connection;
    private readonly IsolationLevel _isolationLevel;
    private bool _disposed;
    private bool _committed;

    /// <summary>
    /// 初始化 QuackTransaction 实例。
    /// </summary>
    /// <param name="connection">关联的数据库连接。</param>
    /// <param name="isolationLevel">事务隔离级别，若为 Unspecified 则默认使用 ReadCommitted。</param>
    internal QuackTransaction(QuackConnection connection, IsolationLevel isolationLevel)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _isolationLevel = isolationLevel == IsolationLevel.Unspecified
            ? IsolationLevel.ReadCommitted
            : isolationLevel;
    }

    /// <summary>
    /// 获取事务的隔离级别。
    /// </summary>
    public override IsolationLevel IsolationLevel => _isolationLevel;

    /// <summary>
    /// 获取与此事务关联的数据库连接。
    /// </summary>
    protected override DbConnection DbConnection => _connection;

    /// <summary>
    /// 同步提交事务。
    /// </summary>
    public override void Commit()
    {
        CommitAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 异步提交事务。
    /// </summary>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public new async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_committed)
            throw new InvalidOperationException("Transaction has already been committed.");

        await using var command = _connection.CreateCommand();
        command.CommandText = "COMMIT";
        await ((DbCommand)command).ExecuteNonQueryAsync(cancellationToken);

        _committed = true;
        _connection.ClearTransaction();
    }

    /// <summary>
    /// 同步回滚事务。
    /// </summary>
    public override void Rollback()
    {
        RollbackAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 异步回滚事务。
    /// </summary>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public new async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_committed)
            throw new InvalidOperationException("Transaction has already been committed and cannot be rolled back.");

        await using var command = _connection.CreateCommand();
        command.CommandText = "ROLLBACK";
        await ((DbCommand)command).ExecuteNonQueryAsync(cancellationToken);

        _committed = true;
        _connection.ClearTransaction();
    }

    /// <summary>
    /// 释放事务占用的资源。若事务尚未提交，将自动回滚。
    /// </summary>
    /// <param name="disposing">若为 true 则释放托管资源和非托管资源；否则仅释放非托管资源。</param>
    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;

            if (disposing && !_committed)
            {
                try
                {
                    Rollback();
                }
                catch
                {
                    // Ignore errors during dispose rollback
                }
            }
        }

        base.Dispose(disposing);
    }
}
