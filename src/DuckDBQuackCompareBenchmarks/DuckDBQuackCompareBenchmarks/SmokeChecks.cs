using System.Data.Common;
using AzrngQuackConnection = Azrng.DuckDB.Quack.QuackConnection;
using LocalQuackConnection = Quack.DuckDB.QuackDuckDbConnection;

namespace DuckDBQuackCompareBenchmarks;

internal static class SmokeChecks
{
    public static async Task RunAsync()
    {
        try
        {
            var localScalar = await ExecuteScalarAsync(
                () => new LocalQuackConnection(Program.ConnectionString),
                "SELECT 1");
            var azrngScalar = await ExecuteScalarAsync(
                () => new AzrngQuackConnection(Program.ConnectionString),
                "SELECT 1");

            EnsureEqual(localScalar, azrngScalar, "SELECT 1");

            var localParameterized = await ExecuteScalarAsync(
                () => new LocalQuackConnection(Program.ConnectionString),
                "SELECT @a + @b",
                ("@a", 17L),
                ("@b", 25L));
            var azrngParameterized = await ExecuteScalarAsync(
                () => new AzrngQuackConnection(Program.ConnectionString),
                "SELECT @a + @b",
                ("@a", 17L),
                ("@b", 25L));

            EnsureEqual(localParameterized, azrngParameterized, "SELECT @a + @b");
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

    private static async Task<object?> ExecuteScalarAsync(
        Func<DbConnection> connectionFactory,
        string sql,
        params (string Name, object Value)[] parameters)
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

    private static void EnsureEqual(object? localValue, object? azrngValue, string scenario)
    {
        if (!Equals(Convert.ToString(localValue), Convert.ToString(azrngValue)))
        {
            throw new InvalidOperationException(
                $"Smoke check failed for {scenario}. Local={localValue ?? "<null>"}, Azrng={azrngValue ?? "<null>"}.");
        }
    }
}
