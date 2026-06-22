using System.Data.Common;
using AzrngQuackConnection = Azrng.DuckDB.Quack.QuackConnection;
using LocalQuackConnection = Azrng.DuckDB.Data.Quack.QuackDuckDbConnection;

namespace DuckDBQuackCompareBenchmarks;

internal static class SmokeChecks
{
    public static async Task RunAsync()
    {
        try
        {
            await EnsureScalarParityAsync("SELECT 1");
            await EnsureScalarParityAsync(
                "SELECT @a + @b",
                ("@a", 17L),
                ("@b", 25L));
            await EnsureScalarParityAsync("SELECT NULL");
            await EnsureScalarParityAsync(
                "SELECT @text",
                ("@text", "中文 'quote' emoji 😀"));
            await EnsureScalarParityAsync(
                "SELECT @flag",
                ("@flag", true));
            await EnsureScalarParityAsync(
                "SELECT @amount",
                ("@amount", 12345.6789m));
            await EnsureAzrngScalarAsync(
                "SELECT CAST(@day AS DATE)",
                new DateOnly(2026, 6, 21),
                ("@day", new DateOnly(2026, 6, 21)));

            await EnsureRowsParityAsync(
                "SELECT i, CAST(i AS VARCHAR) AS label FROM range(0, 5) t(i)");

            await EnsureAzrngDmlRoundtripAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Benchmark smoke check failed. Start the shared Quack container with " +
                "`docker compose -f docker/compose.yml up -d`, " +
                "or set QUACK_PROTOCOL_CONNECTION_STRING to a reachable Quack server.",
                ex);
        }
    }

    private static async Task EnsureScalarParityAsync(
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        var localScalar = await ExecuteScalarAsync(
            () => new LocalQuackConnection(Program.LocalQueryConnectionString),
            sql,
            parameters);
        var azrngScalar = await ExecuteScalarAsync(
            () => new AzrngQuackConnection(Program.ConnectionString),
            sql,
            parameters);

        EnsureEqual(localScalar, azrngScalar, sql);
    }

    private static async Task EnsureRowsParityAsync(string sql)
    {
        var localRows = await ExecuteRowsAsync(() => new LocalQuackConnection(Program.LocalQueryConnectionString), sql);
        var azrngRows = await ExecuteRowsAsync(() => new AzrngQuackConnection(Program.ConnectionString), sql);

        if (localRows.Count != azrngRows.Count)
        {
            throw new InvalidOperationException(
                $"Smoke check failed for {sql}. Local row count={localRows.Count}, Azrng row count={azrngRows.Count}.");
        }

        for (var row = 0; row < localRows.Count; row++)
        {
            var local = localRows[row];
            var azrng = azrngRows[row];
            if (local.Length != azrng.Length)
            {
                throw new InvalidOperationException(
                    $"Smoke check failed for {sql} row {row}. Local column count={local.Length}, Azrng column count={azrng.Length}.");
            }

            for (var col = 0; col < local.Length; col++)
            {
                EnsureEqual(local[col], azrng[col], $"{sql} row {row} col {col}");
            }
        }
    }

    private static async Task EnsureAzrngScalarAsync(
        string sql,
        object? expected,
        params (string Name, object? Value)[] parameters)
    {
        var actual = await ExecuteScalarAsync(
            () => new AzrngQuackConnection(Program.ConnectionString),
            sql,
            parameters);

        EnsureEqual(expected, actual, sql);
    }

    private static async Task EnsureAzrngDmlRoundtripAsync()
    {
        var tableName = "smoke_azrng_" + Guid.NewGuid().ToString("N")[..12];
        await using var connection = new AzrngQuackConnection(Program.ConnectionString);
        await connection.OpenAsync();

        try
        {
            await ExecuteNonQueryAsync(connection, $"CREATE TABLE {tableName} (id INTEGER, label VARCHAR)");
            await ExecuteNonQueryAsync(connection, $"INSERT INTO {tableName} VALUES (1, 'alpha'), (2, '中文')");
            var count = await ExecuteScalarAsync(() => new AzrngQuackConnection(Program.ConnectionString), $"SELECT COUNT(*) FROM {tableName}");
            EnsureEqual(2L, count, "Azrng DDL/DML roundtrip count");
        }
        finally
        {
            await ExecuteNonQueryAsync(connection, $"DROP TABLE IF EXISTS {tableName}");
        }
    }

    private static async Task<object?> ExecuteScalarAsync(
        Func<DbConnection> connectionFactory,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using var connection = connectionFactory();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? reader.GetValue(0) : null;
    }

    private static async Task<List<object?[]>> ExecuteRowsAsync(
        Func<DbConnection> connectionFactory,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using var connection = connectionFactory();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object?[]>();
        while (await reader.ReadAsync())
        {
            var values = new object?[reader.FieldCount];
            for (var i = 0; i < values.Length; i++)
            {
                var value = reader.GetValue(i);
                values[i] = value == DBNull.Value ? null : value;
            }

            rows.Add(values);
        }

        return rows;
    }

    private static async Task ExecuteNonQueryAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static void EnsureEqual(object? localValue, object? azrngValue, string scenario)
    {
        if (!Equals(Convert.ToString(localValue), Convert.ToString(azrngValue)))
        {
            throw new InvalidOperationException(
                $"Smoke check failed for {scenario}. Local={localValue ?? "<null>"}, Azrng={azrngValue ?? "<null>"}.");
        }
    }
}
