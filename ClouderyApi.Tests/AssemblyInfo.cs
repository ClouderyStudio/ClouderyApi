using Xunit;

// 集成测试共用同一个 MySQL 实例，并依赖进程级环境变量注入连接串；
// 串行执行避免不同测试类互相覆盖配置或争抢同一份库。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
