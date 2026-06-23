using Xunit;
using DuckDBParameter = DuckDB.NET.Data.DuckDBParameter;

namespace Azrng.DuckDB.Data.Quack.Tests;

/// <summary>
/// QuackDataProvider 集成测试
/// 需要设置环境变量 QUACK_TEST_HOST, QUACK_TEST_PORT, QUACK_TEST_TOKEN 才会执行
/// </summary>
[Trait("Category", "Integration")]
public class QuackDataProviderIntegrationTests : IDisposable
{
    private readonly QuackDataProvider? _provider;
    private readonly bool _skipTests;

    public QuackDataProviderIntegrationTests()
    {
        var host = Environment.GetEnvironmentVariable("QUACK_TEST_HOST");
        var portStr = Environment.GetEnvironmentVariable("QUACK_TEST_PORT");
        var token = Environment.GetEnvironmentVariable("QUACK_TEST_TOKEN");

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(portStr) || string.IsNullOrWhiteSpace(token))
        {
            _skipTests = true;
            return;
        }

        if (!int.TryParse(portStr, out var port))
        {
            _skipTests = true;
            return;
        }

        _provider = new QuackDataProvider(host, port, token);
    }

    [Fact]
    public void AttachRemote_WhenCalled_SetsIsAttached()
    {
        if (_skipTests) return;

        _provider!.AttachRemote();

        Assert.True(_provider!.IsAttached);
    }

    [Fact]
    public void ExecuteQuery_ReturnsResults()
    {
        if (_skipTests) return;

        var results = _provider!.ExecuteQuery("SELECT 1 as test_col");

        Assert.NotEmpty(results);
        Assert.Equal(2, results.Count); // header + 1 row
        Assert.Equal("test_col", results[0][0]);
        // DuckDB 把无类型上下文的整数字面量推断为 INTEGER(32位)，经 ATTACH 远端返回 Int32。
        // 归一化为 long 比较，避免对列的物理存储类型做强假设。
        Assert.Equal(1L, Convert.ToInt64(results[1][0]));
    }

    [Fact]
    public void ExecuteQuery_WithParameters()
    {
        if (_skipTests) return;

        var results = _provider!.ExecuteQuery(
            "SELECT ? as val",
            [new DuckDBParameter { Value = 42 }]);

        Assert.NotEmpty(results);
        Assert.Equal(42L, Convert.ToInt64(results[1][0]));
    }

    [Fact]
    public void ExecuteQuery_WithNamedParams()
    {
        if (_skipTests) return;

        // 使用 @paramName 形式，内部自动转换
        var results = _provider!.ExecuteQuery(
            "SELECT @val as val",
            new { val = 42 });

        Assert.NotEmpty(results);
        Assert.Equal(42L, Convert.ToInt64(results[1][0]));
    }

    [Fact]
    public void ExecuteQuery_Generic_ReturnsTypedResults()
    {
        if (_skipTests) return;

        var results = _provider!.ExecuteQuery<TestDto>("SELECT 1 as Id, 'test' as Name");

        Assert.Single(results);
        Assert.Equal(1L, results[0].Id);
        Assert.Equal("test", results[0].Name);
    }

    [Fact]
    public void ExecuteQuery_Generic_WithNamedParams()
    {
        if (_skipTests) return;

        // 使用 @paramName 形式，内部自动转换
        var results = _provider!.ExecuteQuery<TestDto>(
            "SELECT @id as Id, @name as Name",
            new { id = 42, name = "hello" });

        Assert.Single(results);
        Assert.Equal(42L, results[0].Id);
        Assert.Equal("hello", results[0].Name);
    }

    [Fact]
    public void ExecuteScalar_ReturnsValue()
    {
        if (_skipTests) return;

        var result = _provider!.ExecuteScalar("SELECT 42");

        Assert.Equal(42L, Convert.ToInt64(result));
    }

    [Fact]
    public void ExecuteScalar_WithNamedParams()
    {
        if (_skipTests) return;

        // 使用 @paramName 形式，内部自动转换
        var result = _provider!.ExecuteScalar("SELECT @val", new { val = 99 });

        Assert.Equal(99L, Convert.ToInt64(result));
    }

    [Fact]
    public void ConnectionReuse_MultipleQueries()
    {
        if (_skipTests) return;

        var r1 = _provider!.ExecuteScalar("SELECT 1");
        var r2 = _provider.ExecuteScalar("SELECT 2");
        var r3 = _provider.ExecuteScalar("SELECT 3");

        Assert.Equal(1L, Convert.ToInt64(r1));
        Assert.Equal(2L, Convert.ToInt64(r2));
        Assert.Equal(3L, Convert.ToInt64(r3));
    }

    [Fact]
    public void InformationSchema_SchemasQuery()
    {
        if (_skipTests) return;

        var results = _provider!.ExecuteQuery("SELECT schema_name FROM information_schema.schemata ORDER BY schema_name");

        Assert.NotEmpty(results);
        Assert.True(results.Count > 1); // 至少有 header + 一个 schema
    }

    [Fact]
    public void NormalizeSql_ThreePartReferences()
    {
        var sql = "SELECT source.orders.order_id FROM source.orders WHERE source.orders.status = 'completed'";
        var result = QuackDataProvider.NormalizeSql(sql);

        Assert.Contains("FROM source.orders", result);
        Assert.DoesNotContain("source.orders.order_id", result);
    }

    [Fact]
    public void BuildQuackQueryByNameSql_DefaultMode_UsesQuackQuery()
    {
        if (_skipTests) return;

        var wrapped = _provider!.BuildQuackQueryByNameSql("SELECT * FROM source.orders WHERE status = 'completed'");

        Assert.Contains("quack_query", wrapped);
        Assert.DoesNotContain("quack_query_by_name", wrapped);
        Assert.Contains("SELECT", wrapped);
    }

    [Fact]
    public void ExecuteQueryViaQuack_ReturnsResults()
    {
        if (_skipTests) return;

        var results = _provider!.ExecuteQueryViaQuack("SELECT 1 as test_col");

        Assert.NotEmpty(results);
        Assert.Equal(2, results.Count); // header + 1 row
        Assert.Equal("test_col", results[0][0]);
    }

    [Fact]
    public void ExecuteQueryViaQuack_Generic_ReturnsTypedResults()
    {
        if (_skipTests) return;

        var results = _provider!.ExecuteQueryViaQuack<TestDto>("SELECT 1 as Id, 'test' as Name");

        Assert.Single(results);
        Assert.Equal(1L, results[0].Id);
        Assert.Equal("test", results[0].Name);
    }

    public void Dispose()
    {
        _provider?.Dispose();
    }

    private sealed class TestDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
    }
}
