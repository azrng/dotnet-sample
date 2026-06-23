# DuckDB Quack 基准测试

对比三种 .NET Quack 客户端实现的性能差异，均连接同一 Quack/DuckDB 服务器。

## 三种模式

| 模式 | 连接类 | 连接字符串 | 说明 |
|------|--------|-----------|------|
| **Azrng** | `AzrngQuackConnection` | 普通 | 纯 C# HTTP 客户端，直接调用 Quack 服务器 HTTP API，不依赖 DuckDB 引擎和扩展 |
| **Local Attach** | `LocalQuackConnection` | `+Attach=true` | 本地 DuckDB 引擎 + Quack 扩展，通过 `ATTACH` 挂载远程目录；**读查询改走 `quack_query` 远端执行**（v1.5.3 扩展 attached-table 下推不可靠，见测试结果） |
| **Local Query** | `LocalQuackConnection` | 普通 | 本地 DuckDB 引擎 + Quack 扩展，通过 `quack_query()` 函数包装 SQL 远程执行 |

### 架构差异

```
Local Query 模式:
Client → 嵌入式 DuckDB → quack_query() 函数 → HTTP POST → Quack Server

Azrng 模式:
Client → HTTP Client → HTTP POST → Quack Server

Local Attach 模式:
Client → 嵌入式 DuckDB → ATTACH 挂载
  ├─ 读查询 → quack_query() 远端执行（规避下推丢失）
  └─ 写/DDL → 原生 attached-table 参数绑定
```

## 前置条件

- .NET SDK 10.0+
- Docker Desktop 或 Docker Engine

## 启动服务器

在仓库根目录执行：

```bash
docker compose -f docker/compose.yml up -d
docker compose -f docker/compose.yml ps
```

等待容器状态显示 `healthy`。默认连接字符串：

```text
Host=localhost;Port=9494;Token=E7231CE2CE78902BA280F3B9158BEB30;DisableSsl=true;Catalog=test
```

如需覆盖，设置环境变量 `QUACK_PROTOCOL_CONNECTION_STRING`：

```bash
# Linux / macOS
export QUACK_PROTOCOL_CONNECTION_STRING="Host=localhost;Port=9494;Token=YOUR_TOKEN;DisableSsl=true;Catalog=test"

# Windows PowerShell
$env:QUACK_PROTOCOL_CONNECTION_STRING = "Host=localhost;Port=9494;Token=YOUR_TOKEN;DisableSsl=true;Catalog=test"
```

## 预下载扩展（可选）

DuckDB 扩展首次使用时会从官方仓库自动下载。如需预下载：

```bash
dotnet run --project src/DuckDBQuackCompareBenchmarks/DuckDBExtensionDownloader/DuckDBExtensionDownloader.csproj
```

### 为什么需要扩展（引擎 ≠ 扩展）

`Local` 客户端内嵌 DuckDB 引擎，通过 Quack 扩展访问服务器。涉及两层不同的 native 资源：

| | 是什么 | 由谁提供 |
| --- | --- | --- |
| DuckDB 引擎（`libduckdb`） | 数据库引擎本体（C++ 运行时） | `DuckDB.NET.Data.Full` NuGet 自带 |
| `quack.duckdb_extension` / `httpfs.duckdb_extension` | 引擎运行时 `LOAD` 进去的插件 | `extensions/` 目录（+ 上面的可选下载器） |

引擎本身无法连接 Quack 服务器——`quack`、`httpfs` 是可加载插件，不是引擎核心的一部分。本地自带可让基准测试：

1. **离线运行**：`INSTALL` 需联网从 extensions.duckdb.org 拉取；自带可让 CI、防火墙或离线环境无需联网即可加载。
2. **版本与平台锁定**：目录 `extensions/v1.5.3/{platform}/` 把扩展锁定到与引擎版本（1.5.3）和运行平台精确匹配的构建。
3. **启动快且可复现**：无首次下载，每台机器使用同一份扩展字节。

