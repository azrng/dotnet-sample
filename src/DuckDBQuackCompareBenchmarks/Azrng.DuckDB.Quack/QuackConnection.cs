using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Azrng.DuckDB.Quack;

/// <summary>
/// DuckDB 数据库连接的实现，封装了 Quack 协议的连接管理。
/// 支持连接状态管理、事务处理和健康检查。
/// </summary>
public sealed class QuackConnection : DbConnection
{
    private readonly object _syncRoot = new();
    private readonly Func<IQuackProtocolBridge> _bridgeFactory;
    private QuackProtocolConfig _config;
    private IQuackProtocolBridge? _bridge;
    private QuackProtocolSession? _session;
    private ConnectionState _state = ConnectionState.Closed;
    private bool _queryInProgress;
    private bool _disposed;
    private QuackTransaction? _currentTransaction;

    /// <summary>
    /// 连接状态发生变化时触发的事件。
    /// </summary>
    public event EventHandler<ConnectionStateEventArgs>? StateChanged;

    /// <summary>
    /// 发生错误时触发的事件。
    /// </summary>
    public event EventHandler<ConnectionErrorEventArgs>? ErrorOccurred;

    /// <summary>
    /// 使用默认连接字符串初始化 <see cref="QuackConnection"/> 的新实例。
    /// </summary>
    public QuackConnection()
        : this("Host=localhost;Port=9494;Token=change-me")
    {
    }

    /// <summary>
    /// 使用指定的连接字符串初始化 <see cref="QuackConnection"/> 的新实例。
    /// </summary>
    /// <param name="connectionString">数据库连接字符串。</param>
    public QuackConnection(string connectionString)
        : this(connectionString, () => new PureQuackProtocolBridge())
    {
    }

    /// <summary>
    /// 使用指定的协议配置初始化 <see cref="QuackConnection"/> 的新实例。
    /// </summary>
    /// <param name="config">协议连接配置。</param>
    public QuackConnection(QuackProtocolConfig config)
        : this(config, () => new PureQuackProtocolBridge())
    {
    }

    /// <summary>
    /// 使用连接字符串和桥接工厂初始化（内部使用）。
    /// </summary>
    /// <param name="connectionString">数据库连接字符串。</param>
    /// <param name="bridgeFactory">协议桥接器的工厂方法。</param>
    internal QuackConnection(string connectionString, Func<IQuackProtocolBridge> bridgeFactory)
        : this(QuackProtocolConfig.FromConnectionString(connectionString), bridgeFactory)
    {
    }

    /// <summary>
    /// 使用协议配置和桥接工厂初始化（内部使用）。
    /// </summary>
    /// <param name="config">协议连接配置。</param>
    /// <param name="bridgeFactory">协议桥接器的工厂方法。</param>
    internal QuackConnection(QuackProtocolConfig config, Func<IQuackProtocolBridge> bridgeFactory)
    {
        config.Validate();
        _config = config;
        _bridgeFactory = bridgeFactory;
    }

    /// <summary>获取协议桥接器实例。连接未打开时抛出异常。</summary>
    internal IQuackProtocolBridge Bridge => _bridge ?? throw new InvalidOperationException("Connection is not open.");
    /// <summary>获取当前协议会话。连接未打开时抛出异常。</summary>
    internal QuackProtocolSession Session => _session ?? throw new InvalidOperationException("Connection is not open.");

    /// <summary>
    /// 获取查询锁，确保同一连接上不会有并发查询。
    /// </summary>
    internal void AcquireQueryLock()
    {
        lock (_syncRoot)
        {
            if (_state != ConnectionState.Open)
                throw new InvalidOperationException("Connection is not open.");

            if (_queryInProgress)
                throw new InvalidOperationException("A query is already executing on this connection. Quack private protocol v1 does not support concurrent queries on a single session.");

            _queryInProgress = true;
        }
    }

    /// <summary>
    /// 释放查询锁，允许执行新的查询。
    /// </summary>
    internal void ReleaseQueryLock()
    {
        lock (_syncRoot)
        {
            _queryInProgress = false;
        }
    }

    [AllowNull]
    public override string ConnectionString
    {
        get => _config.ToString();
        set
        {
            lock (_syncRoot)
            {
                if (_state != ConnectionState.Closed)
                    throw new InvalidOperationException("Connection string cannot be changed while the connection is open.");

                _config = QuackProtocolConfig.FromConnectionString(value ?? "");
            }
        }
    }

    /// <summary>获取当前数据库名称，始终返回空字符串。</summary>
    public override string Database => "";
    /// <summary>获取连接字符串中配置的 Catalog 名称，未配置时为 null。</summary>
    public string? Catalog => _config.Catalog;
    /// <summary>获取数据源的端点地址。</summary>
    public override string DataSource => _config.Endpoint.ToString();
    /// <summary>获取服务器版本信息。</summary>
    public override string ServerVersion => "quack-protocol";
    /// <summary>获取当前连接的状态。</summary>
    public override ConnectionState State => _state;
    /// <summary>获取连接超时时间（秒）。</summary>
    public override int ConnectionTimeout => _config.TimeoutSeconds;

