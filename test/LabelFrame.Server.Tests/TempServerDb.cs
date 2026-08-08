using LabelFrame.Server;

namespace LabelFrame.Server.Tests;

/// <summary>测试用临时 Server 存储 + 业务服务。</summary>
public sealed class TempServer : IDisposable
{
    public TempServer()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lfsrv-{Guid.NewGuid():N}.db");
        Db = new ServerDb(Path);
        Db.InitializeAsync().GetAwaiter().GetResult();
        Service = new ServerService(Db);
    }

    public string Path { get; }

    public ServerDb Db { get; }

    public ServerService Service { get; }

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