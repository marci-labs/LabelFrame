using System.Diagnostics;
using LabelFrame.Core.Data;
using LabelFrame.Core.Jobs;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace LabelFrame.Core.Tests.Jobs;

public class ClaimStagesPerfTests(ITestOutputHelper output)
{
    private const string CandidatesSql = """
        SELECT id, created_at FROM jobs
        WHERE status IN ('Pending', 'Printing')
          AND EXISTS (SELECT 1 FROM job_items WHERE job_id = jobs.id AND status = 'Pending');
        """;
    private const string PayloadSql = """
        SELECT id, item_index, zpl, command_bytes FROM job_items
        WHERE job_id = $jobId AND status = 'Pending' ORDER BY item_index LIMIT 1;
        """;

    [Fact]
    public async Task Measure_claim_transaction_stages_and_query_plans()
    {
        for (var round = 1; round <= 4; round++)
        {
            foreach (var payloadLength in round % 2 == 0 ? new[] { 210000, 64 } : new[] { 64, 210000 })
            {
                await Measure(round, payloadLength);
            }
        }

        async Task Measure(int round, int payloadLength)
        {
            using var db = new TempJobDb();
            var payload = new string('A', payloadLength);
            var (job, _) = await db.Queue.SubmitAsync("claim-stages", Enumerable.Repeat(payload, 200).ToArray());
            var stages = new Dictionary<string, long>();
            for (var index = 0; index < 200; index++)
            {
                var start = Stopwatch.GetTimestamp();
                await using (var connection = await SqliteSupport.OpenAsync(SqliteSupport.BuildConnectionString(db.Path)))
                {
                    Add("open", start);
                    if (round == 1 && index == 0)
                    {
                        foreach (var sql in new[] { CandidatesSql, PayloadSql })
                        {
                            await using var explain = connection.CreateCommand();
                            explain.CommandText = "EXPLAIN QUERY PLAN " + sql;
                            if (sql == PayloadSql) explain.Parameters.AddWithValue("$jobId", job.Id);
                            await using var plan = await explain.ExecuteReaderAsync();
                            while (await plan.ReadAsync()) output.WriteLine($"plan: {plan.GetString(3)}");
                        }
                    }
                    start = Stopwatch.GetTimestamp();
                    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
                    Add("begin", start);
                    start = Stopwatch.GetTimestamp();
                    var candidates = new List<(string Id, DateTimeOffset CreatedAt)>();
                    await using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = CandidatesSql;
                        await using var reader = await command.ExecuteReaderAsync();
                        while (await reader.ReadAsync()) candidates.Add((reader.GetString(0), SqliteSupport.Parse(reader.GetString(1))));
                    }
                    var jobId = candidates.OrderBy(j => j.CreatedAt).ThenBy(j => j.Id).Select(j => j.Id).FirstOrDefault();
                    Add("candidates", start);
                    Assert.Equal(job.Id, jobId);
                    start = Stopwatch.GetTimestamp();
                    LabelJobItem item;
                    await using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = PayloadSql;
                        command.Parameters.AddWithValue("$jobId", jobId);
                        await using var reader = await command.ExecuteReaderAsync();
                        Assert.True(await reader.ReadAsync());
                        item = new LabelJobItem
                        {
                            Id = reader.GetString(0), JobId = jobId!, Index = reader.GetInt32(1),
                            Zpl = reader.GetString(2), CommandBytes = reader.IsDBNull(3) ? null : (byte[])reader[3],
                            Status = LabelJobItemStatus.Printing,
                        };
                    }
                    Add("payload", start);
                    Assert.Equal(index, item.Index);
                    Assert.Equal(payload, item.Zpl);
                    Assert.Null(item.CommandBytes);
                    start = Stopwatch.GetTimestamp();
                    await using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = """
                            UPDATE job_items SET status = 'Printing', error_code = NULL, error_message = NULL,
                                updated_at = $updatedAt WHERE id = $itemId AND job_id = $jobId;
                            UPDATE jobs SET status = 'Printing', updated_at = $updatedAt WHERE id = $jobId;
                            """;
                        command.Parameters.AddWithValue("$updatedAt", SqliteSupport.Format(DateTimeOffset.UtcNow));
                        command.Parameters.AddWithValue("$itemId", item.Id);
                        command.Parameters.AddWithValue("$jobId", jobId);
                        Assert.Equal(2, await command.ExecuteNonQueryAsync());
                    }
                    Add("updates", start);
                    start = Stopwatch.GetTimestamp();
                    await transaction.CommitAsync();
                    Add("commit", start);
                }
                await db.Queue.CompleteItemWithoutResultAsync(job.Id, job.Items[index].Id);
            }
            Assert.Null(await db.Queue.ClaimNextItemAsync());
            var final = (await db.Queue.GetAsync(job.Id))!;
            Assert.Equal(LabelJobStatus.Completed, final.Status);
            Assert.All(final.Items, item =>
            {
                Assert.Equal(LabelJobItemStatus.Completed, item.Status);
                Assert.Equal(payload, item.Zpl);
            });
            output.WriteLine($"round={round}; count=200; payloadLength={payloadLength}; " + string.Join("; ", stages.Select(s => $"{s.Key}Ms={s.Value * 1000d / Stopwatch.Frequency:F2}")));

            void Add(string name, long start)
                => stages[name] = stages.GetValueOrDefault(name) + Stopwatch.GetTimestamp() - start;
        }
    }
}
