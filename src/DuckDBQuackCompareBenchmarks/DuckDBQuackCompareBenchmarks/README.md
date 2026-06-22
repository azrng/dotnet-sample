# DuckDB Quack Compare Benchmarks

Performance comparison between two .NET Quack client implementations targeting the same Quack/DuckDB server:

- **Local**: Vendored `Quack.DuckDB` implementation (embedded DuckDB + Quack extension)
- **Azrng**: NuGet package [`Azrng.DuckDB.Quack`](https://www.nuget.org/packages/Azrng.DuckDB.Quack) `1.0.0-beta2`

Both clients share a single Docker container to ensure identical server conditions (CPU, memory, cache state, connection handling). Running separate containers would invalidate the comparison due to differences in scheduling, resource allocation, and data lifetime.

## Prerequisites

- .NET SDK 10.0+
- Docker Desktop or Docker Engine

## Start the Server

From the repository root:

```bash
docker compose -f docker/compose.yml up -d
docker compose -f docker/compose.yml ps
```

Wait until the container status shows `healthy`. The default connection string is:

```text
Host=localhost;Port=9494;Token=E7231CE2CE78902BA280F3B9158BEB30;DisableSsl=true
```

To override, set the `QUACK_PROTOCOL_CONNECTION_STRING` environment variable:

```bash
# Linux / macOS
export QUACK_PROTOCOL_CONNECTION_STRING="Host=localhost;Port=9494;Token=YOUR_TOKEN;DisableSsl=true"

# Windows PowerShell
$env:QUACK_PROTOCOL_CONNECTION_STRING = "Host=localhost;Port=9494;Token=YOUR_TOKEN;DisableSsl=true"
```

## Pre-download Extensions (Optional)

DuckDB extensions are downloaded automatically from the official repository on first use. To pre-download:

```bash
dotnet run --project src/DuckDBQuackCompareBenchmarks/DuckDBExtensionDownloader/DuckDBExtensionDownloader.csproj
```

### 为什么需要扩展（引擎 ≠ 扩展）

`Local`（内置的 `Quack.DuckDB`）客户端内嵌 DuckDB 引擎，通过 Quack 扩展访问服务器。这里涉及两层不同的 native 资源，而 NuGet 包只提供其中一层：

| | 是什么 | 由谁提供 |
| --- | --- | --- |
| DuckDB 引擎（`libduckdb`） | 数据库引擎本体（C++ 运行时） | `DuckDB.NET.Data.Full` NuGet 自带 |
| `quack.duckdb_extension` / `httpfs.duckdb_extension` | 引擎运行时 `LOAD` 进去的插件 | `extensions/` 目录（+ 上面的可选下载器） |

引擎本身无法连接 Quack 服务器——`quack`、`httpfs` 是可加载插件，不是引擎核心的一部分。本地自带（也可用上面的下载器预取）是为了让基准测试：

1. **离线运行**：`INSTALL` 需联网从 extensions.duckdb.org 拉取；自带可让 CI、防火墙或离线环境无需联网即可加载。
2. **版本与平台锁定**：目录 `extensions/v1.5.3/{platform}/` 把扩展锁定到与引擎版本（1.5.3）和运行平台精确匹配的构建；`INSTALL` 会取仓库当前提供的版本，可能在每次运行间漂移。
3. **启动快且可复现**：无首次下载，每台机器使用同一份扩展字节。

> `Azrng` 客户端是纯 C# HTTP 客户端：**不**加载 DuckDB 引擎，也**不**加载任何扩展。这是它部署简单的根本原因，也是它无法匹配 `Local` 客户端 warm 查询延迟的原因。

## Run Benchmarks

```bash
dotnet restore src/DuckDBQuackCompareBenchmarks/DuckDBQuackCompareBenchmarks.slnx
dotnet build src/DuckDBQuackCompareBenchmarks/DuckDBQuackCompareBenchmarks.slnx -c Release
dotnet run -c Release --project src/DuckDBQuackCompareBenchmarks/DuckDBQuackCompareBenchmarks/DuckDBQuackCompareBenchmarks.csproj
```

Run a subset:

```bash
# Smoke test only (validates connectivity without running benchmarks)
dotnet run -c Release --project ... -- --smoke-only

# Filter by benchmark name
dotnet run -c Release --project ... -- --filter "*Query*"
dotnet run -c Release --project ... -- --filter "*Pool*"
```

BenchmarkDotNet writes Markdown and JSON reports to `BenchmarkDotNet.Artifacts/results/`.

## Benchmark Groups

### Fair Comparisons (Local vs Azrng)

| Group | Local | Azrng | Purpose |
|---|---|---|---|
| Connection | `Local_OpenDispose` | `Azrng_OpenDispose` | Fresh connection open/dispose cost |
| Query | `Local_Select1`, `Local_Aggregate10k` | `Azrng_Select1`, `Azrng_Aggregate10k` | Warm connection query latency |
| Parameters | `Local_ParameterizedSelect` | `Azrng_ParameterizedSelect` | Parameterized query rendering |
| Result set | `Local_ReadRows` | `Azrng_ReadRows` | Reader throughput and allocation |
| Reader access | — | `Azrng_TypedGetters`, `Azrng_GetValue`, `Azrng_GetValues` | Reader accessor allocation and throughput |
| Concurrency | `Local_ParallelQueries` | `Azrng_ParallelQueries` | Parallel query execution on independent connections |

### Azrng-only Comparisons

| Group | Benchmarks | Purpose |
|---|---|---|
| Pool | `AzrngPool_AcquireReturn`, `AzrngPool_RentDispose`, `AzrngPool_Select1`, `AzrngPool_LeaseSelect1`, `AzrngPool_ParallelSelect1`, `AzrngPool_LeaseParallelSelect1` | Manual return vs lease-based pooling cost |
| Batch insert | `Azrng_BatchInsert`, `Azrng_PagedBatchInsert` vs `Azrng_PerRowInsert` and `Local_PerRowInsert` | Batch API benefit and batch size impact |

> **Note**: Pool and batch groups are not strict API parity tests. The vendored local implementation does not include equivalent pool or batch helpers.

## Test Results

See [BENCHMARK_RESULTS.md](./BENCHMARK_RESULTS.md) for detailed results and analysis.
