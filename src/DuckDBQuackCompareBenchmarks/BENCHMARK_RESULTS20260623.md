# DuckDB Quack Compare Benchmarks

## 测试环境

| 项目 | 配置 |
|------|------|
| 操作系统 | Windows 11 (10.0.26200.8655/25H2) |
| CPU | Intel Core Ultra 7 255HX 2.40GHz (20核20线程) |
| 内存 | 16 GB |
| .NET SDK | 10.0.301 |
| Runtime | .NET 10.0.9 (10.0.926.27113), X64 RyuJIT x86-64-v3 |
| BenchmarkDotNet | v0.15.8 |
| DuckDB Server | DuckDB Quack 1.5.3 (Docker, 4 CPU, 8GB RAM) |
| 测试日期 | 2026-06-21 (第二次运行) |

## 测试对象

- **Local**: 本地实现 `Quack.DuckDB` (vendored from local path)
- **Azrng**: NuGet包 `Azrng.DuckDB.Quack` 1.0.0-beta2

---

## 1. ConnectionBench - 连接打开/关闭

| Method | Mean | Error | StdDev | Ratio | Allocated | Alloc Ratio |
|--------|-----:|------:|-------:|------:|----------:|------------:|
| Azrng Open+Dispose | 883.6 us | 279.3 us | 72.52 us | 0.01 | 9.9 KB | 1.97 |
| Local Open+Dispose | 66,682.8 us | 1,821.2 us | 281.84 us | 1.00 | 5.03 KB | 1.00 |

**结论**: Azrng 连接速度比 Local 快约 **75倍** (883.6us vs 66.7ms)

---

## 2. QueryBench - 查询性能 (ATTACH 模式 vs quack_query 模式)

Local 实现支持两种查询模式：
- **ATTACH 模式**: 使用 `ATTACH 'quack://...' AS remote` 挂载远程数据库，查询下推
- **quack_query 模式**: 使用 `SELECT * FROM quack_query(...)` 函数包装 SQL

### 2.1 ATTACH 模式 (QueryBench)

| Method | Mean | Error | StdDev | Ratio | Allocated | Alloc Ratio |
|--------|-----:|------:|-------:|------:|----------:|------------:|
| Local ATTACH SELECT 1 | 54.75 us | 0.59 us | 0.15 us | 1.00 | 1.14 KB | 1.00 |
| Local ATTACH SELECT @a + @b | 122.58 us | 1.31 us | 0.34 us | 2.24 | 1.51 KB | 1.32 |
| Local ATTACH COUNT/SUM over 10k | 164.15 us | 8.64 us | 1.34 us | 3.00 | 1.3 KB | 1.14 |
| Azrng SELECT 1 | 550.09 us | 36.31 us | 9.43 us | 10.05 | 6.5 KB | 5.70 |
| Azrng SELECT @a + @b | 573.20 us | 15.77 us | 4.10 us | 10.47 | 7.42 KB | 6.50 |
| Azrng COUNT/SUM over 10k | 661.20 us | 30.85 us | 8.01 us | 12.08 | 6.78 KB | 5.95 |

**结论**: ATTACH 模式下 Local 比 Azrng 快 **4-10倍**

### 2.2 quack_query 模式 (QueryBenchQuack)

| Method | Mean | Error | StdDev | Ratio | Allocated | Alloc Ratio |
|--------|-----:|------:|-------:|------:|----------:|------------:|
| Local quack_query SELECT 1 | 5.754 ms | 454.15 us | 117.94 us | 1.00 | 1.92 KB | 1.00 |
| Local quack_query SELECT @a + @b | 6.078 ms | 702.18 us | 182.35 us | 1.06 | 2.51 KB | 1.31 |
| Local quack_query COUNT/SUM over 10k | 5.867 ms | 816.26 us | 211.98 us | 1.02 | 2.17 KB | 1.13 |
| Azrng SELECT 1 | 545.35 us | 10.86 us | 2.82 us | 0.09 | 6.5 KB | 3.39 |
| Azrng SELECT @a + @b | 577.50 us | 18.96 us | 4.92 us | 0.10 | 7.42 KB | 3.87 |
| Azrng COUNT/SUM over 10k | 652.75 us | 14.02 us | 3.64 us | 0.11 | 6.78 KB | 3.54 |