> `Azrng` 客户端是纯 C# HTTP 客户端：**不**加载 DuckDB 引擎，也**不**加载任何扩展。这是它部署简单的根本原因。

## 运行基准测试

```bash
dotnet restore src/DuckDBQuackCompareBenchmarks/DuckDBQuackCompareBenchmarks.slnx
dotnet build src/DuckDBQuackCompareBenchmarks/DuckDBQuackCompareBenchmarks.slnx -c Release
dotnet run -c Release --project src/DuckDBQuackCompareBenchmarks/DuckDBQuackCompareBenchmarks/DuckDBQuackCompareBenchmarks.csproj
```

运行子集：

```bash
# 仅冒烟测试（验证连通性，不运行基准）
dotnet run -c Release --project ... -- --smoke-only

# 按名称过滤
dotnet run -c Release --project ... -- --filter "*Query*"
dotnet run -c Release --project ... -- --filter "*Pool*"
```

BenchmarkDotNet 会将 Markdown 和 JSON 报告按运行时间戳归档到 `BenchmarkDotNet.Artifacts/run-yyyyMMdd-HHmmss/results/`，每次运行独立保留、不互相覆盖。

## 基准测试分组

### 三模式对比（Azrng vs Local Attach vs Local Query）

| 分组 | Azrng | Local Attach | Local Query | 目的 |
|------|-------|-------------|-------------|------|
| 初始化 | `Azrng_OpenDispose` | `LocalAttach_OpenDispose` | `LocalQuery_OpenDispose` | 客户端初始化/销毁开销；Local 包含 DuckDB 引擎初始化和扩展加载 |
| 冷查询 | `Azrng_ColdEqualityFilter` | `LocalAttach_ColdEqualityFilter` | `LocalQuery_ColdEqualityFilter` | 打开连接后立即执行一次远端表过滤查询 |
| 查询 | `Azrng_EqualityFilter`, `Azrng_Aggregate10k` | `LocalAttach_EqualityFilter`, `LocalAttach_Aggregate10k` | `LocalQuery_EqualityFilter`, `LocalQuery_Aggregate10k` | 同一远端表上的 warm query 延迟 |
| 参数 | `Azrng_ParameterizedAggregate` | `LocalAttach_ParameterizedAggregate` | `LocalQuery_ParameterizedAggregate` | 参数化远端查询 |
| 结果集 | `Azrng_ReadRows` | `LocalAttach_ReadRows` | `LocalQuery_ReadRows` | 同一远端表的读取吞吐量 |
| 读取器 | `Azrng_TypedGetters` 等 | — | — | 同一远端表上的读取器访问方式对比 |
| 并发 | `Azrng_ParallelQueries` | `LocalAttach_ParallelQueries` | `LocalQuery_ParallelQueries` | 独立连接上的并行远端查询性能 |

查询、结果集、并发分组都会在 `GlobalSetup` 中完成连接打开、Local 扩展加载、ATTACH 以及远端测试表准备。单个 benchmark 方法只测 warm connection 上的查询执行路径。

### Azrng 专项对比

| 分组 | 基准测试 | 目的 |
|------|---------|------|
| 连接池 | `AzrngPool_AcquireReturn`, `AzrngPool_RentDispose`, `AzrngPool_EqualityFilter`, `AzrngPool_LeaseEqualityFilter`, `AzrngPool_ParallelEqualityFilter`, `AzrngPool_LeaseParallelEqualityFilter` | 手动归还 vs lease 模式连接池开销，以及连接池复用下的远端表过滤查询性能 |
| 逐行插入 | `Azrng_PerRowInsert` vs `Local_PerRowInsert` | 逐行写入性能 |
| 批量插入 | `Azrng_BatchInsert`, `Azrng_PagedBatchInsert` | 批量 API 收益及 batch size 影响 |

> **说明**：连接池和批量插入分组不是严格的 API 对等测试。Local 实现不包含等效的连接池或批量插入辅助方法。

## 测试结果

### 环境

