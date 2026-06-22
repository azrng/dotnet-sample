# DuckDB Quack 基准测试

对比三种 .NET Quack 客户端实现的性能差异，均连接同一 Quack/DuckDB 服务器。

## 三种模式

| 模式 | 连接类 | 连接字符串 | 说明 |
|------|--------|-----------|------|
| **Azrng** | `AzrngQuackConnection` | 普通 | 纯 C# HTTP 客户端，直接调用 Quack 服务器 HTTP API，不依赖 DuckDB 引擎和扩展 |
| **Local Attach** | `LocalQuackConnection` | `+Attach=true` | 本地 DuckDB 引擎 + Quack 扩展，通过 `ATTACH` 挂载远程目录，查询自动下推 |
| **Local Query** | `LocalQuackConnection` | 普通 | 本地 DuckDB 引擎 + Quack 扩展，通过 `quack_query()` 函数包装 SQL 远程执行 |

### 架构差异

```
Local Query 模式:
Client → 嵌入式 DuckDB → quack_query() 函数 → HTTP POST → Quack Server

Azrng 模式:
Client → HTTP Client → HTTP POST → Quack Server

Local Attach 模式:
Client → 嵌入式 DuckDB → ATTACH → 原生协议 → Quack Server (查询下推)
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

BenchmarkDotNet 会将 Markdown 和 JSON 报告输出到 `BenchmarkDotNet.Artifacts/results/`。

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
- 默认 `IterationCount=10, WarmupCount=3`（`InsertPerRow/InsertBatch` 因单次耗时长保持 3）；`QueryBench`/`ReaderAccessBench` 为 10 次迭代验证版，其余为首跑（3–5 次）数据。Local ATTACH 路径存在重尾（下推病态，见下文结论），故同时给 Mean 与 Median。

### 关键结论

1. **Azrng 纯 HTTP 客户端全场景最快**（点查 ~1.9 ms）。它把 SQL 直接 POST 给远端执行，只回小结果，且无嵌入式引擎开销。
2. **Local ATTACH 对带过滤的查询有严重 pushdown 病态**：`WHERE` 没下推到远端，本地把整表拉回再筛——点查比 Azrng 慢 30×、比 quack_query 慢 3×，且重尾方差大（Mean 与 Median 差很多）。全表读（无过滤）则与 quack_query 持平。
3. **Local quack_query** 是稳定的中间档（点查 ~21 ms）。
4. **Azrng 读路径内存约 3× 于 Local**（换取更低延迟）；已对 typed-getter 去装箱优化（见 ReaderAccessBench）。
5. 连接池有效（acquire ~1.7 ms）；批量插入**单次全量 > 分页**（1000 行：全量 26 ms vs 分页 100=160 ms）。

### ConnectionBench（初始化+销毁，IterationCount=5）

| 方法 | Mean | Allocated |
|------|-----:|----------:|
| Azrng connect+dispose                 |   3.83 ms | 16.47 KB |
| Local ATTACH initialize+dispose       |  61.72 ms | 12.19 KB |
| Local quack_query initialize+dispose  |  17.81 ms | 11.07 KB |

> ATTACH 含嵌入式 DuckDB 引擎初始化 + httpfs/quack 扩展加载，故最慢；Azrng 不加载引擎/扩展。

### ColdQueryBench（每查询新开连接，IterationCount=5）

| 方法 | Mean | Allocated |
|------|-----:|----------:|
| Azrng cold equality filter                 |   5.33 ms | 24.01 KB |
| Local ATTACH cold equality filter          | 135.54 ms | 14.12 KB |
| Local quack_query cold equality filter     |  36.68 ms | 14.41 KB |

### QueryBench（warm connection，IterationCount=10，验证版）

| 方法 | Mean | Median | Allocated |
|------|-----:|-------:|----------:|
| Azrng remote equality filter              |   1.87 ms |  1.87 ms | 8.07 KB |
| Local ATTACH remote equality filter       |  59.64 ms | 57.88 ms | 1.90 KB |
| Local quack_query remote equality filter  |  21.27 ms | 21.04 ms | 3.30 KB |
| Azrng remote parameterized aggregate      |   2.25 ms |  2.27 ms | 8.73 KB |
| Local ATTACH remote parameterized aggregate | 34.79 ms | 35.72 ms | 2.45 KB |
| Local quack_query remote parameterized aggregate | 20.73 ms | 20.41 ms | 4.12 KB |
| Azrng remote aggregate 10k                |   2.09 ms |  2.08 ms | 7.31 KB |
| Local ATTACH remote aggregate 10k         | 130.77 ms | 47.84 ms | 1.78 KB |
| Local quack_query remote aggregate 10k    |  22.26 ms | 22.08 ms | 2.64 KB |

### ResultSetBench（读 N 行，IterationCount=3）

| 方法 | Rows | Mean | Allocated |
|------|-----:|-----:|----------:|
| Azrng remote read N rows             |  10000 |   5.72 ms |   887.18 KB |
| Local ATTACH remote read N rows      |  10000 |  13.55 ms |   314.02 KB |
| Local quack_query remote read N rows |  10000 |  27.68 ms |   315.07 KB |
| Azrng remote read N rows             | 100000 |  39.21 ms |  9005.6 KB |
| Local ATTACH remote read N rows      | 100000 | 170.12 ms |  3128.15 KB |
| Local quack_query remote read N rows | 100000 | 168.50 ms |  3129.26 KB |

### ReaderAccessBench（Azrng reader 访问方式，IterationCount=10，验证版）

| 方法 | Rows | Mean | Allocated |
|------|-----:|-----:|----------:|
| Azrng reader GetValue       |  10000 |  8.10 ms |   2.28 MB |
| Azrng reader GetValues      |  10000 |  8.52 ms |   2.28 MB |
| Azrng reader typed getters  |  10000 |  8.15 ms |   1.82 MB |
| Azrng reader GetValue       | 100000 | 68.40 ms |  22.91 MB |
| Azrng reader GetValues      | 100000 | 72.25 ms |  22.91 MB |
| Azrng reader typed getters  | 100000 | 67.45 ms |  18.33 MB |

> typed getters 经 columnar 原生数组快路径去装箱后，100k 行分配 21.6 MB → 18.3 MB（-15%）。

### ConcurrencyBench（并行，IterationCount=3）

| 方法 | Degree | Mean | Allocated |
|------|-------:|-----:|----------:|
| Azrng parallel equality filter              |  4 |     2.48 ms |  31.27 KB |
| Local ATTACH parallel equality filter       |  4 |  1092.87 ms |   6.79 KB |
| Local quack_query parallel equality filter  |  4 |    70.80 ms |  12.93 KB |
| Azrng parallel equality filter              | 16 |     4.57 ms | 122.82 KB |
| Local ATTACH parallel equality filter       | 16 |  2283.55 ms |  33.99 KB |
| Local quack_query parallel equality filter  | 16 |   291.03 ms |  50.93 KB |

### PoolBench（Azrng 连接池，IterationCount=5）

| 方法 | Degree | Mean | Allocated |
|------|-------:|-----:|----------:|
| Azrng pool acquire + return               |  4 | 1.67 ms |   7.41 KB |
| Azrng pool lease remote equality filter   |  4 | 3.95 ms |  15.45 KB |
| Azrng pool lease parallel equality filter | 16 | 8.82 ms | 242.39 KB |

### InsertPerRowBench（逐行插入，IterationCount=3）

| 方法 | Rows | Mean | Allocated |
|------|-----:|-----:|----------:|
| Azrng per-row insert |  100 |  1.143 s |  839.33 KB |
| Local per-row insert |  100 |  2.881 s |  321.45 KB |
| Azrng per-row insert | 1000 | 12.240 s | 8260.03 KB |

> Local per-row insert（1000 行）在该环境超时未取得稳定结果（NA）。

### InsertBatchBench（Azrng 批量插入，IterationCount=3）

| 方法 | Rows | BatchSize | Mean | Allocated |
|------|-----:|----------:|-----:|----------:|
| Azrng batch insert all rows | 1000 |  500 |  26.23 ms | 180.41 KB |
| Azrng paged batch insert    | 1000 |  100 | 160.36 ms | 261.74 KB |
| Azrng paged batch insert    | 1000 |  500 |  51.18 ms | 194.71 KB |

> 单次全量批量（26 ms）远优于分页；分页时 batch size 越大越快（100→160 ms，500→51 ms）。

---

详细结果与分析另见 [BENCHMARK_RESULTS.md](../BENCHMARK_RESULTS.md)。报告原文在 `BenchmarkDotNet.Artifacts/results/`。
