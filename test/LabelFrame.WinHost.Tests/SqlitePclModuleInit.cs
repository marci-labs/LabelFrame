using System.Runtime.CompilerServices;
using SQLitePCL;

namespace LabelFrame.WinHost.Tests;

/// <summary>
/// 测试进程启动时一次性初始化 SQLitePCLRaw（e_sqlite3 provider）。
/// 避免并行测试首次使用 SQLite 存储时出现 Provider 未设置的竞态（CI 偶发）。
/// </summary>
internal static class SqlitePclModuleInit
{
    [ModuleInitializer]
    public static void Initialize()
    {
        raw.SetProvider(new SQLite3Provider_e_sqlite3());
    }
}
