using LabelFrame.Core.Logs;

namespace LabelFrame.WinHost.Tests.Logs;

public class SqliteLogStoreTests
{
    [Fact]
    public async Task Append_and_query_should_round_trip()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-log-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteLogStore(dbPath);
            await store.InitializeAsync();
            await store.AppendAsync("pda-1", ["打印完成", "第 1 张 OK"], CancellationToken.None);

            var entries = await store.QueryAsync("pda-1", null, CancellationToken.None);

            var entry = Assert.Single(entries);
            Assert.Equal("pda-1", entry.DeviceId);
            Assert.Contains("打印完成", entry.Line);
            Assert.Contains("第 1 张 OK", entry.Line);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public async Task Query_without_device_should_return_all()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-log-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteLogStore(dbPath);
            await store.InitializeAsync();
            await store.AppendAsync("pda-1", ["A"], CancellationToken.None);
            await store.AppendAsync("pda-2", ["B"], CancellationToken.None);

            var entries = await store.QueryAsync(null, null, CancellationToken.None);

            Assert.Equal(2, entries.Count);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }
}
