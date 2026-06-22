using System.Data;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// QuackCommandExtendedTests 的单元测试
/// </summary>
public class QuackCommandExtendedTests
{
    /// <summary>
    /// Prepare 抛出NotSupported
    /// </summary>
    [Fact]
    public async Task Prepare_ThrowsNotSupported()
    {
        using var connection = QuackConnectionTests.CreateConnection(new FakeQuackProtocolBridge());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        Assert.Throws<NotSupportedException>(() => command.Prepare());
    }

    /// <summary>
    /// CommandType NonText 抛出NotSupported
    /// </summary>
    [Fact]
    public async Task CommandType_NonText_ThrowsNotSupported()
    {
        using var connection = QuackConnectionTests.CreateConnection(new FakeQuackProtocolBridge());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 1";
        command.CommandType = CommandType.StoredProcedure;

        await Assert.ThrowsAsync<NotSupportedException>(() => command.ExecuteReaderAsync());
    }

    /// <summary>
    /// CommandText Empty 抛出InvalidOperation
    /// </summary>
    [Fact]
    public async Task CommandText_Empty_ThrowsInvalidOperation()
    {
        using var connection = QuackConnectionTests.CreateConnection(new FakeQuackProtocolBridge());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "";

        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteReaderAsync());
    }

    /// <summary>
    /// CommandText Whitespace 抛出InvalidOperation
    /// </summary>
    [Fact]
    public async Task CommandText_Whitespace_ThrowsInvalidOperation()
    {
        using var connection = QuackConnectionTests.CreateConnection(new FakeQuackProtocolBridge());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "   ";

        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteReaderAsync());
    }

    /// <summary>
    /// ExecuteScalar EmptyResult 返回Null
    /// </summary>
    [Fact]
    public async Task ExecuteScalar_EmptyResult_ReturnsNull()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("id", "BIGINT", typeof(long))],
            [],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 1 where false";

        Assert.Null(command.ExecuteScalar());
    }

    /// <summary>
    /// CreateDbParameter 返回QuackParameter
    /// </summary>
    [Fact]
    public async Task CreateDbParameter_ReturnsQuackParameter()
    {
        using var connection = QuackConnectionTests.CreateConnection(new FakeQuackProtocolBridge());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        var parameter = command.CreateParameter();
        Assert.IsType<QuackParameter>(parameter);
    }

    /// <summary>
    /// Parameters Collection Works
    /// </summary>
    [Fact]
    public async Task Parameters_Collection_Works()
    {
        using var connection = QuackConnectionTests.CreateConnection(new FakeQuackProtocolBridge());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select @id";

        var param = command.CreateParameter();
        param.ParameterName = "@id";
        param.Value = 42;
        command.Parameters.Add(param);

        Assert.Single(command.Parameters);
    }

    /// <summary>
    /// DesignTimeVisible Default是否False
    /// </summary>
    [Fact]
    public async Task DesignTimeVisible_DefaultIsFalse()
    {
        using var connection = QuackConnectionTests.CreateConnection(new FakeQuackProtocolBridge());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        Assert.False(command.DesignTimeVisible);
        command.DesignTimeVisible = true;
        Assert.True(command.DesignTimeVisible);
    }

    /// <summary>
    /// UpdatedRowSource Default是否None
    /// </summary>
    [Fact]
    public async Task UpdatedRowSource_DefaultIsNone()
    {
        using var connection = QuackConnectionTests.CreateConnection(new FakeQuackProtocolBridge());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        Assert.Equal(UpdateRowSource.None, command.UpdatedRowSource);
    }
}
