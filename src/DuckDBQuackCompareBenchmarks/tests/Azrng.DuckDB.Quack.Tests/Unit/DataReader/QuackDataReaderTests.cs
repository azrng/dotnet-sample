namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// QuackDataReader 数据读取器的单元测试
/// </summary>
public class QuackDataReaderTests
{
    /// <summary>
    /// 验证读取 NULL 值时返回 DBNull 并正确报告 IsDBNull
    /// </summary>
    [Fact]
    public async Task GetValue_NullValue_ReturnsDBNull()
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
        Assert.Equal(DBNull.Value, reader.GetValue(0));
        Assert.True(reader.IsDBNull(0));
    }

    /// <summary>
    /// 验证读取布尔值时返回正确的类型和值
    /// </summary>
    [Fact]
    public async Task GetValue_BoolValue_ReturnsCorrectType()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("val", "BOOLEAN", typeof(bool))],
            [new object?[] { true }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select true";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.Equal(true, reader[0]);
        Assert.Equal(true, reader["val"]);
    }

    /// <summary>
    /// 验证读取 Decimal 值时返回正确的类型和精度
    /// </summary>
    [Fact]
    public async Task GetValue_DecimalValue_ReturnsCorrectType()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("val", "DECIMAL(10,2)", typeof(decimal))],
            [new object?[] { 99.99m }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 99.99";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(99.99m, reader.GetDecimal(0));
    }

    /// <summary>
    /// 验证读取 DateTime 时间戳值时返回正确的类型
    /// </summary>
    [Fact]
    public async Task GetValue_DateTimeValue_ReturnsCorrectType()
    {
        var dt = new DateTime(2026, 6, 19, 12, 30, 0);
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("val", "TIMESTAMP", typeof(DateTime))],
            [new object?[] { dt }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select now()";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(dt, reader.GetDateTime(0));
    }

    /// <summary>
    /// 验证读取 UUID/GUID 值时返回正确的 Guid 类型
    /// </summary>
    [Fact]
    public async Task GetValue_GuidValue_ReturnsCorrectType()
    {
        var guid = Guid.NewGuid();
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("val", "UUID", typeof(Guid))],
            [new object?[] { guid.ToString() }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select uuid()";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(guid, reader.GetGuid(0));
    }

    /// <summary>
    /// 验证按列名获取正确的序号索引，不存在的列名返回 -1
    /// </summary>
    [Fact]
    public async Task GetOrdinal_ByName_ReturnsIndex()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [
                new QuackColumnInfo("id", "BIGINT", typeof(long)),
                new QuackColumnInfo("name", "VARCHAR", typeof(string)),
                new QuackColumnInfo("value", "DOUBLE", typeof(double))
            ],
            [new object?[] { 1L, "test", 1.5 }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 1, 'test', 1.5";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetOrdinal("id"));
        Assert.Equal(1, reader.GetOrdinal("name"));
        Assert.Equal(2, reader.GetOrdinal("value"));
        Assert.Equal(-1, reader.GetOrdinal("nonexistent"));
    }

    /// <summary>
    /// 验证列名查找不区分大小写
    /// </summary>
    [Fact]
    public async Task GetOrdinal_CaseInsensitive_ReturnsIndex()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("MyColumn", "BIGINT", typeof(long))],
            [new object?[] { 1L }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 1";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetOrdinal("mycolumn"));
        Assert.Equal(0, reader.GetOrdinal("MYCOLUMN"));
        Assert.Equal(0, reader.GetOrdinal("MyColumn"));
    }

    /// <summary>
    /// 验证 GetValues 将当前行的所有列值填充到数组并返回正确的列数
    /// </summary>
    [Fact]
    public async Task GetValues_FillsArray_ReturnsCount()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [
                new QuackColumnInfo("a", "BIGINT", typeof(long)),
                new QuackColumnInfo("b", "VARCHAR", typeof(string))
            ],
            [new object?[] { 1L, "hello" }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 1, 'hello'";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        var values = new object[2];
        var count = reader.GetValues(values);
        Assert.Equal(2, count);
        Assert.Equal(1L, values[0]);
        Assert.Equal("hello", values[1]);
    }

    /// <summary>
    /// 验证当数组长度大于列数时，GetValues 只填充实际列数的数据
    /// </summary>
    [Fact]
    public async Task GetValues_LargerArray_FillsOnlyColumns()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("a", "BIGINT", typeof(long))],
            [new object?[] { 1L }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 1";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        var values = new object[5];
        var count = reader.GetValues(values);
        Assert.Equal(1, count);
        Assert.Equal(1L, values[0]);
    }

    /// <summary>
    /// 验证空结果集时 HasRows 返回 false 且 Read 返回 false
    /// </summary>
    [Fact]
    public async Task HasRows_EmptyResult_ReturnsFalse()
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
        await using var reader = await command.ExecuteReaderAsync();

        Assert.False(reader.HasRows);
        Assert.False(await reader.ReadAsync());
    }

    /// <summary>
    /// 验证有数据行时 HasRows 返回 true
    /// </summary>
    [Fact]
    public async Task HasRows_WithRows_ReturnsTrue()
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
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(reader.HasRows);
    }

    /// <summary>
    /// 验证存在后续 Fetch 批次时 HasRows 仍返回 true
    /// </summary>
    [Fact]
    public async Task HasRows_WithMoreBatches_ReturnsTrue()
    {
        var bridge = new FakeQuackProtocolBridge();
        var columns = new[] { new QuackColumnInfo("id", "BIGINT", typeof(long)) };
        bridge.Results.Enqueue(new QuackQueryResult("q1", columns, [new object?[] { 1L }], true) { FetchToken = "t1" });
        bridge.FetchResults.Enqueue(new QuackQueryResult("q1", columns, [], false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 1";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(reader.HasRows);
    }

    /// <summary>
    /// 验证释放后 IsClosed 属性返回 true
    /// </summary>
    [Fact]
    public async Task IsClosed_AfterDispose_ReturnsTrue()
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
        Assert.False(reader.IsClosed);
        await reader.DisposeAsync();
        Assert.True(reader.IsClosed);
    }

    /// <summary>
    /// 验证关闭后调用 Read 抛出 InvalidOperationException
    /// </summary>
    [Fact]
    public async Task Read_AfterClose_Throws()
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
        await reader.DisposeAsync();

        Assert.Throws<InvalidOperationException>(() => reader.Read());
    }

    /// <summary>
    /// 验证在调用 Read 之前调用 GetValue 抛出 InvalidOperationException
    /// </summary>
    [Fact]
    public async Task GetValue_BeforeRead_Throws()
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
        await using var reader = await command.ExecuteReaderAsync();

        Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
    }

    /// <summary>
    /// 验证 NextResult 同步和异步方法始终返回 false（不支持多结果集）
    /// </summary>
    [Fact]
    public async Task NextResult_AlwaysReturnsFalse()
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
        await using var reader = await command.ExecuteReaderAsync();

        Assert.False(reader.NextResult());
        Assert.False(await reader.NextResultAsync());
    }

    /// <summary>
    /// 验证 Depth 属性始终返回 0（不支持嵌套结果集）
    /// </summary>
    [Fact]
    public async Task Depth_ReturnsZero()
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
        await using var reader = await command.ExecuteReaderAsync();

        Assert.Equal(0, reader.Depth);
    }

    /// <summary>
    /// 验证 RecordsAffected 在有数据时反映首个单元格的值
    /// </summary>
    [Fact]
    public async Task RecordsAffected_ReflectsFirstCellWhenAvailable()
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
        await using var reader = await command.ExecuteReaderAsync();

        Assert.Equal(1, reader.RecordsAffected);
    }

    /// <summary>
    /// 验证空结果集时 RecordsAffected 返回 -1
    /// </summary>
    [Fact]
    public async Task RecordsAffected_EmptyResult_ReturnsMinusOne()
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
        await using var reader = await command.ExecuteReaderAsync();

        Assert.Equal(-1, reader.RecordsAffected);
    }

    /// <summary>
    /// 验证通过 while 循环可以正确遍历所有行
    /// </summary>
    [Fact]
    public async Task GetEnumerator_IteratesRows()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("id", "BIGINT", typeof(long))],
            [
                new object?[] { 1L },
                new object?[] { 2L },
                new object?[] { 3L }
            ],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 1 union all select 2 union all select 3";
        await using var reader = await command.ExecuteReaderAsync();

        var count = 0;
        while (reader.Read())
            count++;

        Assert.Equal(3, count);
    }

    /// <summary>
    /// 验证 GetDataTypeName 返回正确的数据库类型名称
    /// </summary>
    [Fact]
    public async Task GetDataTypeName_ReturnsTypeName()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [
                new QuackColumnInfo("a", "BIGINT", typeof(long)),
                new QuackColumnInfo("b", "VARCHAR", typeof(string)),
                new QuackColumnInfo("c", "DECIMAL(10,2)", typeof(decimal))
            ],
            [new object?[] { 1L, "test", 9.99m }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 1, 'test', 9.99";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal("BIGINT", reader.GetDataTypeName(0));
        Assert.Equal("VARCHAR", reader.GetDataTypeName(1));
        Assert.Equal("DECIMAL(10,2)", reader.GetDataTypeName(2));
    }

    /// <summary>
    /// 验证 GetFieldType 返回正确的 CLR 类型
    /// </summary>
    [Fact]
    public async Task GetFieldType_ReturnsClrType()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [
                new QuackColumnInfo("a", "BIGINT", typeof(long)),
                new QuackColumnInfo("b", "VARCHAR", typeof(string)),
                new QuackColumnInfo("c", "BOOLEAN", typeof(bool))
            ],
            [new object?[] { 1L, "test", true }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 1, 'test', true";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(typeof(long), reader.GetFieldType(0));
        Assert.Equal(typeof(string), reader.GetFieldType(1));
        Assert.Equal(typeof(bool), reader.GetFieldType(2));
    }

    /// <summary>
    /// 验证按顺序读取多行数据并读取完毕后返回 false
    /// </summary>
    [Fact]
    public async Task MultipleRows_ReadSequentially()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("id", "BIGINT", typeof(long))],
            [
                new object?[] { 10L },
                new object?[] { 20L },
                new object?[] { 30L }
            ],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 10 union all select 20 union all select 30";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(10L, reader.GetInt64(0));

        Assert.True(await reader.ReadAsync());
        Assert.Equal(20L, reader.GetInt64(0));

        Assert.True(await reader.ReadAsync());
        Assert.Equal(30L, reader.GetInt64(0));

        Assert.False(await reader.ReadAsync());
    }

    /// <summary>
    /// 验证读取空字符串返回正确的值
    /// </summary>
    [Fact]
    public async Task GetString_ReturnsEmptyString()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("val", "VARCHAR", typeof(string))],
            [new object?[] { "" }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select ''";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal("", reader.GetString(0));
    }

    /// <summary>
    /// 验证读取 Int32 整数值正确
    /// </summary>
    [Fact]
    public async Task GetInt32_ReturnsCorrectValue()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("val", "INTEGER", typeof(int))],
            [new object?[] { 42 }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 42::int";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(42, reader.GetInt32(0));
    }

    /// <summary>
    /// 验证读取 Int16 短整数值正确
    /// </summary>
    [Fact]
    public async Task GetInt16_ReturnsCorrectValue()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("val", "SMALLINT", typeof(short))],
            [new object?[] { (short)123 }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 123::smallint";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(123, reader.GetInt16(0));
    }

    /// <summary>
    /// 验证读取 Float 单精度浮点值正确
    /// </summary>
    [Fact]
    public async Task GetFloat_ReturnsCorrectValue()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("val", "FLOAT", typeof(float))],
            [new object?[] { 3.14f }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 3.14::float";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(3.14f, reader.GetFloat(0));
    }

    /// <summary>
    /// 验证读取 Double 双精度浮点值正确
    /// </summary>
    [Fact]
    public async Task GetDouble_ReturnsCorrectValue()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("val", "DOUBLE", typeof(double))],
            [new object?[] { 3.14159 }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 3.14159";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(3.14159, reader.GetDouble(0));
    }

    /// <summary>
    /// 验证读取 Byte 字节值正确
    /// </summary>
    [Fact]
    public async Task GetByte_ReturnsCorrectValue()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("val", "TINYINT", typeof(byte))],
            [new object?[] { (byte)255 }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 255::tinyint";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(255, reader.GetByte(0));
    }

    /// <summary>
    /// 验证多批次 Fetch 能够正确读取所有行并记录 Fetch 次数
    /// </summary>
    [Fact]
    public async Task FetchMultipleBatches_ReadsAllRows()
    {
        var bridge = new FakeQuackProtocolBridge();
        var columns = new[] { new QuackColumnInfo("id", "BIGINT", typeof(long)) };

        bridge.Results.Enqueue(new QuackQueryResult("q1", columns, [new object?[] { 1L }], true) { FetchToken = "t1" });
        bridge.FetchResults.Enqueue(new QuackQueryResult("q1", columns, [new object?[] { 2L }], true) { FetchToken = "t2" });
        bridge.FetchResults.Enqueue(new QuackQueryResult("q1", columns, [new object?[] { 3L }], false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 1";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));

        Assert.True(await reader.ReadAsync());
        Assert.Equal(2L, reader.GetInt64(0));

        Assert.True(await reader.ReadAsync());
        Assert.Equal(3L, reader.GetInt64(0));

        Assert.False(await reader.ReadAsync());
        Assert.Equal(2, bridge.FetchCount);
    }

    /// <summary>
    /// 验证 Fetch 遇到空批次时自动跳过并继续读取后续数据
    /// </summary>
    [Fact]
    public async Task FetchEmptyBatch_SkipsAndContinues()
    {
        var bridge = new FakeQuackProtocolBridge();
        var columns = new[] { new QuackColumnInfo("id", "BIGINT", typeof(long)) };

        bridge.Results.Enqueue(new QuackQueryResult("q1", columns, [new object?[] { 1L }], true) { FetchToken = "t1" });
        bridge.FetchResults.Enqueue(new QuackQueryResult("q1", columns, [], true) { FetchToken = "t2" });
        bridge.FetchResults.Enqueue(new QuackQueryResult("q1", columns, [new object?[] { 2L }], false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 1";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));

        Assert.True(await reader.ReadAsync());
        Assert.Equal(2L, reader.GetInt64(0));

        Assert.False(await reader.ReadAsync());
    }
}
