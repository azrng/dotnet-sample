# DuckDB Quack 基准测试结果 — 2026-06-23（第 2 轮）

> 本报告由 2026-06-23 修复 `Azrng.DuckDB.Quack` 纯 C# 桥接（补齐 TIMESTAMP_S/MS/NS 精度变体解码、
> 修复全 NULL 的 DATE/TIMESTAMP 列读取崩溃）后的代码生成。
> 三种模式（Azrng HTTP 客户端 / Local ATTACH / Local quack_query）连接同一台远程 Quack 服务。
>
> **本次目的**：验证桥接正确性修复**未引入性能回归**。基准查询用的是 BIGINT/VARCHAR/BOOLEAN/DOUBLE,
> 不触碰本次改动的 DATE/TIMESTAMP 解码路径——若改动正确,数值应与上一轮（[BENCHMARK_RESULTS_20260623.md](./BENCHMARK_RESULTS_20260623.md)）同档。

## 测试环境

| 项目 | 配置 |
|------|------|
| 操作系统 | Windows 10 (10.0.19045.6466 / 22H2 / 2022Update) |
| CPU | Intel Core i3-9100 3.60GHz (1 CPU, 4 核 4 线程) |
| .NET SDK | 10.0.300 |
| Runtime | .NET 10.0.8 (10.0.826.23019), X64 RyuJIT x86-64-v3 |
| GC | Concurrent Server |
| BenchmarkDotNet | v0.15.8 |
| DuckDB Server | Quack Server **172.16.100.26:9494**（远程，Catalog=test）|
| 运行时段 | 2026-06-23 18:32–18:52 |
| 归档目录 | `BenchmarkDotNet.Artifacts/run-20260623-183254/`（7 个快速类）、`run-20260623-184821/`（2 个插入类）|

## 测试对象

三种模式连接同一台远程 Quack 服务器：

| 模式 | 连接类 | 说明 |
|------|--------|------|
| **Azrng** | `AzrngQuackConnection` | 纯 C# HTTP 客户端，直接调用 Quack HTTP API |
| **Local Attach** | `QuackDuckDbConnection` (`Attach=true`) | 本地嵌入式 DuckDB + quack 扩展；读查询改写为 `quack_query(uri,...)` 远端执行 |
| **Local Query** | `QuackDuckDbConnection` (`Attach=false`) | 本地嵌入式 DuckDB，经 `quack_query()` 函数远程执行 |

---

## 1. ConnectionBench — 连接建立/销毁（冷启动）

| 模式 | Mean | Error | Allocated |
|------|-----:|------:|----------:|
| **Azrng connect+dispose** | **3.726 ms** | 0.45 ms | 18.05 KB |
| Local ATTACH initialize+dispose | 82.196 ms | 3.89 ms | 12.25 KB |
| Local quack_query initialize+dispose | 17.887 ms | 0.65 ms | 11.07 KB |

**结论**：Azrng 连接建立最快（3.7ms），比 Local ATTACH 快 **~22×**。Local ATTACH 冷启动需加载
httpfs + quack 扩展并执行 ATTACH,开销最重；Local quack_query 仅加载扩展、不 ATTACH,居中。
（上轮 Azrng 4.1ms、ATTACH 67.9ms,本轮 ATTACH 偏高属扩展加载正常波动,Azrng 同档。）

---

## 2. ColdQueryBench — 冷查询（每次新建连接的等值过滤点查）

baseline = Local ATTACH。

| 模式 | Mean | Error | Ratio |
|------|-----:|------:|------:|
| **Azrng cold remote equality filter** | **5.598 ms** | 0.36 ms | 0.06 |
| Local ATTACH cold remote equality filter | 99.155 ms | 4.43 ms | 1.00 |
| Local quack_query cold remote equality filter | 38.205 ms | 4.30 ms | 0.39 |

**结论**：冷连接场景下 Azrng 点查最快,比 Local ATTACH 快 **~17.7×**。
（上轮 Azrng 5.8ms、ATTACH 77.9ms,本轮 ATTACH cold 99ms 略高,含 ~82ms 的连接初始化,属扩展加载方差。）

