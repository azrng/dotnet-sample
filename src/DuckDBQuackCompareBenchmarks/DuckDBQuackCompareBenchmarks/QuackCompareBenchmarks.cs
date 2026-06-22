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
    [Benchmark(Description = "Local ATTACH Open+Dispose")]
    public async Task LocalAttach_OpenDispose()
    {
        await using var connection = new LocalQuackConnection(Program.LocalAttachConnectionString);
        await connection.OpenAsync();
    }

    [Benchmark(Description = "Local quack_query Open+Dispose")]
    public async Task LocalQuery_OpenDispose()
    {
        await using var connection = new LocalQuackConnection(Program.LocalQueryConnectionString);
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
    private LocalQuackConnection _localAttachConnection = null!;
    private LocalQuackConnection _localQueryConnection = null!;
    private AzrngQuackConnection _azrngConnection = null!;
    private string _table = "";
    private string _attachTable = "";

    [GlobalSetup]
    public async Task Setup()
    {
        _localAttachConnection = new LocalQuackConnection(Program.LocalAttachConnectionString);
        await _localAttachConnection.OpenAsync();
        _table = "bench_query_" + Guid.NewGuid().ToString("N")[..12];
        _attachTable = $"{Program.LocalCatalog}.main.{_table}";
        await ExecuteNonQueryAsync(_localAttachConnection, $"CREATE TABLE {_attachTable} AS SELECT i::BIGINT AS i, CAST(i AS VARCHAR) AS label FROM range(0, 100000) t(i)");

        _azrngConnection = new AzrngQuackConnection(Program.ConnectionString);
        await _azrngConnection.OpenAsync();
        _localQueryConnection = new LocalQuackConnection(Program.LocalQueryConnectionString);
        await _localQueryConnection.OpenAsync();

        await ExecuteReadFirstAsync(_azrngConnection, BuildPointLookupSql(_table), ("@id", 42L));
        await ExecuteReadFirstAsync(_localAttachConnection, BuildAttachPointLookupSql(_attachTable), ("", 42L));
        await ExecuteReadFirstAsync(_localQueryConnection, BuildPointLookupSql(_table), ("@id", 42L));
    }

    [Benchmark(Description = "Azrng remote point lookup")]
    public async Task Azrng_PointLookup()
    {
        await ExecuteReadFirstAsync(_azrngConnection, BuildPointLookupSql(_table), ("@id", 42L));
    }

    [Benchmark(Baseline = true, Description = "Local ATTACH remote point lookup")]
    public async Task LocalAttach_PointLookup()
    {
        await ExecuteReadFirstAsync(_localAttachConnection, BuildAttachPointLookupSql(_attachTable), ("", 42L));
    }

    [Benchmark(Description = "Local quack_query remote point lookup")]
    public async Task LocalQuery_PointLookup()
    {
        await ExecuteReadFirstAsync(_localQueryConnection, BuildPointLookupSql(_table), ("@id", 42L));
    }

    [Benchmark(Description = "Azrng remote parameterized aggregate")]
    public async Task Azrng_ParameterizedAggregate()
    {
        await ExecuteReadFirstAsync(_azrngConnection, BuildParameterizedAggregateSql(_table), ("@start", 1000L), ("@end", 11000L));
    }

    [Benchmark(Description = "Local ATTACH remote parameterized aggregate")]
    public async Task LocalAttach_ParameterizedAggregate()
    {
        await ExecuteReadFirstAsync(_localAttachConnection, BuildAttachParameterizedAggregateSql(_attachTable), ("", 1000L), ("", 11000L));
    }

    [Benchmark(Description = "Local quack_query remote parameterized aggregate")]
    public async Task LocalQuery_ParameterizedAggregate()
    {
        await ExecuteReadFirstAsync(_localQueryConnection, BuildParameterizedAggregateSql(_table), ("@start", 1000L), ("@end", 11000L));
    }

    [Benchmark(Description = "Azrng remote aggregate 10k")]
    public async Task Azrng_Aggregate10k()
    {
        await ExecuteReadFirstAsync(_azrngConnection, BuildAggregateSql(_table));
    }

    [Benchmark(Description = "Local ATTACH remote aggregate 10k")]
    public async Task LocalAttach_Aggregate10k()
    {
        await ExecuteReadFirstAsync(_localAttachConnection, BuildAggregateSql(_attachTable));
    }

    [Benchmark(Description = "Local quack_query remote aggregate 10k")]
    public async Task LocalQuery_Aggregate10k()
    {
        await ExecuteReadFirstAsync(_localQueryConnection, BuildAggregateSql(_table));
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_azrngConnection is not null && !string.IsNullOrWhiteSpace(_table))
            await ExecuteNonQueryAsync(_azrngConnection, $"DROP TABLE IF EXISTS {_table}");
    }

    public async ValueTask DisposeAsync()
    {
        if (_localAttachConnection is not null)
            await _localAttachConnection.DisposeAsync();
        if (_localQueryConnection is not null)
            await _localQueryConnection.DisposeAsync();
        if (_azrngConnection is not null)
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

    private static async Task ExecuteNonQueryAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
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

    private static string BuildPointLookupSql(string table) =>
        $"SELECT label FROM {table} WHERE i = @id";

    private static string BuildAttachPointLookupSql(string table) =>
        $"SELECT label FROM {table} WHERE i = ?";

    private static string BuildParameterizedAggregateSql(string table) =>
        $"SELECT COUNT(*), SUM(i) FROM {table} WHERE i >= @start AND i < @end";

    private static string BuildAttachParameterizedAggregateSql(string table) =>
        $"SELECT COUNT(*), SUM(i) FROM {table} WHERE i >= ? AND i < ?";

    private static string BuildAggregateSql(string table) =>
        $"SELECT COUNT(*), SUM(i) FROM {table} WHERE i < 10000";
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 3)]
public class ResultSetBench : IAsyncDisposable
{
    private LocalQuackConnection _localAttachConnection = null!;
    private LocalQuackConnection _localQueryConnection = null!;
    private AzrngQuackConnection _azrngConnection = null!;
    private string _table = "";
    private string _attachTable = "";

