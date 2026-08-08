using LabelFrame.Core.Jobs;

namespace LabelFrame.Core.Tests.Jobs;

public class LabelJobQueueTests
{
    [Fact]
    public async Task Submit_should_create_pending_job_with_items_in_order()
    {
        using var db = new TempJobDb();

        var (job, created) = await db.Queue.SubmitAsync("req-1", ["zpl-0", "zpl-1", "zpl-2"]);

        Assert.True(created);
        Assert.Equal(LabelJobStatus.Pending, job.Status);
        Assert.Equal(3, job.Items.Count);
        Assert.Equal(["zpl-0", "zpl-1", "zpl-2"], job.Items.Select(i => i.Zpl));
        Assert.Equal([0, 1, 2], job.Items.Select(i => i.Index));
        Assert.All(job.Items, i => Assert.Equal(LabelJobItemStatus.Pending, i.Status));
    }

    [Fact]
    public async Task Submit_same_request_id_should_return_existing_job()
    {
        using var db = new TempJobDb();

        var (first, created1) = await db.Queue.SubmitAsync("req-idem", ["zpl-0"]);
        var (second, created2) = await db.Queue.SubmitAsync("req-idem", ["zpl-1", "zpl-2"]);

        Assert.True(created1);
        Assert.False(created2);
        Assert.Equal(first.Id, second.Id);
        Assert.Single(second.Items);
    }

    [Fact]
    public async Task Claim_should_return_items_in_batch_order()
    {
        using var db = new TempJobDb();
        await db.Queue.SubmitAsync("req-order", ["zpl-0", "zpl-1", "zpl-2"]);

        var first = await db.Queue.ClaimNextItemAsync();
        var second = await db.Queue.ClaimNextItemAsync();
        var third = await db.Queue.ClaimNextItemAsync();
        var none = await db.Queue.ClaimNextItemAsync();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(third);
        Assert.Null(none);
        Assert.Equal(0, first!.Value.Item.Index);
        Assert.Equal(1, second!.Value.Item.Index);
        Assert.Equal(2, third!.Value.Item.Index);
        Assert.Equal(LabelJobItemStatus.Printing, third.Value.Item.Status);
        Assert.Equal(LabelJobStatus.Printing, (await db.Queue.GetAsync(first.Value.JobId))!.Status);
    }

    [Fact]
    public async Task Complete_all_items_should_mark_job_completed()
    {
        using var db = new TempJobDb();
        await db.Queue.SubmitAsync("req-done", ["zpl-0", "zpl-1"]);

        var (jobId, item0) = (await db.Queue.ClaimNextItemAsync())!.Value;
        await db.Queue.CompleteItemAsync(jobId, item0.Id);
        var (_, item1) = (await db.Queue.ClaimNextItemAsync())!.Value;
        var job = await db.Queue.CompleteItemAsync(jobId, item1.Id);

        Assert.Equal(LabelJobStatus.Completed, job.Status);
        Assert.All(job.Items, i => Assert.Equal(LabelJobItemStatus.Completed, i.Status));
    }

    [Fact]
    public async Task Fail_mid_batch_should_suspend_and_resume_continues_remaining()
    {
        using var db = new TempJobDb();
        await db.Queue.SubmitAsync("req-suspend", ["zpl-0", "zpl-1", "zpl-2"]);

        var (jobId, item0) = (await db.Queue.ClaimNextItemAsync())!.Value;
        await db.Queue.CompleteItemAsync(jobId, item0.Id);
        var (_, item1) = (await db.Queue.ClaimNextItemAsync())!.Value;

        var suspended = await db.Queue.FailItemAsync(jobId, item1.Id, JobErrorCodes.TransportSendFailed, "打印机离线");

        Assert.Equal(LabelJobStatus.Suspended, suspended.Status);
        Assert.Equal(LabelJobItemStatus.Failed, suspended.Items[1].Status);
        Assert.Equal("打印机离线", suspended.Items[1].ErrorMessage);
        Assert.Equal(LabelJobItemStatus.Pending, suspended.Items[2].Status);
        // 失败项不重打
        Assert.Equal(LabelJobItemStatus.Completed, suspended.Items[0].Status);

        // 挂起时 Worker 不取新 Item
        Assert.Null(await db.Queue.ClaimNextItemAsync());
        var resumed = await db.Queue.ResumeAsync(jobId);
        Assert.Equal(LabelJobStatus.Pending, resumed.Status);

        var next = await db.Queue.ClaimNextItemAsync();
        Assert.NotNull(next);
        Assert.Equal(2, next!.Value.Item.Index);

        var completed = await db.Queue.CompleteItemAsync(jobId, next.Value.Item.Id);
        // 批内存在失败项：作业结束为 Failed（失败项单独重打在迭代 6）
        Assert.Equal(LabelJobStatus.Failed, completed.Status);
        Assert.Equal(LabelJobItemStatus.Completed, completed.Items[0].Status);
        Assert.Equal(LabelJobItemStatus.Failed, completed.Items[1].Status);
        Assert.Equal(LabelJobItemStatus.Completed, completed.Items[2].Status);
    }

