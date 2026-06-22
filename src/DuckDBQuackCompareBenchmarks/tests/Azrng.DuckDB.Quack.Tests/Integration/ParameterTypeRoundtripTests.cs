using System.Data;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// End-to-end type coverage: push each .NET primitive through a parameterized query, read it back,
/// and assert the value survives the wire round-trip. Exposes gaps in <see cref="QuackParameterSqlRenderer"/>.
/// </summary>
[Trait("Category", "Integration")]
[Collection(IntegrationTestCollection.Name)]
public sealed class ParameterTypeRoundtripTests
{
    private readonly TestOptions _options;

    public ParameterTypeRoundtripTests(TestOptions options)
    {
        _options = options;
    }

    private static QuackParameter Param(string name, object? value)
    {
        var p = new QuackParameter { ParameterName = name, Value = value };
        return p;
    }

    private async Task<object?> ScalarAsync(string sql, params QuackParameter[] parameters)
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var p in parameters)
        {
            command.Parameters.Add(p);
        }

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return reader.GetValue(0);
    }

    [Fact]
    public async Task Guid_Parameter_Roundtrips()
    {
        var expected = Guid.Parse("f0a1b2c3-d4e5-6789-abcd-ef0123456789");
        var result = await ScalarAsync("SELECT CAST(@g AS UUID)", Param("@g", expected));
        Assert.Equal(expected, reader_Guid(result));
    }

    private static Guid reader_Guid(object? value)
    {
        // Server may return Guid directly or a string/byte representation.
        return value switch
        {
            Guid g => g,
            string s => Guid.Parse(s),
            _ => throw new Xunit.Sdk.XunitException($"Unexpected Guid representation: {value?.GetType().Name}")
        };
    }

    [Fact]
    public async Task DateTime_Parameter_Roundtrips()
    {
        var expected = new DateTime(2026, 6, 20, 13, 45, 30, 123, DateTimeKind.Unspecified);
        var result = await ScalarAsync("SELECT CAST(@d AS TIMESTAMP)", Param("@d", expected));
        var actual = Convert.ToDateTime(result);
        // Microsecond precision may be truncated; compare to the millisecond.
        Assert.Equal(expected.Ticks / TimeSpan.TicksPerMillisecond, actual.Ticks / TimeSpan.TicksPerMillisecond);
    }

    [Fact]
    public async Task DateOnly_Parameter_Roundtrips()
    {
        var expected = new DateOnly(2026, 6, 21);
        var result = await ScalarAsync("SELECT CAST(@d AS DATE)", Param("@d", expected));
        var actual = result switch
        {
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => throw new Xunit.Sdk.XunitException($"Unexpected DateOnly representation: {result?.GetType().Name}")
        };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task DateTimeOffset_Parameter_Roundtrips()
    {
        var expected = new DateTimeOffset(2026, 1, 15, 8, 30, 0, TimeSpan.FromHours(8));
        var result = await ScalarAsync("SELECT CAST(@d AS TIMESTAMPTZ)", Param("@d", expected));
        // The renderer normalises to UTC (DuckDB rejects numeric offsets); the round-tripped
        // instant must still match the original, expressed in UTC.
        var roundtripped = result switch
        {
            DateTimeOffset dto => dto.UtcDateTime,
            DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
            _ => throw new Xunit.Sdk.XunitException($"Unexpected DateTimeOffset representation: {result?.GetType().Name}")
        };
        Assert.Equal(expected.UtcDateTime.Ticks / TimeSpan.TicksPerMillisecond,
            roundtripped.Ticks / TimeSpan.TicksPerMillisecond);
    }

    [Fact]
    public async Task Decimal_Parameter_Roundtrips()
    {
        var expected = 1234.56m;
        var result = await ScalarAsync("SELECT CAST(@d AS DECIMAL(10,2))", Param("@d", expected));
        Assert.Equal(expected, Convert.ToDecimal(result));
    }

    [Fact]
    public async Task Boolean_Parameter_Roundtrips()
    {
        var result = await ScalarAsync("SELECT @b", Param("@b", true));
        Assert.True(Convert.ToBoolean(result));
    }

    [Fact]
    public async Task Long_Parameter_Roundtrips()
    {
        const long expected = 9223372036854775806L;
        var result = await ScalarAsync("SELECT @n", Param("@n", expected));
        Assert.Equal(expected, Convert.ToInt64(result));
    }

    [Fact]
    public async Task String_WithSpecialChars_Roundtrips()
    {
        const string expected = "O'Brien; -- injection\n\"quoted\"";
        var result = await ScalarAsync("SELECT @s", Param("@s", expected));
        Assert.Equal(expected, result?.ToString());
    }

    [Fact]
    public async Task Null_Parameter_Roundtrips()
    {
        var result = await ScalarAsync("SELECT @n", Param("@n", DBNull.Value));
        Assert.True(result is DBNull or null);
    }

    [Fact]
    public async Task ByteArray_Parameter_RoundtripsAsBlob()
    {
        var expected = new byte[] { 0x00, 0x01, 0xFF, 0xAB, 0xCD };
        var result = await ScalarAsync("SELECT CAST(@b AS BLOB)", Param("@b", expected));

        var actual = result switch
        {
            byte[] b => b,
            string s when s.StartsWith("\\x") => Convert.FromHexString(s[2..]),
            _ => throw new Xunit.Sdk.XunitException($"Unexpected BLOB representation: {result?.GetType().Name}")
        };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task MixedTypes_InSingleQuery_AllResolve()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @id, @label, @amount, @active";
        command.Parameters.Add(Param("@id", 42));
        command.Parameters.Add(Param("@label", "mixed"));
        command.Parameters.Add(Param("@amount", 99.95m));
        command.Parameters.Add(Param("@active", false));

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(42L, Convert.ToInt64(reader.GetValue(0)));
        Assert.Equal("mixed", reader.GetString(1));
        Assert.Equal(99.95m, Convert.ToDecimal(reader.GetValue(2)));
        Assert.False(reader.GetBoolean(3));
    }

    [Fact]
    public async Task ParameterizedInsert_PersistsAndReadsBack()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        var tableName = $"param_roundtrip_{Guid.NewGuid():N}".Substring(0, 30);

        await using (var create = connection.CreateCommand())
        {
            create.CommandText = $"CREATE TABLE {tableName} (id INTEGER, val VARCHAR)";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.CommandText = $"INSERT INTO {tableName} VALUES (@id, @val)";
                insert.Parameters.Add(Param("@id", 7));
                insert.Parameters.Add(Param("@val", "persisted"));
                await insert.ExecuteNonQueryAsync();
            }

            await using var read = connection.CreateCommand();
            read.CommandText = $"SELECT id, val FROM {tableName}";
            await using var reader = await read.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(7L, Convert.ToInt64(reader.GetValue(0)));
            Assert.Equal("persisted", reader.GetString(1));
        }
        finally
        {
            await using var cleanup = connection.CreateCommand();
            cleanup.CommandText = $"DROP TABLE IF EXISTS {tableName}";
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