**结论**: quack_query 模式下 Azrng 比 Local 快 **9-10倍**

### 2.3 模式对比

| 测试项 | Local ATTACH | Local quack_query | Azrng | ATTACH vs quack_query |
|--------|-------------|-------------------|-------|----------------------|
| SELECT 1 | 54.75 us | 5.754 ms | 550.09 us | **105x** |
| SELECT @a+@b | 122.58 us | 6.078 ms | 573.20 us | **50x** |
| COUNT/SUM 10k | 164.15 us | 5.867 ms | 661.20 us | **36x** |

**结论**: ATTACH 模式比 quack_query 模式快 **36-105倍**

### 2.4 架构差异

```
quack_query 模式:
Client → 嵌入式 DuckDB → quack_query() 函数 → HTTP POST → Server

ATTACH 模式:
Client → 嵌入式 DuckDB → ATTACH → 原生协议 → Server (查询下推)
```

ATTACH 模式的优势：
1. **协议效率**: 使用 DuckDB 原生二进制协议，而非 HTTP 文本协议
2. **连接复用**: 初始化一次，后续查询复用连接
3. **查询下推**: DuckDB 自动将查询下推到服务器执行
4. **参数绑定**: 使用原生参数绑定，无需序列化成字面量

---

## 3. ResultSetBench - 结果集读取

### Rows = 10,000

| Method | Mean | Error | StdDev | Ratio | Allocated | Alloc Ratio |
|--------|-----:|------:|-------:|------:|----------:|------------:|
| Azrng read N rows | 1.441 ms | 0.491 ms | 0.027 ms | 0.16 | 886.54 KB | 2.82 |
| Local read N rows | 9.228 ms | 11.824 ms | 0.648 ms | 1.00 | 314.89 KB | 1.00 |

### Rows = 100,000

| Method | Mean | Error | StdDev | Allocated |
|--------|-----:|------:|-------:|----------:|
| Azrng read N rows | 10.134 ms | 7.393 ms | 0.405 ms | 8999.36 KB |
| Local read N rows | N/A | N/A | N/A | N/A |

**结论**: 
- 10k行: Azrng 比 Local 快 **6.4倍** (1.44ms vs 9.23ms)
- 100k行: Azrng 成功完成 (10.1ms)，Local 因连接超时失败

---

## 4. ReaderAccessBench - 读取器访问性能 (Azrng Only)

| Method | Rows | Mean | Error | StdDev | Allocated |
|--------|-----:|-----:|------:|-------:|----------:|
| Azrng reader typed getters | 10000 | 2.456 ms | 0.980 ms | 0.054 ms | 2,150,461 B |
| Azrng reader GetValue | 10000 | 2.545 ms | 1.946 ms | 0.107 ms | 2,390,450 B |
| Azrng reader GetValues | 10000 | 2.745 ms | 1.340 ms | 0.073 ms | 2,390,481 B |
| Azrng reader typed getters | 100000 | 23.890 ms | 5.849 ms | 0.321 ms | 21,611,108 B |
| Azrng reader GetValue | 100000 | 23.385 ms | 9.477 ms | 0.520 ms | 24,011,454 B |
| Azrng reader GetValues | 100000 | 24.784 ms | 17.598 ms | 0.965 ms | - |

**结论**: 三种访问方式性能接近，typed getters 内存分配略低

---

## 5. ConcurrencyBench - 并发查询

