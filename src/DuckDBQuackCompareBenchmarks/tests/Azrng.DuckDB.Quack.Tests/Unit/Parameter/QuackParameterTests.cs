using System.Data;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// QuackParameter 参数类的单元测试
/// </summary>
public class QuackParameterTests
{
    /// <summary>
    /// 验证新建参数的默认属性值是否正确
    /// </summary>
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var param = new QuackParameter();

        Assert.Equal("", param.ParameterName);
        Assert.Null(param.Value);
        Assert.Equal(DbType.Object, param.DbType);
        Assert.Equal(0, param.Size);
        Assert.Equal(ParameterDirection.Input, param.Direction);
        Assert.False(param.IsNullable);
        Assert.Equal(0, param.Precision);
        Assert.Equal(0, param.Scale);
        Assert.Equal("", param.SourceColumn);
        Assert.False(param.SourceColumnNullMapping);
        Assert.Equal(DataRowVersion.Current, param.SourceVersion);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var param = new QuackParameter
        {
            ParameterName = "@id",
            Value = 42,
            DbType = DbType.Int64,
            Size = 8,
            Direction = ParameterDirection.Input,
            IsNullable = true,
            Precision = 10,
            Scale = 2,
            SourceColumn = "id",
            SourceColumnNullMapping = true,
            SourceVersion = DataRowVersion.Original
        };

        Assert.Equal("@id", param.ParameterName);
        Assert.Equal(42, param.Value);
        Assert.Equal(DbType.Int64, param.DbType);
        Assert.Equal(8, param.Size);
        Assert.Equal(ParameterDirection.Input, param.Direction);
        Assert.True(param.IsNullable);
        Assert.Equal(10, param.Precision);
        Assert.Equal(2, param.Scale);
        Assert.Equal("id", param.SourceColumn);
        Assert.True(param.SourceColumnNullMapping);
        Assert.Equal(DataRowVersion.Original, param.SourceVersion);
    }

    [Fact]
    public void ResetDbType_ResetsToDefault()
    {
        var param = new QuackParameter
        {
            DbType = DbType.String
        };

        Assert.Equal(DbType.String, param.DbType);

        param.ResetDbType();

        Assert.Equal(DbType.Object, param.DbType);
    }

    [Fact]
    public void Value_CanBeNull()
    {
        var param = new QuackParameter
        {
            ParameterName = "@val",
            Value = null
        };

        Assert.Null(param.Value);
    }

    [Fact]
    public void Value_CanBeDBNull()
    {
        var param = new QuackParameter
        {
            ParameterName = "@val",
            Value = DBNull.Value
        };

        Assert.Equal(DBNull.Value, param.Value);
    }

    [Fact]
    public void Value_CanBeDifferentTypes()
    {
        var param = new QuackParameter();

        param.Value = 42;
        Assert.Equal(42, param.Value);

        param.Value = "hello";
        Assert.Equal("hello", param.Value);

        param.Value = true;
        Assert.Equal(true, param.Value);

        param.Value = 3.14m;
        Assert.Equal(3.14m, param.Value);

        param.Value = DateTime.Now;
        Assert.IsType<DateTime>(param.Value);
    }
}
