# DuckDB Quack Compare Benchmarks

## 测试环境

| 项目 | 配置 |
|------|------|
| 操作系统 | Windows 11 (10.0.26200.8655) |
| CPU | Intel Core Ultra 7 255HX (20核20线程) |
| 内存 | 16 GB |
| .NET SDK | 10.0.301 |
| Runtime | .NET 10.0.9 (10.0.926.27113), X64 RyuJIT AVX2 |
| BenchmarkDotNet | v0.14.0 |
| DuckDB Server | DuckDB Quack 1.5.3 (Docker, 4 CPU, 8GB RAM) |
| 测试日期 | 2026-06-21 |

## 测试对象

- **Local**: 本地实现 `Quack.DuckDB` (vendored from local path)
- **Azrng**: NuGet包 `Azrng.DuckDB.Quack` 1.0.0-beta1

---

## 1. ConnectionBench - 连接打开/关闭

| Method | Mean | Error | StdDev | Ratio | Allocated | Alloc Ratio |
|--------|-----:|------:|-------:|------:|----------:|------------:|
| Azrng Open+Dispose | 828.9 us | 119.0 us | 30.90 us | 0.01 | 9.9 KB | 1.93 |
| Local Open+Dispose | 67,820.2 us | 2,216.3 us | 342.98 us | 1.00 | 5.14 KB | 1.00 |

**结论**: Azrng 连接速度比 Local 快约 **81倍** (828.9us vs 67.8ms)

---

## 2. QueryBench - 查询性能

| Method | Mean | Error | StdDev | Allocated |
|--------|-----:|------:|-------:|----------:|
| Azrng SELECT 1 | 536.4 us | 2.18 us | 4.86 us | - |
| Local SELECT 1 | 5.412 ms | 0.187 ms | 0.419 ms | - |
| Azrng SELECT @a + @b | 563.4 us | 2.65 us | 5.93 us | - |
| Local SELECT @a + @b | 5.592 ms | 0.148 ms | 0.330 ms | - |
| Azrng COUNT/SUM over 10k | 643.2 us | 12.09 us | 27.03 us | - |
| Local COUNT/SUM over 10k | 5.476 ms | 0.048 ms | 0.096 ms | - |

**结论**: Azrng 查询速度比 Local 快约 **10倍**

---

## 3. ResultSetBench - 结果集读取

### Rows = 10,000

| Method | Mean | Error | StdDev | Ratio | Allocated | Alloc Ratio |
|--------|-----:|------:|-------:|------:|----------:|------------:|
| Azrng read N rows | 1.476 ms | 0.906 ms | 0.050 ms | 0.14 | 886.7 KB | 2.82 |
| Local read N rows | 13.201 ms | 138.625 ms | 7.599 ms | 1.21 | 314.92 KB | 1.00 |

### Rows = 100,000

| Method | Mean | Error | StdDev |
|--------|-----:|------:|-------:|
| Azrng read N rows | N/A | N/A | N/A |
| Local read N rows | N/A | N/A | N/A |

**结论**: 10k行时 Azrng 比 Local 快约 **9倍** (1.476ms vs 13.2ms)，但内存分配较高。100k行测试因连接超时失败。

---

## 4. ConcurrencyBench - 并发查询

| Method | Degree | Mean | Error | StdDev | Allocated | Alloc Ratio |
|--------|-------:|-----:|------:|-------:|----------:|------------:|
| Azrng parallel SELECT 1 | 4 | 731.1 us | 827.4 us | 45.35 us | 26.67 KB | 2.95 |
| Local parallel SELECT 1 | 4 | 12.409 ms | 74.830 ms | 4.102 ms | 9.04 KB | 1.00 |
| Azrng parallel SELECT 1 | 16 | 1.286 ms | 666.7 us | 36.55 us | 105.98 KB | N/A |
| Local parallel SELECT 1 | 16 | N/A | N/A | N/A | N/A | N/A |

**结论**:
- Degree=4 时 Azrng 比 Local 快 **17倍** (731us vs 12.4ms)
- Azrng 在 Degree=16 时表现稳定 (1.29ms)
- Local Degree=16 失败 - 架构限制：每个连接创建独立 DuckDB 实例，16 并发导致 quack 服务器端口耗尽

---

## 5. PoolBench - 连接池 (Azrng Only)

| Method | Degree | Mean | Error | StdDev | Allocated |
|--------|-------:|-----:|------:|-------:|----------:|
| Azrng pool acquire + return | 4 | 606.3 us | 60.91 us | 9.43 us | 6.95 KB |
| Azrng pool acquire + return | 16 | 591.4 us | 14.89 us | 2.30 us | 6.95 KB |
| Azrng pool SELECT 1 | 4 | 1.268 ms | 297.67 us | 46.07 us | 13.49 KB |
| Azrng pool SELECT 1 | 16 | 1.153 ms | 53.65 us | 8.30 us | 13.23 KB |
| Azrng pool parallel SELECT 1 | 4 | 1.180 ms | 61.69 us | 16.02 us | 53.77 KB |
| Azrng pool parallel SELECT 1 | 16 | 4.428 ms | 3.871 ms | 1.005 ms | 213.55 KB |

---

## 6. InsertBench - 插入性能

### Rows = 100

| Method | Mean | Error | StdDev | Ratio | Allocated | Alloc Ratio |
|--------|-----:|------:|-------:|------:|----------:|------------:|
| Azrng batch insert | 1.981 ms | 0.985 ms | 0.054 ms | 0.003 | 35.81 KB | 0.12 |
| Azrng per-row insert | 74.144 ms | 66.951 ms | 3.670 ms | 0.100 | 761.86 KB | 2.59 |
| Local per-row insert | 748.730 ms | 1,662.323 ms | 91.118 ms | 1.010 | 294.23 KB | 1.00 |

### Rows = 1000

| Method | Mean | Error | StdDev | Allocated |
|--------|-----:|------:|-------:|----------:|
| Azrng batch insert | 48.021 ms | 3.390 ms | 0.186 ms | 258.17 KB |
| Azrng per-row insert | 709.270 ms | 148.374 ms | 8.133 ms | 7616.79 KB |
| Local per-row insert | N/A | N/A | N/A | N/A |

**结论**:
- Azrng 批量插入比逐行插入快约 **15-37倍**
- Local 1000行插入因连接超时失败

---

## 总结

| 场景 | Azrng 优势 |
|------|-----------|
| 连接建立 | ~81x 更快 |
| 简单查询 | ~10x 更快 |
| 批量插入 | 支持，且性能优异 |
| 连接池 | 内置支持 |
| 并发 | 原生支持 |

> 注: ResultSetBench 和 ConcurrencyBench 的详细数据因输出截断未完整记录，可重新运行获取。