- BenchmarkDotNet 0.15.8 / .NET 10.0.8 / Windows 10 (Intel Core i3-9100, 4 核)
- 目标：`Host=172.16.100.26;Port=9494;Catalog=test`（`DisableSsl=true`，纯 HTTP）
- 三种模式连同一台远端 Quack/DuckDB（v1.5.3 扩展）
- `IterationCount=10, WarmupCount=3`（`InsertPerRow/InsertBatch` 因单次耗时长保持 3）。下列数据来自同一次运行（`BenchmarkDotNet.Artifacts/run-20260623-001321/`）。

### 关键结论

1. **Azrng 纯 HTTP 客户端全场景最快**（warm 点查 ~1.9 ms）。它把 SQL 直接 POST 给远端执行，只回小结果，且无嵌入式引擎开销。
2. **Local ATTACH 读查询的 pushdown 病态已修复**：原 v1.5.3 扩展对 attached-table 的 `WHERE` 下推丢失（整表拉回本地筛）已通过让 ATTACH **读查询改走 `quack_query` 远端执行**绕开。warm 点查 279 ms → 23 ms、并发 D=16 从 2284 ms → 337 ms，且 `Error ≪ Mean`（重尾方差消除）。**ATTACH 读与 Local quack_query 已收敛到同一档（~20 ms）**。
3. 现在三档差异主要是客户端架构：**Azrng 直连 HTTP（~2 ms）≫ Local ATTACH ≈ Local quack_query（嵌入式 DuckDB + `quack_query` 包装，~20 ms）**。后两者每次查询都经 `quack_query()` 函数包装 + 嵌入式引擎调度。
4. **ATTACH 连接初始化仍最贵**（~62 ms，含嵌入式 DuckDB 引擎初始化 + httpfs/quack 扩展加载）；写/DDL 仍走原生 ATTACH 参数绑定。
5. **Azrng 读路径内存约 3× 于 Local**（换延迟）；typed-getter 经 columnar 原生数组快路径去装箱（ReaderAccessBench 100k：21.6 MB → 18.3 MB）。
6. 连接池有效（acquire ~1.5 ms）；批量插入**单次全量 > 分页**（1000 行：全量 ~20 ms vs 分页 100=93 ms）。

### ConnectionBench（初始化+销毁）

| 方法 | Mean | Allocated |
|------|-----:|----------:|
| Azrng connect+dispose                 |   3.4 ms | 16.5 KB |
| Local ATTACH initialize+dispose       |  62.2 ms | 12.2 KB |
| Local quack_query initialize+dispose  |  18.0 ms | 11.1 KB |

> ATTACH 含嵌入式 DuckDB 引擎初始化 + httpfs/quack 扩展加载，故最慢；Azrng 不加载引擎/扩展。

### ColdQueryBench（每查询新开连接）

| 方法 | Mean | Allocated |
|------|-----:|----------:|
| Azrng cold equality filter                 |   5.1 ms | 24.2 KB |
| Local ATTACH cold equality filter          |  84.6 ms | 17.2 KB |
| Local quack_query cold equality filter     |  41.6 ms | 14.4 KB |

> ATTACH cold 仍偏高是因为包含 ~62 ms 的连接初始化（引擎+扩展加载）；读本身已走远端执行。

### QueryBench（warm connection）

| 方法 | Mean | Allocated |
|------|-----:|----------:|
| Azrng remote equality filter              |   1.89 ms | 8.34 KB |
| Local ATTACH remote equality filter       |  23.43 ms | 4.93 KB |
| Local quack_query remote equality filter  |  19.97 ms | 3.30 KB |
| Azrng remote parameterized aggregate      |   2.17 ms | 8.97 KB |
| Local ATTACH remote parameterized aggregate | 20.41 ms | 5.74 KB |
| Local quack_query remote parameterized aggregate | 21.69 ms | 4.12 KB |
| Azrng remote aggregate 10k                |   1.97 ms | 7.68 KB |
| Local ATTACH remote aggregate 10k         |  19.34 ms | 4.40 KB |
| Local quack_query remote aggregate 10k    |  20.10 ms | 2.64 KB |

