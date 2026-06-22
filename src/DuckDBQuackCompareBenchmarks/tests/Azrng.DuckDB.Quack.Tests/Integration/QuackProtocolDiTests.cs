namespace Azrng.DuckDB.Quack.Tests;

[Trait("Category", "Integration")]
[Collection(IntegrationTestCollection.Name)]
public class QuackProtocolDiTests
{
    private readonly TestOptions _options;

    public QuackProtocolDiTests(TestOptions options)
    {
        _options = options;
    }

    [Fact]
    public void Options_HasConnectionString()
    {
        Assert.False(string.IsNullOrEmpty(_options.ConnectionString));
    }

    [Fact]
    public async Task Connection_CanBeOpenedFromOptions()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();
        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }
}
