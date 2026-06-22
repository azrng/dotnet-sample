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
    [Benchmark(Description = "Local ATTACH initialize+dispose")]
    public async Task LocalAttach_OpenDispose()
    {
        await using var connection = new LocalQuackConnection(Program.LocalAttachConnectionString);
        await connection.OpenAsync();
    }

    [Benchmark(Description = "Local quack_query initialize+dispose")]
    public async Task LocalQuery_OpenDispose()
    {
        await using var connection = new LocalQuackConnection(Program.LocalQueryConnectionString);
        await connection.OpenAsync();
    }

    [Benchmark(Description = "Azrng connect+dispose")]
    public async Task Azrng_OpenDispose()
    {
        await using var connection = new AzrngQuackConnection(Program.ConnectionString);
        await connection.OpenAsync();
    }
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 5)]
public class ColdQueryBench : IAsyncDisposable
{
    private LocalQuackConnection _setupConnection = null!;
    private AzrngQuackConnection _remoteSetupConnection = null!;
    private string _table = "";
    private string _attachTable = "";

    [GlobalSetup]
    public async Task Setup()
    {
        _setupConnection = new LocalQuackConnection(Program.LocalAttachConnectionString);
        await _setupConnection.OpenAsync();
        _remoteSetupConnection = new AzrngQuackConnection(Program.ConnectionString);
        await _remoteSetupConnection.OpenAsync();
        _table = "bench_cold_" + Guid.NewGuid().ToString("N")[..12];
        _attachTable = $"{Program.LocalAttachCatalog}.main.{_table}";
        await ExecuteNonQueryAsync(_setupConnection, $"CREATE TABLE {_attachTable} AS SELECT i::BIGINT AS i, CAST(i AS VARCHAR) AS label FROM range(0, 100000) t(i)");
        await ExecuteNonQueryAsync(_remoteSetupConnection, $"CREATE TABLE {_table} AS SELECT i::BIGINT AS i, CAST(i AS VARCHAR) AS label FROM range(0, 100000) t(i)");
    }

    [Benchmark(Description = "Azrng cold remote equality filter")]
    public async Task Azrng_ColdEqualityFilter()
    {
        await using var connection = new AzrngQuackConnection(Program.ConnectionString);
        await connection.OpenAsync();
        await ExecuteReadFirstAsync(connection, BuildNamedEqualityFilterSql(_table), ("@id", 42L));
    }

    [Benchmark(Baseline = true, Description = "Local ATTACH cold remote equality filter")]
    public async Task LocalAttach_ColdEqualityFilter()
    {
        await using var connection = new LocalQuackConnection(Program.LocalAttachConnectionString);
        await connection.OpenAsync();
        await ExecuteReadFirstAsync(connection, BuildQuestionMarkEqualityFilterSql(_attachTable), ("", 42L));
    }

