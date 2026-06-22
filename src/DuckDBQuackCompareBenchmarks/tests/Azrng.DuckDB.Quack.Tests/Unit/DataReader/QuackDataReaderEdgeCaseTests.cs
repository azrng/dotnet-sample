namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// QuackDataReader 边界情况和异常场景测试
/// </summary>
public class QuackDataReaderEdgeCaseTests
{
    /// <summary>
    /// 验证 GetBytes 方法抛出 NotSupportedException
    /// </summary>
    [Fact]
    public async Task GetBytes_ThrowsNotSupported()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("val", "BLOB", typeof(string))],
            [new object?[] { "data" }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 'data'::blob";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Throws<NotSupportedException>(() => reader.GetBytes(0, 0, null, 0, 0));
    }

    /// <summary>
    /// 验证 GetChars 方法抛出 NotSupportedException
    /// </summary>
    [Fact]
    public async Task GetChars_ThrowsNotSupported()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("val", "VARCHAR", typeof(string))],
            [new object?[] { "hello" }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 'hello'";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Throws<NotSupportedException>(() => reader.GetChars(0, 0, null, 0, 0));
    }

    /// <summary>
    /// 验证通过序号索引器能正确返回列值
    /// </summary>
    [Fact]
    public async Task Indexer_ByOrdinal_ReturnsValue()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("val", "BIGINT", typeof(long))],
            [new object?[] { 42L }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 42";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(42L, reader[0]);
    }

    /// <summary>
    /// 验证通过列名索引器能正确返回列值
    /// </summary>
    [Fact]
    public async Task Indexer_ByName_ReturnsValue()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("answer", "BIGINT", typeof(long))],
            [new object?[] { 42L }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 42 as answer";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(42L, reader["answer"]);
    }

    /// <summary>
    /// 验证列名索引器支持大小写不敏感匹配
    /// </summary>
    [Fact]
    public async Task Indexer_ByName_CaseInsensitive()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("MyCol", "BIGINT", typeof(long))],
            [new object?[] { 42L }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 42 as MyCol";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(42L, reader["mycol"]);
        Assert.Equal(42L, reader["MYCOL"]);
    }

    /// <summary>
    /// 验证 GetString 对 null 值返回空字符串
    /// </summary>
    [Fact]
    public async Task GetString_NullValue_ReturnsEmptyString()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("val", "VARCHAR", typeof(string))],
            [new object?[] { null }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select null";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        // GetString on null returns empty string via Convert.ToString
        var value = reader.GetString(0);
        Assert.Equal("", value);
    }

    /// <summary>
    /// 验证同步 Read 方法与异步 ReadAsync 行为一致
    /// </summary>
    [Fact]
    public async Task Read_Synchronous_WorksSameAsAsync()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("id", "BIGINT", typeof(long))],
            [
                new object?[] { 1L },
                new object?[] { 2L }
            ],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 1 union all select 2";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetInt64(0));

        Assert.True(reader.Read());
        Assert.Equal(2L, reader.GetInt64(0));

        Assert.False(reader.Read());
    }

    /// <summary>
    /// 验证多列全为 null 时返回 DBNull
    /// </summary>
    [Fact]
    public async Task MultipleColumns_AllNull_ReturnsDBNull()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [
                new QuackColumnInfo("a", "BIGINT", typeof(long)),
                new QuackColumnInfo("b", "VARCHAR", typeof(string)),
                new QuackColumnInfo("c", "BOOLEAN", typeof(bool))
            ],
            [new object?[] { null, null, null }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select null, null, null";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.True(reader.IsDBNull(0));
        Assert.True(reader.IsDBNull(1));
        Assert.True(reader.IsDBNull(2));
        Assert.Equal(DBNull.Value, reader[0]);
        Assert.Equal(DBNull.Value, reader["a"]);
    }

    /// <summary>
    /// 验证在已关闭的 DataReader 上调用方法抛出异常
    /// </summary>
    [Fact]
    public async Task GetValue_ClosedReader_Throws()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("id", "BIGINT", typeof(long))],
            [new object?[] { 1L }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 1";

        var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        await reader.DisposeAsync();

        Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
        Assert.Throws<InvalidOperationException>(() => reader.GetInt64(0));
        Assert.Throws<InvalidOperationException>(() => reader.GetString(0));
        Assert.Throws<InvalidOperationException>(() => reader.GetBoolean(0));
        Assert.Throws<InvalidOperationException>(() => reader.IsDBNull(0));
    }

    /// <summary>
    /// 验证 GetName 方法返回正确的列名
    /// </summary>
    [Fact]
    public async Task GetName_ReturnsColumnName()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [
                new QuackColumnInfo("first_name", "VARCHAR", typeof(string)),
                new QuackColumnInfo("LAST_NAME", "VARCHAR", typeof(string))
            ],
            [new object?[] { "John", "Doe" }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 'John', 'Doe'";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.Equal("first_name", reader.GetName(0));
        Assert.Equal("LAST_NAME", reader.GetName(1));
    }

    /// <summary>
    /// 验证 FieldCount 属性返回正确的列数
    /// </summary>
    [Fact]
    public async Task FieldCount_ReturnsColumnCount()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [
                new QuackColumnInfo("a", "BIGINT", typeof(long)),
                new QuackColumnInfo("b", "VARCHAR", typeof(string)),
                new QuackColumnInfo("c", "BOOLEAN", typeof(bool)),
                new QuackColumnInfo("d", "DOUBLE", typeof(double))
            ],
            [new object?[] { 1L, "test", true, 1.5 }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 1, 'test', true, 1.5";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.Equal(4, reader.FieldCount);
    }
}
