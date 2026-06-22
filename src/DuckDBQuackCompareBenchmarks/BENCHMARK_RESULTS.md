# DuckDB Quack 基准测试结果

## 测试环境

| 项目 | 配置 |
|------|------|
| 操作系统 | Windows 10 (10.0.19045.6466/22H2) |
| CPU | AMD Ryzen 5 PRO 4650U 2.10GHz (6核12线程) |
| .NET SDK | 10.0.202 |
| Runtime | .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3 |
| BenchmarkDotNet | v0.15.8 |
| DuckDB Server | Quack Server (172.16.100.26:9494, Catalog=test) |
| 测试日期 | 2026-06-22 |

## 测试对象

三种模式均连接同一 Quack 服务器：

| 模式 | 连接类 | 连接字符串 | 说明 |
|------|--------|-----------|------|
| **Azrng** | `AzrngQuackConnection` | 普通 | 纯 C# HTTP 客户端，直接调用 Quack 服务器 HTTP API |
| **Local Attach** | `LocalQuackConnection` | `+Attach=true` | 本地 DuckDB 引擎 + Quack 扩展，通过 `ATTACH` 挂载远程目录，查询下推 |
| **Local Query** | `LocalQuackConnection` | 普通 | 本地 DuckDB 引擎 + Quack 扩展，通过 `quack_query()` 函数远程执行 |

---

## 1. ConnectionBench - 连接建立/销毁

| 模式 | Mean | Ratio |
|------|-----:|------:|
| Azrng | 19.58 ms | 0.86x |
| Local Attach | 22.68 ms | 1.00x (baseline) |

**结论**：Azrng 连接比 Local Attach 快约 14%。Local Attach 需要在 `Open()` 时加载 httpfs + quack 扩展并执行 `ATTACH`，开销略高。

---

## 2. QueryBench - 查询延迟

### 2.1 Local Attach 模式（QueryBench）

| 查询 | Azrng | Local Attach | Azrng / Local |
|------|------:|-------------:|-------------:|
| SELECT 1 | 7.16 ms | 0.22 ms | 32x |
| SELECT @a + @b | 6.61 ms | 0.46 ms | 14x |
| COUNT/SUM over 10k | 8.38 ms | 0.47 ms | 18x |

**结论**：Local Attach 模式下查询延迟远低于 Azrng。Attach 模式使用 DuckDB 原生协议，查询自动下推到服务器执行，延迟在亚毫秒级。

### 2.2 Local Query 模式（QueryBenchQuack）

| 查询 | Azrng | Local Query | Azrng 倍率 |
|------|------:|------------:|----------:|
| SELECT 1 | 6.31 ms | 51.22 ms | **7.7x 快** |
| SELECT @a + @b | 6.23 ms | 48.00 ms | **7.7x 快** |
| COUNT/SUM over 10k | 8.74 ms | 56.32 ms | **6.4x 快** |

**结论**：同为远程协议模式，Azrng 比 Local Query 快 6-8 倍。Local Query 每次查询需经过 `quack_query()` 函数包装、SQL 序列化、HTTP 调用等额外开销。

### 2.3 三种模式对比

| 查询 | Local Attach | Azrng | Local Query |
|------|------------:|------:|------------:|
| SELECT 1 | **0.22 ms** | 7.16 ms | 51.22 ms |
| SELECT @a + @b | **0.46 ms** | 6.61 ms | 48.00 ms |
| COUNT/SUM over 10k | **0.47 ms** | 8.38 ms | 56.32 ms |

**结论**：Local Attach 最快（亚毫秒），Azrng 次之（6-8ms），Local Query 最慢（48-56ms）。

### 2.4 架构差异

```
Local Attach 模式（最快）:
Client → 嵌入式 DuckDB → ATTACH → 原生协议 → Quack Server（查询下推）

Azrng 模式（中等）:
Client → 纯 HTTP Client → HTTP POST → Quack Server

Local Query 模式（最慢）:
Client → 嵌入式 DuckDB → quack_query() 函数 → HTTP POST → Quack Server
```

Local Attach 优势：
1. **协议效率**：使用 Quack 原生协议，而非 HTTP 文本协议
2. **连接复用**：初始化一次，后续查询复用连接
3. **查询下推**：DuckDB 自动将查询下推到服务器执行
4. **参数绑定**：使用原生参数绑定，无需序列化成字面量

---

## 3. ResultSetBench - 结果集读取

### Rows = 10,000

| 模式 | Mean | Ratio |
|------|-----:|------:|
| Azrng | 29.00 ms | 0.37x |
| Local Attach | 78.35 ms | 1.00x (baseline) |

### Rows = 100,000

| 模式 | Mean | Ratio |
|------|-----:|------:|
| Azrng | 304.21 ms | 0.92x |
| Local Attach | 331.90 ms | 1.00x (baseline) |

**结论**：
- 小结果集（10k 行）Azrng 比 Local Attach 快 2.7 倍
- 大结果集（100k 行）两者接近，Azrng 略快

---

## 4. ReaderAccessBench - 读取器访问方式（仅 Azrng）

| 访问方式 | 10,000 行 | 100,000 行 |
|---------|----------:|----------:|
| Typed Getters | 61.93 ms | 665.58 ms |
| GetValue | 61.86 ms | 647.26 ms |
| GetValues | 62.01 ms | 650.40 ms |

