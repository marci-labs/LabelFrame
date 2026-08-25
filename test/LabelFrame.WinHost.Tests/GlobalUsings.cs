global using Xunit;
// CI 高负载下并行测试类争抢 SQLite I/O 导致 Worker 节流等时序测试抖动，关闭程序集级并行（串行更确定）。
