using System.Data.Common;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Azrng.DuckDB.Quack.Tests;

[Trait("Category", "Integration")]
[Collection(IntegrationTestCollection.Name)]
public sealed class QuackProtocolIntegrationTests
{
    private readonly TestOptions _options;
    private readonly ILogger<QuackProtocolIntegrationTests> _logger;

    public QuackProtocolIntegrationTests(TestOptions options, ILogger<QuackProtocolIntegrationTests> logger)
    {
        _options = options;
        _logger = logger;
    }

    [Fact]
    public async Task ExecuteReader_SelectOne_ReturnsResult()
    {
        _logger.LogInformation("Testing SELECT 1");
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 AS value";

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, Convert.ToInt64(reader.GetValue(0)));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task ExecuteReader_ParameterizedLiterals_ReturnsResult()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @id AS id, @name AS name, @enabled AS enabled, @empty AS empty_value";

        AddParameter(command, "@id", 9494);
        AddParameter(command, "@name", "O'Brien");
        AddParameter(command, "@enabled", true);
        AddParameter(command, "@empty", DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(9494L, Convert.ToInt64(reader.GetValue(0)));
        Assert.Equal("O'Brien", reader.GetString(1));
        Assert.True(reader.GetBoolean(2));
        Assert.True(reader.IsDBNull(3));
    }

    [Fact]
    public async Task ExecuteReader_BatchFetch_ReturnsAllRows()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT i AS id, 'row_' || CAST(i AS VARCHAR) AS name FROM range(0, 100) t(i)";

        await using var reader = await command.ExecuteReaderAsync();
        var rowCount = 0;
        while (await reader.ReadAsync())
        {
            Assert.Equal((long)rowCount, reader.GetInt64(0));
            rowCount++;
        }