    [Params(10000, 100000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _localAttachConnection = new LocalQuackConnection(Program.LocalAttachConnectionString);
        await _localAttachConnection.OpenAsync();
        _table = "bench_result_" + Guid.NewGuid().ToString("N")[..12];
        _attachTable = $"{Program.LocalCatalog}.main.{_table}";
        await ExecuteNonQueryAsync(_localAttachConnection, $"CREATE TABLE {_attachTable} AS SELECT i::BIGINT AS i, CAST(i AS VARCHAR) AS label FROM range(0, {Rows}) t(i)");

        _azrngConnection = new AzrngQuackConnection(Program.ConnectionString);
        await _azrngConnection.OpenAsync();
        _localQueryConnection = new LocalQuackConnection(Program.LocalQueryConnectionString);
        await _localQueryConnection.OpenAsync();
    }

    [Benchmark(Description = "Azrng remote read N rows")]
    public async Task Azrng_ReadRows()
    {
        await ReadRowsAsync(_azrngConnection, _table);
    }

    [Benchmark(Baseline = true, Description = "Local ATTACH remote read N rows")]
    public async Task LocalAttach_ReadRows()
    {
        await ReadRowsAsync(_localAttachConnection, _attachTable);
    }

    [Benchmark(Description = "Local quack_query remote read N rows")]
    public async Task LocalQuery_ReadRows()
    {
        await ReadRowsAsync(_localQueryConnection, _table);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_localAttachConnection is not null && !string.IsNullOrWhiteSpace(_attachTable))
            await ExecuteNonQueryAsync(_localAttachConnection, $"DROP TABLE IF EXISTS {_attachTable}");
    }

    public async ValueTask DisposeAsync()
    {
        if (_localAttachConnection is not null)
            await _localAttachConnection.DisposeAsync();
        if (_localQueryConnection is not null)
            await _localQueryConnection.DisposeAsync();
        if (_azrngConnection is not null)
            await _azrngConnection.DisposeAsync();
    }

