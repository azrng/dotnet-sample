# Local ATTACH 与 Azrng.DuckDB.Quack 性能分析

## 摘要

本文解释为什么当前基准测试中 **Local ATTACH 在 warm query 场景下比 Azrng.DuckDB.Quack 快 4-10 倍**，并列出 `Azrng.DuckDB.Quack` 后续追赶甚至超过 `DuckDB.NET.Data.Full` + DuckDB 原生扩展路径的可行优化方向。

最关键的结论是：**Local ATTACH 并不是和 Azrng 同类型的普通 HTTP 客户端路径**。

Local ATTACH 的调用链路是：

```text
.NET
 -> DuckDB.NET.Data.Full
 -> native DuckDB engine
 -> ATTACH quack catalog
 -> DuckDB optimizer / executor
 -> Quack extension native connector
 -> server
```

Azrng 的调用链路是：

```text
.NET
 -> Azrng.DuckDB.Quack ADO.NET provider
 -> C# 参数渲染
 -> C# Quack 二进制协议序列化
 -> HttpClient POST
 -> C# 响应解析
 -> QuackDataReader
```

所以 4-10 倍的差距本质上是：

```text
native DuckDB extension 热路径
vs
纯 C# HTTP Quack 协议客户端热路径
```

不能简单理解为“一个 HTTP 请求比另一个 HTTP 请求快”。

## 当前基准信号

当前基准结果如下：

| 场景 | Local ATTACH | Azrng | 差异 |
|---|---:|---:|---:|
| `SELECT 1` | 54.75 us | 550.09 us | Local ATTACH 约快 10 倍 |
| `SELECT @a + @b` | 122.58 us | 573.20 us | Local ATTACH 约快 4.7 倍 |
| `COUNT/SUM over 10k` | 164.15 us | 661.20 us | Local ATTACH 约快 4 倍 |

这些结果说明差距主要来自每次查询的固定开销：

- `SELECT 1` 几乎没有计算成本，但 Azrng 仍然要支付完整协议栈和 ADO.NET 包装成本。
- `COUNT/SUM over 10k` 最终只返回一行聚合结果，因此结果集物化成本很小，Local ATTACH 的 native 热路径仍然占优。
- 随着查询本身的工作量增加，差距会缩小，这说明 Azrng 的主要问题不是 DuckDB 计算速度，而是每次查询周边的客户端和协议固定开销。

## 为什么 Local ATTACH 更快

### 1. ATTACH 走的是 DuckDB 原生执行器

在 ATTACH 模式下，本地连接直接返回底层 DuckDB 命令：

```csharp
if (_config.Attach)
    return _provider!.Connection.CreateCommand();
```

这意味着查询执行交给 `DuckDB.NET.Data.Full` 和 native DuckDB 处理。远端 catalog ATTACH 完成后，查询规划、表达式执行、参数绑定和结果读取都尽量留在 DuckDB 原生执行路径里。

Azrng 不能使用这条 native engine 路径，因为它的设计目标是纯托管 C#，不依赖 DuckDB native DLL。

### 2. ATTACH 一次性支付初始化成本，热路径很短

Local ATTACH 打开连接慢，是因为它需要创建内存 DuckDB 连接、加载扩展并 ATTACH 远端 catalog：

```text
DuckDBConnection("Data Source=:memory:")
LOAD httpfs
LOAD quack
ATTACH 'quack://host:port' AS remote (...)
```

但当前 QueryBench 测的是 warm connection。连接打开后，ATTACH 已经完成，后续查询路径非常短。

Azrng 打开连接更快，但每次查询仍然要经过托管协议栈。

### 3. Azrng 每次查询都有固定 HTTP / 协议开销

Azrng 每次普通查询大致都会经过：

```text
QuackCommand.ExecuteReaderAsync
 -> QuackParameterSqlRenderer.Render
 -> IQuackProtocolBridge.ExecuteQueryAsync
 -> QuackProtocolBridge.PrepareAndExecuteAsync
 -> QuackWriter 写入请求字段
 -> HttpClient.PostAsync
 -> 读取响应 byte[]
 -> QuackBinaryReader 解析 header/result/chunks
 -> QuackQueryResult
 -> QuackDataReader
```