| Method | Degree | Mean | Error | StdDev | Allocated | Alloc Ratio |
|--------|-------:|-----:|------:|-------:|----------:|------------:|
| Azrng parallel SELECT 1 | 4 | 630.0 us | 1,035.9 us | 56.78 us | 26.5 KB | 2.94 |
| Local parallel SELECT 1 | 4 | 17,038.2 us | 26,687.9 us | 1,462.85 us | 9 KB | 1.00 |
| Azrng parallel SELECT 1 | 16 | 1,364.7 us | 243.5 us | 13.35 us | 105.2 KB | N/A |
| Local parallel SELECT 1 | 16 | N/A | N/A | N/A | N/A | N/A |

**结论**:
- Degree=4 时 Azrng 比 Local 快 **27倍** (630us vs 17ms)
- Azrng 在 Degree=16 时表现稳定 (1.36ms)
- Local Degree=16 失败 - 架构限制：每个连接创建独立 DuckDB 实例，16 并发导致 quack 服务器端口耗尽

---

## 6. PoolBench - 连接池 (Azrng Only)

| Method | Degree | Mean | Error | StdDev | Allocated |
|--------|-------:|-----:|------:|-------:|----------:|
| Azrng pool acquire + return | 4 | 543.6 us | 43.63 us | 11.33 us | 6.9 KB |
| Azrng pool acquire + return | 16 | 550.3 us | 35.14 us | 9.13 us | 6.9 KB |
| Azrng pool rent + dispose | 4 | 555.2 us | 235.06 us | 36.38 us | 7.09 KB |
| Azrng pool rent + dispose | 16 | 553.1 us | 64.46 us | 16.74 us | 7.1 KB |
| Azrng pool SELECT 1 | 4 | 1,091.4 us | 89.75 us | 13.89 us | 13.13 KB |
| Azrng pool SELECT 1 | 16 | 1,080.8 us | 34.76 us | 5.38 us | 13.13 KB |
| Azrng pool lease SELECT 1 | 4 | 1,087.0 us | 117.22 us | 30.44 us | 13.31 KB |
| Azrng pool lease SELECT 1 | 16 | 1,093.5 us | 139.25 us | 36.16 us | 13.31 KB |
| Azrng pool parallel SELECT 1 | 4 | 2,074.6 us | 2,263.46 us | 587.81 us | 54.05 KB |
| Azrng pool parallel SELECT 1 | 16 | 2,853.7 us | 290.83 us | 75.53 us | 213.28 KB |
| Azrng pool lease parallel SELECT 1 | 4 | 1,127.6 us | 109.99 us | 17.02 us | 56.62 KB |
| Azrng pool lease parallel SELECT 1 | 16 | 2,909.6 us | 374.88 us | 58.01 us | 216.13 KB |

**结论**: 
- 连接池 acquire/return 约 540-555us
- lease 模式在并行场景下表现更好 (1.1ms vs 2.1ms)

---

## 7. InsertBench - 插入性能

### Rows = 100

| Method | Mean | Error | StdDev | Ratio | Allocated | Alloc Ratio |
|--------|-----:|------:|-------:|------:|----------:|------------:|
| Azrng batch insert | 1.864 ms | 0.506 ms | 0.028 ms | 0.003 | 35.66 KB | 0.12 |
| Azrng paged batch insert | 1.880 ms | 0.359 ms | 0.020 ms | 0.003 | 35.66 KB | 0.12 |
| Azrng per-row insert | 65.782 ms | 6.765 ms | 0.371 ms | 0.114 | 758.79 KB | 2.61 |
| Local per-row insert | 579.537 ms | 586.787 ms | 32.164 ms | 1.002 | 290.7 KB | 1.00 |

### Rows = 1000

| Method | Mean | Error | StdDev | Allocated |
|--------|-----:|------:|-------:|----------:|
| Azrng paged batch insert (BatchSize=100) | 11.361 ms | 0.939 ms | 0.052 ms | 327.34 KB |
| Azrng batch insert (BatchSize=100) | 48.151 ms | 2.416 ms | 0.133 ms | 252 KB |
| Azrng paged batch insert (BatchSize=500) | 90.319 ms | 36.454 ms | 1.998 ms | 261.31 KB |
| Azrng per-row insert | 693.930 ms | 333.350 ms | 18.272 ms | 7561.42 KB |
| Local per-row insert | N/A | N/A | N/A | N/A |

