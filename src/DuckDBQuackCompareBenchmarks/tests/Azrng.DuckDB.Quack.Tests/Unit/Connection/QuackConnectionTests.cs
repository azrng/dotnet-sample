using System.Data;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// QuackConnectionTests 的单元测试
/// </summary>
public class QuackConnectionTests
{
    /// <summary>
    /// OpenAsync 当可以celed DoesNotOpen
    /// </summary>
    [Fact]
    public async Task OpenAsync_WhenCanceled_DoesNotOpen()
    {
        using var connection = CreateConnection(new FakeQuackProtocolBridge());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => connection.OpenAsync(cts.Token));
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    /// <summary>
    /// OpenAndClose UpdateStateAndBridge
    /// </summary>
    [Fact]
    public async Task OpenAndClose_UpdateStateAndBridge()
    {
        var bridge = new FakeQuackProtocolBridge();
        using var connection = CreateConnection(bridge);

        await connection.OpenAsync();
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Equal(1, bridge.ConnectCount);

        connection.Close();
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Equal(1, bridge.CloseCount);
        Assert.True(bridge.Disposed);
    }

    /// <summary>
    /// ConnectionString 可以notChange当Open
    /// </summary>
    [Fact]
    public async Task ConnectionString_CannotChangeWhenOpen()
    {
        using var connection = CreateConnection(new FakeQuackProtocolBridge());
        await connection.OpenAsync();

        Assert.Throws<InvalidOperationException>(() => connection.ConnectionString = "Host=other;Token=abc");
    }

    /// <summary>
    /// BeginTransaction 当Closed 抛出
    /// </summary>
    [Fact]
    public async Task BeginTransaction_WhenClosed_Throws()
    {
        using var connection = CreateConnection(new FakeQuackProtocolBridge());

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.BeginTransactionAsync());
    }

    internal static QuackConnection CreateConnection(FakeQuackProtocolBridge bridge)
    {
        return new QuackConnection(
            "Host=quack.example;Port=9494;Token=abc",
            () => bridge);
    }
}
