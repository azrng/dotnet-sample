using System.Data.Common;
using BenchmarkDotNet.Attributes;
using Azrng.DuckDB.Quack;
using AzrngQuackConnection = Azrng.DuckDB.Quack.QuackConnection;
using LocalQuackConnection = Azrng.DuckDB.Data.Quack.QuackDuckDbConnection;

namespace DuckDBQuackCompareBenchmarks;

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 5)]
public class ConnectionBench
{
    [Benchmark(Baseline = true, Description = "Local Open+Dispose")]
    public async Task Local_OpenDispose()
    {
        await using var connection = new LocalQuackConnection(Program.ConnectionString);
        await connection.OpenAsync();
    }

    [Benchmark(Description = "Azrng Open+Dispose")]
    public async Task Azrng_OpenDispose()
    {
        await using var connection = new AzrngQuackConnection(Program.ConnectionString);
        await connection.OpenAsync();
    }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 5)]
public class QueryBench : IAsyncDisposable
{
    private LocalQuackConnection _localConnection = null!;
    private AzrngQuackConnection _azrngConnection = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _localConnection = new LocalQuackConnection(Program.LocalAttachConnectionString);
        await _localConnection.OpenAsync();
        _azrngConnection = new AzrngQuackConnection(Program.ConnectionString);
        await _azrngConnection.OpenAsync();
    }

    [Benchmark(Baseline = true, Description = "Local SELECT 1")]
    public async Task Local_Select1()
    {
        await ExecuteReadFirstAsync(_localConnection, "SELECT 1");
    }

    [Benchmark(Description = "Azrng SELECT 1")]
    public async Task Azrng_Select1()
    {
        await ExecuteReadFirstAsync(_azrngConnection, "SELECT 1");
    }

    [Benchmark(Description = "Local SELECT @a + @b")]
    public async Task Local_ParameterizedSelect()
    {
        await using var command = _localConnection.CreateCommand();
        command.CommandText = "SELECT ? + ?";
        var p1 = command.CreateParameter();
        p1.Value = 17L;
        command.Parameters.Add(p1);
        var p2 = command.CreateParameter();
        p2.Value = 25L;
        command.Parameters.Add(p2);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
    }

    [Benchmark(Description = "Azrng SELECT @a + @b")]
    public async Task Azrng_ParameterizedSelect()
    {
        await ExecuteReadFirstAsync(_azrngConnection, "SELECT @a + @b", ("@a", 17L), ("@b", 25L));
    }

    [Benchmark(Description = "Local COUNT/SUM over 10k")]
    public async Task Local_Aggregate10k()
    {
        await ExecuteReadFirstAsync(_localConnection, "SELECT COUNT(*), SUM(i) FROM range(0, 10000) t(i)");
    }

    [Benchmark(Description = "Azrng COUNT/SUM over 10k")]
    public async Task Azrng_Aggregate10k()
    {
        await ExecuteReadFirstAsync(_azrngConnection, "SELECT COUNT(*), SUM(i) FROM range(0, 10000) t(i)");
    }

    public async ValueTask DisposeAsync()
    {
        await _localConnection.DisposeAsync();
        await _azrngConnection.DisposeAsync();
    }

    private static async Task ExecuteReadFirstAsync(
        DbConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
    }

    private static void AddParameters(DbCommand command, (string Name, object Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 5)]
