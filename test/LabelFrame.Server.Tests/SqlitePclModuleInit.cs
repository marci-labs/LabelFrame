using System.Runtime.CompilerServices;
using SQLitePCL;

namespace LabelFrame.Server.Tests;

/// <summary>
/// 测试进程启动时一次性初始化 SQLitePCLRaw（e_sqlite3 provider）。
/// 避免并行测试首次构造 ServerDb 时出现 Provider 未设置的竞态（CI 偶发）。
/// </summary>
internal static class SqlitePclModuleInit
{
    [ModuleInitializer]
    public static void Initialize()
    {
        raw.SetProvider(new SQLite3Provider_e_sqlite3());
    }
}