### ResultSetBench（读 N 行）

| 方法 | Rows | Mean | Allocated |
|------|-----:|-----:|----------:|
| Azrng remote read N rows             |  10000 |   4.70 ms |   887.28 KB |
| Local ATTACH remote read N rows      |  10000 |  26.36 ms |   316.8 KB |
| Local quack_query remote read N rows |  10000 |  25.28 ms |   315.09 KB |
| Azrng remote read N rows             | 100000 |  39.12 ms |  9008.03 KB |
| Local ATTACH remote read N rows      | 100000 |  95.95 ms（中位 76） |  3131.06 KB |
| Local quack_query remote read N rows | 100000 | 342.74 ms（中位 335） | 3129.19 KB |

> 全表读（无过滤）三者都回全部行；走 quack_query 的两条（ATTACH/quack_query）结果相近，该路径批量传输有较大运行间方差，故附 Median。

### ReaderAccessBench（Azrng reader 访问方式）

| 方法 | Rows | Mean | Allocated |
|------|-----:|-----:|----------:|
| Azrng reader GetValue       |  10000 |  8.10 ms |   2.28 MB |
| Azrng reader GetValues      |  10000 |  8.52 ms |   2.28 MB |
| Azrng reader typed getters  |  10000 |  8.15 ms |   1.82 MB |
| Azrng reader GetValue       | 100000 | 68.40 ms |  22.91 MB |
| Azrng reader GetValues      | 100000 | 72.25 ms |  22.91 MB |
| Azrng reader typed getters  | 100000 | 67.45 ms |  18.33 MB |

> typed getters 经 columnar 原生数组快路径去装箱后，100k 行分配 21.6 MB → 18.3 MB（-15%）。

### ConcurrencyBench（并行）

| 方法 | Degree | Mean | Allocated |
|------|-------:|-----:|----------:|
| Azrng parallel equality filter              |  4 |     2.25 ms |  30.89 KB |
| Local ATTACH parallel equality filter       |  4 |    85.07 ms |  19.51 KB |
| Local quack_query parallel equality filter  |  4 |    82.31 ms |  12.95 KB |
| Azrng parallel equality filter              | 16 |     4.51 ms | 122.60 KB |
| Local ATTACH parallel equality filter       | 16 |   336.95 ms |  77.20 KB |
| Local quack_query parallel equality filter  | 16 |   292.31 ms |  51.37 KB |

### PoolBench（Azrng 连接池）

| 方法 | Degree | Mean | Allocated |
|------|-------:|-----:|----------:|
| Azrng pool acquire + return               |  4 | 1.52 ms |   7.48 KB |
| Azrng pool lease remote equality filter   |  4 | 3.36 ms |  15.71 KB |
| Azrng pool lease parallel equality filter | 16 | 7.35 ms | 243.04 KB |

### InsertPerRowBench（逐行插入）

| 方法 | Rows | Mean | Allocated |
|------|-----:|-----:|----------:|
| Azrng per-row insert |  100 |  916 ms |  827.52 KB |
| Local per-row insert |  100 | 2707 ms |  321.45 KB |
| Azrng per-row insert | 1000 | 9011 ms | 8441.11 KB |

> Local per-row insert（1000 行）在该环境超时未取得稳定结果（NA）。

### InsertBatchBench（Azrng 批量插入）

| 方法 | Rows | BatchSize | Mean | Allocated |
|------|-----:|----------:|-----:|----------:|
| Azrng batch insert all rows | 1000 |  500 | 19.97 ms（中位 15.9） | 179.75 KB |
| Azrng paged batch insert    | 1000 |  100 | 93.01 ms | 268.30 KB |
| Azrng paged batch insert    | 1000 |  500 | 28.53 ms | 194.05 KB |

---

详细结果与分析另见 [BENCHMARK_RESULTS.md](../BENCHMARK_RESULTS.md)。报告原文在 `BenchmarkDotNet.Artifacts/run-<时间戳>/results/`（每次运行独立归档）。