public class QueryBenchQuack : IAsyncDisposable
{
    private LocalQuackConnection _localConnection = null!;
    private AzrngQuackConnection _azrngConnection = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _localConnection = new LocalQuackConnection(Program.ConnectionString);
        await _localConnection.OpenAsync();
        _azrngConnection = new AzrngQuackConnection(Program.ConnectionString);
        await _azrngConnection.OpenAsync();
    }

    [Benchmark(Baseline = true, Description = "Local quack_query SELECT 1")]
    public async Task Local_Select1()
    {
        await ExecuteReadFirstAsync(_localConnection, "SELECT 1");
    }

    [Benchmark(Description = "Azrng SELECT 1")]
    public async Task Azrng_Select1()
    {
        await ExecuteReadFirstAsync(_azrngConnection, "SELECT 1");
    }

    [Benchmark(Description = "Local quack_query SELECT @a + @b")]
    public async Task Local_ParameterizedSelect()
    {
        await ExecuteReadFirstAsync(_localConnection, "SELECT @a + @b", ("@a", 17L), ("@b", 25L));
    }

    [Benchmark(Description = "Azrng SELECT @a + @b")]
    public async Task Azrng_ParameterizedSelect()
    {
        await ExecuteReadFirstAsync(_azrngConnection, "SELECT @a + @b", ("@a", 17L), ("@b", 25L));
    }

    [Benchmark(Description = "Local quack_query COUNT/SUM over 10k")]
    public async Task Local_Aggregate10k()
    {
        await ExecuteReadFirstAsync(_localConnection, "SELECT COUNT(*), SUM(i) FROM range(0, 10000) t(i)");
    }

    [Benchmark(Description = "Azrng COUNT/SUM over 10k")]
    public async Task Azrng_Aggregate10k()
    {
        await ExecuteReadFirstAsync(_azrngConnection, "SELECT COUNT(*), SUM(i) FROM range(0, 10000) t(i)");
    }

    public async ValueTask DisposeAsync()
    {
        await _localConnection.DisposeAsync();
        await _azrngConnection.DisposeAsync();
    }

    private static async Task ExecuteReadFirstAsync(
        DbConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
    }

    private static void AddParameters(DbCommand command, (string Name, object Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 3)]
public class ResultSetBench : IAsyncDisposable
{
    private LocalQuackConnection _localConnection = null!;
    private AzrngQuackConnection _azrngConnection = null!;

    [Params(10000, 100000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _localConnection = new LocalQuackConnection(Program.ConnectionString);
        await _localConnection.OpenAsync();
        _azrngConnection = new AzrngQuackConnection(Program.ConnectionString);
        await _azrngConnection.OpenAsync();
    }

    [Benchmark(Baseline = true, Description = "Local read N rows")]
    public async Task Local_ReadRows()
    {
        await ReadRowsAsync(_localConnection, Rows);
    }

    [Benchmark(Description = "Azrng read N rows")]
    public async Task Azrng_ReadRows()
    {
        await ReadRowsAsync(_azrngConnection, Rows);
    }

    public async ValueTask DisposeAsync()
    {
        await _localConnection.DisposeAsync();
        await _azrngConnection.DisposeAsync();
    }

    private static async Task ReadRowsAsync(DbConnection connection, int rows)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT i, CAST(i AS VARCHAR) FROM range(0, {rows}) t(i)";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            _ = reader.GetInt64(0);
            _ = reader.GetString(1);
        }
    }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 3)]
public class ReaderAccessBench : IAsyncDisposable
{
    private AzrngQuackConnection _connection = null!;
    private object[] _values = [];

    [Params(10000, 100000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _connection = new AzrngQuackConnection(Program.ConnectionString);
        await _connection.OpenAsync();
        _values = new object[4];
    }

    [Benchmark(Description = "Azrng reader typed getters")]
    public async Task Azrng_TypedGetters()
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT i, CAST(i AS VARCHAR), i % 2 = 0, i * 1.25 FROM range(0, {Rows}) t(i)";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            _ = reader.GetInt64(0);
            _ = reader.GetString(1);
            _ = reader.GetBoolean(2);
            _ = reader.GetDouble(3);
        }
    }

    [Benchmark(Description = "Azrng reader GetValue")]
    public async Task Azrng_GetValue()
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT i, CAST(i AS VARCHAR), i % 2 = 0, i * 1.25 FROM range(0, {Rows}) t(i)";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            _ = reader.GetValue(0);
            _ = reader.GetValue(1);
            _ = reader.GetValue(2);
            _ = reader.GetValue(3);
        }
    }

    [Benchmark(Description = "Azrng reader GetValues")]
    public async Task Azrng_GetValues()
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT i, CAST(i AS VARCHAR), i % 2 = 0, i * 1.25 FROM range(0, {Rows}) t(i)";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            _ = reader.GetValues(_values);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 3)]
public class ConcurrencyBench : IAsyncDisposable
{
    private LocalQuackConnection[] _localConnections = [];
    private AzrngQuackConnection[] _azrngConnections = [];

