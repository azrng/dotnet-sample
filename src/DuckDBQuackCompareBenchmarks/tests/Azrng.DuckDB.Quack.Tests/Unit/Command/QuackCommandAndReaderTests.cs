using System.Data;
using Dapper;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// QuackCommandAndReaderTests 的单元测试
/// </summary>
public class QuackCommandAndReaderTests
{
    /// <summary>
    /// ExecuteReader 读取RowsAndSchema
    /// </summary>
    [Fact]
    public async Task ExecuteReader_ReadsRowsAndSchema()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [
                new QuackColumnInfo("id", "BIGINT", typeof(long)),
                new QuackColumnInfo("name", "VARCHAR", typeof(string))
            ],
            [
                new object?[] { 1L, "alpha" }
            ],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 1 as id, 'alpha' as name";

        await using var reader = await command.ExecuteReaderAsync();

        Assert.Equal(2, reader.FieldCount);
        Assert.Equal("id", reader.GetName(0));
        Assert.Equal(typeof(long), reader.GetFieldType(0));
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal("alpha", reader.GetString(1));
        Assert.False(await reader.ReadAsync());
        Assert.Equal("select 1 as id, 'alpha' as name", bridge.LastSql);
    }

    /// <summary>
    /// ExecuteReader FetchesNextBatch
    /// </summary>
    [Fact]
    public async Task ExecuteReader_FetchesNextBatch()
    {
        var bridge = new FakeQuackProtocolBridge();
        var columns = new[]
        {
            new QuackColumnInfo("id", "BIGINT", typeof(long))
        };

        bridge.Results.Enqueue(new QuackQueryResult("q1", columns, [new object?[] { 1L }], true) { FetchToken = "token-1" });
        bridge.FetchResults.Enqueue(new QuackQueryResult("q1", columns, [new object?[] { 2L }], false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select id from t";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2L, reader.GetInt64(0));
        Assert.False(await reader.ReadAsync());
        Assert.Equal(1, bridge.FetchCount);
    }

    /// <summary>
    /// ExecuteScalar 返回FirstValue
    /// </summary>
    [Fact]
    public async Task ExecuteScalar_ReturnsFirstValue()
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
        command.CommandText = "select 42";

        Assert.Equal(42L, command.ExecuteScalar());
    }

    /// <summary>
    /// ExecuteReader RendersAdoNetParameters
    /// </summary>
    [Fact]
    public async Task ExecuteReader_RendersAdoNetParameters()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("ok", "BOOLEAN", typeof(bool))],
            [new object?[] { true }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT @id AS id, @name AS name, @enabled AS enabled, @created AS created, @missing AS missing, '@id literal' AS literal
            """;

        AddParameter(command, "@id", 42);
        AddParameter(command, "@name", "O'Brien");
        AddParameter(command, "@enabled", true);
        AddParameter(command, "@created", new DateTime(2026, 6, 18, 12, 13, 14, DateTimeKind.Unspecified));
        AddParameter(command, "@missing", DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(
            "SELECT 42 AS id, 'O''Brien' AS name, TRUE AS enabled, TIMESTAMP '2026-06-18 12:13:14.0000000' AS created, NULL AS missing, '@id literal' AS literal",
            NormalizeSql(bridge.LastSql));
    }

    /// <summary>
    /// ExecuteReader RendersPositionalAndListParameters
    /// </summary>
    [Fact]
    public async Task ExecuteReader_RendersPositionalAndListParameters()
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
        command.CommandText = "SELECT * FROM source.orders WHERE order_id = ? AND order_status IN ?";

        AddParameter(command, "", 1001);
        AddParameter(command, "", new[] { "completed", "pending" });

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(
            "SELECT * FROM source.orders WHERE order_id = 1001 AND order_status IN ('completed', 'pending')",
            bridge.LastSql);
    }

    /// <summary>
    /// ExecuteReader MissingParameter 抛出
    /// </summary>
    [Fact]
    public async Task ExecuteReader_MissingParameter_Throws()
    {
        using var connection = QuackConnectionTests.CreateConnection(new FakeQuackProtocolBridge());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select @id";
        AddParameter(command, "@other", 1);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteReaderAsync());
        Assert.Contains("@id", ex.Message);
    }

    /// <summary>
    /// Dapper Query RendersParametersAndMapsRows
    /// </summary>
    [Fact]
    public async Task Dapper_Query_RendersParametersAndMapsRows()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [
                new QuackColumnInfo("Id", "BIGINT", typeof(long)),
                new QuackColumnInfo("Name", "VARCHAR", typeof(string))
            ],
            [
                new object?[] { 7L, "alpha" }
            ],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();

        var rows = (await connection.QueryAsync<OrderRow>(
            "SELECT @id AS Id, @name AS Name",
            new { id = 7, name = "alpha" })).ToList();

        var row = Assert.Single(rows);
        Assert.Equal(7, row.Id);
        Assert.Equal("alpha", row.Name);
        Assert.Equal("SELECT 7 AS Id, 'alpha' AS Name", bridge.LastSql);
    }

    /// <summary>
    /// NonQuery 执行Successfully
    /// </summary>
    [Fact]
    public async Task NonQuery_ExecutesSuccessfully()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("count", "BIGINT", typeof(long))],
            [new object?[] { 0L }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "delete from t";

        var result = await command.ExecuteNonQueryAsync();
        Assert.Equal(0, result);
    }

    /// <summary>
    /// GetSchemaTable 返回FullMetadata
    /// </summary>
    [Fact]
    public async Task GetSchemaTable_ReturnsFullMetadata()
    {
        var bridge = new FakeQuackProtocolBridge();
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [
                new QuackColumnInfo("id", "BIGINT", typeof(long)),
                new QuackColumnInfo("name", "VARCHAR", typeof(string)),
                new QuackColumnInfo("price", "DECIMAL(10,2)", typeof(decimal)),
                new QuackColumnInfo("active", "BOOLEAN", typeof(bool)),
                new QuackColumnInfo("data", "BLOB", typeof(string))
            ],
            [new object?[] { 1L, "test", 9.99m, true, null }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select * from t";
        await using var reader = await command.ExecuteReaderAsync();

        var schema = reader.GetSchemaTable();
        Assert.NotNull(schema);
        Assert.Equal(5, schema.Rows.Count);

        var idRow = schema.Rows[0];
        Assert.Equal("id", idRow["ColumnName"]);
        Assert.Equal(0, idRow["ColumnOrdinal"]);
        Assert.Equal(typeof(long), idRow["DataType"]);
        Assert.Equal("BIGINT", idRow["DataTypeName"]);
        Assert.Equal(true, idRow["AllowDBNull"]);
        Assert.Equal(8, idRow["ColumnSize"]);
        Assert.Equal(false, idRow["IsKey"]);
        Assert.Equal(false, idRow["IsUnique"]);

        var priceRow = schema.Rows[2];
        Assert.Equal("DECIMAL(10,2)", priceRow["DataTypeName"]);
        Assert.Equal((byte)10, priceRow["NumericPrecision"]);
        Assert.Equal((byte)2, priceRow["NumericScale"]);

        var activeRow = schema.Rows[3];
        Assert.Equal(1, activeRow["ColumnSize"]);
        Assert.Equal(typeof(bool), activeRow["DataType"]);

        var dataRow = schema.Rows[4];
        Assert.Equal(true, dataRow["IsLong"]);
        Assert.Equal(typeof(string), dataRow["DataType"]);
    }

    /// <summary>
    /// 可以cel StopsExecution
    /// </summary>
    [Fact]
    public async Task Cancel_StopsExecution()
    {
        var bridge = new FakeQuackProtocolBridge { ExecuteDelay = TimeSpan.FromSeconds(5) };
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("id", "BIGINT", typeof(long))],
            [new object?[] { 1L }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 1";

        var task = command.ExecuteReaderAsync();
        command.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    /// <summary>
    /// CommandTimeout FiresAfterDelay
    /// </summary>
    [Fact]
    public async Task CommandTimeout_FiresAfterDelay()
    {
        var bridge = new FakeQuackProtocolBridge { ExecuteDelay = TimeSpan.FromSeconds(5) };
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("id", "BIGINT", typeof(long))],
            [new object?[] { 1L }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 1";
        command.CommandTimeout = 1;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command.ExecuteReaderAsync());
    }

    /// <summary>
    /// External可以cellation 是否Respected
    /// </summary>
    [Fact]
    public async Task ExternalCancellation_IsRespected()
    {
        var bridge = new FakeQuackProtocolBridge { ExecuteDelay = TimeSpan.FromSeconds(5) };
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("id", "BIGINT", typeof(long))],
            [new object?[] { 1L }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 1";

        using var cts = new CancellationTokenSource();
        var task = command.ExecuteReaderAsync(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    /// <summary>
    /// ConcurrentQuery 抛出OnSecondExecution
    /// </summary>
    [Fact]
    public async Task ConcurrentQuery_ThrowsOnSecondExecution()
    {
        var bridge = new FakeQuackProtocolBridge
        {
            ExecuteGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        bridge.Results.Enqueue(new QuackQueryResult(
            "q1",
            [new QuackColumnInfo("id", "BIGINT", typeof(long))],
            [new object?[] { 1L }],
            false));
        bridge.Results.Enqueue(new QuackQueryResult(
            "q2",
            [new QuackColumnInfo("id", "BIGINT", typeof(long))],
            [new object?[] { 2L }],
            false));

        using var connection = QuackConnectionTests.CreateConnection(bridge);
        await connection.OpenAsync();

        var command1 = connection.CreateCommand();
        command1.CommandText = "select 1";
        var task1 = command1.ExecuteReaderAsync();

        // Wait until command1 has actually acquired the query lock inside the bridge.
        await (bridge.ExecuteEntered?.Task ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(1));

        await using var command2 = connection.CreateCommand();
        command2.CommandText = "select 2";
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => command2.ExecuteReaderAsync());
        Assert.Contains("concurrent", ex.Message, StringComparison.OrdinalIgnoreCase);

        bridge.ExecuteGate!.SetResult();
        try { await task1; } catch { /* expected */ }
        await command1.DisposeAsync();
    }

    private static void AddParameter(IDbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string? NormalizeSql(string? sql)
    {
        return sql?.Replace("\r", "").Replace("\n", "").Replace("  ", " ").Trim();
    }

    private sealed class OrderRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
    }
}
