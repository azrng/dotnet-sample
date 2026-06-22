using System.Data;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// QuackConnectionExtendedTests 的单元测试
/// </summary>
public class QuackConnectionExtendedTests
{
    /// <summary>
    /// Database 返回EmptyString
    /// </summary>
    [Fact]
    public void Database_ReturnsEmptyString()
    {
        using var connection = QuackConnectionTests.CreateConnection(new FakeQuackProtocolBridge());

        Assert.Equal("", connection.Database);
    }

    /// <summary>
    /// DataSource 返回Endpoint
    /// </summary>
    [Fact]
    public void DataSource_ReturnsEndpoint()
    {
        using var connection = QuackConnectionTests.CreateConnection(new FakeQuackProtocolBridge());

        Assert.Contains("quack.example", connection.DataSource);
        Assert.Contains("9494", connection.DataSource);
    }

    /// <summary>
    /// ServerVersion 返回QuackProtocol
    /// </summary>
    [Fact]
    public void ServerVersion_ReturnsQuackProtocol()
    {
        using var connection = QuackConnectionTests.CreateConnection(new FakeQuackProtocolBridge());

        Assert.Equal("quack-protocol", connection.ServerVersion);
    }

    /// <summary>
    /// ConnectionTimeout 返回ConfiguredValue
    /// </summary>
    [Fact]
    public void ConnectionTimeout_ReturnsConfiguredValue()
    {
        using var connection = QuackConnectionTests.CreateConnection(new FakeQuackProtocolBridge());

        Assert.Equal(30, connection.ConnectionTimeout);
    }

    /// <summary>
    /// ChangeDatabase 抛出NotSupported
    /// </summary>
    [Fact]
    public void ChangeDatabase_ThrowsNotSupported()
    {
        using var connection = QuackConnectionTests.CreateConnection(new FakeQuackProtocolBridge());

        Assert.Throws<NotSupportedException>(() => connection.ChangeDatabase("other"));
    }