    [Benchmark(Description = "Local quack_query cold remote equality filter")]
    public async Task LocalQuery_ColdEqualityFilter()
    {
        await using var connection = new LocalQuackConnection(Program.LocalQueryConnectionString);
        await connection.OpenAsync();
        await ExecuteReadFirstAsync(connection, BuildNamedEqualityFilterSql(_table), ("@id", 42L));
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_setupConnection is not null && !string.IsNullOrWhiteSpace(_attachTable))
            await ExecuteNonQueryAsync(_setupConnection, $"DROP TABLE IF EXISTS {_attachTable}");
        if (_remoteSetupConnection is not null && !string.IsNullOrWhiteSpace(_table))
            await ExecuteNonQueryAsync(_remoteSetupConnection, $"DROP TABLE IF EXISTS {_table}");
    }

    public async ValueTask DisposeAsync()
    {
        if (_setupConnection is not null)
            await _setupConnection.DisposeAsync();
        if (_remoteSetupConnection is not null)
            await _remoteSetupConnection.DisposeAsync();
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

    private static string BuildNamedEqualityFilterSql(string table) =>
        $"SELECT label FROM {table} WHERE i = @id";

    private static string BuildQuestionMarkEqualityFilterSql(string table) =>
        $"SELECT label FROM {table} WHERE i = ?";
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
        _attachTable = $"{Program.LocalAttachCatalog}.main.{_table}";
        await ExecuteNonQueryAsync(_localAttachConnection, $"CREATE TABLE {_attachTable} AS SELECT i::BIGINT AS i, CAST(i AS VARCHAR) AS label FROM range(0, 100000) t(i)");

        _azrngConnection = new AzrngQuackConnection(Program.ConnectionString);
        await _azrngConnection.OpenAsync();
        await ExecuteNonQueryAsync(_azrngConnection, $"CREATE TABLE {_table} AS SELECT i::BIGINT AS i, CAST(i AS VARCHAR) AS label FROM range(0, 100000) t(i)");
        _localQueryConnection = new LocalQuackConnection(Program.LocalQueryConnectionString);
        await _localQueryConnection.OpenAsync();

        await ExecuteReadFirstAsync(_azrngConnection, BuildEqualityFilterSql(_table), ("@id", 42L));
        await ExecuteReadFirstAsync(_localAttachConnection, BuildAttachEqualityFilterSql(_attachTable), ("", 42L));
        await ExecuteReadFirstAsync(_localQueryConnection, BuildEqualityFilterSql(_table), ("@id", 42L));
    }

    [Benchmark(Description = "Azrng remote equality filter")]
    public async Task Azrng_EqualityFilter()
    {
        await ExecuteReadFirstAsync(_azrngConnection, BuildEqualityFilterSql(_table), ("@id", 42L));
    }

    [Benchmark(Baseline = true, Description = "Local ATTACH remote equality filter")]
    public async Task LocalAttach_EqualityFilter()
    {
        await ExecuteReadFirstAsync(_localAttachConnection, BuildAttachEqualityFilterSql(_attachTable), ("", 42L));
    }

    [Benchmark(Description = "Local quack_query remote equality filter")]
    public async Task LocalQuery_EqualityFilter()
    {
        await ExecuteReadFirstAsync(_localQueryConnection, BuildEqualityFilterSql(_table), ("@id", 42L));
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

    private static string BuildEqualityFilterSql(string table) =>
        $"SELECT label FROM {table} WHERE i = @id";

    private static string BuildAttachEqualityFilterSql(string table) =>
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
        _attachTable = $"{Program.LocalAttachCatalog}.main.{_table}";
        await ExecuteNonQueryAsync(_localAttachConnection, $"CREATE TABLE {_attachTable} AS SELECT i::BIGINT AS i, CAST(i AS VARCHAR) AS label FROM range(0, {Rows}) t(i)");

        _azrngConnection = new AzrngQuackConnection(Program.ConnectionString);
        await _azrngConnection.OpenAsync();
        await ExecuteNonQueryAsync(_azrngConnection, $"CREATE TABLE {_table} AS SELECT i::BIGINT AS i, CAST(i AS VARCHAR) AS label FROM range(0, {Rows}) t(i)");
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

    private static async Task ReadRowsAsync(DbConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT i, label FROM {table}";
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
    private LocalQuackConnection _setupConnection = null!;
    private AzrngQuackConnection _remoteSetupConnection = null!;
    private AzrngQuackConnection _connection = null!;
    private object[] _values = [];
    private string _table = "";
    private string _attachTable = "";

    [Params(10000, 100000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _setupConnection = new LocalQuackConnection(Program.LocalAttachConnectionString);
        await _setupConnection.OpenAsync();
        _remoteSetupConnection = new AzrngQuackConnection(Program.ConnectionString);
        await _remoteSetupConnection.OpenAsync();
        _table = "bench_reader_" + Guid.NewGuid().ToString("N")[..12];
        _attachTable = $"{Program.LocalAttachCatalog}.main.{_table}";
        await ExecuteNonQueryAsync(
            _setupConnection,
            $"CREATE TABLE {_attachTable} AS SELECT i::BIGINT AS i, CAST(i AS VARCHAR) AS label, i % 2 = 0 AS is_even, i * 1.25 AS amount FROM range(0, {Rows}) t(i)");
        await ExecuteNonQueryAsync(
            _remoteSetupConnection,
            $"CREATE TABLE {_table} AS SELECT i::BIGINT AS i, CAST(i AS VARCHAR) AS label, i % 2 = 0 AS is_even, i * 1.25 AS amount FROM range(0, {Rows}) t(i)");

        _connection = new AzrngQuackConnection(Program.ConnectionString);
        await _connection.OpenAsync();
        _values = new object[4];
    }

    [Benchmark(Description = "Azrng reader typed getters")]
    public async Task Azrng_TypedGetters()
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = BuildReaderSql(_table);
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
        command.CommandText = BuildReaderSql(_table);
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
        command.CommandText = BuildReaderSql(_table);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            _ = reader.GetValues(_values);
        }
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_setupConnection is not null && !string.IsNullOrWhiteSpace(_attachTable))
            await ExecuteNonQueryAsync(_setupConnection, $"DROP TABLE IF EXISTS {_attachTable}");
        if (_remoteSetupConnection is not null && !string.IsNullOrWhiteSpace(_table))
            await ExecuteNonQueryAsync(_remoteSetupConnection, $"DROP TABLE IF EXISTS {_table}");
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
        if (_setupConnection is not null)
            await _setupConnection.DisposeAsync();
        if (_remoteSetupConnection is not null)
            await _remoteSetupConnection.DisposeAsync();
    }

    private static string BuildReaderSql(string table) =>
        $"SELECT i, label, is_even, amount FROM {table}";

    private static async Task ExecuteNonQueryAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
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
    private AzrngQuackConnection _remoteSetupConnection = null!;
    private string _table = "";
    private string _attachTable = "";

    [Params(4, 16)]
    public int Degree { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _setupConnection = new LocalQuackConnection(Program.LocalAttachConnectionString);
        await _setupConnection.OpenAsync();
        _remoteSetupConnection = new AzrngQuackConnection(Program.ConnectionString);
        await _remoteSetupConnection.OpenAsync();
        _table = "bench_concurrency_" + Guid.NewGuid().ToString("N")[..12];
        _attachTable = $"{Program.LocalAttachCatalog}.main.{_table}";
        await ExecuteNonQueryAsync(_setupConnection, $"CREATE TABLE {_attachTable} AS SELECT i::BIGINT AS i, CAST(i AS VARCHAR) AS label FROM range(0, 100000) t(i)");
        await ExecuteNonQueryAsync(_remoteSetupConnection, $"CREATE TABLE {_table} AS SELECT i::BIGINT AS i, CAST(i AS VARCHAR) AS label FROM range(0, 100000) t(i)");

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

    [Benchmark(Description = "Azrng parallel remote equality filter")]
    public async Task Azrng_ParallelQueries()
    {
        await RunParallelAsync(_azrngConnections, BuildNamedEqualityFilterSql(_table), namedParameter: true);
    }

    [Benchmark(Baseline = true, Description = "Local ATTACH parallel remote equality filter")]
    public async Task LocalAttach_ParallelQueries()
    {
        await RunParallelAsync(_localAttachConnections, BuildQuestionMarkEqualityFilterSql(_attachTable), namedParameter: false);
    }

    [Benchmark(Description = "Local quack_query parallel remote equality filter")]
    public async Task LocalQuery_ParallelQueries()
    {
        await RunParallelAsync(_localQueryConnections, BuildNamedEqualityFilterSql(_table), namedParameter: true);
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
            tasks[i] = RunOneAsync(conn, sql, namedParameter);
        }

        return Task.WhenAll(tasks);

        static async Task RunOneAsync(DbConnection conn, string sql, bool namedParameter)
        {
            await using var command = conn.CreateCommand();
            command.CommandText = sql;
            var parameter = command.CreateParameter();
            parameter.ParameterName = namedParameter ? "@id" : "";
            parameter.Value = 42L;
            command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
        }
    }

    private static async Task ExecuteNonQueryAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static string BuildNamedEqualityFilterSql(string table) =>
        $"SELECT label FROM {table} WHERE i = @id";

    private static string BuildQuestionMarkEqualityFilterSql(string table) =>
        $"SELECT label FROM {table} WHERE i = ?";
}

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 5)]
public class PoolBench : IAsyncDisposable
{
    private LocalQuackConnection _setupConnection = null!;
    private AzrngQuackConnection _remoteSetupConnection = null!;
    private QuackConnectionPool _pool = null!;
    private string _table = "";
    private string _attachTable = "";

