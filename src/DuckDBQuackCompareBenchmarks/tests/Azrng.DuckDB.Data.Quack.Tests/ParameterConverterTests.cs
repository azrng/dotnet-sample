using Xunit;
using DuckDBParameter = DuckDB.NET.Data.DuckDBParameter;

namespace Azrng.DuckDB.Data.Quack.Tests;

public class ParameterConverterTests
{
    [Fact]
    public void ConvertNamedToQuestionMark_SingleParam()
    {
        var (sql, parameters) = ParameterConverter.ConvertNamedToQuestionMark(
            "SELECT * FROM orders WHERE status = @status",
            new { status = "completed" });

        Assert.Equal("SELECT * FROM orders WHERE status = ?", sql);
        Assert.Single(parameters);
        Assert.Equal("completed", ((DuckDBParameter)parameters[0]).Value);
    }

    [Fact]
    public void ConvertNamedToQuestionMark_MultipleParams()
    {
        var (sql, parameters) = ParameterConverter.ConvertNamedToQuestionMark(
            "SELECT * FROM orders WHERE status = @status AND amount >= @minAmount",
            new { status = "completed", minAmount = 100.0m });

        Assert.Equal("SELECT * FROM orders WHERE status = ? AND amount >= ?", sql);
        Assert.Equal(2, parameters.Length);
        Assert.Equal("completed", ((DuckDBParameter)parameters[0]).Value);
        Assert.Equal(100.0m, ((DuckDBParameter)parameters[1]).Value);
    }

    [Fact]
    public void ConvertNamedToQuestionMark_SameParamMultipleTimes()
    {
        var (sql, parameters) = ParameterConverter.ConvertNamedToQuestionMark(
            "SELECT * FROM orders WHERE amount >= @min AND amount <= @max AND total >= @min",
            new { min = 100.0m, max = 500.0m });

        Assert.Equal("SELECT * FROM orders WHERE amount >= ? AND amount <= ? AND total >= ?", sql);
        Assert.Equal(2, parameters.Length);
        Assert.Equal(100.0m, ((DuckDBParameter)parameters[0]).Value);
        Assert.Equal(500.0m, ((DuckDBParameter)parameters[1]).Value);
    }

    [Fact]
    public void ConvertNamedToQuestionMark_NullParam()
    {
        var (sql, parameters) = ParameterConverter.ConvertNamedToQuestionMark(
            "SELECT * FROM orders WHERE name = @name",
            new { name = (string?)null });

        Assert.Equal("SELECT * FROM orders WHERE name = ?", sql);
        Assert.Single(parameters);
        Assert.Equal(DBNull.Value, ((DuckDBParameter)parameters[0]).Value);
    }

    [Fact]
    public void ConvertNamedToQuestionMark_NullParameters_ReturnsOriginal()
    {
        var originalSql = "SELECT * FROM orders";
        var (sql, parameters) = ParameterConverter.ConvertNamedToQuestionMark(originalSql, null!);

        Assert.Equal(originalSql, sql);
        Assert.Empty(parameters);
    }

    [Fact]
    public void ConvertNamedToQuestionMark_NoParamsInSql()
    {
        var (sql, parameters) = ParameterConverter.ConvertNamedToQuestionMark(
            "SELECT * FROM orders",
            new { status = "completed" });

        Assert.Equal("SELECT * FROM orders", sql);
        Assert.Empty(parameters);
    }

    [Fact]
    public void ConvertNamedToPositional_SingleParam()
    {
        var (sql, parameters) = ParameterConverter.ConvertNamedToPositional(
            "SELECT * FROM orders WHERE status = @status",
            new { status = "completed" });

        Assert.Equal("SELECT * FROM orders WHERE status = $1", sql);
        Assert.NotNull(parameters);
    }

    [Fact]
    public void ConvertNamedToPositional_MultipleParams()
    {
        var (sql, parameters) = ParameterConverter.ConvertNamedToPositional(
            "SELECT * FROM orders WHERE status = @status AND amount >= @minAmount",
            new { status = "completed", minAmount = 100.0m });

        Assert.Equal("SELECT * FROM orders WHERE status = $1 AND amount >= $2", sql);
        Assert.NotNull(parameters);
    }

    [Fact]
    public void ConvertNamedToPositional_SameParamMultipleTimes()
    {
        var (sql, parameters) = ParameterConverter.ConvertNamedToPositional(
            "SELECT * FROM orders WHERE amount >= @min AND amount <= @max AND total >= @min",
            new { min = 100.0m, max = 500.0m });

        Assert.Equal("SELECT * FROM orders WHERE amount >= $1 AND amount <= $2 AND total >= $1", sql);
        Assert.NotNull(parameters);
    }

    [Fact]
    public void ConvertNamedToPositional_NullParameters_ReturnsOriginal()
    {
        var originalSql = "SELECT * FROM orders";
        var (sql, parameters) = ParameterConverter.ConvertNamedToPositional(originalSql, null);

        Assert.Equal(originalSql, sql);
        Assert.Null(parameters);
    }

    [Fact]
    public void ConvertNamedToQuestionMark_DictionaryParams()
    {
        var dict = new Dictionary<string, object>
        {
            ["status"] = "completed",
            ["minAmount"] = 100.0m
        };

        var (sql, parameters) = ParameterConverter.ConvertNamedToQuestionMark(
            "SELECT * FROM orders WHERE status = @status AND amount >= @minAmount",
            dict);

        Assert.Equal("SELECT * FROM orders WHERE status = ? AND amount >= ?", sql);
        Assert.Equal(2, parameters.Length);
    }

    [Fact]
    public void ConvertNamedToPositional_MissingParam_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            ParameterConverter.ConvertNamedToPositional(
                "SELECT * FROM orders WHERE status = @status",
                new { wrongParam = "value" }));
    }

    [Fact]
    public void ResolveNamedParameters_WithPatientNameParameter_ReplacesLiteral()
    {
        var sql = ParameterConverter.ResolveNamedParameters(
            "SELECT * FROM source.patient WHERE patientname = @patientname",
            new { patientname = "O'Reilly" });

        Assert.Equal("SELECT * FROM source.patient WHERE patientname = 'O''Reilly'", sql);
        Assert.DoesNotContain("@patientname", sql);
    }

    [Fact]
    public void ResolveNamedParameters_WithSimilarParameterNames_DoesNotMixValues()
    {
        var sql = ParameterConverter.ResolveNamedParameters(
            "SELECT * FROM source.patient WHERE patient = @patient AND patientname = @patientname",
            new { patient = "P001", patientname = "张三" });

        Assert.Equal("SELECT * FROM source.patient WHERE patient = 'P001' AND patientname = '张三'", sql);
    }

    [Fact]
    public void ResolveQuestionMarkParameters_ReplacesParametersInOrder()
    {
        var sql = ParameterConverter.ResolveQuestionMarkParameters(
            "SELECT ? AS id, ? AS name",
            [
                new DuckDBParameter { Value = 1 },
                new DuckDBParameter { Value = "张三" }
            ]);

        Assert.Equal("SELECT 1 AS id, '张三' AS name", sql);
    }
}
