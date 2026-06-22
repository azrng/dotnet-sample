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
Host=localhost;Port=9494;Token=E7231CE2CE78902BA280F3B9158BEB30;DisableSsl=true
```

如需覆盖，设置环境变量 `QUACK_PROTOCOL_CONNECTION_STRING`：

```bash
# Linux / macOS
export QUACK_PROTOCOL_CONNECTION_STRING="Host=localhost;Port=9494;Token=YOUR_TOKEN;DisableSsl=true"

# Windows PowerShell
$env:QUACK_PROTOCOL_CONNECTION_STRING = "Host=localhost;Port=9494;Token=YOUR_TOKEN;DisableSsl=true"
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
| 连接 | `Azrng_OpenDispose` | `LocalAttach_OpenDispose` | `LocalQuery_OpenDispose` | 连接建立/销毁开销；Local 包含 DuckDB 引擎初始化和扩展加载 |
| 查询 | `Azrng_PointLookup`, `Azrng_Aggregate10k` | `LocalAttach_PointLookup`, `LocalAttach_Aggregate10k` | `LocalQuery_PointLookup`, `LocalQuery_Aggregate10k` | 同一远端表上的 warm query 延迟 |
| 参数 | `Azrng_ParameterizedAggregate` | `LocalAttach_ParameterizedAggregate` | `LocalQuery_ParameterizedAggregate` | 参数化远端查询 |
| 结果集 | `Azrng_ReadRows` | `LocalAttach_ReadRows` | `LocalQuery_ReadRows` | 同一远端表的读取吞吐量 |
| 读取器 | `Azrng_TypedGetters` 等 | — | — | 读取器访问方式对比 |
| 并发 | `Azrng_ParallelQueries` | `LocalAttach_ParallelQueries` | `LocalQuery_ParallelQueries` | 独立连接上的并行远端查询性能 |

查询、结果集、并发分组都会在 `GlobalSetup` 中完成连接打开、Local 扩展加载、ATTACH 以及远端测试表准备。单个 benchmark 方法只测 warm connection 上的查询执行路径。

### Azrng 专项对比

| 分组 | 基准测试 | 目的 |
|------|---------|------|
| 连接池 | `AzrngPool_AcquireReturn`, `AzrngPool_RentDispose`, `AzrngPool_Select1`, `AzrngPool_LeaseSelect1`, `AzrngPool_ParallelSelect1`, `AzrngPool_LeaseParallelSelect1` | 手动归还 vs lease 模式连接池开销 |
| 批量插入 | `Azrng_BatchInsert`, `Azrng_PagedBatchInsert` vs `Azrng_PerRowInsert` 和 `Local_PerRowInsert` | 批量 API 收益及 batch size 影响 |

> **说明**：连接池和批量插入分组不是严格的 API 对等测试。Local 实现不包含等效的连接池或批量插入辅助方法。

## 测试结果

详细结果和分析见 [BENCHMARK_RESULTS.md](../BENCHMARK_RESULTS.md)。
