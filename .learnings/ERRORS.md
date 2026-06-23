## [ERR-20260622-001] dotnet_build_file_lock

**Logged**: 2026-06-22T00:00:00+08:00
**Priority**: low
**Status**: pending
**Area**: infra

### Summary
`dotnet build` can fail transiently on Windows when antivirus locks generated DLLs under `obj/`.

### Error
```text
CSC : error CS2012: cannot open ... obj\Debug\net10.0\Azrng.DuckDB.Data.Quack.dll for writing because it is being used by another process; possibly locked by Huorong Internet Security Daemon.
```

### Context
- Command attempted: `dotnet build src/DuckDBQuackCompareBenchmarks/Azrng.DuckDB.Data.Quack/Azrng.DuckDB.Data.Quack.csproj`
- Subsequent `dotnet test` and benchmark project build succeeded.

### Suggested Fix
Retry the build after a short delay; treat as transient unless it repeats.

### Metadata
- Reproducible: unknown
- Related Files: src/DuckDBQuackCompareBenchmarks/Azrng.DuckDB.Data.Quack/Azrng.DuckDB.Data.Quack.csproj

---

## [ERR-20260622-002] quack_two_parsers_disagree_on_ssl_default

**Logged**: 2026-06-22T00:00:00+08:00
**Priority**: high
**Status**: resolved
**Area**: correctness

### Summary
两个连接字符串解析器对 `DisableSsl` 的默认值不一致，导致同一连接字符串在不同 client 下走不同协议。

### Error
用户连接字符串 `Host=...;Port=9494;Token=...;Catalog=test`（未显式指定 DisableSsl）：
- `Azrng.DuckDB.Data.Quack.QuackConnectionStringParser` 默认 `DisableSsl=true`（HTTP）→ Local ATTACH/quack_query 正常
- `Azrng.DuckDB.Quack.QuackProtocolConnectionStringParser` 原默认 `DisableSsl=false`（HTTPS）→ Azrng 纯协议 client 触发 TLS 握手失败：`The SSL connection could not be established ... Received an unexpected EOF or 0 bytes from the transport stream.`（Quack 默认容器 9494 端口为纯 HTTP）

不仅阻塞 smoke check，还让三方对比变成 HTTP vs HTTPS 的不公平比较。

### Fix
将 `QuackProtocolConfig.DisableSsl` 默认值与 `QuackProtocolConnectionStringParser`（key-value 与 URI 两条路径）统一为 `true`，与同仓 Data.Quack 解析器、Azrng.DuckDB.Quack README 文档、docker/compose.yml（纯 HTTP）一致。更新了 2 个原先 pin 旧默认值的单测（`Parse_QuackUri_UsesDefaults`、`Parse_KeyValue_DefaultsToSslDisabled`）。

### Metadata
- Reproducible: yes（任意不含 DisableSsl 的连接字符串）
- Related Files: src/DuckDBQuackCompareBenchmarks/Azrng.DuckDB.Quack/QuackProtocolConfig.cs, QuackProtocolConnectionStringParser.cs

---

## [ERR-20260622-003] benchmarkdotnet_async_iteration_setup_cs0407

**Logged**: 2026-06-22T00:00:00+08:00
**Priority**: high
**Status**: resolved
**Area**: infra

### Summary
BenchmarkDotNet 0.15.8 不支持返回 `Task` 的 `[IterationSetup]`，会让整个自动生成程序集编译失败，导致**整套基准零执行**（所有报告 NA）。

### Error
`[IterationSetup] public async Task IterationSetup()` 触发：
```
error CS0407: "Task InsertBatchBench.IterationSetup()"的返回类型错误
```
BDN 代码生成对 `[GlobalSetup]/[GlobalCleanup]` 做了 `() => AwaitHelper.GetResult(Setup())` 包装，但对 `[IterationSetup]` 直接 `iterationSetupAction = IterationSetup;`（赋值给 `Action`），故 `Func<Task>` 无法赋给 `Action`。单个 benchmark 的编译错误会破坏整份自动生成代码，连带同程序集内其它 benchmark（含 ConnectionBench）全部 0 执行。

### Fix
`[IterationSetup]` 方法改为返回 `void`，内部 `.GetAwaiter().GetResult()` 同步阻塞（IterationSetup 不计入计时，sync-over-async 无副作用）。`[GlobalSetup]`/`[GlobalCleanup]` 仍可安全用 `async Task`。

### Metadata
- Reproducible: yes
- Related Files: src/DuckDBQuackCompareBenchmarks/DuckDBQuackCompareBenchmarks/QuackCompareBenchmarks.cs (InsertPerRowBench/InsertBatchBench)