---

## 3. QueryBench — 热查询（连接复用，等值过滤 / 参数化聚合 / 10k 聚合）

baseline = Local ATTACH。

| 查询 | Mean | Error | Ratio |
|------|-----:|------:|------:|
| **Azrng remote equality filter** | **1.893 ms** | 0.21 ms | 0.10 |
| Azrng remote parameterized aggregate | 2.284 ms | 0.25 ms | 0.13 |
| Azrng remote aggregate 10k | 2.110 ms | 0.15 ms | 0.12 |
| Local ATTACH remote equality filter | 18.305 ms | 2.99 ms | 1.01 |
| Local ATTACH remote parameterized aggregate | 20.368 ms | 4.51 ms | 1.12 |
| Local ATTACH remote aggregate 10k | 21.164 ms | 4.34 ms | 1.17 |
| Local quack_query remote equality filter | 20.150 ms | 3.99 ms | 1.11 |
| Local quack_query remote parameterized aggregate | 22.457 ms | 6.72 ms | 1.24 |
| Local quack_query remote aggregate 10k | 17.854 ms | 2.85 ms | 0.98 |

**结论**：连接已预热时,**Azrng 在所有点查/聚合上最快（~1.9-2.3ms）,比两种 Local 模式都快约 9-11×**。
两种 Local 模式（ATTACH / quack_query）性能**趋于一致**（18-22ms）,证实 ATTACH 读路径与 quack_query 走同一通道。
（上轮 Azrng equality filter 1.94ms,本轮 1.89ms——**几乎完全一致,核心热路径零回归**。）

---

## 4. ResultSetBench — 大结果集读取

| Rows | Azrng | Local ATTACH | Local quack_query |
|-----:|------:|-------------:|------------------:|
| 10,000 | **5.467 ms** | 20.779 ms | NA* |
| 100,000 | NA* | NA* | NA* |

> *本轮 NA 较上轮增多：基准运行期间远端服务出现瞬时 `Could not connect to server`
> （HTTP POST 到 172.16.100.26:9494/quack 失败）,发生在 GlobalSetup/GlobalCleanup 的建表/删表阶段。
> **属远端稳定性抖动,非查询本身性能问题**（README 第 168-172 行已记录该现象）。
> Azrng 10k 成功测得 5.47ms（上轮 6.09ms,同档）。

---

## 5. ReaderAccessBench — 读取器访问方式（仅 Azrng）

| 访问方式 | 10,000 行 | 100,000 行 |
|---------|----------:|----------:|
| reader GetValue | NA* | 71.120 ms |
| reader GetValues | NA* | NA* |
| reader typed getters | NA* | 67.660 ms |

> *本轮 10k 行三项及 GetValues 100k 项因远端抖动 NA。成功测得的 typed getters 100k 为 67.7ms
> （上轮 70.6ms,同档）,GetValue 100k 为 71.1ms（上轮 71.9ms,几乎一致）。
> typed getters 内存分配 18.33 MB,优于 GetValue/GetValues 的 22.91 MB（-20%）,去装箱收益保持。

---

## 6. ConcurrencyBench — 并发查询（等值过滤点查）

baseline = Local ATTACH。

| Degree | 模式 | Mean | Error | Ratio |
|-------:|------|-----:|------:|------:|
| 4 | **Azrng parallel** | **2.600 ms** | 0.26 ms | 0.03 |
| 4 | Local ATTACH parallel | 77.168 ms | 15.70 ms | 1.02 |
| 4 | Local quack_query parallel | 74.197 ms | 20.96 ms | 0.98 |
| 16 | **Azrng parallel** | **4.454 ms** | 0.27 ms | 0.01 |
| 16 | Local ATTACH parallel | 331.433 ms | 77.02 ms | 1.02 |
| 16 | Local quack_query parallel | 297.275 ms | 60.74 ms | 0.92 |