    [Fact]
    public async Task Cancel_should_mark_remaining_items_cancelled()
    {
        using var db = new TempJobDb();
        await db.Queue.SubmitAsync("req-cancel", ["zpl-0", "zpl-1", "zpl-2"]);
        var job = await db.Store.GetJobByRequestIdAsync("req-cancel");

        var cancelled = await db.Queue.CancelAsync(job!.Id);

        Assert.Equal(LabelJobStatus.Cancelled, cancelled.Status);
        Assert.All(cancelled.Items, i => Assert.Equal(LabelJobItemStatus.Cancelled, i.Status));
    }

    [Fact]
    public async Task Restart_should_preserve_job_and_reset_inflight_item()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftest-restart-{Guid.NewGuid():N}.db");
        try
        {
            string jobId;
            string itemId;
            {
                var store1 = new SqliteLabelJobStore(dbPath);
                await store1.InitializeAsync();
                var first = new LabelJobQueue(store1);
                var (job, _) = await first.SubmitAsync("req-restart", ["zpl-0", "zpl-1"]);
                jobId = job.Id;
                var claimed = await first.ClaimNextItemAsync();
                itemId = claimed!.Value.Item.Id;
                // 模拟进程退出：不调用 Complete，直接释放
            }

            var store2 = new SqliteLabelJobStore(dbPath);
            await store2.InitializeAsync();
            var second = new LabelJobQueue(store2);

            var persisted = await second.GetAsync(jobId);
            Assert.NotNull(persisted);
            Assert.Equal(LabelJobStatus.Printing, persisted!.Status);
            Assert.Equal(LabelJobItemStatus.Printing, persisted.Items[0].Status);

            await second.MarkInterruptedJobsSuspendedAsync();

            var interrupted = await second.GetAsync(jobId);
            Assert.Equal(LabelJobStatus.Suspended, interrupted!.Status);
            Assert.Equal(LabelJobItemStatus.Pending, interrupted.Items[0].Status);

            await second.ResumeAsync(jobId);
            var next = await second.ClaimNextItemAsync();
            Assert.NotNull(next);
            Assert.Equal(0, next!.Value.Item.Index);
            Assert.Equal(itemId, next.Value.Item.Id);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task Invalid_transitions_should_throw_problem_code()
    {
        using var db = new TempJobDb();
        var (job, _) = await db.Queue.SubmitAsync("req-trans", ["zpl-0"]);
        var claimed = await db.Queue.ClaimNextItemAsync();
        await db.Queue.CompleteItemAsync(job.Id, claimed!.Value.Item.Id);

        var exception = await Assert.ThrowsAsync<LabelJobException>(() => db.Queue.ResumeAsync(job.Id));
        Assert.Equal(JobErrorCodes.InvalidTransition, exception.Code);
        exception = await Assert.ThrowsAsync<LabelJobException>(() => db.Queue.SuspendAsync(job.Id));
        Assert.Equal(JobErrorCodes.InvalidTransition, exception.Code);
        exception = await Assert.ThrowsAsync<LabelJobException>(() => db.Queue.CancelAsync(job.Id));
        Assert.Equal(JobErrorCodes.InvalidTransition, exception.Code);
    }

    [Fact]
    public async Task Retry_failed_item_should_set_pending_and_reprint()
    {
        using var db = new TempJobDb();
        await db.Queue.SubmitAsync("req-retry", ["zpl-0", "zpl-1"]);
        var (jobId, item0) = (await db.Queue.ClaimNextItemAsync())!.Value;
        await db.Queue.CompleteItemAsync(jobId, item0.Id);
        var (_, item1) = (await db.Queue.ClaimNextItemAsync())!.Value;
        await db.Queue.FailItemAsync(jobId, item1.Id, JobErrorCodes.TransportSendFailed, "离线");

        var retried = await db.Queue.RetryItemAsync(jobId, 1);

        Assert.Equal(LabelJobStatus.Pending, retried.Status);
        Assert.Equal(LabelJobItemStatus.Pending, retried.Items[1].Status);
        Assert.Null(retried.Items[1].ErrorMessage);

        var next = await db.Queue.ClaimNextItemAsync();
        Assert.NotNull(next);
        Assert.Equal(1, next!.Value.Item.Index);
        await db.Queue.CompleteItemAsync(jobId, next.Value.Item.Id);
        Assert.Equal(LabelJobStatus.Completed, (await db.Queue.GetAsync(jobId))!.Status);
    }

    [Fact]
    public async Task Retry_non_failed_item_should_throw()
    {
        using var db = new TempJobDb();
        await db.Queue.SubmitAsync("req-retry-bad", ["zpl-0"]);

        var exception = await Assert.ThrowsAsync<LabelJobException>(async () => await db.Queue.RetryItemAsync((await db.Store.GetJobByRequestIdAsync("req-retry-bad"))!.Id, 0));

        Assert.Equal(JobErrorCodes.InvalidTransition, exception.Code);
    }

    [Fact]
    public async Task Missing_job_should_throw_job_not_found()
    {
        using var db = new TempJobDb();

        var exception = await Assert.ThrowsAsync<LabelJobException>(() => db.Queue.SuspendAsync("missing-job"));

        Assert.Equal(JobErrorCodes.JobNotFound, exception.Code);
    }
}