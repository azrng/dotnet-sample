# DuckDB Quack 基准测试结果 — 2026-06-23

> 本报告由 2026-06-23 修复 `Azrng.DuckDB.Data.Quack`（ATTACH 读查询改走 quack_query 远端执行、
> 连接池 idleTimeout 参数化、Catalog 注入修复、TimeoutSeconds 透传等）后的代码生成。
> 三种模式（Azrng HTTP 客户端 / Local ATTACH / Local quack_query）连接同一台远程 Quack 服务。

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
| 运行时段 | 2026-06-23 12:27–12:45 |

## 测试对象

三种模式连接同一台远程 Quack 服务器：

| 模式 | 连接类 | 说明 |
|------|--------|------|
| **Azrng** | `AzrngQuackConnection` | 纯 C# HTTP 客户端，直接调用 Quack HTTP API |
| **Local Attach** | `QuackDuckDbConnection` (`Attach=true`) | 本地嵌入式 DuckDB + quack 扩展；**读查询改写为 `quack_query(uri,...)` 远端执行**（修复 pushdown 丢失） |
| **Local Query** | `QuackDuckDbConnection` (`Attach=false`) | 本地嵌入式 DuckDB，经 `quack_query()` 函数远程执行 |

> ⚠️ 关键架构差异：本代码库的 Local Attach 已不再走 attached-table 原生读路径——
> quack 扩展 v1.5.3 对 attached-table 的 WHERE 下推丢失（整表拉回本地筛），
> 故 `QuackDbCommand` 把 ATTACH 读查询（SELECT/WITH/VALUES…）改写为
> `USE "catalog"; <sql>` 包进 `quack_query(uri, ...)` 远端执行。三者的网络往返代价趋于一致，
> 差异主要来自协议效率（HTTP 文本 vs quack 原生二进制）与每次请求的包装开销。

---

## 1. ConnectionBench — 连接建立/销毁（冷启动）

| 模式 | Mean | Error | Allocated |
|------|-----:|------:|----------:|
| **Azrng connect+dispose** | **4.102 ms** | 0.69 ms | 17.22 KB |
| Local ATTACH initialize+dispose | 67.929 ms | 21.30 ms | 12.23 KB |
| Local quack_query initialize+dispose | 17.425 ms | 0.40 ms | 11.07 KB |

**结论**：Azrng 连接建立最快（4.1ms），比 Local ATTACH 快 **16.5×**。Local ATTACH 冷启动需
在 `Open()` 时加载 httpfs + quack 扩展并执行 ATTACH，开销最重；Local quack_query 仅加载扩展、不 ATTACH，居中。

---

## 2. ColdQueryBench — 冷查询（每次新建连接的等值过滤点查）

baseline = Local ATTACH。

| 模式 | Mean | Error | Ratio |
|------|-----:|------:|------:|
| **Azrng cold remote equality filter** | **5.835 ms** | 0.75 ms | 0.08 |
| Local ATTACH cold remote equality filter | 77.871 ms | 12.04 ms | 1.01 |
| Local quack_query cold remote equality filter | 36.017 ms | 1.31 ms | 0.47 |

**结论**：冷连接场景下 Azrng 点查最快，比 Local ATTACH 快 **13.3×**。Local ATTACH 冷路径要付
扩展加载 + ATTACH + 首次 quack_query 远端执行的多重开销，方差大（Error 12ms）。

---

## 3. QueryBench — 热查询（连接复用，等值过滤 / 参数化聚合 / 10k 聚合）

baseline = Local ATTACH。

| 查询 | Azrng | Local ATTACH | Local quack_query |
|------|------:|-------------:|------------------:|
| **equality filter** | **1.939 ms** | 19.842 ms | 20.420 ms |
| **parameterized aggregate** | **2.186 ms** | 20.544 ms | 17.259 ms |
| **aggregate 10k** | **2.150 ms** | 20.479 ms | 17.905 ms |

完整统计（Ratio 以 Local ATTACH equality filter 为基准）：

| 查询 | Mean | Error | Ratio |
|------|-----:|------:|------:|
| Azrng remote equality filter | 1.939 ms | 0.18 ms | 0.10 |
| Azrng remote parameterized aggregate | 2.186 ms | 0.12 ms | 0.11 |
| Azrng remote aggregate 10k | 2.150 ms | 0.21 ms | 0.11 |
| Local ATTACH remote equality filter | 19.842 ms | 3.39 ms | 1.01 |
| Local ATTACH remote parameterized aggregate | 20.544 ms | 2.52 ms | 1.05 |
| Local ATTACH remote aggregate 10k | 20.479 ms | 3.88 ms | 1.04 |
| Local quack_query remote equality filter | 20.420 ms | 3.54 ms | 1.04 |
| Local quack_query remote parameterized aggregate | 17.259 ms | 2.43 ms | 0.88 |
| Local quack_query remote aggregate 10k | 17.905 ms | 2.12 ms | 0.91 |

