using System.Data;
using Dapper;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// 覆盖 DuckDB 全部标量类型的端到端测试,按 bridge 实际读写能力分层:
/// <list type="bullet">
/// <item><see cref="AllReadableTypes_RoundtripThroughFullLifecycle"/>:bridge 能读回的类型,走完整生命周期并对每列做严格值断言。</item>
/// <item><see cref="WriteOnlyTypes_DdlDmlPersists"/>:bridge 暂不支持读回的类型,仅断言 DDL/DML 不抛且行已持久化,不做列值断言(否则会假绿)。</item>
/// </list>
/// 分层依据:<see cref="Internal.QuackProtocolBridge"/> 的 <c>ReadVectorData</c> 只解码 12 个基类型,
/// 其余类型走 <c>SkipUnknownVectorData</c> 静默返回空数组。对读不回的类型做值断言要么失败、要么因空数据假性通过。
/// </summary>
[Trait("Category", "Integration")]
[Collection(IntegrationTestCollection.Name)]
public sealed class AllTypesTest
{
    private readonly TestOptions _options;

    public AllTypesTest(TestOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// 用 bridge 支持读回的全部类型建表,跑完 建库→建 schema→建表→字面量插入→参数化插入→查询断言→NULL 行→删表→删 schema→DETACH 全流程,
    /// 逐列断言写入值能原样读回。
    /// </summary>
    [Fact]
    public async Task AllReadableTypes_RoundtripThroughFullLifecycle()
    {
        // 每次运行唯一 catalog:连接时 bridge 自动 ATTACH(见 QuackProtocolBridge.Connect)
        var catalog = "all_types_" + Guid.NewGuid().ToString("N")[..4];
        var config = QuackProtocolConfig.FromConnectionString(_options.ConnectionString) with { Catalog = catalog };

        await using var connection = new QuackConnection(config);
        await connection.OpenAsync();

        // 1. 建库:catalog 由 OpenAsync 自动 ATTACH,确认服务端已存在
        var allDbs = (await connection.QueryAsync<string>(
            "SELECT database_name FROM duckdb_databases()")).ToList();
        Assert.True(allDbs.Contains(catalog), $"Catalog {catalog} not found; all DBs: {string.Join(",", allDbs)}");

        // 2. 建 schema(限定到当前 catalog 计数,避免命中历史 catalog 残留的同名 schema)
        await connection.ExecuteAsync("CREATE SCHEMA IF NOT EXISTS types");
        Assert.Equal(1L, await CountAsync(connection,
            $"SELECT count(*) FROM information_schema.schemata WHERE schema_name = 'types' AND catalog_name = '{catalog}'"));

        // 3. 建表:覆盖 bridge 能读回的全部类型
        await connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS types.readable_types (
                c_boolean    BOOLEAN,
                c_int        INTEGER,
                c_bigint     BIGINT,
                c_hugeint    HUGEINT,
                c_float      FLOAT,
                c_double     DOUBLE,
                c_decimal    DECIMAL(12,2),
                c_varchar    VARCHAR,
                c_blob       BLOB,
                c_uuid       UUID,
                c_date       DATE,
                c_timestamp  TIMESTAMP,
                c_timestamptz TIMESTAMP WITH TIME ZONE
            )
            """);

        // 4. 插入-字面量(一行已知值)
        await connection.ExecuteAsync(
            """
            INSERT INTO types.readable_types VALUES (
                TRUE, 42, 9000000000, 9223372036854775807,
                3.14, 2.718281828459045, 1234.56, 'Alice',
                '\x00\x01\xFF\xAB\xCD', 'f0a1b2c3-d4e5-6789-abcd-ef0123456789',
                DATE '2026-06-21', TIMESTAMP '2026-06-21 13:45:30.123456',
                TIMESTAMPTZ '2026-01-15 08:30:00+08'
            )
            """);
        Assert.Equal(1L, await CountAsync(connection, "SELECT count(*) FROM types.readable_types"));

        // 5. 插入-参数化(逐行绑定 @param)
        var expectedUuid = Guid.Parse("12345678-1234-5678-9abc-def012345678");
        var expectedBlob = new byte[] { 0xFE, 0xDC, 0xBA, 0x98 };
        var expectedDto = new DateTimeOffset(2026, 1, 15, 8, 30, 0, TimeSpan.FromHours(8));

        await using (var insertCmd = connection.CreateCommand())
        {
            insertCmd.CommandText =
                """
                INSERT INTO types.readable_types VALUES (
                    @boolean, @int, @bigint, @hugeint,
                    @float, @double, @decimal, @varchar,
                    @blob, @uuid, @date, @timestamp, @timestamptz
                )
                """;

            insertCmd.AddParam("@boolean", false);
            insertCmd.AddParam("@int", -7);
            insertCmd.AddParam("@bigint", -123456789012L);
            insertCmd.AddParam("@hugeint", 42L);
            insertCmd.AddParam("@float", 1.5f);
            insertCmd.AddParam("@double", -0.5);
            insertCmd.AddParam("@decimal", 99.99m);
            insertCmd.AddParam("@varchar", "Bob");
            insertCmd.AddParam("@blob", expectedBlob);
            insertCmd.AddParam("@uuid", expectedUuid);
            insertCmd.AddParam("@date", new DateOnly(2026, 6, 22));
            insertCmd.AddParam("@timestamp", new DateTime(2026, 6, 22, 9, 0, 0, 0, DateTimeKind.Unspecified));
            insertCmd.AddParam("@timestamptz", expectedDto);
            await insertCmd.ExecuteNonQueryAsync();
        }
        Assert.Equal(2L, await CountAsync(connection, "SELECT count(*) FROM types.readable_types"));

        // 6. 查询断言:逐列 GetValue,按 bridge 实际返回类型 switch 取值(按 c_varchar 排序:Alice < Bob)
        await using (var queryCmd = connection.CreateCommand())
        {
            queryCmd.CommandText = """
                SELECT c_boolean, c_int, c_bigint, c_hugeint,
                       c_float, c_double, c_decimal, c_varchar, c_blob, c_uuid,
                       c_date, c_timestamp, c_timestamptz
                FROM types.readable_types ORDER BY c_varchar
                """;
            await using var reader = await queryCmd.ExecuteReaderAsync();

            // 第 1 行(字面量,c_int = 42)
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.GetBoolean(0));
            Assert.Equal(42, Convert.ToInt32(reader.GetValue(1)));
            Assert.Equal(9000000000L, Convert.ToInt64(reader.GetValue(2)));
            Assert.Equal(9223372036854775807L, Convert.ToInt64(reader.GetValue(3))); // HUGEINT 落在 long 范围
            Assert.Equal(3.14f, Convert.ToSingle(reader.GetValue(4)), 4);
            Assert.Equal(2.718281828459045, Convert.ToDouble(reader.GetValue(5)), 15);
            Assert.Equal(1234.56m, Convert.ToDecimal(reader.GetValue(6)));
            Assert.Equal("Alice", reader.GetString(7));
            Assert.Equal(new byte[] { 0x00, 0x01, 0xFF, 0xAB, 0xCD }, AsBlob(reader.GetValue(8)));
            Assert.Equal(Guid.Parse("f0a1b2c3-d4e5-6789-abcd-ef0123456789"), AsGuid(reader.GetValue(9)));
            Assert.Equal(new DateOnly(2026, 6, 21), AsDateOnly(reader.GetValue(10)));
            Assert.Equal(new DateTime(2026, 6, 21, 13, 45, 30, 123),
                TruncateToMillisecond(Convert.ToDateTime(reader.GetValue(11))));
            // TIMESTAMPTZ 渲染时归一化为 UTC,按毫秒断言同一 instant
            Assert.Equal(expectedDto.UtcDateTime,
                DateTime.SpecifyKind(TruncateToMillisecond(Convert.ToDateTime(reader.GetValue(12))), DateTimeKind.Utc));

            // 第 2 行(参数化,c_int = -7)
            Assert.True(await reader.ReadAsync());
            Assert.False(reader.GetBoolean(0));
            Assert.Equal(-7, Convert.ToInt32(reader.GetValue(1)));
            Assert.Equal(-123456789012L, Convert.ToInt64(reader.GetValue(2)));
            Assert.Equal(42L, Convert.ToInt64(reader.GetValue(3)));
            Assert.Equal(1.5f, Convert.ToSingle(reader.GetValue(4)), 4);
            Assert.Equal(-0.5, Convert.ToDouble(reader.GetValue(5)), 15);
            Assert.Equal(99.99m, Convert.ToDecimal(reader.GetValue(6)));
            Assert.Equal("Bob", reader.GetString(7));
            Assert.Equal(expectedBlob, AsBlob(reader.GetValue(8)));
            Assert.Equal(expectedUuid, AsGuid(reader.GetValue(9)));
            Assert.Equal(new DateOnly(2026, 6, 22), AsDateOnly(reader.GetValue(10)));
            Assert.Equal(new DateTime(2026, 6, 22, 9, 0, 0, 0),
                TruncateToMillisecond(Convert.ToDateTime(reader.GetValue(11))));
            Assert.Equal(expectedDto.UtcDateTime,
                DateTime.SpecifyKind(TruncateToMillisecond(Convert.ToDateTime(reader.GetValue(12))), DateTimeKind.Utc));

            Assert.False(await reader.ReadAsync(), "Expected exactly 2 rows before NULL insert");
        }

        // 7. NULL 行:再插一行全 NULL,断言所有列 IsDBNull。
        //    其中 c_date / c_timestamp 整列全 NULL 的场景回归验证 bridge 修复:
        //    此前 ReadDateVector / ReadTimestampVector 解码 NULL 单元格的 0x80...0 哨兵时会溢出崩溃。
        await connection.ExecuteAsync(
            """
            INSERT INTO types.readable_types (c_boolean) VALUES (NULL)
            """);
        Assert.Equal(3L, await CountAsync(connection, "SELECT count(*) FROM types.readable_types"));

        await using (var nullCmd = connection.CreateCommand())
        {
            nullCmd.CommandText =
                """
                SELECT c_int, c_varchar, c_uuid, c_date, c_timestamp, c_timestamptz, c_blob
                FROM types.readable_types WHERE c_int IS NULL
                """;
            await using var nullReader = await nullCmd.ExecuteReaderAsync();
            Assert.True(await nullReader.ReadAsync());
            Assert.True(nullReader.IsDBNull(0)); // c_int
            Assert.True(nullReader.IsDBNull(1)); // c_varchar
            Assert.True(nullReader.IsDBNull(2)); // c_uuid
            Assert.True(nullReader.IsDBNull(3)); // c_date   — 回归:全 NULL DATE 列不再崩溃
            Assert.True(nullReader.IsDBNull(4)); // c_timestamp — 回归:全 NULL TIMESTAMP 列不再崩溃
            Assert.True(nullReader.IsDBNull(5)); // c_timestamptz
            Assert.True(nullReader.IsDBNull(6)); // c_blob
            Assert.False(await nullReader.ReadAsync());
        }

        // 8. 删表
        await connection.ExecuteAsync("DROP TABLE IF EXISTS types.readable_types");
        Assert.Equal(0L, await CountAsync(connection,
            $"SELECT count(*) FROM information_schema.tables WHERE table_schema = 'types' AND table_name = 'readable_types' AND table_catalog = '{catalog}'"));

        // 9. 删 schema
        await connection.ExecuteAsync("DROP SCHEMA IF EXISTS types");
        Assert.Equal(0L, await CountAsync(connection,
            $"SELECT count(*) FROM information_schema.schemata WHERE schema_name = 'types' AND catalog_name = '{catalog}'"));

        // 10. DETACH:用不带 catalog 的连接卸载,避免删除当前默认库(同 BaseOperationTest)
        var baseConfig = QuackProtocolConfig.FromConnectionString(_options.ConnectionString);
        await using var detachConnection = new QuackConnection(baseConfig);
        await detachConnection.OpenAsync();
        await detachConnection.ExecuteAsync($"DETACH \"{catalog}\"");
    }

    /// <summary>
    /// 用 bridge 暂不支持读回、但 DDL/DML 应正常工作的类型建表。
    /// 只断言建表/插入不抛、行已持久化(<c>SELECT count(*)</c> 返回 BIGINT,与列类型无关,可解码),
    /// <b>不做列值断言</b>:这些类型在 bridge 中走 SkipUnknownVectorData 静默返回空数组,读了也是假断言。
    /// 嵌套类型(ARRAY/LIST/MAP/STRUCT/UNION/VARIANT)、ENUM(需 CREATE TYPE)、JSON(需扩展)本测试不纳入,
    /// 待 bridge 支持这些类型的读回后再扩展,避免依赖扩展加载导致 flaky。
    /// </summary>
    [Fact]
    public async Task WriteOnlyTypes_DdlDmlPersists()
    {
        var catalog = "all_types_" + Guid.NewGuid().ToString("N")[..4];
        var config = QuackProtocolConfig.FromConnectionString(_options.ConnectionString) with { Catalog = catalog };

        await using var connection = new QuackConnection(config);
        await connection.OpenAsync();

        await connection.ExecuteAsync("CREATE SCHEMA IF NOT EXISTS types");

        // 建表:均为 scalar 标量类型,无需扩展、无需 CREATE TYPE
        await connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS types.writeonly_types (
                c_tinyint   TINYINT,
                c_smallint  SMALLINT,
                c_utinyint  UTINYINT,
                c_usmallint USMALLINT,
                c_uinteger  UINTEGER,
                c_ubigint   UBIGINT,
                c_time      TIME,
                c_interval  INTERVAL,
                c_bit       BIT
            )
            """);

        // 字面量插入两行(含各类型合法字面量),断言不抛
        await connection.ExecuteAsync(
            """
            INSERT INTO types.writeonly_types VALUES (
                1, 100, 200, 40000, 4000000000, 18000000000000000000,
                TIME '13:45:00', INTERVAL '1' DAY, BIT '1010'
            )
            """);
        await connection.ExecuteAsync(
            """
            INSERT INTO types.writeonly_types VALUES (
                -1, -100, 0, 0, 0, 0,
                TIME '00:00:00', INTERVAL '30' MINUTE, BIT '0000'
            )
            """);

        // 行已持久化(count 返回 BIGINT,与列类型无关)
        Assert.Equal(2L, await CountAsync(connection, "SELECT count(*) FROM types.writeonly_types"));

        // 清理(限定到当前 catalog 计数,避免命中历史 catalog 残留的同名表)
        await connection.ExecuteAsync("DROP TABLE IF EXISTS types.writeonly_types");
        Assert.Equal(0L, await CountAsync(connection,
            $"SELECT count(*) FROM information_schema.tables WHERE table_schema = 'types' AND table_name = 'writeonly_types' AND table_catalog = '{catalog}'"));
        await connection.ExecuteAsync("DROP SCHEMA IF EXISTS types");

        // DETACH
        var baseConfig = QuackProtocolConfig.FromConnectionString(_options.ConnectionString);
        await using var detachConnection = new QuackConnection(baseConfig);
        await detachConnection.OpenAsync();
        await detachConnection.ExecuteAsync($"DETACH \"{catalog}\"");
    }