    /// <summary>
    /// 打开数据库连接（同步方式）。
    /// </summary>
    public override void Open()
    {
        OpenAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 异步打开数据库连接。
    /// </summary>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public override async Task OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (_state == ConnectionState.Open)
                return;

            if (_state == ConnectionState.Connecting)
                throw new InvalidOperationException("Connection is already opening.");

            _state = ConnectionState.Connecting;
            _bridge = _bridgeFactory();
        }

        OnStateChanged(ConnectionState.Closed, ConnectionState.Connecting);

        try
        {
            var session = await _bridge!.ConnectAsync(_config, cancellationToken).ConfigureAwait(false);
            lock (_syncRoot)
            {
                _session = session;
                _state = ConnectionState.Open;
            }

            OnStateChanged(ConnectionState.Connecting, ConnectionState.Open);
        }
        catch (Exception ex)
        {
            lock (_syncRoot)
            {
                _bridge?.Dispose();
                _bridge = null;
                _session = null;
                _state = ConnectionState.Closed;
            }

            OnStateChanged(ConnectionState.Connecting, ConnectionState.Closed);
            OnError(ex);
            throw;
        }
    }

    /// <summary>
    /// 关闭数据库连接并释放相关资源。
    /// </summary>
    public override void Close()
    {
        IQuackProtocolBridge? bridge;
        QuackProtocolSession? session;

        lock (_syncRoot)
        {
            bridge = _bridge;
            session = _session;
            _bridge = null;
            _session = null;
            _state = ConnectionState.Closed;
        }

        if (bridge is not null && session is not null)
        {
            try
            {
                bridge.CloseSessionAsync(session, CancellationToken.None).GetAwaiter().GetResult();
            }
            finally
            {
                bridge.Dispose();
            }
        }
        else
        {
            bridge?.Dispose();
        }

        OnStateChanged(ConnectionState.Open, ConnectionState.Closed);
    }

    /// <summary>
    /// 切换当前数据库（Quack 协议不支持此操作）。
    /// </summary>
    /// <param name="databaseName">目标数据库名称。</param>
    public override void ChangeDatabase(string databaseName)
    {
        throw new NotSupportedException("Quack private protocol does not support ChangeDatabase.");
    }

    /// <summary>
    /// 通过执行简单查询检查连接是否健康。
    /// </summary>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>如果连接健康则返回 true；否则返回 false。</returns>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        if (_state != ConnectionState.Open || _bridge is null || _session is null)
            return false;

        try
        {
            using var command = CreateDbCommand();
            command.CommandText = "SELECT 1";
            using var reader = await ((DbCommand)command).ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 开始数据库事务（同步方式）。
    /// </summary>
    /// <param name="isolationLevel">事务隔离级别。</param>
    /// <returns>新创建的事务对象。</returns>
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        return BeginTransactionAsync(isolationLevel, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 异步开始数据库事务。
    /// </summary>
    /// <param name="isolationLevel">事务隔离级别，默认为 Unspecified（将使用 ReadCommitted）。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>新创建的事务对象。</returns>
    public new async Task<QuackTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.Unspecified, CancellationToken cancellationToken = default)
    {
        if (_state != ConnectionState.Open)
            throw new InvalidOperationException("Connection is not open.");

        if (_currentTransaction is not null)
            throw new InvalidOperationException("A transaction is already in progress on this connection.");

        // Begin transaction
        await using var beginCmd = CreateDbCommand();
        beginCmd.CommandText = "BEGIN";
        await ((DbCommand)beginCmd).ExecuteNonQueryAsync(cancellationToken);

        _currentTransaction = new QuackTransaction(this, isolationLevel == IsolationLevel.Unspecified ? IsolationLevel.ReadCommitted : isolationLevel);
        return _currentTransaction;
    }

    /// <summary>
    /// 清除当前事务引用（内部使用）。
    /// </summary>
    internal void ClearTransaction()
    {
        _currentTransaction = null;
    }

    /// <summary>
    /// 创建与当前连接关联的命令对象。
    /// </summary>
    /// <returns>新创建的命令对象。</returns>
    protected override DbCommand CreateDbCommand()
    {
        if (_state != ConnectionState.Open)
            throw new InvalidOperationException("Connection is not open.");

        return new QuackCommand(this);
    }

    /// <summary>
    /// 释放连接占用的资源。
    /// </summary>
    /// <param name="disposing">如果为 true 则释放托管资源。</param>
    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;

            if (disposing)
            {
                _currentTransaction?.Dispose();
                _currentTransaction = null;
                Close();
            }
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// 触发连接状态变化事件。
    /// </summary>
    /// <param name="oldState">旧的连接状态。</param>
    /// <param name="newState">新的连接状态。</param>
    private void OnStateChanged(ConnectionState oldState, ConnectionState newState)
    {
        StateChanged?.Invoke(this, new ConnectionStateEventArgs(oldState, newState));
    }

    /// <summary>
    /// 触发错误事件。
    /// </summary>
    /// <param name="exception">发生的异常。</param>
    private void OnError(Exception exception)
    {
        ErrorOccurred?.Invoke(this, new ConnectionErrorEventArgs(exception));
    }
}
