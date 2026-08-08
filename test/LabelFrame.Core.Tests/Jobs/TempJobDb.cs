using LabelFrame.Core.Jobs;

namespace LabelFrame.Core.Tests.Jobs;

/// <summary>测试用临时 SQLite 作业库，释放时删除数据库文件。</summary>
public sealed class TempJobDb : IDisposable
{
    public TempJobDb()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftest-{Guid.NewGuid():N}.db");
        Store = new SqliteLabelJobStore(Path);
        Store.InitializeAsync().GetAwaiter().GetResult();
        Queue = new LabelJobQueue(Store);
    }

    public string Path { get; }

    public SqliteLabelJobStore Store { get; }

    public LabelJobQueue Queue { get; }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch
        {
            // 测试清理失败不影响结果
        }
    }
}