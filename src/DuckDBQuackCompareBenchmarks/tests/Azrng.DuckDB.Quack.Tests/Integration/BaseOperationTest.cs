using Azrng.DuckDB.Quack;
using System.Data;
using Dapper;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// 端到端基础操作全流程:建库 → 建 schema → 建表 → 插入(普通 + 参数化)→ 查询 → 更新 → 删除 → 删表 → 删 schema → 删库。
/// 每次运行使用唯一 catalog 名隔离,保证可重入、不与其他集成测试相互干扰。
/// </summary>
[Trait("Category", "Integration")]
[Collection(IntegrationTestCollection.Name)]
public sealed class BaseOperationTest
{
    private readonly TestOptions _options;

    public BaseOperationTest(TestOptions options)
    {
        _options = options;
    }

    [Fact]
    public async Task FullLifecycle_Test()
    {
        // 每次运行唯一 catalog:连接时 bridge 自动 ATTACH 创建同名数据库文件(见 QuackProtocolBridge.Connect)
        var catalog = "base_op_" + Guid.NewGuid().ToString("N")[..4];
        var config = QuackProtocolConfig.FromConnectionString(_options.ConnectionString) with { Catalog = catalog };

        await using var connection = new QuackConnection(config);
        await connection.OpenAsync();

        // 1. 建库:catalog 由 OpenAsync 自动 ATTACH,确认服务端已存在
        Assert.Equal(1L, await CountAsync(connection,
            $"SELECT count(*) FROM duckdb_databases() WHERE database_name = '{catalog}'"));

        // 2. 建 schema
        await connection.ExecuteAsync("CREATE SCHEMA IF NOT EXISTS demo");
        Assert.Equal(1L, await CountAsync(connection,
            "SELECT count(*) FROM information_schema.schemata WHERE schema_name = 'demo'"));

        // 3. 建表
        await connection.ExecuteAsync(
            "CREATE TABLE IF NOT EXISTS demo.record (id INTEGER, name VARCHAR, amount DECIMAL(10,2), created_at TIMESTAMP)");

        // 4. 插入-普通(字面量 SQL)
        await connection.ExecuteAsync(
            "INSERT INTO demo.record VALUES (1, 'Alice', 1.12, TIMESTAMP '2026-01-01 10:00:00')");
        Assert.Equal(1L, await CountAsync(connection, "SELECT count(*) FROM demo.record"));

        // 5. 插入-参数化(逐行绑定 @param,复用同一 command)
        await using (var insertCmd = connection.CreateCommand())
        {
            insertCmd.CommandText = "INSERT INTO demo.record VALUES (@id, @name, @amount, @created)";

            insertCmd.AddParam("@id", 2);
            insertCmd.AddParam("@name", "Bob");
            insertCmd.AddParam("@amount", 2.50m);
            insertCmd.AddParam("@created", new DateTime(2026, 1, 2, 11, 0, 0));
            await insertCmd.ExecuteNonQueryAsync();

            insertCmd.Parameters.Clear();
            insertCmd.AddParam("@id", 3);
            insertCmd.AddParam("@name", "Charlie");
            insertCmd.AddParam("@amount", 6.12m);
            insertCmd.AddParam("@created", new DateTime(2026, 1, 3, 12, 0, 0));
            await insertCmd.ExecuteNonQueryAsync();
        }
        Assert.Equal(3L, await CountAsync(connection, "SELECT count(*) FROM demo.record"));

        // 6. 查询:ExecuteReader 读行 + Dapper 类型化映射
        var names = new List<string>();
        await using (var queryCmd = connection.CreateCommand())
        {
            queryCmd.CommandText = "SELECT name FROM demo.record ORDER BY id";
            await using var reader = await queryCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(0));
            }
        }
        Assert.Equal(new[] { "Alice", "Bob", "Charlie" }, names);

        var records = (await connection.QueryAsync<Record>("SELECT name, amount FROM demo.record ORDER BY id")).ToList();
        Assert.Equal(3, records.Count);
        Assert.Equal("Bob", records[1].Name);

        // 7. 更新
        await connection.ExecuteAsync("UPDATE demo.record SET amount = 9.99 WHERE id = 1");
        var updatedAmount = (await connection.QueryAsync<decimal>("SELECT amount FROM demo.record WHERE id = 1")).First();
        Assert.Equal(9.99m, updatedAmount);

        // 8. 删除
        await connection.ExecuteAsync("DELETE FROM demo.record WHERE id = 2");
        Assert.Equal(2L, await CountAsync(connection, "SELECT count(*) FROM demo.record"));
        Assert.Equal(0L, await CountAsync(connection, "SELECT count(*) FROM demo.record WHERE id = 2"));

        // 9. 删表
        await connection.ExecuteAsync("DROP TABLE IF EXISTS demo.record");
        Assert.Equal(0L, await CountAsync(connection,
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'demo' AND table_name = 'record'"));

        // 10. 删 schema
        await connection.ExecuteAsync("DROP SCHEMA IF EXISTS demo");
        Assert.Equal(0L, await CountAsync(connection,
            "SELECT count(*) FROM information_schema.schemata WHERE schema_name = 'demo'"));

        // 11. 删库:DuckDB 无 DROP DATABASE,用 DETACH 从会话卸载 catalog。
        //     使用不指定 Catalog 的连接执行 DETACH，避免删除当前默认数据库的错误。
        //     DETACH 不删除服务端持久化文件,跨运行通过唯一 catalog 名规避冲突。
        var baseConfig = QuackProtocolConfig.FromConnectionString(_options.ConnectionString);
        await using var detachConnection = new QuackConnection(baseConfig);
        await detachConnection.OpenAsync();
        await detachConnection.ExecuteAsync($"DETACH \"{catalog}\"");
    }

    private static async Task<long> CountAsync(QuackConnection connection, string sql)
    {
        return (await connection.QueryAsync<long>(sql)).First();
    }

}

internal sealed class Record
{
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
}
