using BenchmarkDotNet.Running;

// 渲染 / 编码 / Excel 热路径基准（迭代 33）：
//   dotnet run -c Release --project test/LabelFrame.Benchmarks
//   快速冒烟（缩短运行时间）：-- --filter * --job short
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
