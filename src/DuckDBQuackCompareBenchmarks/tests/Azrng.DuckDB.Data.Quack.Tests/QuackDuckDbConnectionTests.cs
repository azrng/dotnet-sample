using System.Data;
using Xunit;

namespace Azrng.DuckDB.Data.Quack.Tests;

public class QuackDuckDbConnectionTests
{
    [Fact]
    public void Constructor_InvalidConfig_ThrowsArgumentException()
    {
        var config = new QuackConnectionConfig
        {
            Host = "10.21.50.221",
            Port = 9494,
            Token = "",
        };

        Assert.Throws<ArgumentException>(() => new QuackDuckDbConnection(config));
    }

    [Fact]
    public void CreateCommand_WhenClosed_ThrowsInvalidOperationException()
    {
        using var connection = new QuackDuckDbConnection("Host=10.21.50.221;Port=9494;Token=abc");

        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Throws<InvalidOperationException>(() => connection.CreateCommand());
    }

    [Fact]
    public async Task OpenAsync_WhenCanceled_ReturnsCanceledTask()
    {
        using var connection = new QuackDuckDbConnection("Host=10.21.50.221;Port=9494;Token=abc");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => connection.OpenAsync(cts.Token));
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void ConnectionString_CanBeChangedWhenClosed()
    {
        using var connection = new QuackDuckDbConnection("Host=10.21.50.221;Port=9494;Token=abc");

        connection.ConnectionString = "Host=127.0.0.1;Port=9495;Token=def";

        Assert.Equal("127.0.0.1:9495", connection.ConnectionString);
    }

    [Fact]
    public void Database_WithCatalog_ReturnsCatalog()
    {
        using var connection = new QuackDuckDbConnection(
            "Host=172.16.68.108;Port=9494;Token=abc;Catalog=duckflight");

        Assert.Equal("duckflight", connection.Database);
    }

    [Fact]
    public void UseAttachMode_DefaultConnectionString_ReturnsFalse()
    {
        using var connection = new QuackDuckDbConnection("Host=10.21.50.221;Port=9494;Token=abc");

        Assert.False(connection.UseAttachMode());
    }

    [Fact]
    public void UseAttachMode_WithAttachTrue_ReturnsTrue()
    {
        using var connection = new QuackDuckDbConnection("Host=10.21.50.221;Port=9494;Token=abc;Attach=true");

        Assert.True(connection.UseAttachMode());
    }

    [Fact]
    public void BuildAttachSql_WithCatalog_UsesCatalogAsAttachAlias()
    {
        using var connection = new QuackDuckDbConnection(
            "Host=172.16.68.108;Port=9494;Token=abc;Catalog=duckflight;Attach=true");

        var sql = connection.BuildAttachSql();

        Assert.Contains("ATTACH 'quack://172.16.68.108:9494' AS duckflight", sql);
        Assert.DoesNotContain("USE duckflight", sql, StringComparison.OrdinalIgnoreCase);
    }
}