    /// <summary>
    /// DuckDB 时间戳精度变体 TIMESTAMP_S / TIMESTAMP_MS / TIMESTAMP_NS。
    /// 物理布局与 TIMESTAMP 一致(8 字节 LE 整数),仅单位不同(秒/毫秒/纳秒)。
    /// 此前 bridge 只解码 TIMESTAMP / TIMESTAMP_TZ,其余变体走 SkipUnknownVectorData 静默返回空,
    /// 本测试回归验证三种变体的值能正确读回,且 NULL 单元格不崩溃。
    /// </summary>
    [Fact]
    public async Task TimestampPrecisionVariants_Roundtrip()
    {
        var catalog = "all_types_" + Guid.NewGuid().ToString("N")[..4];
        var config = QuackProtocolConfig.FromConnectionString(_options.ConnectionString) with { Catalog = catalog };

        await using var connection = new QuackConnection(config);
        await connection.OpenAsync();

        await connection.ExecuteAsync("CREATE SCHEMA IF NOT EXISTS types");
        await connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS types.ts_variants (
                c_ts_s  TIMESTAMP_S,
                c_ts_ms TIMESTAMP_MS,
                c_ts_ns TIMESTAMP_NS
            )
            """);

        // 选一个三种精度都能无歧义表示的时刻:2026-06-21 13:45:30(秒级精度,无小数尾)。
        // 用字面量插入,绕开参数渲染器(它只输出默认 TIMESTAMP 字面量,带不上精度后缀)。
        await connection.ExecuteAsync(
            """
            INSERT INTO types.ts_variants VALUES (
                TIMESTAMP_S '2026-06-21 13:45:30',
                TIMESTAMP_MS '2026-06-21 13:45:30.123',
                TIMESTAMP_NS '2026-06-21 13:45:30.123456789'
            )
            """);
        Assert.Equal(1L, await CountAsync(connection, "SELECT count(*) FROM types.ts_variants"));

        // 读回断言:三种精度各自截断到自身单位,按毫秒比较(_S/_MS 精确到秒/毫秒;_NS 桥接按纳秒/100=ticks,亚微秒部分丢弃)
        await using (var queryCmd = connection.CreateCommand())
        {
            queryCmd.CommandText = "SELECT c_ts_s, c_ts_ms, c_ts_ns FROM types.ts_variants";
            await using var reader = await queryCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());

            // TIMESTAMP_S:整数 = 秒,精度到秒
            Assert.Equal(new DateTime(2026, 6, 21, 13, 45, 30, DateTimeKind.Utc),
                TruncateToMillisecond(Convert.ToDateTime(reader.GetValue(0))));

            // TIMESTAMP_MS:整数 = 毫秒,精度到毫秒
            Assert.Equal(new DateTime(2026, 6, 21, 13, 45, 30, 123, DateTimeKind.Utc),
                TruncateToMillisecond(Convert.ToDateTime(reader.GetValue(1))));

            // TIMESTAMP_NS:桥接按 纳秒/100 = ticks 解码。.NET DateTime 精度上限 100ns(1 tick),
            // 亚 100ns 必然丢失(DateTime/DateTimeOffset 同限,非桥接 bug),故只做毫秒级断言。
            Assert.Equal(new DateTime(2026, 6, 21, 13, 45, 30, 123, DateTimeKind.Utc),
                TruncateToMillisecond(Convert.ToDateTime(reader.GetValue(2))));

            Assert.False(await reader.ReadAsync());
        }

        // NULL 行回归:三种精度变体的全 NULL 单元格不崩溃(此前 wire 上是 long.MinValue 哨兵,
        // 直接换算会溢出)。这里 Select 出 1 行全 NULL,断言 IsDBNull。
        await connection.ExecuteAsync("INSERT INTO types.ts_variants (c_ts_s) VALUES (NULL)");
        Assert.Equal(2L, await CountAsync(connection, "SELECT count(*) FROM types.ts_variants"));

        await using (var nullCmd = connection.CreateCommand())
        {
            nullCmd.CommandText = "SELECT c_ts_s, c_ts_ms, c_ts_ns FROM types.ts_variants WHERE c_ts_s IS NULL";
            await using var nullReader = await nullCmd.ExecuteReaderAsync();
            Assert.True(await nullReader.ReadAsync());
            Assert.True(nullReader.IsDBNull(0));
            Assert.True(nullReader.IsDBNull(1));
            Assert.True(nullReader.IsDBNull(2));
            Assert.False(await nullReader.ReadAsync());
        }

        // 清理
        await connection.ExecuteAsync("DROP TABLE IF EXISTS types.ts_variants");
        await connection.ExecuteAsync("DROP SCHEMA IF EXISTS types");
        var baseConfig = QuackProtocolConfig.FromConnectionString(_options.ConnectionString);
        await using var detachConnection = new QuackConnection(baseConfig);
        await detachConnection.OpenAsync();
        await detachConnection.ExecuteAsync($"DETACH \"{catalog}\"");
    }

    private static async Task<long> CountAsync(QuackConnection connection, string sql)
    {
        return (await connection.QueryAsync<long>(sql)).First();
    }

    private static Guid AsGuid(object? value)
    {
        return value switch
        {
            Guid g => g,
            string s => Guid.Parse(s),
            _ => throw new Xunit.Sdk.XunitException($"Unexpected Guid representation: {value?.GetType().Name}")
        };
    }

    private static DateOnly AsDateOnly(object? value)
    {
        return value switch
        {
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => throw new Xunit.Sdk.XunitException($"Unexpected DateOnly representation: {value?.GetType().Name}")
        };
    }

    private static byte[] AsBlob(object? value)
    {
        return value switch
        {
            byte[] b => b,
            string s when s.StartsWith("\\x") => Convert.FromHexString(s[2..]),
            _ => throw new Xunit.Sdk.XunitException($"Unexpected BLOB representation: {value?.GetType().Name}")
        };
    }

    private static DateTime TruncateToMillisecond(DateTime value)
    {
        return new DateTime(value.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, value.Kind);
    }
}