**结论**：高并发下 Azrng 优势压倒性——Degree=16 时比两种 Local 模式快 **~67-75×**。
两种 Local 模式各自维护独立嵌入式 DuckDB 实例,扩展层并发开销重且方差极大（Error 60-77ms）。
（上轮 Azrng D=16 = 4.69ms,本轮 4.45ms,同档；Local 模式比上轮 478ms 低,属并发方差正常范围。）

---

## 7. PoolBench — 连接池（仅 Azrng）

| 操作 | Degree=4 | Degree=16 |
|------|---------:|----------:|
| acquire + return | 1.645 ms | 1.661 ms |
| rent + dispose | 1.428 ms | 1.646 ms |
| remote equality filter | 3.263 ms | 3.671 ms |
| lease remote equality filter | 3.276 ms | 3.593 ms |
| parallel remote equality filter | 3.956 ms | 7.604 ms |
| lease parallel remote equality filter | 4.298 ms | 7.723 ms |

**结论**：连接池获取/归还 ~1.4-1.7ms；从池中取连接执行点查 ~3.3-3.7ms（含网络）；
并发从 4→16 仅从 ~4ms 升到 ~7.6ms,扩展性良好。Lease（自动归还）与手动归还性能持平。
（上轮 acquire+return 1.72ms、lease filter 3.65ms,本轮同档。）

---

## 8. InsertPerRowBench — 逐行插入

baseline = Local per-row（Rows=100）。

| Rows | 模式 | Mean | Ratio |
|-----:|------|-----:|------:|
| 100 | **Azrng per-row** | **913.5 ms** | 0.34 |
| 100 | Local per-row | 2,721.6 ms | 1.00 |
| 1000 | Azrng per-row | 9,860.7 ms | — |
| 1000 | Local per-row | NA* | — |

> *Local 1000 行逐行插入失败（远端连接抖动）。
> 上轮 Azrng 100 行 881ms、1000 行 8,972ms,本轮 914ms / 9,861ms,**同档（+4%/+10%,在误差内）**。

**结论**：逐行插入 Azrng 比 Local 快 **~3.0×**（100 行）。但逐行插入本身低效（每行一次往返）,
1000 行需 ~10 秒——见下方批量插入对比。

---

## 9. InsertBatchBench — 批量插入（仅 Azrng）

| Rows | BatchSize | batch insert (all rows) | paged batch insert |
|-----:|----------:|------------------------:|-------------------:|
| 100 | 100 | 10.088 ms | 10.480 ms |
| 100 | 500 | 10.133 ms | 9.289 ms |
| 1000 | 100 | 13.437 ms | 108.959 ms |
| 1000 | 500 | 18.557 ms | 34.163 ms |

**结论**：
- 批量 vs 逐行：Azrng 1000 行批量（~14-19ms）比逐行（9,861ms）快 **~520-700×**。
- BatchSize 影响：1000 行时 `batch insert all rows`（不分页,13-19ms）优于 `paged batch`；
  分页模式下 BatchSize=500（34ms）明显优于 BatchSize=100（109ms）——分页越细往返越多越慢。
- 上轮 1000/500 全量 20.7ms、分页 100=108.8ms / 500=30.7ms,本轮**同档**。
  （注：插入类用 `IterationCount=3`,Error 列偏大属样本少的统计噪声,看 Mean 即可。）

---

## 三种模式综合对比

### 查询延迟（连接复用）

| 场景 | Azrng | Local ATTACH | Local quack_query | 最优 |
|------|------:|-------------:|------------------:|------|
| 等值过滤点查 | **1.9 ms** | 18.3 ms | 20.2 ms | Azrng |
| 参数化聚合 | **2.3 ms** | 20.4 ms | 22.5 ms | Azrng |
| 10k 聚合 | **2.1 ms** | 21.2 ms | 17.9 ms | Azrng |

### 各场景最优模式

| 场景 | 最优模式 | 倍率 |
|------|---------|------|
| 连接建立 | Azrng | 比 Local ATTACH 快 ~22× |
| 冷查询点查 | Azrng | 比 Local ATTACH 快 ~17.7× |
| 热查询点查/聚合 | Azrng | 比 Local 模式快 9-11× |
| 并发查询 (16) | Azrng | 比 Local 模式快 ~67-75× |
| 批量插入 | Azrng batch API | 比逐行快 ~520-700× |
| 内存分配 | Local 模式 | Azrng 内存高 ~1.5-2×（HTTP 协议开销） |

