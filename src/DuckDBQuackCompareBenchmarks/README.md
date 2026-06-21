# DuckDB Quack Compare Benchmarks

This sample compares two .NET Quack clients against the same Quack/DuckDB server:

- Local copied implementation from `C:\Work\gitee\studyDemo\TempSample\src\Quack.DuckDB`
- NuGet package `Azrng.DuckDB.Quack` `1.0.0-beta2`

Use one shared container for both clients. Do not run separate DuckDB containers for each implementation, because container scheduling, cache state, data lifetime, and server configuration would pollute the comparison.

## Start The Server

From the repository root:

```bash
docker compose -f docker/compose.yml up -d
docker compose -f docker/compose.yml ps
```

Wait until the container is healthy. The default connection string is:

```text
Host=localhost;Port=9494;Token=E7231CE2CE78902BA280F3B9158BEB30;DisableSsl=true
```

Override it with:

```bash
export QUACK_PROTOCOL_CONNECTION_STRING="Host=localhost;Port=9494;Token=YOUR_TOKEN;DisableSsl=true"
```

On Windows PowerShell:

```powershell
$env:QUACK_PROTOCOL_CONNECTION_STRING = "Host=localhost;Port=9494;Token=YOUR_TOKEN;DisableSsl=true"
```

## Pre-download Extensions (Optional)

DuckDB extensions are downloaded automatically from the official repository on first use. To pre-download them:

```bash
dotnet run --project src/DuckDBQuackCompareBenchmarks/DuckDBExtensionDownloader/DuckDBExtensionDownloader.csproj
```

## Run

```bash
dotnet restore src/DuckDBQuackCompareBenchmarks/DuckDBQuackCompareBenchmarks.slnx
dotnet build src/DuckDBQuackCompareBenchmarks/DuckDBQuackCompareBenchmarks.slnx -c Release
dotnet run -c Release --project src/DuckDBQuackCompareBenchmarks/DuckDBQuackCompareBenchmarks/DuckDBQuackCompareBenchmarks.csproj
```

Run a subset:

```bash
dotnet run -c Release --project src/DuckDBQuackCompareBenchmarks/DuckDBQuackCompareBenchmarks/DuckDBQuackCompareBenchmarks.csproj -- --smoke-only
dotnet run -c Release --project src/DuckDBQuackCompareBenchmarks/DuckDBQuackCompareBenchmarks/DuckDBQuackCompareBenchmarks.csproj -- --filter "*Query*"
dotnet run -c Release --project src/DuckDBQuackCompareBenchmarks/DuckDBQuackCompareBenchmarks/DuckDBQuackCompareBenchmarks.csproj -- --filter "*Pool*"
```

BenchmarkDotNet writes Markdown and JSON reports under `BenchmarkDotNet.Artifacts/results/`.

## Result Groups

Fair comparisons:

| Group | Local | Azrng | Purpose |
|---|---|---|---|
| Connection | `Local_OpenDispose` | `Azrng_OpenDispose` | Fresh connection cost |
| Query | `Local_Select1`, `Local_Aggregate10k` | `Azrng_Select1`, `Azrng_Aggregate10k` | Warm connection latency |
| Parameters | `Local_ParameterizedSelect` | `Azrng_ParameterizedSelect` | Parameter rendering path |
| Result set | `Local_ReadRows` | `Azrng_ReadRows` | Reader throughput and allocation |
| Reader access | - | `Azrng_TypedGetters`, `Azrng_GetValue`, `Azrng_GetValues` | Isolates reader accessor allocation and throughput |
| Concurrency | `Local_ParallelQueries` | `Azrng_ParallelQueries` | Independent ordinary connections |

Azrng capability comparisons:

| Group | Benchmarks | Purpose |
|---|---|---|
| Pool | `AzrngPool_AcquireReturn`, `AzrngPool_RentDispose`, `AzrngPool_Select1`, `AzrngPool_LeaseSelect1`, `AzrngPool_ParallelSelect1`, `AzrngPool_LeaseParallelSelect1` | Shows manual return and lease-based pooling cost |
| Batch insert | `Azrng_BatchInsert`, `Azrng_PagedBatchInsert` vs `Azrng_PerRowInsert` and `Local_PerRowInsert` | Shows the benefit of Azrng's batch API and batch size impact |

The pool and batch groups are not strict API parity tests because the copied local implementation has no equivalent built-in pool or batch helper.