    /// <summary>
    /// BeginTransaction 当Closed 抛出InvalidOperation
    /// </summary>
    [Fact]
    public async Task BeginTransaction_WhenClosed_ThrowsInvalidOperation()
    {
        using var connection = QuackConnectionTests.CreateConnection(new FakeQuackProtocolBridge());

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.BeginTransactionAsync(IsolationLevel.ReadCommitted));
    }

    /// <summary>
    /// OpenAsync 当AlreadyOpen DoesNothing
    /// </summary>
    [Fact]
    public async Task OpenAsync_WhenAlreadyOpen_DoesNothing()
    {
        var bridge = new FakeQuackProtocolBridge();
        using var connection = QuackConnectionTests.CreateConnection(bridge);

        await connection.OpenAsync();
        await connection.OpenAsync(); // Should not throw or increment count

        Assert.Equal(1, bridge.ConnectCount);
        Assert.Equal(ConnectionState.Open, connection.State);
    }

    /// <summary>
    /// OpenAsync 当Connecting 抛出InvalidOperation
    /// </summary>
    [Fact]
    public async Task OpenAsync_WhenConnecting_ThrowsInvalidOperation()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowBridge = new SlowConnectBridge(gate.Task);
        var connection = new QuackConnection(
            "Host=quack.example;Port=9494;Token=abc",
            () => slowBridge);

        var task = connection.OpenAsync();
        await slowBridge.ConnectEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.OpenAsync());

        gate.SetResult();
        await task;
    }

    /// <summary>
    /// Dispose 关闭Connection
    /// </summary>
    [Fact]
    public async Task Dispose_ClosesConnection()
    {
        var bridge = new FakeQuackProtocolBridge();
        var connection = QuackConnectionTests.CreateConnection(bridge);

        await connection.OpenAsync();
        Assert.Equal(ConnectionState.Open, connection.State);

        connection.Dispose();
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.True(bridge.Disposed);
    }

    /// <summary>
    /// ConnectionString Get 返回FormattedString
    /// </summary>
    [Fact]
    public async Task ConnectionString_Get_ReturnsFormattedString()
    {
        using var connection = QuackConnectionTests.CreateConnection(new FakeQuackProtocolBridge());

        var cs = connection.ConnectionString;
        Assert.Contains("Host=quack.example", cs);
        Assert.Contains("Port=9494", cs);
        // Token should be masked
        Assert.DoesNotContain("Token=abc", cs);
        Assert.Contains("****", cs);
    }

    /// <summary>
    /// ConnectionString Set 当Closed 更新Config
    /// </summary>
    [Fact]
    public void ConnectionString_Set_WhenClosed_UpdatesConfig()
    {
        using var connection = QuackConnectionTests.CreateConnection(new FakeQuackProtocolBridge());

        connection.ConnectionString = "Host=other;Port=1234;Token=newtoken";

        Assert.Contains("Host=other", connection.ConnectionString);
    }

    /// <summary>
    /// CreateCommand 当Open 返回Command
    /// </summary>
    [Fact]
    public async Task CreateCommand_WhenOpen_ReturnsCommand()
    {
        using var connection = QuackConnectionTests.CreateConnection(new FakeQuackProtocolBridge());
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        Assert.NotNull(command);
        Assert.IsType<QuackCommand>(command);
    }

    /// <summary>
    /// OpenAsync BridgeConnectFails State是否Closed
    /// </summary>
    [Fact]
    public async Task OpenAsync_BridgeConnectFails_StateIsClosed()
    {
        var throwingBridge = new ThrowingBridge();
        var connection = new QuackConnection(
            "Host=quack.example;Port=9494;Token=abc",
            () => throwingBridge);

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.OpenAsync());
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    /// <summary>
    /// Close 当AlreadyClosed DoesNothing
    /// </summary>
    [Fact]
    public async Task Close_WhenAlreadyClosed_DoesNothing()
    {
        using var connection = QuackConnectionTests.CreateConnection(new FakeQuackProtocolBridge());

        // Should not throw
        connection.Close();
        connection.Close();
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    private sealed class ThrowingBridge : IQuackProtocolBridge
    {
        public Task<QuackProtocolBridgeVersion> GetVersionAsync(CancellationToken cancellationToken)
            => Task.FromResult(new QuackProtocolBridgeVersion(1, "1.5.3", "v1.5-variegata"));

        public Task<QuackProtocolSession> ConnectAsync(QuackProtocolConfig config, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Connection failed");

        public Task<QuackQueryResult> ExecuteQueryAsync(QuackProtocolSession session, string sql, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<QuackQueryResult?> FetchAsync(QuackProtocolSession session, string queryId, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task CloseSessionAsync(QuackProtocolSession session, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public void Dispose() { }
    }

    private sealed class SlowConnectBridge : IQuackProtocolBridge
    {
        private readonly Task _gate;

        public SlowConnectBridge(Task gate)
        {
            _gate = gate;
            ConnectEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public TaskCompletionSource ConnectEntered { get; }

        public Task<QuackProtocolBridgeVersion> GetVersionAsync(CancellationToken cancellationToken)
            => Task.FromResult(new QuackProtocolBridgeVersion(1, "1.5.3", "v1.5-variegata"));

        public async Task<QuackProtocolSession> ConnectAsync(QuackProtocolConfig config, CancellationToken cancellationToken)
        {
            ConnectEntered.SetResult();
            await _gate.WaitAsync(cancellationToken);
            return new QuackProtocolSession("1", config);
        }

        public Task<QuackQueryResult> ExecuteQueryAsync(QuackProtocolSession session, string sql, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<QuackQueryResult?> FetchAsync(QuackProtocolSession session, string queryId, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task CloseSessionAsync(QuackProtocolSession session, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public void Dispose() { }
    }
}
