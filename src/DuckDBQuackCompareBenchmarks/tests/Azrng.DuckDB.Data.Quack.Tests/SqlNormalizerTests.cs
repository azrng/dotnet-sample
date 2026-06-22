using Xunit;

namespace Azrng.DuckDB.Data.Quack.Tests;

public class SqlNormalizerTests
{
    [Fact]
    public void NormalizeColumnReferences_SingleTable_SimplifiesColumns()
    {
        var sql = "SELECT source.orders.order_id, source.orders.order_amount FROM source.orders WHERE source.orders.status = 'completed'";
        var result = SqlNormalizer.NormalizeColumnReferences(sql);

        Assert.Contains("order_id", result);
        Assert.Contains("order_amount", result);
        Assert.DoesNotContain("source.orders.order_id", result);
        Assert.DoesNotContain("source.orders.order_amount", result);
        Assert.Contains("FROM source.orders", result); // FROM 子句保留
    }

    [Fact]
    public void NormalizeColumnReferences_MultipleTables_PreservesAll()
    {
        var sql = "SELECT a.col1, b.col2 FROM schema.table_a a JOIN schema.table_b b ON a.id = b.id";
        var result = SqlNormalizer.NormalizeColumnReferences(sql);

        // 多表时不做简化
        Assert.Equal(sql, result);
    }

    [Fact]
    public void NormalizeColumnReferences_NoThreePartRefs_ReturnsSame()
    {
        var sql = "SELECT order_id, order_amount FROM orders WHERE status = 'completed'";
        var result = SqlNormalizer.NormalizeColumnReferences(sql);

        Assert.Equal(sql, result);
    }

    [Fact]
    public void NormalizeColumnReferences_EmptySql_ReturnsSame()
    {
        Assert.Equal("", SqlNormalizer.NormalizeColumnReferences(""));
        Assert.Equal("  ", SqlNormalizer.NormalizeColumnReferences("  "));
    }

    [Fact]
    public void NormalizeColumnReferences_NullSql_ReturnsSame()
    {
        Assert.Null(SqlNormalizer.NormalizeColumnReferences(null!));
    }

    [Fact]
    public void NormalizeSchemaPrefix_DefaultToMain()
    {
        var sql = "SELECT * FROM default.orders";
        var result = SqlNormalizer.NormalizeSchemaPrefix(sql);

        Assert.Equal("SELECT * FROM main.orders", result);
    }

    [Fact]
    public void NormalizeSchemaPrefix_CaseInsensitive()
    {
        var sql = "SELECT * FROM Default.orders";
        var result = SqlNormalizer.NormalizeSchemaPrefix(sql);

        Assert.Equal("SELECT * FROM main.orders", result);
    }

    [Fact]
    public void NormalizeSchemaPrefix_NoDefault_ReturnsSame()
    {
        var sql = "SELECT * FROM source.orders";
        var result = SqlNormalizer.NormalizeSchemaPrefix(sql);

        Assert.Equal(sql, result);
    }

    [Fact]
    public void Normalize_CombinesBoth()
    {
        var sql = "SELECT default.orders.order_id FROM default.orders WHERE default.orders.status = 'completed'";
        var result = SqlNormalizer.Normalize(sql);

        // default -> main
        Assert.Contains("main.orders", result);
        // 三段式列引用简化（单表）
        Assert.DoesNotContain("main.orders.order_id", result);
        // FROM 子句保留
        Assert.Contains("FROM main.orders", result);
    }

    [Fact]
    public void Normalize_JoinClause_PreservesTableRefs()
    {
        var sql = "SELECT a.col1 FROM source.table_a a JOIN source.table_b b ON a.id = b.id";
        var result = SqlNormalizer.Normalize(sql);

        Assert.Contains("FROM source.table_a", result);
        Assert.Contains("JOIN source.table_b", result);
    }

    [Fact]
    public void Normalize_WhereClause_SimplifiesColumns()
    {
        var sql = "SELECT source.orders.id FROM source.orders WHERE source.orders.amount > 100";
        var result = SqlNormalizer.Normalize(sql);

        Assert.Contains("FROM source.orders", result);
        Assert.DoesNotContain("source.orders.amount", result);
        Assert.Contains("amount > 100", result);
    }
}