    [Params(4, 16)]
    public int Degree { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _localConnections = new LocalQuackConnection[Degree];
        _azrngConnections = new AzrngQuackConnection[Degree];
        for (var i = 0; i < Degree; i++)
        {
            _localConnections[i] = new LocalQuackConnection(Program.ConnectionString);
            await OpenWithRetryAsync(_localConnections[i]);
            _azrngConnections[i] = new AzrngQuackConnection(Program.ConnectionString);
            await OpenWithRetryAsync(_azrngConnections[i]);
            await Task.Delay(50);
        }
    }

    private static async Task OpenWithRetryAsync(DbConnection connection, int maxRetries = 3)
    {
        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                await connection.OpenAsync();
                return;
            }
            catch when (i < maxRetries - 1)
            {
                await Task.Delay(100 * (i + 1));
            }
        }
    }

    [Benchmark(Baseline = true, Description = "Local parallel SELECT 1")]
    public async Task Local_ParallelQueries()
    {
        await RunParallelAsync(_localConnections);
    }

    [Benchmark(Description = "Azrng parallel SELECT 1")]
    public async Task Azrng_ParallelQueries()
    {
        await RunParallelAsync(_azrngConnections);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var conn in _localConnections)
            await conn.DisposeAsync();
        foreach (var conn in _azrngConnections)
            await conn.DisposeAsync();
    }

    private static Task RunParallelAsync(DbConnection[] connections)
    {
        var tasks = new Task[connections.Length];
        for (var i = 0; i < connections.Length; i++)
        {
            var conn = connections[i];
            tasks[i] = Task.Run(async () =>
            {
                await using var command = conn.CreateCommand();
                command.CommandText = "SELECT 1";
                await using var reader = await command.ExecuteReaderAsync();
                await reader.ReadAsync();
            });
        }

        return Task.WhenAll(tasks);
    }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 5)]
public class PoolBench : IAsyncDisposable
{
    private QuackConnectionPool _pool = null!;

    [Params(4, 16)]
    public int Degree { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _pool = new QuackConnectionPool(Program.ConnectionString, maxPoolSize: 16);
        await _pool.WarmUpAsync(16);
    }

    [Benchmark(Description = "Azrng pool acquire + return")]
    public async Task AzrngPool_AcquireReturn()
    {
        var connection = await _pool.GetConnectionAsync();
        _pool.ReturnConnection(connection);
    }

    [Benchmark(Description = "Azrng pool rent + dispose")]
    public async Task AzrngPool_RentDispose()
    {
        await using var lease = await _pool.RentConnectionAsync();
    }

    [Benchmark(Description = "Azrng pool SELECT 1")]
    public async Task AzrngPool_Select1()
    {
        var connection = await _pool.GetConnectionAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
        }
        finally
        {
            _pool.ReturnConnection(connection);
        }
    }

    [Benchmark(Description = "Azrng pool lease SELECT 1")]
    public async Task AzrngPool_LeaseSelect1()
    {
        await using var lease = await _pool.RentConnectionAsync();
        await using var command = lease.Connection.CreateCommand();
        command.CommandText = "SELECT 1";
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
    }

    [Benchmark(Description = "Azrng pool parallel SELECT 1")]
    public async Task AzrngPool_ParallelSelect1()
    {
        var tasks = new Task[Degree];
        for (var i = 0; i < Degree; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                var connection = await _pool.GetConnectionAsync();
                try
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "SELECT 1";
                    await using var reader = await command.ExecuteReaderAsync();
                    await reader.ReadAsync();
                }
                finally
                {
                    _pool.ReturnConnection(connection);
                }
            });
        }

        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "Azrng pool lease parallel SELECT 1")]
    public async Task AzrngPool_LeaseParallelSelect1()
    {
        var tasks = new Task[Degree];
        for (var i = 0; i < Degree; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                await using var lease = await _pool.RentConnectionAsync();
                await using var command = lease.Connection.CreateCommand();
                command.CommandText = "SELECT 1";
                await using var reader = await command.ExecuteReaderAsync();
                await reader.ReadAsync();
            });
        }

        await Task.WhenAll(tasks);
    }

    public async ValueTask DisposeAsync()
    {
        await _pool.DisposeAsync();
    }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 3)]
