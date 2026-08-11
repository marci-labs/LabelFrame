using LabelFrame.Core.Jobs;
using LabelFrame.WinHost.Api;

namespace LabelFrame.WinHost.Tests.Jobs;

public class JobViewListTests
{
    [Fact]
    public void JobViews_From_should_include_history_columns()
    {
        var created = new DateTimeOffset(2026, 8, 11, 8, 0, 0, TimeSpan.Zero);
        var job = new LabelJob
        {
            Id = "job-1",
            RequestId = "req-1",
            CreatedAt = created,
            Items =
            [
                new LabelJobItem { Id = "i1", JobId = "job-1", Index = 0, Status = LabelJobItemStatus.Failed, Zpl = "x", ErrorCode = "LF_IO_001", ErrorMessage = "发送失败" },
                new LabelJobItem { Id = "i2", JobId = "job-1", Index = 1, Status = LabelJobItemStatus.Completed, Zpl = "y" },
            ],
        };

        var view = JobViews.From(job);

        Assert.Equal(created, view.CreatedAt);
        Assert.Equal(1, view.FailedItems);
        Assert.Equal("发送失败", view.ErrorMessage);
        Assert.Null(view.TargetDeviceId);
        Assert.Equal(2, view.TotalItems);
        Assert.Equal(1, view.CompletedItems);
    }

    [Fact]
    public async Task Store_ListRecentAsync_should_return_newest_first_with_limit()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-jobs-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteLabelJobStore(dbPath);
            await store.InitializeAsync();
            await store.CreateJobAsync(new LabelJob
            {
                Id = "j1",
                RequestId = "r1",
                CreatedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                Items = [],
            });
            await store.CreateJobAsync(new LabelJob
            {
                Id = "j2",
                RequestId = "r2",
                CreatedAt = new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero),
                Items = [],
            });
            await store.CreateJobAsync(new LabelJob
            {
                Id = "j3",
                RequestId = "r3",
                CreatedAt = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero),
                Items = [],
            });

            var recent = await store.ListRecentAsync(2);

            Assert.Equal(2, recent.Count);
            Assert.Equal("j3", recent[0].Id);
            Assert.Equal("j2", recent[1].Id);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }
}
