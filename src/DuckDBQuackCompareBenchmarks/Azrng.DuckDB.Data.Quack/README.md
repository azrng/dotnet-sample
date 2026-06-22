# Quack.DuckDB

Quack.DuckDB 是一个基于 DuckDB.NET 和 Quack 扩展的 .NET 类库，用于访问远程 Quack/DuckDB 服务。

## 性能优化 (v2.0)

基于基准测试，**ATTACH 模式比 quack_query 模式快 36-105 倍**，因此从 v2.0 开始默认启用 ATTACH 模式。

| 查询类型 | ATTACH 模式 | quack_query 模式 | 性能提升 |
|---------|------------|-----------------|---------|
| SELECT 1 | ~55 us | ~5.8 ms | **105x** |
| 参数化查询 | ~123 us | ~6.1 ms | **50x** |
| 聚合查询 (10k行) | ~164 us | ~5.9 ms | **36x** |

### 主要优化点

1. **默认 ATTACH 模式**: 使用 DuckDB 原生协议下推查询，避免 HTTP 开销
2. **原生参数绑定**: ATTACH 模式下使用 `?` 占位符，无需序列化成字面量
3. **扩展加载优化**: 先尝试 LOAD，失败才执行 FORCE INSTALL

## 安装

```bash
dotnet add package Quack.DuckDB
```

## 连接字符串

推荐使用 key/value 格式：

```text
Host=172.16.68.108;Port=9494;Token=your-token
```

指定远端 catalog：

```text
Host=172.16.68.108;Port=9494;Token=your-token;Catalog=duckflight
```

使用旧的 quack_query 模式（不推荐，性能较低）：

```text
Host=172.16.68.108;Port=9494;Token=your-token;Attach=false
```

也支持 URI 格式：

```text
quack://172.16.68.108:9494?token=your-token&tls=false
```

不支持 `jdbc:quack://...` 写法。

## 连接参数

| 参数 | 必填 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `Host` | 是 | | Quack 服务地址或 IP。 |
| `Port` | 是 | | Quack 服务端口。 |
| `Token` | 是 | | 认证 token。也可以使用 `Password` 作为别名。 |
| `DisableSsl` | 否 | `true` | 是否禁用 SSL。 |
| `Catalog` | 否 | `remote` | 远端服务端 catalog 名称；`Attach=true` 时也作为本地挂载名。 |
| `Attach` | 否 | `true` | 是否启用 ATTACH 模式（推荐）。设为 `false` 使用旧的 quack_query 模式。 |

`Catalog=duckflight` 不是连接地址，也不会替代 `Host`、`Port`、`Token`。它表示远端 DuckDB 的 catalog 名称，业务 SQL 可以写成 `duckflight.source.orders`。如果配置了 `Attach=true`，同一个值会用于 `ATTACH ... AS duckflight`。

## 快速使用

```csharp
using Quack.DuckDB;

var connString = "Host=172.16.68.108;Port=9494;Token=your-token;Catalog=duckflight";

using var quack = new QuackDataProvider(
    QuackConnectionConfig.FromConnectionString(connString));

var rows = quack.ExecuteQuery(
    "select count(1) as total from duckflight.source.orders");
```

## ADO.NET 用法

```csharp
using Quack.DuckDB;

await using var connection = new QuackDuckDbConnection(
    "Host=172.16.68.108;Port=9494;Token=your-token;Catalog=duckflight");

await connection.OpenAsync();

await using var command = connection.CreateCommand();
command.CommandText = "select * from duckflight.source.patient where patientname = @patientname";

var parameter = command.CreateParameter();
parameter.ParameterName = "patientname";
parameter.Value = "张三";
command.Parameters.Add(parameter);

await using var reader = await command.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    // 读取字段
}
```

ATTACH 模式下，参数使用原生绑定（`?` 占位符），性能最优。

## Dapper 用法

```csharp
using Dapper;
using Quack.DuckDB;

using var connection = new QuackDuckDbConnection(
    "Host=172.16.68.108;Port=9494;Token=your-token;Catalog=duckflight");

connection.Open();

var patients = connection.Query<PatientDto>(
    "select * from duckflight.source.patient where patientname = @patientname",
    new { patientname = "张三" });
```

## 模式对比

### ATTACH 模式（默认）

```sql
-- 初始化（自动执行）
ATTACH 'quack://172.16.68.108:9494' AS duckflight (TYPE quack, TOKEN '...', DISABLE_SSL true);

-- 查询直接下推到服务器
SELECT * FROM duckflight.source.orders WHERE id = ?;
```

**优势**:
- 使用 DuckDB 原生二进制协议
- 查询自动下推到服务器执行
- 原生参数绑定，无序列化开销
- 性能最优（快 36-105 倍）

### quack_query 模式（旧版兼容）

```sql
-- 每次查询都包装成函数调用
SELECT * FROM quack_query('quack://172.16.68.108:9494', 'SELECT * FROM orders WHERE id = 1', token := '...', disable_ssl := true);
```

**适用场景**:
- 需要兼容旧代码
- 特殊的 SQL 处理需求

## 扩展文件

包内包含本地 Quack/httpfs 扩展文件，默认路径如下：

```text
extensions/v1.5.3/{platform}/
```

类库会自动从包输出目录或源码调试目录中查找并加载 `httpfs.duckdb_extension` 和 `quack.duckdb_extension`，连接字符串不需要配置扩展路径或扩展版本。


### 为什么扩展要单独打包（引擎 ≠ 扩展）

本类库依赖的 `DuckDB.NET.Data.Full`（同族的 `DuckDB.NET.Bindings.Full` 同样如此）已自带各平台的 **DuckDB 引擎** native 二进制（`libduckdb.dll/.so/.dylib`），但 `extensions/` 里的 `*.duckdb_extension` 是**另一层东西，不能省略**：

| | 是什么 | 由谁提供 |
| --- | --- | --- |
| DuckDB 引擎（`libduckdb`） | 数据库引擎本体（C++ 运行时） | `DuckDB.NET.Data.Full` NuGet 自带 |
| `quack.duckdb_extension` / `httpfs.duckdb_extension` | 引擎运行时 `LOAD` 进去的插件 | 本包 `extensions/` 目录 |

引擎只提供运行时；`quack`、`httpfs` 是**插件**，必须单独提供才能被 `LOAD`。需要自带的原因：

1. **quack 是第三方、未签名扩展，不在 DuckDB 官方扩展仓库（extensions.duckdb.org）**，无法通过 `INSTALL quack` 从官方拉取，只能自备文件并以 `allow_unsigned_extensions` 加载。
2. **离线与版本锁定**：httpfs 虽为官方扩展，自带可避免启动时联网下载，并锁定到与引擎匹配的版本。
3. **扩展按"引擎版本 + 平台"绑定**：目录 `v1.5.3/{platform}/` 中，`v1.5.3` 必须与 `DuckDB.NET.Data.Full` 的引擎版本精确对齐，`{platform}` 必须与运行平台一致，否则 `LOAD` 会失败。

简言之：`DuckDB.NET.Data.Full` 提供"引擎"，`extensions/` 提供"插件"，两者配套、缺一不可。


### 扩展加载策略

1. 优先尝试 `LOAD`（扩展已存在时速度快）
2. 如果失败，执行 `FORCE INSTALL` + `LOAD`（首次使用或扩展缺失时）

## 更新日志

### v2.0 (2026-06-21)

- **Breaking**: 默认模式从 `quack_query` 改为 `ATTACH`（性能提升 36-105 倍）
- ATTACH 模式下使用原生参数绑定
- 优化扩展加载策略
- 新增 `UseAttachMode()` 方法
