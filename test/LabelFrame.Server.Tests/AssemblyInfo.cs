using Xunit;

// 集成与性能测试通过进程环境变量隔离数据库路径，类间并行会互相覆盖。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