即使 SQL 只是 `SELECT 1`，这些固定成本也不会消失。这解释了为什么 Azrng 简单查询基本稳定在 550-660 us，而 Local ATTACH 可以在几十微秒内完成 warm query。

### 4. native extension 路径减少了托管对象分配

Azrng 每次查询都需要创建和映射一批托管对象：

- 请求内容；
- 响应 byte array；
- 查询结果对象；
- 列元数据；
- reader 包装对象；
- 行或列式 batch 表示；
- ADO.NET 表层对象。

其中一部分已经通过 buffer pool 和 columnar batch 优化过，但相比 native ATTACH 热路径，托管层工作仍然更多。

### 5. 当前测试场景非常适合 ATTACH

当前 QueryBench 都是小结果集：

- `SELECT 1`：一行一列；
- `SELECT @a + @b`：一行一列；
- `COUNT/SUM over 10k`：一行两个聚合列。

这类场景非常看重单次查询固定延迟，正好是 Local ATTACH 最强的地方。Azrng 的优势更容易体现在部署简单、连接池、并发控制、批量辅助 API、无 native 依赖等场景。

## Azrng 如何追赶或超过 DuckDB.NET.Data.Full

Azrng 不应该只靠局部小优化去追 Local ATTACH。当前差距是架构性的。更现实的路线是降低单次查询固定成本、引入可选加速路径，并在 native ATTACH 不擅长的产品场景中取胜。

### 1. 降低每次查询的固定开销

优先优化 `ExecuteReaderAsync` 到 `PrepareAndExecuteAsync` 的热路径。

建议改进：

- 在同一连接上更积极地复用请求 buffer；
- 对单行/单列结果避免创建完整结果包装栈；
- 为 `ExecuteScalar`、`ExecuteNonQuery` 和一行结果增加 fast path；
- 当调用方只消费 `DbDataReader` 时，避免构造不必要的 `QuackQueryResult.Rows`；
- 以 typed columnar data 作为主要结果表示，行数组仅作为兼容性 fallback。

预期收益：

- 直接降低 `SELECT 1`、标量查询、DML affected-row 读取和轻量元数据查询的延迟；
- 这是压低当前 550 us 基线最直接的方向。

### 2. 增加真正的 scalar fast path

当前 scalar 执行会经过 reader 创建：

```text
ExecuteScalar
 -> ExecuteReader
 -> QuackDataReader
 -> Read
 -> GetValue(0)
```

可以增加专门的协议到值路径：

```text
ExecuteScalarAsync
 -> PrepareAndExecuteAsync
 -> 只解析第一行第一列
 -> 返回值
```

这样可以避免 reader 分配和部分元数据包装，适合这些高频查询：

```sql
SELECT 1
SELECT COUNT(*)
SELECT MAX(id)
INSERT ... RETURNING id
```

### 3. 拆分 prepare 和 execute，并缓存 statement

Azrng 当前每次查询基本是 prepare-and-execute 风格。要接近 ATTACH 热路径，需要减少重复 SQL preparation 成本。

建议行为：

- 如果 Quack 协议和服务端支持，增加 prepared statement handle；
- 按 session 和 SQL shape 缓存 prepared handle；
- 对重复参数化查询复用 handle；
- 使用有界 LRU 淘汰，避免服务端 statement 泄漏。

这类优化尤其适合：

- 重复 OLTP 查询；
- Dapper 中 SQL shape 稳定、参数变化的查询；
- 无法批量化的逐行操作。

### 4. 优化 HTTP 之外的传输层成本

Azrng 已经通过共享 `HttpClient` 复用连接池，但 Local ATTACH 很可能通过 native extension 内部路径避开了部分托管 HTTP 对象开销。

可评估方向：

- 显式使用并调优 `SocketsHttpHandler`；
- 对比 HTTP/1.1 keep-alive 和 HTTP/2 在并发请求下的表现；
- 如果 Quack server/protocol 支持，评估 raw TCP transport；
- 同机部署时评估 Linux Unix domain socket 和 Windows named pipe；
- 在安全可控的前提下，避免 `ReadAsByteArrayAsync` 的整块响应分配，改为 pooled-buffer parsing 或流式解析。

