using SQLitePCL;

namespace LabelFrame.Core.Jobs;

/// <summary>SQLite 运行库初始化（仅一次）。</summary>
internal static class SqliteSupport
{
    private static int _initialized;

    /// <summary>确保 SQLitePCLRaw 的 e_sqlite3 provider 已设置。</summary>
    public static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            raw.SetProvider(new SQLite3Provider_e_sqlite3());
        }
    }
}