    [Params(4, 16)]
    public int Degree { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _setupConnection = new LocalQuackConnection(Program.LocalAttachConnectionString);
        await _setupConnection.OpenAsync();
        _remoteSetupConnection = new AzrngQuackConnection(Program.ConnectionString);
        await _remoteSetupConnection.OpenAsync();
        _table = "bench_pool_" + Guid.NewGuid().ToString("N")[..12];
        _attachTable = $"{Program.LocalAttachCatalog}.main.{_table}";
        await ExecuteNonQueryAsync(_setupConnection, $"CREATE TABLE {_attachTable} AS SELECT i::BIGINT AS i, CAST(i AS VARCHAR) AS label FROM range(0, 100000) t(i)");
        await ExecuteNonQueryAsync(_remoteSetupConnection, $"CREATE TABLE {_table} AS SELECT i::BIGINT AS i, CAST(i AS VARCHAR) AS label FROM range(0, 100000) t(i)");

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

    [Benchmark(Description = "Azrng pool remote equality filter")]
    public async Task AzrngPool_EqualityFilter()
    {
        var connection = await _pool.GetConnectionAsync();
        try
        {
            await ExecuteEqualityFilterAsync(connection, _table);
        }
        finally
        {
            _pool.ReturnConnection(connection);
        }
    }

    [Benchmark(Description = "Azrng pool lease remote equality filter")]
    public async Task AzrngPool_LeaseEqualityFilter()
    {
        await using var lease = await _pool.RentConnectionAsync();
        await ExecuteEqualityFilterAsync(lease.Connection, _table);
    }

    [Benchmark(Description = "Azrng pool parallel remote equality filter")]
    public async Task AzrngPool_ParallelEqualityFilter()
    {
        var tasks = new Task[Degree];
        for (var i = 0; i < Degree; i++)
        {
            tasks[i] = RunPooledQueryAsync();
        }

        await Task.WhenAll(tasks);

        async Task RunPooledQueryAsync()
        {
            var connection = await _pool.GetConnectionAsync();
            try
            {
                await ExecuteEqualityFilterAsync(connection, _table);
            }
            finally
            {
                _pool.ReturnConnection(connection);
            }
        }
    }

    [Benchmark(Description = "Azrng pool lease parallel remote equality filter")]
    public async Task AzrngPool_LeaseParallelEqualityFilter()
    {
        var tasks = new Task[Degree];
        for (var i = 0; i < Degree; i++)
        {
            tasks[i] = RunLeaseQueryAsync();
        }

        await Task.WhenAll(tasks);

        async Task RunLeaseQueryAsync()
        {
            await using var lease = await _pool.RentConnectionAsync();
            await ExecuteEqualityFilterAsync(lease.Connection, _table);
        }
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_setupConnection is not null && !string.IsNullOrWhiteSpace(_attachTable))
            await ExecuteNonQueryAsync(_setupConnection, $"DROP TABLE IF EXISTS {_attachTable}");
        if (_remoteSetupConnection is not null && !string.IsNullOrWhiteSpace(_table))
            await ExecuteNonQueryAsync(_remoteSetupConnection, $"DROP TABLE IF EXISTS {_table}");
    }

