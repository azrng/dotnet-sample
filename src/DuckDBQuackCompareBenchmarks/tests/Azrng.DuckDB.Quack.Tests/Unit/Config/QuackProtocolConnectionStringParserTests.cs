namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// QuackProtocolConnectionStringParserTests 的单元测试
/// </summary>
public class QuackProtocolConnectionStringParserTests
{
    /// <summary>
    /// Parse KeyValue 返回Config
    /// </summary>
    [Fact]
    public void Parse_KeyValue_ReturnsConfig()
    {
        var config = QuackProtocolConnectionStringParser.Parse("Host=localhost;Port=9494;Token=sample-token;DisableSsl=true");

        Assert.Equal("localhost", config.Host);
        Assert.Equal(9494, config.Port);
        Assert.Equal("sample-token", config.Token);
        Assert.True(config.DisableSsl);
        Assert.Equal("http://localhost:9494/quack", config.Endpoint.ToString());
    }

    /// <summary>
    /// Parse KeyValue WithHttps
    /// </summary>
    [Fact]
    public void Parse_KeyValue_WithHttps()
    {
        var config = QuackProtocolConnectionStringParser.Parse("Host=example.com;Port=9443;Token=abc;DisableSsl=false");

        Assert.False(config.DisableSsl);
        Assert.Equal("https://example.com:9443/quack", config.Endpoint.ToString());
    }

    /// <summary>
    /// Parse QuackUri UsesDefaults
    /// </summary>
    [Fact]
    public void Parse_QuackUri_UsesDefaults()
    {
        var config = QuackProtocolConnectionStringParser.Parse("quack://demo.example?token=abc");

        Assert.Equal("demo.example", config.Host);
        Assert.Equal(9494, config.Port);
        Assert.Equal("abc", config.Token);
        Assert.False(config.DisableSsl);
        Assert.Equal("https://demo.example:9494/quack", config.Endpoint.ToString());
    }

    /// <summary>
    /// Parse QuackUri TlsFalse DisablesSsl
    /// </summary>
    [Fact]
    public void Parse_QuackUri_TlsFalse_DisablesSsl()
    {
        var config = QuackProtocolConnectionStringParser.Parse("quack://demo.example?token=abc&tls=false");

        Assert.True(config.DisableSsl);
        Assert.Equal("http://demo.example:9494/quack", config.Endpoint.ToString());
    }

    /// <summary>
    /// Parse KeyValue DefaultsToTlsEnabled
    /// </summary>
    [Fact]
    public void Parse_KeyValue_DefaultsToTlsEnabled()
    {
        var config = QuackProtocolConnectionStringParser.Parse("Host=demo.example;Port=9494;Token=abc");

        Assert.False(config.DisableSsl);
        Assert.Equal("https://demo.example:9494/quack", config.Endpoint.ToString());
    }

    /// <summary>
    /// Parse QuackUri WithTlsTrue EnablesSsl
    /// </summary>
    [Fact]
    public void Parse_QuackUri_WithTlsTrue_EnablesSsl()
    {
        var config = QuackProtocolConnectionStringParser.Parse("jdbc:quack://host:9494/quack?token=abc&tls=true");

        Assert.False(config.DisableSsl);
        Assert.Equal("https://host:9494/quack", config.Endpoint.ToString());
    }

    /// <summary>
    /// Parse InvalidValues Throw
    /// </summary>
    [Fact]
    public void Parse_InvalidValues_Throw()
    {
        Assert.Throws<ArgumentException>(() => QuackProtocolConnectionStringParser.Parse(""));
        Assert.Throws<ArgumentException>(() => QuackProtocolConnectionStringParser.Parse("Host= ;Port=9494;Token=abc"));
        Assert.Throws<FormatException>(() => QuackProtocolConnectionStringParser.Parse("Host=host;Port=abc;Token=abc"));
        Assert.Throws<ArgumentOutOfRangeException>(() => QuackProtocolConnectionStringParser.Parse("Host=host;Port=0;Token=abc"));
        Assert.Throws<ArgumentException>(() => QuackProtocolConnectionStringParser.Parse("Host=host;Port=9494;Token="));
    }

    /// <summary>
    /// ToString MasksToken
    /// </summary>
    [Fact]
    public void ToString_MasksToken()
    {
        var config = QuackProtocolConnectionStringParser.Parse("Host=localhost;Port=9494;Token=sample-token;DisableSsl=true");
        var result = config.ToString();

        Assert.DoesNotContain("sample-token", result);
        Assert.Contains("sa****en", result);
        Assert.Contains("Host=localhost", result);
    }

    /// <summary>
    /// ToString ShortToken FullyMasked
    /// </summary>
    [Fact]
    public void ToString_ShortToken_FullyMasked()
    {
        var config = new QuackProtocolConfig { Host = "h", Port = 9494, Token = "abc" };
        var result = config.ToString();

        Assert.DoesNotContain("abc", result);
        Assert.Contains("****", result);
    }
}