        Assert.Equal(100, rowCount);
    }

    [Fact]
    public async Task ExecuteReader_InvalidToken_ThrowsProtocolException()
    {
        // 用真实可达的 host/port + 坏 token：这样能建到服务端的 TCP 连接，
        // 由服务端在认证阶段拒绝 token，抛出预期的 QuackProtocolException。
        // （此前硬编码 localhost，本机无服务时会被连接拒绝抛 HttpRequestException，
        // 到不了 token 校验，断言失败。）
        var baseConfig = QuackProtocolConfig.FromConnectionString(_options.ConnectionString);
        var badConfig = baseConfig with { Token = "INVALID_TOKEN_12345" };
        await using var connection = new QuackConnection(badConfig);
        await Assert.ThrowsAsync<QuackProtocolException>(() => connection.OpenAsync());
    }

    [Fact]
    public async Task ExecuteReader_InvalidSql_ThrowsProtocolException()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM nonexistent_table_xyz_12345";
        await Assert.ThrowsAsync<QuackProtocolException>(() => command.ExecuteReaderAsync());
    }

    [Fact]
    public async Task ExecuteReader_UnreachableHost_ThrowsException()
    {
        var badConnectionString = "Host=192.0.2.1;Port=19494;Token=abc;DisableSsl=true";
        await using var connection = new QuackConnection(badConnectionString);
        await Assert.ThrowsAnyAsync<Exception>(() => connection.OpenAsync());
    }

    [Fact]
    public async Task Dapper_Query_WithParameters_ReturnsMappedRows()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        var rows = (await connection.QueryAsync<ProtocolSampleRow>(
            "SELECT @id AS Id, @name AS Name",
            new { id = 9494, name = "remote" })).ToList();

        var row = Assert.Single(rows);
        Assert.Equal(9494, row.Id);
        Assert.Equal("remote", row.Name);
    }

    [Fact]
    public async Task ExecuteReader_RangeQuery_ReturnsMultipleRows()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT i AS id, 'item_' || CAST(i AS VARCHAR) AS name FROM range(0, 50) t(i)";

        await using var reader = await command.ExecuteReaderAsync();
        var rowCount = 0;
        while (await reader.ReadAsync())
        {
            Assert.Equal((long)rowCount, reader.GetInt64(0));
            rowCount++;
        }
        Assert.Equal(50, rowCount);
    }

    [Fact]
    public async Task ExecuteReader_RangeQuery_WithFilter()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) AS cnt FROM range(0, 100) t(i) WHERE i % 2 = 0";

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(50L, Convert.ToInt64(reader.GetValue(0)));
    }

    [Fact]
    public async Task ExecuteReader_RangeQuery_WithAggregation()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SUM(i) AS total FROM range(0, 10) t(i)";

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(45L, Convert.ToInt64(reader.GetValue(0)));
    }

    [Fact]
    public async Task ExecuteReader_MultipleColumns_ReturnsCorrectTypes()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 42 AS int_val, 3.14 AS float_val, 'hello' AS str_val, true AS bool_val";

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(42L, Convert.ToInt64(reader.GetValue(0)));
        Assert.True(Convert.ToDouble(reader.GetValue(1)) > 3.0);
        Assert.Equal("hello", reader.GetString(2));
        Assert.True(reader.GetBoolean(3));
    }

    [Fact]
    public async Task Orders_SelectAll_ReturnsRows()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH orders AS (
                SELECT 1001 AS order_id, 101 AS user_id, 'completed' AS order_status, 299.00 AS order_amount, 'alipay' AS payment_method
                UNION ALL SELECT 1002, 102, 'completed', 899.50, 'wechat'
                UNION ALL SELECT 1003, 103, 'completed', 1599.00, 'card'
                UNION ALL SELECT 1004, 101, 'completed', 450.00, 'alipay'
                UNION ALL SELECT 1005, 104, 'pending', 199.00, 'alipay'
                UNION ALL SELECT 1006, 105, 'cancelled', 699.00, 'wechat'
                UNION ALL SELECT 1007, 106, 'completed', 1299.00, 'card'
                UNION ALL SELECT 1008, 107, 'pending', 99.00, 'alipay'
            )
            SELECT * FROM orders ORDER BY order_id
            """;

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(reader.FieldCount >= 5);

        var columnNames = GetColumnNames(reader);
        Assert.Contains("order_id", columnNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("user_id", columnNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("order_status", columnNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("order_amount", columnNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("payment_method", columnNames, StringComparer.OrdinalIgnoreCase);

        var rowCount = 0;
        while (await reader.ReadAsync())
        {
            Assert.True(reader.GetInt64(0) > 0); // order_id
            rowCount++;
        }
        Assert.Equal(8, rowCount);
    }

    [Fact]
    public async Task Orders_FilterByStatus_ReturnsFilteredRows()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH orders AS (
                SELECT 1001 AS order_id, 'completed' AS order_status
                UNION ALL SELECT 1002, 'completed'
                UNION ALL SELECT 1003, 'completed'
                UNION ALL SELECT 1004, 'completed'
                UNION ALL SELECT 1005, 'pending'
                UNION ALL SELECT 1006, 'cancelled'
                UNION ALL SELECT 1007, 'completed'
                UNION ALL SELECT 1008, 'pending'
            )
            SELECT order_id, order_status FROM orders WHERE order_status = 'completed' ORDER BY order_id
            """;

        await using var reader = await command.ExecuteReaderAsync();
        var rowCount = 0;
        while (await reader.ReadAsync())
        {
            Assert.Equal("completed", reader.GetString(1));
            rowCount++;
        }
        Assert.Equal(5, rowCount);
    }

    [Fact]
    public async Task Orders_AggregateByUser_ReturnsGroupedResults()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH orders AS (
                SELECT 101 AS user_id, 299.00 AS order_amount
                UNION ALL SELECT 102, 899.50
                UNION ALL SELECT 103, 1599.00
                UNION ALL SELECT 101, 450.00
                UNION ALL SELECT 104, 199.00
                UNION ALL SELECT 105, 699.00
                UNION ALL SELECT 106, 1299.00
                UNION ALL SELECT 107, 99.00
            )
            SELECT user_id, COUNT(*) AS order_count, SUM(order_amount) AS total_amount
            FROM orders GROUP BY user_id ORDER BY user_id
            """;

        await using var reader = await command.ExecuteReaderAsync();
        var rowCount = 0;
        while (await reader.ReadAsync())
        {
            Assert.True(reader.GetInt64(0) > 0); // user_id
            Assert.True(reader.GetInt64(1) > 0); // order_count
            rowCount++;
        }
        Assert.True(rowCount >= 4, $"Expected at least 4 user groups, got {rowCount}");
    }

    [Fact]
    public async Task Orders_DapperQuery_ReturnsMappedObjects()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        var orders = (await connection.QueryAsync<OrderRow>(
            "SELECT 1001 AS OrderId, 101 AS UserId, 'completed' AS OrderStatus")).ToList();

        Assert.Single(orders);
        Assert.Equal(1001, orders[0].OrderId);
        Assert.Equal(101, orders[0].UserId);
        Assert.Equal("completed", orders[0].OrderStatus);
    }

    [Fact]
    public async Task Orders_WithParameters_ReturnsFilteredResults()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH orders AS (
                SELECT 101 AS user_id, 299.00 AS order_amount
                UNION ALL SELECT 101, 450.00
                UNION ALL SELECT 102, 899.50
                UNION ALL SELECT 103, 1599.00
            )
            SELECT user_id, order_amount FROM orders WHERE user_id = @userId AND order_amount > @minAmount ORDER BY order_amount
            """;

        AddParameter(command, "@userId", 101L);
        AddParameter(command, "@minAmount", 200.0);

        await using var reader = await command.ExecuteReaderAsync();
        var rowCount = 0;
        while (await reader.ReadAsync())
        {
            Assert.Equal(101L, reader.GetInt64(0));
            rowCount++;
        }
        Assert.Equal(2, rowCount);
    }

    [Fact]
    public async Task Orders_LimitOffset_ReturnsPaginatedResults()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH orders AS (
                SELECT 1001 AS order_id
                UNION ALL SELECT 1002
                UNION ALL SELECT 1003
                UNION ALL SELECT 1004
                UNION ALL SELECT 1005
                UNION ALL SELECT 1006
                UNION ALL SELECT 1007
                UNION ALL SELECT 1008
            )
            SELECT order_id FROM orders ORDER BY order_id LIMIT 3 OFFSET 2
            """;

        await using var reader = await command.ExecuteReaderAsync();
        var rowCount = 0;
        var firstOrderId = 0L;
        while (await reader.ReadAsync())
        {
            if (rowCount == 0) firstOrderId = reader.GetInt64(0);
            rowCount++;
        }
        Assert.Equal(3, rowCount);
        Assert.Equal(1003L, firstOrderId);
    }

    internal static string[] GetColumnNames(DbDataReader reader)
    {
        var names = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
            names[i] = reader.GetName(i);

        return names;
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed class ProtocolSampleRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class OrderRow
    {
        public long OrderId { get; set; }
        public long UserId { get; set; }
        public string OrderStatus { get; set; } = "";
        public decimal OrderAmount { get; set; }
    }
}