**结论**：三种读取方式性能几乎无差异，Typed Getters 内存分配略低。

---

## 5. ConcurrencyBench - 并发查询

| 模式 | Degree=4 | Degree=16 |
|------|---------:|----------:|
| Azrng | 6.84 ms | 8.51 ms |
| Local Attach | 64.50 ms | 77.03 ms |
| **Azrng 倍率** | **9.4x 快** | **9.1x 快** |

**结论**：高并发下 Azrng 优势显著。Local Attach 使用独立 DuckDB 实例，并发时有额外开销；Azrng 使用 HTTP 连接池，并发扩展性更好。

---

## 6. PoolBench - 连接池（仅 Azrng）

| 操作 | Degree=4 | Degree=16 |
|------|---------:|----------:|
| Acquire + Return | 5.52 ms | 5.22 ms |
| Rent + Dispose | 5.38 ms | 5.70 ms |
| SELECT 1 | 10.89 ms | 10.74 ms |
| Parallel SELECT 1 | 11.40 ms | 12.67 ms |
| Lease SELECT 1 | 10.86 ms | 10.19 ms |
| Lease Parallel SELECT 1 | 11.45 ms | 13.55 ms |

**结论**：
- 连接池获取/归还约 5ms
- 执行查询约 10-13ms
- 并发度从 4 到 16 性能基本稳定
- Lease 模式与手动归还模式性能接近

---

## 7. InsertBench - 插入性能

### Rows = 100

| 方法 | Mean | Ratio |
|------|-----:|------:|
| Azrng batch insert | 31.29 ms | 0.006x |
| Azrng paged batch insert | 27.74 ms | 0.005x |
| Azrng per-row insert | 1.20 s | 0.233x |
| Local per-row insert | 5.16 s | 1.000x (baseline) |

### Rows = 1000

| 方法 | BatchSize | Mean | Ratio |
|------|----------|-----:|------:|
| Azrng batch insert | 100 | 50.0 ms | 0.001x |
| Azrng batch insert | 500 | 49.6 ms | 0.001x |
| Azrng paged batch insert | 100 | 145.7 ms | 0.003x |
| Azrng paged batch insert | 500 | 68.6 ms | 0.001x |
| Azrng per-row insert | — | 14.57 s | 0.267x |
| Local per-row insert | — | 55.49 s | 1.017x |

**结论**：
- **批量插入 vs 逐行**：Azrng 批量插入比逐行快 **25-290 倍**（1000 行时：50ms vs 14.57s）
- **Azrng vs Local 逐行**：Azrng 逐行比 Local 逐行快 **3.8-4.3 倍**
- **BatchSize 影响**：1000 行时 BatchSize=500 的 paged batch（68.6ms）比 BatchSize=100（145.7ms）快 2 倍

---

## 总结

### 三种模式查询延迟对比

| 查询 | Local Attach | Azrng | Local Query |
|------|------------:|------:|------------:|
| SELECT 1 | **0.22 ms** | 7.16 ms | 51.22 ms |
| SELECT @a + @b | **0.46 ms** | 6.61 ms | 48.00 ms |
| COUNT/SUM over 10k | **0.47 ms** | 8.38 ms | 56.32 ms |

### 各场景性能对比

| 场景 | 最优模式 | 说明 |
|------|---------|------|
| 查询延迟 | Local Attach | 亚毫秒级，比 Azrng 快 15-30 倍 |
| 连接建立 | Azrng | 比 Local Attach 快 14% |
| 结果集读取 | Azrng | 小结果集快 2.7 倍，大结果集接近 |
| 并发查询 | Azrng | 比 Local Attach 快 9 倍 |
| 批量插入 | Azrng Batch API | 比逐行插入快 25-290 倍 |

### 推荐配置

1. **低延迟查询场景**：使用 Local Attach 模式，查询延迟最优（亚毫秒级）
2. **通用场景**：使用 Azrng，无需 DuckDB 引擎和扩展，部署简单，性能优秀
3. **高并发场景**：使用 Azrng + 连接池，并发扩展性好
4. **批量写入场景**：使用 Azrng Batch API，性能远超逐行插入
5. **兼容性场景**：使用 Local Query 模式，兼容旧代码

### Azrng 主要优势

1. **无需 DuckDB 引擎**：纯 C# 实现，部署简单
2. **原生异步**：全面支持 async/await
3. **批量操作**：内置 batch insert API（比逐行快 25-290 倍）
4. **连接池**：内置连接池和 lease 模式
5. **并发稳定**：高并发场景下表现稳定（9x 优于 Local Attach）

### Local Attach 主要优势

1. **查询延迟最低**：亚毫秒级，适合延迟敏感场景
2. **原生协议**：使用 Quack 原生协议，协议效率高
3. **查询下推**：DuckDB 自动将查询下推到服务器

### 已知限制

- Azrng 内存分配比 Local Attach 高 2-7 倍（HTTP 协议开销）
- Local Query 模式性能最慢（每次查询需经过 quack_query 函数包装）
- Local Attach 模式在高并发时有额外开销（独立 DuckDB 实例）
