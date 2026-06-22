using Xunit;

namespace Azrng.DuckDB.Data.Quack.Tests;

public class QuackConnectionStringParserTests
{
    [Fact]
    public void Parse_KeyValueFormat_ReturnsConfig()
    {
        var config = QuackConnectionStringParser.Parse("Host=10.21.50.221;Port=9494;Token=change-me");

        Assert.Equal("10.21.50.221", config.Host);
        Assert.Equal(9494, config.Port);
        Assert.Equal("change-me", config.Token);
        Assert.Equal("remote", config.Catalog);
        Assert.False(config.Attach);
        Assert.True(config.DisableSsl);
    }

    [Fact]
    public void Parse_KeyValueFormat_WithDisableSsl()
    {
        var config = QuackConnectionStringParser.Parse("Host=10.21.50.221;Port=9494;Token=abc;DisableSsl=false");

        Assert.False(config.DisableSsl);
    }

    [Fact]
    public void Parse_KeyValueFormat_WithCatalogAndAttach()
    {
        var config = QuackConnectionStringParser.Parse(
            "Host=172.16.68.108;Port=9494;Token=abc;Catalog=duckflight;Attach=true");

        Assert.Equal("duckflight", config.Catalog);
        Assert.True(config.Attach);
    }

    [Fact]
    public void Parse_KeyValueFormat_WithDatabase_UsesDatabaseAsCatalog()
    {
        var config = QuackConnectionStringParser.Parse(
            "Host=172.16.68.108;Port=9494;Token=abc;Database=duckflight");

        Assert.Equal("duckflight", config.Catalog);
    }

    [Fact]
    public void Parse_KeyValueFormat_WithExtensionDirectory_SetsDirectory()
    {
        var config = QuackConnectionStringParser.Parse(
            "Host=10.21.50.221;Port=9494;Token=abc;ExtensionDirectory=/var/quack-ext");

        Assert.Equal("/var/quack-ext", config.ExtensionDirectory);
    }

    [Fact]
    public void Parse_KeyValueFormat_WithExtensionsAlias_SetsDirectory()
    {
        var config = QuackConnectionStringParser.Parse(
            "Host=10.21.50.221;Port=9494;Token=abc;Extensions=./ext");

        Assert.Equal("./ext", config.ExtensionDirectory);
    }

    [Fact]
    public void Parse_KeyValueFormat_WithoutExtensionDirectory_NullDirectory()
    {
        var config = QuackConnectionStringParser.Parse("Host=10.21.50.221;Port=9494;Token=abc");

        Assert.Null(config.ExtensionDirectory);
    }

    [Fact]
    public void Parse_QuackUri_ReturnsConfig()
    {
        var config = QuackConnectionStringParser.Parse("quack://10.21.50.221:9494?token=change-me");

        Assert.Equal("10.21.50.221", config.Host);
        Assert.Equal(9494, config.Port);
        Assert.Equal("change-me", config.Token);
    }

    [Fact]
    public void Parse_QuackUri_WithTls()
    {
        var config = QuackConnectionStringParser.Parse("quack://10.21.50.221:9494?token=abc&tls=false");

        Assert.Equal("abc", config.Token);
        Assert.True(config.DisableSsl); // tls=false means disable ssl = true
    }

    [Fact]
    public void Parse_JdbcQuackUri_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() =>
            QuackConnectionStringParser.Parse("jdbc:quack://10.21.50.221:9494?token=change-me&tls=false"));
    }

    [Fact]
    public void Parse_EmptyString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QuackConnectionStringParser.Parse(""));
    }

    [Fact]
    public void Parse_NullString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QuackConnectionStringParser.Parse(null!));
    }

    [Fact]
    public void Parse_InvalidPort_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => QuackConnectionStringParser.Parse("Host=10.21.50.221;Port=abc;Token=abc"));
    }

    [Fact]
    public void Parse_EmptyHost_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QuackConnectionStringParser.Parse("Host= ;Port=9494;Token=abc"));
    }

    [Fact]
    public void Parse_OutOfRangePort_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => QuackConnectionStringParser.Parse("Host=10.21.50.221;Port=70000;Token=abc"));
    }

    [Fact]
    public void Parse_EmptyToken_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QuackConnectionStringParser.Parse("Host=10.21.50.221;Port=9494;Token="));
    }

    [Fact]
    public void Parse_InvalidCatalog_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QuackConnectionStringParser.Parse("Host=10.21.50.221;Port=9494;Token=abc;Catalog=my-db"));
    }

    [Fact]
    public void BuildQuackUri_ValidInput_ReturnsUri()
    {
        var uri = QuackConnectionStringParser.BuildQuackUri("10.21.50.221", 9494);

        Assert.Equal("quack://10.21.50.221:9494", uri);
    }

    [Fact]
    public void BuildQuackUri_EmptyHost_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QuackConnectionStringParser.BuildQuackUri("", 9494));
    }

    [Fact]
    public void BuildQuackUri_InvalidPort_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => QuackConnectionStringParser.BuildQuackUri("10.21.50.221", 0));
    }

    [Fact]
    public void BuildQuackUri_QuackUriInput_ReturnsSameUri()
    {
        var uri = QuackConnectionStringParser.BuildQuackUri("quack:host:9494", 1234);
        Assert.Equal("quack://host:9494", uri);
    }

    [Fact]
    public void ValidateIdentifier_ValidInput_ReturnsSame()
    {
        Assert.Equal("my_table", QuackConnectionStringParser.ValidateIdentifier("my_table"));
        Assert.Equal("Table123", QuackConnectionStringParser.ValidateIdentifier("Table123"));
    }

    [Fact]
    public void ValidateIdentifier_EmptyInput_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QuackConnectionStringParser.ValidateIdentifier(""));
    }

    [Fact]
    public void ValidateIdentifier_InvalidChars_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QuackConnectionStringParser.ValidateIdentifier("my-table"));
        Assert.Throws<ArgumentException>(() => QuackConnectionStringParser.ValidateIdentifier("my table"));
        Assert.Throws<ArgumentException>(() => QuackConnectionStringParser.ValidateIdentifier("my.table"));
    }

    [Fact]
    public void ValidateDsn_QuackUri_ReturnsNormalizedDsn()
    {
        var dsn = QuackConnectionStringParser.ValidateDsn("quack://10.21.50.221:9494?token=ignored");

        Assert.Equal("quack://10.21.50.221:9494", dsn);
    }

    [Fact]
    public void ValidateDsn_NonQuackUri_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QuackConnectionStringParser.ValidateDsn("http://10.21.50.221:9494"));
    }

    [Fact]
    public void ValidateDsn_WithWhitespace_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QuackConnectionStringParser.ValidateDsn("quack://bad host:9494"));
    }

    [Fact]
    public void FromConnectionString_Works()
    {
        var config = QuackConnectionConfig.FromConnectionString("Host=10.21.50.221;Port=9494;Token=abc");

        Assert.Equal("10.21.50.221", config.Host);
        Assert.Equal(9494, config.Port);
        Assert.Equal("abc", config.Token);
    }
}