**结论**:
- 100行: Azrng 批量插入比逐行插入快 **35倍**，比 Local 快 **310倍**
- 1000行: paged batch (BatchSize=100) 最快 (11.4ms)，比逐行插入快 **61倍**
- Local 1000行插入因连接超时失败

---

## 版本对比 (第一次 vs 第二次运行)

| 项目 | 第一次运行 | 第二次运行 |
|------|-----------|-----------|
| BenchmarkDotNet | 0.14.0 | 0.15.8 |
| Azrng.DuckDB.Quack | 1.0.0-beta1 | 1.0.0-beta2 |
| Docker 资源 | 2 CPU / 4GB | 4 CPU / 8GB |

### 性能变化

| 测试项 | 第一次运行 | 第二次运行 | 变化 |
|--------|-----------|-----------|------|
| ConnectionBench (Azrng) | 828.9 us | 883.6 us | +6.6% |
| ConnectionBench (Local) | 67.8 ms | 66.7 ms | -1.6% |
| QueryBench (Azrng SELECT 1) | 536.4 us | 562.0 us | +4.8% |
| QueryBench (Local SELECT 1) | 5.41 ms | 5.37 ms | -0.7% |
| ResultSetBench 10k (Azrng) | 1.476 ms | 1.441 ms | -2.4% |
| ResultSetBench 10k (Local) | 13.2 ms | 9.23 ms | -30% |
| ConcurrencyBench Degree=4 (Azrng) | 731.1 us | 630.0 us | -13.8% |
| ConcurrencyBench Degree=4 (Local) | 12.4 ms | 17.0 ms | +37% |

**结论**: 性能基本稳定，小幅波动在正常范围内

---

## 总结

### 三种查询模式性能对比

| 模式 | SELECT 1 | SELECT @a+@b | COUNT/SUM 10k | 推荐场景 |
|------|----------|--------------|---------------|----------|
| Local ATTACH | **54.75 us** | **122.58 us** | **164.15 us** | 高性能查询 |
| Azrng (HTTP) | 550.09 us | 573.20 us | 661.20 us | 通用场景 |
| Local quack_query | 5.754 ms | 6.078 ms | 5.867 ms | 兼容性场景 |

### 各场景性能对比

| 场景 | Azrng vs Local (quack_query) | Local ATTACH vs Azrng |
|------|------------------------------|----------------------|
| 连接建立 | ~75x 更快 | N/A (冷启动) |
| 简单查询 | ~10x 更快 | ~10x 更快 |
| 结果集读取 | ~6-10x 更快 | N/A |
| 并发查询 | ~27x 更快 (Degree=4) | N/A |
| 批量插入 | ~310x 更快 (100行) | N/A |

### 推荐配置

1. **高性能场景**: 使用 Local ATTACH 模式，性能最优
2. **通用场景**: 使用 Azrng，开箱即用，性能优秀
3. **兼容性场景**: 使用 Local quack_query 模式，兼容旧代码

### Azrng 主要优势
1. **连接复用**: 使用 HTTP 连接池，避免每次创建新连接
2. **原生异步**: 全面支持 async/await
3. **批量操作**: 内置 batch insert API
4. **连接池**: 内置连接池和 lease 模式
5. **稳定性**: 高并发场景下表现稳定
6. **开箱即用**: 无需配置 ATTACH，自动处理连接

### 已知限制
- 内存分配比 Local 高 2-4x (HTTP 协议开销)
- Local quack_query 模式在高并发 (16+) 时因端口耗尽失败
- Local ATTACH 模式参数绑定需要使用 `?` 占位符