最大收益来自绕开通用 HTTP 对象管线，尤其是小查询。

### 5. 让 typed reader 真正做到零行分配

Azrng 已经支持 `ColumnarBatch`，但所有公开 ADO.NET 读取路径都应尽量避免行物化。

建议改进：

- `GetInt64`、`GetDouble`、`GetString`、`GetDateTime`、`GetGuid` 等 typed getter 直接读取 `ColumnarBatch`；
- 只有 `GetValue` 走装箱路径；
- `GetValues(object[])` 直接从列式数据填充调用方提供的数组；
- `DbDataReader.Read()` 迭代时不构造 `object?[]` 行；
- 新增 benchmark 区分 `Read()` 游标移动成本和实际取值成本。

这对大结果集读取很重要，也可能让 Azrng 在高吞吐读取场景超过通用 DuckDB.NET reader 路径。

### 6. 在不牺牲纯 C# 部署的前提下提供可选 native acceleration

纯 C# 包有明确的部署优势，应该保留。但如果要在微秒级延迟上和 `DuckDB.NET.Data.Full` 竞争，需要可选 native 路径。

可以考虑拆包：

| 包名 | 作用 |
|---|---|
| `Azrng.DuckDB.Quack` | 默认纯 C# provider |
| `Azrng.DuckDB.Quack.Native` | 可选 native acceleration |
| `Azrng.DuckDB.Quack.Full` | 包含 native acceleration 的便利包 |

可加速方向：

- 用 C ABI 包装 Quack 二进制 encode/decode；
- 用 native parser 把响应 chunk 直接解析到 typed column vectors；
- 如果服务端支持非 HTTP Quack 协议，提供 native transport bridge；
- 对稳定 ABI 使用 source-generated P/Invoke。

这个能力应该是可选的，这样云函数、容器、简单部署场景仍然可以继续使用纯托管包。

### 7. 在 ATTACH 结构性弱点上取胜

Local ATTACH 在 warm low-latency query 上很强，但 Azrng 可以在更完整的产品场景中胜出：

- 冷启动更快；
- 无 DuckDB native 依赖；
- 容器、云函数和简单服务部署更容易；
- 显式连接池 API；
- async-first API；
- 批量写入辅助 API；
- retry、metrics、logging、timeout、SSL 配置更可控；
- 避免 DuckDB extension 安装和版本问题；
- 多租户客户端隔离更简单。

后续 benchmark 不应只围绕单次 warm query latency，也应覆盖这些真实业务场景。

建议新增 benchmark 分组：

- `ExecuteScalarBench`：验证 scalar fast path；
- `PreparedStatementBench`：验证 SQL shape 稳定、参数变化的重复查询；
- `ReaderNoBoxingBench`：验证大结果集 typed getter 吞吐；
- `ColdStartBench`：验证 open/setup/first query 总成本；
- `TransportBench`：验证 HTTP handler 和协议版本差异；
- `RealWorkloadBench`：验证连接池复用下的混合读写负载。

## 实施优先级

1. 增加 scalar 和 non-query fast path。
2. 从 reader 热路径移除行物化。
3. 更积极地复用请求和响应 buffer。
4. 在协议支持的前提下增加 prepared statement 缓存。
5. 基准测试调优后的 `SocketsHttpHandler`、HTTP/2 和同机替代传输。
6. 在 managed 热路径 benchmark 明确剩余不可消除开销后，再考虑可选 native acceleration。

## 最终定位

Local ATTACH 当前在 warm、小结果集查询上更快，因为它直接利用了 DuckDB 原生扩展热路径。

Azrng 可以通过三条路线追赶：

- 降低小查询的托管固定开销；
- 避免结果读取中的行对象和装箱；
- 为需要 ATTACH 级微秒延迟的用户提供可选 native acceleration。

当对比范围扩展到冷启动、部署简单性、async 行为、连接池、可观测性、批量操作和多租户运维要求时，Azrng 有机会在整体使用成本上超过 `DuckDB.NET.Data.Full` 方案。