    private static async Task ReadRowsAsync(DbConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT i, label FROM {table} ORDER BY i";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            _ = reader.GetInt64(0);
            _ = reader.GetString(1);
        }
    }

    private static async Task ExecuteNonQueryAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
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
    private LocalQuackConnection[] _localAttachConnections = [];
    private LocalQuackConnection[] _localQueryConnections = [];
    private AzrngQuackConnection[] _azrngConnections = [];
    private LocalQuackConnection _setupConnection = null!;
    private string _table = "";
    private string _attachTable = "";

    [Params(4, 16)]
    public int Degree { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _setupConnection = new LocalQuackConnection(Program.LocalAttachConnectionString);
        await _setupConnection.OpenAsync();
        _table = "bench_concurrency_" + Guid.NewGuid().ToString("N")[..12];
        _attachTable = $"{Program.LocalCatalog}.main.{_table}";
        await ExecuteNonQueryAsync(_setupConnection, $"CREATE TABLE {_attachTable} AS SELECT i::BIGINT AS i, CAST(i AS VARCHAR) AS label FROM range(0, 100000) t(i)");

        _localAttachConnections = new LocalQuackConnection[Degree];
        _localQueryConnections = new LocalQuackConnection[Degree];
        _azrngConnections = new AzrngQuackConnection[Degree];
        for (var i = 0; i < Degree; i++)
        {
            _localAttachConnections[i] = new LocalQuackConnection(Program.LocalAttachConnectionString);
            await OpenWithRetryAsync(_localAttachConnections[i]);
            _localQueryConnections[i] = new LocalQuackConnection(Program.LocalQueryConnectionString);
            await OpenWithRetryAsync(_localQueryConnections[i]);
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

    [Benchmark(Description = "Azrng parallel remote point lookup")]
    public async Task Azrng_ParallelQueries()
    {
        await RunParallelAsync(_azrngConnections, BuildNamedPointLookupSql(_table), namedParameter: true);
    }

    [Benchmark(Baseline = true, Description = "Local ATTACH parallel remote point lookup")]
    public async Task LocalAttach_ParallelQueries()
    {
        await RunParallelAsync(_localAttachConnections, BuildQuestionMarkPointLookupSql(_attachTable), namedParameter: false);
    }

    [Benchmark(Description = "Local quack_query parallel remote point lookup")]
    public async Task LocalQuery_ParallelQueries()
    {
        await RunParallelAsync(_localQueryConnections, BuildNamedPointLookupSql(_table), namedParameter: true);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_setupConnection is not null && !string.IsNullOrWhiteSpace(_attachTable))
            await ExecuteNonQueryAsync(_setupConnection, $"DROP TABLE IF EXISTS {_attachTable}");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var conn in _localAttachConnections)
            await conn.DisposeAsync();
        foreach (var conn in _localQueryConnections)
            await conn.DisposeAsync();
        foreach (var conn in _azrngConnections)
            await conn.DisposeAsync();
        if (_setupConnection is not null)
            await _setupConnection.DisposeAsync();
    }

    private static Task RunParallelAsync(DbConnection[] connections, string sql, bool namedParameter)
    {
        var tasks = new Task[connections.Length];
        for (var i = 0; i < connections.Length; i++)
        {
            var conn = connections[i];
            tasks[i] = Task.Run(async () =>
            {
                await using var command = conn.CreateCommand();
                command.CommandText = sql;
                var parameter = command.CreateParameter();
                parameter.ParameterName = namedParameter ? "@id" : "";
                parameter.Value = 42L;
                command.Parameters.Add(parameter);
                await using var reader = await command.ExecuteReaderAsync();
                await reader.ReadAsync();
            });
        }

        return Task.WhenAll(tasks);
    }

    private static async Task ExecuteNonQueryAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static string BuildNamedPointLookupSql(string table) =>
        $"SELECT label FROM {table} WHERE i = @id";

    private static string BuildQuestionMarkPointLookupSql(string table) =>
        $"SELECT label FROM {table} WHERE i = ?";
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
    private LocalQuackConnection _setupConnection = null!;
    private string _localTable = "";
    private string _azrngTable = "";

    [Params(100, 1000)]
    public int Rows { get; set; }

    [Params(100, 500)]
    public int BatchSize { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _setupConnection = new LocalQuackConnection(Program.LocalAttachConnectionString);
        await _setupConnection.OpenAsync();
        _localTable = "bench_local_" + Guid.NewGuid().ToString("N")[..12];
        _azrngTable = "bench_azrng_" + Guid.NewGuid().ToString("N")[..12];
        var attachLocalTable = $"{Program.LocalCatalog}.main.{_localTable}";
        var attachAzrngTable = $"{Program.LocalCatalog}.main.{_azrngTable}";
        await ExecuteNonQueryAsync(_setupConnection, $"CREATE TABLE {attachLocalTable} (id INTEGER, label VARCHAR)");
        await ExecuteNonQueryAsync(_setupConnection, $"CREATE TABLE {attachAzrngTable} (id INTEGER, label VARCHAR)");

        _localConnection = new LocalQuackConnection(Program.LocalQueryConnectionString);
        await _localConnection.OpenAsync();
        _azrngConnection = new AzrngQuackConnection(Program.ConnectionString);
        await _azrngConnection.OpenAsync();
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
        if (_setupConnection is not null)
        {
            var attachLocalTable = $"{Program.LocalCatalog}.main.{_localTable}";
            var attachAzrngTable = $"{Program.LocalCatalog}.main.{_azrngTable}";
            await ExecuteNonQueryAsync(_setupConnection, $"DROP TABLE IF EXISTS {attachLocalTable}");
            await ExecuteNonQueryAsync(_setupConnection, $"DROP TABLE IF EXISTS {attachAzrngTable}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _localConnection.DisposeAsync();
        await _azrngConnection.DisposeAsync();
        if (_setupConnection is not null)
            await _setupConnection.DisposeAsync();
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