**结论**：连接已预热时，**Azrng 在所有点查/聚合上最快（~2ms），比两种 Local 模式都快约 9-10×**。
两种 Local 模式（ATTACH / quack_query）性能已**趋于一致**（18-20ms），证实修复后 ATTACH 读路径
与 quack_query 走的是同一条 `quack_query(uri,...)` 远端执行通道——ATTACH 失去了昔日"原生协议+下推"
的虚假优势，但换来了 WHERE 真正下推、避免整表拉回。

> 注：两种 Local 模式比 Azrng 慢，原因是每次查询要在本地 DuckDB 引擎里发起一次 `quack_query` 函数调用，
> 经过扩展层再发 HTTP；Azrng 直接 HTTP，少一层本地引擎中转。

---

## 4. ResultSetBench — 大结果集读取

| Rows | Azrng | Local ATTACH | Local quack_query |
|-----:|------:|-------------:|------------------:|
| 10,000 | 6.092 ms | NA* | NA* |
| 100,000 | NA* | NA* | NA* |

> *部分项 NA：基准运行期间远端服务出现瞬时 `Could not connect to server`（HTTP POST 到
> 172.16.100.26:9494/quack 失败），发生在 GlobalSetup/GlobalCleanup 的建表/删表阶段，
> 属远端稳定性抖动，非查询本身的性能问题。Azrng 10k 成功测得 6.09ms。

---

## 5. ReaderAccessBench — 读取器访问方式（仅 Azrng）

| 访问方式 | 10,000 行 | 100,000 行 |
|---------|----------:|----------:|
| reader GetValue | 8.310 ms | 71.920 ms |
| reader GetValues | 9.100 ms | 75.696 ms |
| reader typed getters | 9.139 ms | 70.587 ms |

**结论**：三种读取方式性能接近，typed getters 在 100k 时略优（内存分配最低 19.2MB vs 24.0MB）。

---

## 6. ConcurrencyBench — 并发查询（等值过滤点查）

baseline = Local ATTACH。

| Degree | Azrng | Local ATTACH | Local quack_query |
|-------:|------:|-------------:|------------------:|
| **4** | **2.421 ms** | 112.854 ms | 113.117 ms |
| **16** | **4.690 ms** | 478.531 ms | 463.781 ms |

完整统计：

| Degree | 模式 | Mean | Ratio |
|-------:|------|-----:|------:|
| 4 | Azrng parallel | 2.421 ms | 0.02 |
| 4 | Local ATTACH parallel | 112.854 ms | 1.13 |
| 4 | Local quack_query parallel | 113.117 ms | 1.13 |
| 16 | Azrng parallel | 4.690 ms | 0.01 |
| 16 | Local ATTACH parallel | 478.531 ms | 1.05 |
| 16 | Local quack_query parallel | 463.781 ms | 1.02 |

**结论**：高并发下 Azrng 优势压倒性——Degree=16 时比两种 Local 模式快 **~100×**。
两种 Local 模式并发时各自维护独立嵌入式 DuckDB 实例，扩展层并发开销重且方差极大（Error 145ms+）。

---

## 7. PoolBench — 连接池（仅 Azrng）

| 操作 | Degree=4 | Degree=16 |
|------|---------:|----------:|
| acquire + return | 1.719 ms | 1.672 ms |
| rent + dispose | 1.635 ms | 1.743 ms |
| remote equality filter | 3.588 ms | 3.847 ms |
| lease remote equality filter | 3.645 ms | 3.847 ms |
| parallel remote equality filter | 4.940 ms | 7.878 ms |
| lease parallel remote equality filter | 4.597 ms | 7.682 ms |

**结论**：连接池获取/归还 ~1.7ms；从池中取连接执行点查 ~3.6ms（含网络）；
并发从 4→16 仅从 4.9ms 升到 7.9ms，扩展性良好。Lease（自动归还）与手动归还性能持平。

---

## 8. InsertPerRowBench — 逐行插入

baseline = Local per-row（Rows=100）。

| Rows | 模式 | Mean | Ratio |
|-----:|------|-----:|------:|
| 100 | **Azrng per-row** | **880.7 ms** | 0.35 |
| 100 | Local per-row | 2,542.9 ms | 1.00 |
| 1000 | Azrng per-row | 8,971.6 ms | — |
| 1000 | Local per-row | NA* | — |