---

## 结论与解读

### ✅ 桥接正确性修复零回归（本次核心结论）

本轮验证对象是 `Azrng.DuckDB.Quack` 的两项修复：
1. 新增 TIMESTAMP_S/MS/NS 精度变体解码（此前走跳过路径返回空）
2. 修复全 NULL 的 DATE/TIMESTAMP 列读取崩溃（NULL 哨兵溢出）

**所有成功测得的 Azrng 项与上一轮同档,核心热路径零回归**：

| 指标 | 上轮 | 本轮 | 偏差 |
|------|-----:|-----:|------|
| warm equality filter | 1.94 ms | 1.89 ms | -2.5% |
| warm aggregate 10k | 2.15 ms | 2.11 ms | -1.9% |
| parallel D=16 | 4.69 ms | 4.45 ms | -5.1% |
| pool acquire+return | 1.72 ms | 1.65 ms | -4.1% |
| per-row insert 100 | 881 ms | 914 ms | +3.7% |
| batch insert 1000/500 | 20.7 ms | 18.6 ms | -10.4% |

所有偏差均在正常测量噪声范围内（±10%）,且**无系统性变慢趋势**。这符合预期——基准查询
用 BIGINT/VARCHAR/BOOLEAN/DOUBLE,完全不触碰本次改动的 DATE/TIMESTAMP 解码路径。

### Azrng（HTTP 客户端）全面领先

三种模式都要付网络 RTT,差异主要来自协议层数。Azrng 直接 HTTP、无本地 DuckDB 引擎中转,
在查询、并发、连接、插入各项上均最快。仅内存分配因 HTTP 文本协议略高。

### Local ATTACH / Local Query 已基本等价

两者都经 `quack_query(uri,...)` 远端执行,性能趋同（点查 ~18-22ms、并发 ~74-331ms）。

### ⚠️ 本轮 NA 增多（非代码问题）

相比上一轮,ResultSet/ReaderAccess/InsertPerRow 部分项 NA。判断依据：
1. Azrng 与 Local 是**完全不同**的客户端实现,却在 ResultSet 同一批次**同时** NA → 共因只可能是远端服务
2. ColdQuery/Query/Connection/Pool 这些跑在服务稳定窗口的项,数据正常且与上轮一致
3. 本次代码改动不可能让 Local ATTACH（用 DuckDB.NET,未改一行）产生 NA

**根因是远端服务在长时间基准运行中的瞬时抖动,与本次桥接改动无关**。
要补齐数据只需在服务更稳定时重跑 ResultSet/ReaderAccess（`--filter "*ResultSet*" "*ReaderAccess*"`）。

### 推荐配置
1. **通用 / 高并发 / 低延迟查询**：Azrng — 各项最优,纯 C# 无需本地 DuckDB 引擎与扩展。
2. **需嵌入式 DuckDB 本地计算能力**（如本地 join 缓存表）：Local ATTACH/Query。
3. **批量写入**：务必用 Azrng `ExecuteBatchInsertAsync`（BatchSize=500,1000 行 ~19ms）,
   绝不用逐行（~10 秒）。

### 与历史报告的关系
- 本轮（`BENCHMARK_RESULTS_20260623_v2.md`）：桥接 timestamp/NULL 修复后,验证零回归。
- 上轮（[BENCHMARK_RESULTS_20260623.md](./BENCHMARK_RESULTS_20260623.md)）：`Azrng.DuckDB.Data.Quack` ATTACH 读路径修复后。
- 更早（[BENCHMARK_RESULTS20260623.md](./BENCHMARK_RESULTS20260623.md)）：本地 Docker 服务端,ATTACH pushdown 病态未修。
- 绝对值因服务端位置/客户端机器/读路径改写叠加,**跨报告不可直接横向比较绝对值**,只看同报告内三模式对比。
