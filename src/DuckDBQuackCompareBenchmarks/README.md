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