    public async ValueTask DisposeAsync()
    {
        if (_pool is not null)
            await _pool.DisposeAsync();
        if (_setupConnection is not null)
            await _setupConnection.DisposeAsync();
        if (_remoteSetupConnection is not null)
            await _remoteSetupConnection.DisposeAsync();
    }

    private static async Task ExecuteEqualityFilterAsync(DbConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT label FROM {table} WHERE i = @id";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = 42L;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
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
public class InsertPerRowBench : IAsyncDisposable
{
    private LocalQuackConnection _localConnection = null!;
    private AzrngQuackConnection _azrngConnection = null!;
    private LocalQuackConnection _setupConnection = null!;
    private string _localTable = "";
    private string _azrngTable = "";

    [Params(100, 1000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _setupConnection = new LocalQuackConnection(Program.LocalAttachConnectionString);
        await _setupConnection.OpenAsync();
        _localTable = "bench_local_" + Guid.NewGuid().ToString("N")[..12];
        _azrngTable = "bench_azrng_" + Guid.NewGuid().ToString("N")[..12];

        _localConnection = new LocalQuackConnection(Program.LocalQueryConnectionString);
        await _localConnection.OpenAsync();
        _azrngConnection = new AzrngQuackConnection(Program.ConnectionString);
        await _azrngConnection.OpenAsync();
        await ExecuteNonQueryAsync(_localConnection, $"CREATE TABLE {_localTable} (id INTEGER, label VARCHAR)");
        await ExecuteNonQueryAsync(_azrngConnection, $"CREATE TABLE {_azrngTable} (id INTEGER, label VARCHAR)");
    }

    [IterationSetup]
    public async Task IterationSetup()
    {
        await ExecuteNonQueryAsync(_localConnection, $"DELETE FROM {_localTable}");
        await ExecuteNonQueryAsync(_azrngConnection, $"DELETE FROM {_azrngTable}");
    }

    [Benchmark(Baseline = true, Description = "Local per-row insert")]
    public async Task Local_PerRowInsert()
    {
        for (var i = 0; i < Rows; i++)
        {
            await InsertOneAsync(_localConnection, _localTable, i);
        }
    }

    [Benchmark(Description = "Azrng per-row insert")]
    public async Task Azrng_PerRowInsert()
    {
        for (var i = 0; i < Rows; i++)
        {
            await InsertOneAsync(_azrngConnection, _azrngTable, i);
        }
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_setupConnection is not null)
        {
            await ExecuteNonQueryAsync(_localConnection, $"DROP TABLE IF EXISTS {_localTable}");
            await ExecuteNonQueryAsync(_azrngConnection, $"DROP TABLE IF EXISTS {_azrngTable}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_localConnection is not null)
            await _localConnection.DisposeAsync();
        if (_azrngConnection is not null)
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

[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 3)]
public class InsertBatchBench : IAsyncDisposable
{
    private AzrngQuackConnection _azrngConnection = null!;
    private string _azrngTable = "";
    private object?[][] _rows = [];

    [Params(100, 1000)]
    public int Rows { get; set; }

    [Params(100, 500)]
    public int BatchSize { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _azrngTable = "bench_azrng_batch_" + Guid.NewGuid().ToString("N")[..12];

        _azrngConnection = new AzrngQuackConnection(Program.ConnectionString);
        await _azrngConnection.OpenAsync();
        await ExecuteNonQueryAsync(_azrngConnection, $"CREATE TABLE {_azrngTable} (id INTEGER, label VARCHAR)");
        _rows = Enumerable.Range(0, Rows)
            .Select(i => new object?[] { i, $"row{i}" })
            .ToArray();
    }

    [IterationSetup]
    public async Task IterationSetup()
    {
        await ExecuteNonQueryAsync(_azrngConnection, $"DELETE FROM {_azrngTable}");
    }

    [Benchmark(Description = "Azrng batch insert all rows")]
    public async Task Azrng_BatchInsert()
    {
        await _azrngConnection.ExecuteBatchInsertAsync(_azrngTable, new[] { "id", "label" }, _rows);
    }

    [Benchmark(Description = "Azrng paged batch insert")]
    public async Task Azrng_PagedBatchInsert()
    {
        await _azrngConnection.ExecuteParameterizedBatchInsertAsync(
            _azrngTable,
            new[] { "id", "label" },
            _rows,
            BatchSize);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_azrngConnection is not null && !string.IsNullOrWhiteSpace(_azrngTable))
            await ExecuteNonQueryAsync(_azrngConnection, $"DROP TABLE IF EXISTS {_azrngTable}");
    }

    public async ValueTask DisposeAsync()
    {
        if (_azrngConnection is not null)
            await _azrngConnection.DisposeAsync();
    }

    private static async Task ExecuteNonQueryAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
