# Azrng.DuckDB.Quack

纯 C# 实现的 DuckDB Quack 协议 ADO.NET 提供程序，无需 native DLL 依赖。

> 当前版本：**1.0.0-beta2**。本版本重点修复大结果集 Fetch 续读、DATE 解码和 benchmark 回归场景。

## 项目背景

本项目基于 [DuckDB Quack 协议](https://github.com/duckdb/duckdb-quack) 迁移实现，将原本依赖 DuckDB native engine + Quack extension 的方式，转换为纯 C# 实现。

- **DuckDB 版本**: `1.5.3`
- **Quack 版本**: `v1.5-variegata`
- **协议文档**: https://duckdb.org/docs/current/quack/overview
- **博客文章**: https://duckdb.org/2026/05/12/quack-remote-protocol

## 功能特性

- 完整的 ADO.NET 接口实现：`QuackConnection`、`QuackCommand`、`QuackDataReader`
- 支持参数化查询（`@name`、`:param` 格式）
- 兼容 Dapper ORM
- 异步 API 支持
- 连接池复用
- SSL/TLS 配置
- 结构化日志（`ILogger`）
- 指标收集（查询耗时、连接数、错误率、P99）
- 事务支持（BEGIN/COMMIT/ROLLBACK）
- 批量操作（批量 INSERT）
- Token 加密（AES-GCM）

## 安装

```powershell
dotnet add package Azrng.DuckDB.Quack
```

## 连接参数

| 参数 | 说明 | 必填 | 默认值 |
|------|------|:----:|--------|
| `Host` | 服务器地址 | ✓ | - |
| `Port` | 端口号 | | `9494` |
| `Token` | 认证令牌 | ✓ | - |
| `Catalog` | 默认数据库，每次查询自动切换（见下方说明） | | - |
| `DisableSsl` | 是否禁用 SSL | | `true` |
| `TimeoutSeconds` | 超时时间（秒） | | `30` |

`Catalog` 说明：

指定 `Catalog` 后，**每次查询自动在该数据库上下文中执行**，用户 SQL 无需手动添加数据库前缀：

- **自动切换**：每次查询前自动拼接 `USE "catalog"; `，利用 DuckDB 多语句执行能力，零额外 HTTP 开销
- **自动创建**：连接时如果该 catalog 不存在，自动执行 `ATTACH 'catalog' AS "catalog"` 在服务端创建同名数据库文件
- **透明使用**：用户 SQL 直接写表名即可，如 `SELECT * FROM orders`，无需写 `SELECT * FROM catalog.schema.orders`

```text
# 指定 Catalog — 后续查询自动在 duckflight 数据库下执行
Host=localhost;Port=9494;Token=xxx;Catalog=duckflight;DisableSsl=true

# 不指定 — 用户自己写全路径
Host=localhost;Port=9494;Token=xxx;DisableSsl=true
```

## 快速开始

```csharp
using Azrng.DuckDB.Quack;

// 连接到指定 Catalog
await using var connection = new QuackConnection(
    "Host=localhost;Port=9494;Token=your-token;Catalog=duckflight;DisableSsl=true");
await connection.OpenAsync();

// 查询数据
await using var command = connection.CreateCommand();
command.CommandText = "SELECT * FROM orders LIMIT 10";

await using var reader = await command.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    Console.WriteLine(reader.GetInt64(0));
}
```

## 参数化查询

```csharp
await using var command = connection.CreateCommand();
command.CommandText = "SELECT * FROM users WHERE id = @id AND name = @name";
command.Parameters.AddWithValue("@id", 42);
command.Parameters.AddWithValue("@name", "Alice");
```

或者使用扩展方法：

```csharp
command.AddParam("@id", 42);
command.AddParam("@name", "Alice");
```

## Dapper 集成

```csharp
using Dapper;

await using var connection = new QuackConnection(connectionString);
await connection.OpenAsync();

var orders = await connection.QueryAsync<Order>(
    "SELECT * FROM orders WHERE status = @Status",
    new { Status = "pending" });
```

## 支持的数据类型

| DuckDB 类型 | .NET 类型 |
|-------------|-----------|
| BOOLEAN | `bool` |
| TINYINT, SMALLINT, INTEGER, BIGINT | `long` |
| HUGEINT | `long` / `decimal` |
| FLOAT, DOUBLE | `double` |
| DECIMAL | `decimal` |
| VARCHAR, CHAR, BLOB | `string` |
| DATE | `DateOnly` |
| TIMESTAMP | `DateTimeOffset` |
| UUID | `Guid` |

## 企业级功能

### 连接池

```csharp
using Microsoft.Extensions.Logging;

var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<QuackConnectionPool>();
await using var pool = new QuackConnectionPool(connectionString, logger, maxPoolSize: 5);

await using var lease = await pool.RentConnectionAsync();
await using var command = lease.Connection.CreateCommand();
command.CommandText = "SELECT 1";
var value = await command.ExecuteScalarAsync();
```

### 事务支持

```csharp
await using var transaction = await connection.BeginTransactionAsync();
try
{
    await using var cmd = connection.CreateCommand();
    cmd.Transaction = transaction;
    cmd.CommandText = "INSERT INTO accounts VALUES (1, 1000)";
    await cmd.ExecuteNonQueryAsync();

    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

### 批量操作

```csharp
var rows = new List<object?[]>
{
    new object?[] { 1, "Alice", "alice@example.com" },
    new object?[] { 2, "Bob", "bob@example.com" }
};

var affected = await connection.ExecuteBatchInsertAsync(
    "users",
    new[] { "id", "name", "email" },
    rows);
```

### DDL/DML 操作

```csharp
await connection.ExecuteAsync("CREATE TABLE IF NOT EXISTS users (id INTEGER, name VARCHAR)");
await connection.ExecuteAsync("INSERT INTO users VALUES (1, 'Alice'), (2, 'Bob')");
await connection.ExecuteAsync("UPDATE users SET name = 'Alice Updated' WHERE id = 1");
await connection.ExecuteAsync("DELETE FROM users WHERE id = 2");
```

## 测试

| 类别 | 数量 |
|------|------|
| 单元测试 | 194 |
| 集成测试 | 90 |
| **总计** | **284** |

运行测试：

```powershell
dotnet test tests\Azrng.DuckDB.Quack.Tests\Azrng.DuckDB.Quack.Tests.csproj
```

## 协议信息

- **DuckDB 版本**: `1.5.3`
- **Quack 版本**: `v1.5-variegata`
- **协议文档**: https://duckdb.org/docs/current/quack/overview

## 版本历史

### 1.0.0-beta2

- 修复大结果集 Fetch 续读时 `result_uuid` 非规范 LEB128 重编码导致的 `Result has been closed` 问题。
- `FetchToken` 改为优先原样回放服务端 UUID wire bytes，并兼容旧 `upper:lower` token。
- 修复 `DATE` 解码偏移问题，补充 `DateOnly` 往返验证。
- 补充 100000 行大结果集、无 Catalog benchmark SQL、signed LEB128 等回归测试。
- 保持 `net8.0;net10.0` 多目标框架与纯托管 ADO.NET 客户端能力。

### 1.0.0-beta1

- 初始预览版本，提供基于 DuckDB Quack 协议的纯 C# ADO.NET Provider。
- 支持连接、查询、参数化查询、Dapper 集成、连接池、事务与批量操作等基础能力。

## 许可证

MIT