> *Local 1000 行逐行插入失败（远端连接抖动）。

**结论**：逐行插入 Azrng 比 Local 快 **2.9×**（100 行）。但逐行插入本身低效（每行一次往返），
1000 行需 9 秒——见下方批量插入对比。

---

## 9. InsertBatchBench — 批量插入（仅 Azrng）

| Rows | BatchSize | batch insert (all rows) | paged batch insert |
|-----:|----------:|------------------------:|-------------------:|
| 100 | 100 | 10.272 ms | 11.876 ms |
| 100 | 500 | 9.452 ms | 10.088 ms |
| 1000 | 100 | 20.293 ms | 108.820 ms |
| 1000 | 500 | 20.725 ms | 30.726 ms |

**结论**：
- 批量 vs 逐行：Azrng 1000 行批量（~20ms）比逐行（8,971ms）快 **~440×**。
- BatchSize 影响：1000 行时 `batch insert all rows`（不分页，20-21ms）优于 `paged batch`；
  分页模式下 BatchSize=500（30.7ms）明显优于 BatchSize=100（108.8ms）——分页越细往返越多越慢。

---

## 三种模式综合对比

### 查询延迟（连接复用）

| 场景 | Azrng | Local ATTACH | Local quack_query | 最优 |
|------|------:|-------------:|------------------:|------|
| 等值过滤点查 | **1.9 ms** | 19.8 ms | 20.4 ms | Azrng |
| 参数化聚合 | **2.2 ms** | 20.5 ms | 17.3 ms | Azrng |
| 10k 聚合 | **2.2 ms** | 20.5 ms | 17.9 ms | Azrng |

### 各场景最优模式

| 场景 | 最优模式 | 倍率 |
|------|---------|------|
| 连接建立 | Azrng | 比 Local ATTACH 快 16.5× |
| 冷查询点查 | Azrng | 比 Local ATTACH 快 13.3× |
| 热查询点查/聚合 | Azrng | 比 Local 模式快 9-10× |
| 并发查询 (16) | Azrng | 比 Local 模式快 ~100× |
| 批量插入 | Azrng batch API | 比逐行快 ~440× |
| 内存分配 | Local 模式 | Azrng 内存高 ~1.5-2×（HTTP 协议开销） |

## 结论与解读

### Azrng（HTTP 客户端）全面领先
修复 ATTACH 读查询路径后，三种模式都要付网络 RTT，差异主要来自协议层数。Azrng 直接 HTTP、
无本地 DuckDB 引擎中转，在**查询、并发、连接、插入**各项上均最快。仅内存分配因 HTTP 文本协议略高。

### Local ATTACH / Local Query 已基本等价
两者都经 `quack_query(uri,...)` 远端执行，性能趋同（点查 ~20ms、并发 ~110-480ms）。
Local ATTACH 失去旧文档宣称的"亚毫秒"——那本是 `SELECT 1` 不触网/不下推的假象，对真实带表查询
反而是 pushdown 丢失的病态（修复前点查 279ms）。

### 与历史报告的关键差异
相比 `BENCHMARK_RESULTS20260623.md`（修复前，本地 Docker 服务端）：
- 旧 ATTACH `SELECT 1` = 54µs 的"亚毫秒"是**无表查询纯本地计算**，未触网络；带表 WHERE 实为病态。
- 本次全部为远程服务端 + 真实带表查询，ATTACH 走 quack_query 远端执行后与 quack_query 模式持平。
- 绝对数值因(a)服务端从本地 Docker 换远程、(b)客户端机器不同（i3-9100 4核 vs 旧 20核）、
  (c)ATTACH 读路径改写，三者叠加，**不可与旧报告直接横向比较绝对值**。

### 推荐配置
1. **通用 / 高并发 / 低延迟查询**：Azrng — 各项最优，纯 C# 无需本地 DuckDB 引擎与扩展。
2. **需嵌入式 DuckDB 本地计算能力**（如本地 join 缓存表）：Local ATTACH/Query。
3. **批量写入**：务必用 Azrng `ExecuteBatchInsertAsync`（BatchSize=500，1000 行 ~20ms），
   绝不用逐行（9 秒）。

### 已知限制
- ResultSetBench 大结果集与部分逐行插入项因远端服务瞬时抖动（HTTP POST 连接失败）数据缺失，
  非查询性能问题；建议稳定网络后重跑该项。
- Local 模式内存分配更低（无 HTTP 协议开销），对极内存敏感场景仍可选。
