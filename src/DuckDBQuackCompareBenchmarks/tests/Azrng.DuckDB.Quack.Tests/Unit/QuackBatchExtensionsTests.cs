using System.Data;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// QuackBatchExtensionsTests 的单元测试
/// </summary>
public class QuackBatchExtensionsTests
{
    /// <summary>
    /// ExecuteBatchInsert QuotesIdentifiersToPreventInjection
    /// </summary>
    [Fact]
    public async Task ExecuteBatchInsert_QuotesIdentifiersToPreventInjection()
    {
        var bridge = new CapturingBridge();
        var connection = new QuackConnection("Host=demo;Token=t", () => bridge);
        await connection.OpenAsync();

        var maliciousTable = "t; DROP TABLE x; --";
        var maliciousColumn = "col')";
        var rows = new List<object?[]> { new object?[] { 1 } };

        await connection.ExecuteBatchInsertAsync(maliciousTable, new[] { maliciousColumn }, rows);

        Assert.Contains("\"t; DROP TABLE x; --\"", bridge.LastSql);
        Assert.Contains("\"col')\"", bridge.LastSql);
        // The dangerous identifiers must be wrapped, not bare.
        Assert.DoesNotContain("INSERT INTO t; ", bridge.LastSql);
    }

    /// <summary>
    /// ExecuteBatchInsert EscapesSingleQuotesInValues
    /// </summary>
    [Fact]
    public async Task ExecuteBatchInsert_EscapesSingleQuotesInValues()
    {
        var bridge = new CapturingBridge();
        var connection = new QuackConnection("Host=demo;Token=t", () => bridge);
        await connection.OpenAsync();

        var rows = new List<object?[]> { new object?[] { "O'Brien" } };

        await connection.ExecuteBatchInsertAsync("users", new[] { "name" }, rows);

        Assert.Contains("'O''Brien'", bridge.LastSql);
        // The unescaped payload must not appear in the rendered SQL.
        Assert.DoesNotContain("'O'Brien'", bridge.LastSql);
    }

    /// <summary>
    /// ExecuteBatchInsert FallsBackToQuotedStringForUnknownValue
    /// </summary>
    [Fact]
    public async Task ExecuteBatchInsert_FallsBackToQuotedStringForUnknownValue()
    {
        var bridge = new CapturingBridge();
        var connection = new QuackConnection("Host=demo;Token=t", () => bridge);
        await connection.OpenAsync();

        var rows = new List<object?[]>
        {
            new object?[] { new CustomFormattable("it's me") }
        };

        await connection.ExecuteBatchInsertAsync("users", new[] { "tag" }, rows);

        // The fallback branch (default => QuoteString(Convert.ToString(...))) must escape the inner quote.
        Assert.Contains("'it''s me'", bridge.LastSql);
    }

    /// <summary>
    /// ExecuteBatchInsert EmptyRows 返回Zero
    /// </summary>
    [Fact]
    public async Task ExecuteBatchInsert_EmptyRows_ReturnsZero()
    {
        var bridge = new CapturingBridge();
        var connection = new QuackConnection("Host=demo;Token=t", () => bridge);
        await connection.OpenAsync();

        var affected = await connection.ExecuteBatchInsertAsync("users", new[] { "id" }, Array.Empty<object?[]>());

        Assert.Equal(0, affected);
        Assert.Null(bridge.LastSql);
    }

    /// <summary>
    /// ExecuteBatchInsert NullValue RendersAsNull
    /// </summary>
    [Fact]
    public async Task ExecuteBatchInsert_NullValue_RendersAsNull()
    {
        var bridge = new CapturingBridge();
        var connection = new QuackConnection("Host=demo;Token=t", () => bridge);
        await connection.OpenAsync();

        var rows = new List<object?[]> { new object?[] { null } };

        await connection.ExecuteBatchInsertAsync("users", new[] { "name" }, rows);

        Assert.Contains("(NULL)", bridge.LastSql);
    }

    /// <summary>
    /// ExecuteBatchInsert EmptyIdentifier 抛出
    /// </summary>
    [Fact]
    public async Task ExecuteBatchInsert_EmptyIdentifier_Throws()
    {
        var bridge = new CapturingBridge();
        var connection = new QuackConnection("Host=demo;Token=t", () => bridge);
        await connection.OpenAsync();

        var rows = new List<object?[]> { new object?[] { 1 } };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            connection.ExecuteBatchInsertAsync("", new[] { "id" }, rows));
    }

    private sealed class CapturingBridge : IQuackProtocolBridge
    {
        public string? LastSql { get; private set; }

        public Task<QuackProtocolBridgeVersion> GetVersionAsync(CancellationToken cancellationToken)
            => Task.FromResult(new QuackProtocolBridgeVersion(1, "1.5.3", "v1"));

        public Task<QuackProtocolSession> ConnectAsync(QuackProtocolConfig config, CancellationToken cancellationToken)
            => Task.FromResult(new QuackProtocolSession("1", config));

        public Task<QuackQueryResult> ExecuteQueryAsync(QuackProtocolSession session, string sql, CancellationToken cancellationToken)
        {
            LastSql = sql;
            return Task.FromResult(new QuackQueryResult(
                "q1",
                new[] { new QuackColumnInfo("count", "BIGINT", typeof(long)) },
                new[] { new object?[] { 1L } },
                false));
        }

        public Task<QuackQueryResult?> FetchAsync(QuackProtocolSession session, string fetchToken, CancellationToken cancellationToken)
            => Task.FromResult<QuackQueryResult?>(null);

        public Task CloseSessionAsync(QuackProtocolSession session, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public void Dispose() { }
    }

    private sealed class CustomFormattable
    {
        private readonly string _value;
        public CustomFormattable(string value) => _value = value;
        public override string ToString() => _value;
    }
}
