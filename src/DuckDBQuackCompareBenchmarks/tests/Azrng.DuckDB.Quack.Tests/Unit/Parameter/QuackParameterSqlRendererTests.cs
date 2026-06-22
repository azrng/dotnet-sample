using System.Data.Common;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// QuackParameterSqlRenderer 的单元测试，验证 SQL 参数渲染器对各种 SQL 语法元素的正确处理。
/// </summary>
public class QuackParameterSqlRendererTests
{
    /// <summary>
    /// 验证美元引号（$$）内的内容不被参数替换，保持原样输出。
    /// </summary>
    [Fact]
    public void DollarQuote_PreservesContent()
    {
        var result = Render("SELECT $$hello @name$$", []);

        Assert.Equal("SELECT $$hello @name$$", result);
    }

    /// <summary>
    /// 验证带标签的美元引号（$tag$...$tag$）内的内容不被参数替换。
    /// </summary>
    [Fact]
    public void DollarQuote_WithTag_PreservesContent()
    {
        var result = Render("SELECT $tag$hello @name$tag$", []);

        Assert.Equal("SELECT $tag$hello @name$tag$", result);
    }

    /// <summary>
    /// 验证美元引号外的参数仍然会被正确替换，而引号内的内容保持不变。
    /// </summary>
    [Fact]
    public void DollarQuote_WithParameterOutside_StillExpands()
    {
        var parameters = CreateParameters(("@name", "world"));
        var result = Render("SELECT $$literal$$, @name", parameters);

        Assert.Equal("SELECT $$literal$$, 'world'", result);
    }

    /// <summary>
    /// 验证嵌套标签的美元引号能正确解析，不会因内部标签而中断。
    /// </summary>
    [Fact]
    public void DollarQuote_NestedTags()
    {
        var result = Render("SELECT $outer$inner $inner$content$inner$ outer$outer$", []);

        Assert.Equal("SELECT $outer$inner $inner$content$inner$ outer$outer$", result);
    }

    /// <summary>
    /// 验证单引号字符串内的美元引号被视为普通文本，不触发特殊解析。
    /// </summary>
    [Fact]
    public void DollarQuote_InsideSingleQuotedString_IsLiteral()
    {
        var result = Render("SELECT '$$not a dollar quote$$'", []);

        Assert.Equal("SELECT '$$not a dollar quote$$'", result);
    }

    /// <summary>
    /// 验证同一 SQL 中存在多个美元引号时均能正确保留内容。
    /// </summary>
    [Fact]
    public void DollarQuote_MultipleDollarQuotes()
    {
        var result = Render("SELECT $$a$$, $$b$$", []);

        Assert.Equal("SELECT $$a$$, $$b$$", result);
    }

    /// <summary>
    /// 验证三引号字符串内的内容不被参数替换，保持原样输出。
    /// </summary>
    [Fact]
    public void TripleQuotedString_PreservesContent()
    {
        var result = Render("SELECT '''hello @name world'''", []);

        Assert.Equal("SELECT '''hello @name world'''", result);
    }

    /// <summary>
    /// 验证包含转义单引号的字符串内的参数名不被替换。
    /// </summary>
    [Fact]
    public void SingleQuotedString_EscapedQuote_PreservesContent()
    {
        var result = Render("SELECT 'it''s @name'", []);

        Assert.Equal("SELECT 'it''s @name'", result);
    }

    /// <summary>
    /// 验证双引号标识符内的参数名不被替换，视为标识符的一部分。
    /// </summary>
    [Fact]
    public void DoubleQuotedIdentifier_PreservesContent()
    {
        var result = Render("SELECT \"@name\" FROM t", []);

        Assert.Equal("SELECT \"@name\" FROM t", result);
    }

    /// <summary>
    /// 验证行注释内的参数名不被替换，注释内容保持原样。
    /// </summary>
    [Fact]
    public void LineComment_PreservesContent()
    {
        var result = Render("SELECT 1 -- @name comment", []);

        Assert.Equal("SELECT 1 -- @name comment", result);
    }

    /// <summary>
    /// 验证块注释内的参数名不被替换，注释内容保持原样。
    /// </summary>
    [Fact]
    public void BlockComment_PreservesContent()
    {
        var result = Render("SELECT 1 /* @name */", []);

        Assert.Equal("SELECT 1 /* @name */", result);
    }

    /// <summary>
    /// 验证美元引号内的冒号风格参数（:param）不被替换。
    /// </summary>
    [Fact]
    public void DollarQuote_WithColonParameter_Inside_IsPreserved()
    {
        var result = Render("SELECT $$:param$$", []);

        Assert.Equal("SELECT $$:param$$", result);
    }

    /// <summary>
    /// 验证美元引号内的参数名不替换，而引号外同名参数被正确替换。
    /// </summary>
    [Fact]
    public void DollarQuote_WithAtParameter_Outside_Expands()
    {
        var parameters = CreateParameters(("@x", 42));
        var result = Render("SELECT $$@x$$, @x", parameters);

        Assert.Equal("SELECT $$@x$$, 42", result);
    }

    /// <summary>
    /// 验证空的美元引号（$$$$）能被正确处理而不报错。
    /// </summary>
    [Fact]
    public void EmptyDollarQuote()
    {
        var result = Render("SELECT $$$$ , 1", []);

        Assert.Equal("SELECT $$$$ , 1", result);
    }

    /// <summary>
    /// 验证美元引号位于 SQL 末尾时能被正确解析。
    /// </summary>
    [Fact]
    public void DollarQuote_AtEndOfInput()
    {
        var result = Render("SELECT $$test$$", []);

        Assert.Equal("SELECT $$test$$", result);
    }

    /// <summary>
    /// 验证混合语法场景下（美元引号、块注释和普通参数共存）各区域的参数替换行为正确。
    /// </summary>
    [Fact]
    public void MixedSyntax_DollarQuoteAndComments()
    {
        var parameters = CreateParameters(("@val", "hi"));
        var result = Render("SELECT $$@val$$ /* @val */, @val", parameters);

        Assert.Equal("SELECT $$@val$$ /* @val */, 'hi'", result);
    }

    private static string Render(string sql, DbParameter[] parameters)
    {
        var collection = new QuackParameterCollection();
        foreach (var p in parameters)
            collection.Add(p);

        return QuackParameterSqlRenderer.Render(sql, collection);
    }

    private static DbParameter[] CreateParameters(params (string Name, object? Value)[] entries)
    {
        var result = new DbParameter[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            var parameter = new QuackParameter
            {
                ParameterName = entries[i].Name,
                Value = entries[i].Value
            };
            result[i] = parameter;
        }

        return result;
    }
}