public class InsertBench : IAsyncDisposable
{
    private LocalQuackConnection _localConnection = null!;
    private AzrngQuackConnection _azrngConnection = null!;
    private string _localTable = "";
    private string _azrngTable = "";

    [Params(100, 1000)]
    public int Rows { get; set; }

    [Params(100, 500)]
    public int BatchSize { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _localConnection = new LocalQuackConnection(Program.ConnectionString);
        await _localConnection.OpenAsync();
        _azrngConnection = new AzrngQuackConnection(Program.ConnectionString);
        await _azrngConnection.OpenAsync();
        _localTable = "bench_local_" + Guid.NewGuid().ToString("N")[..12];
        _azrngTable = "bench_azrng_" + Guid.NewGuid().ToString("N")[..12];
        await ExecuteNonQueryAsync(_localConnection, $"CREATE TABLE {_localTable} (id INTEGER, label VARCHAR)");
        await ExecuteNonQueryAsync(_azrngConnection, $"CREATE TABLE {_azrngTable} (id INTEGER, label VARCHAR)");
    }

    [Benchmark(Baseline = true, Description = "Local per-row insert")]
    public async Task Local_PerRowInsert()
    {
        await ExecuteNonQueryAsync(_localConnection, $"DELETE FROM {_localTable}");
        for (var i = 0; i < Rows; i++)
        {
            await InsertOneAsync(_localConnection, _localTable, i);
        }
    }

    [Benchmark(Description = "Azrng per-row insert")]
    public async Task Azrng_PerRowInsert()
    {
        await ExecuteNonQueryAsync(_azrngConnection, $"DELETE FROM {_azrngTable}");
        for (var i = 0; i < Rows; i++)
        {
            await InsertOneAsync(_azrngConnection, _azrngTable, i);
        }
    }

    [Benchmark(Description = "Azrng batch insert all rows")]
    public async Task Azrng_BatchInsert()
    {
        await ExecuteNonQueryAsync(_azrngConnection, $"DELETE FROM {_azrngTable}");
        var rows = Enumerable.Range(0, Rows)
            .Select(i => new object?[] { i, $"row{i}" });
        await _azrngConnection.ExecuteBatchInsertAsync(_azrngTable, new[] { "id", "label" }, rows);
    }

    [Benchmark(Description = "Azrng paged batch insert")]
    public async Task Azrng_PagedBatchInsert()
    {
        await ExecuteNonQueryAsync(_azrngConnection, $"DELETE FROM {_azrngTable}");
        var rows = Enumerable.Range(0, Rows)
            .Select(i => new object?[] { i, $"row{i}" });
        await _azrngConnection.ExecuteParameterizedBatchInsertAsync(
            _azrngTable,
            new[] { "id", "label" },
            rows,
            BatchSize);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await ExecuteNonQueryAsync(_localConnection, $"DROP TABLE IF EXISTS {_localTable}");
        await ExecuteNonQueryAsync(_azrngConnection, $"DROP TABLE IF EXISTS {_azrngTable}");
    }

    public async ValueTask DisposeAsync()
    {
        await _localConnection.DisposeAsync();
        await _azrngConnection.DisposeAsync();
    }

    private static async Task InsertOneAsync(DbConnection connection, string tableName, int value)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {tableName} (id, label) VALUES (@id, @label)";
        var id = command.CreateParameter();
        id.ParameterName = "@id";
        id.Value = value;
        var label = command.CreateParameter();
        label.ParameterName = "@label";
        label.Value = $"row{value}";
        command.Parameters.Add(id);
        command.Parameters.Add(label);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteNonQueryAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
