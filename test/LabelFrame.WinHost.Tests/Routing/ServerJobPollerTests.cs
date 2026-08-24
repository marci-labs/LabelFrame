using System.Net;
using LabelFrame.Core.Layout;
using LabelFrame.WinHost.Routing;

namespace LabelFrame.WinHost.Tests.Routing;

public class ServerJobPollerTests
{
    [Fact]
    public async Task RegisterAsync_should_post_device()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(new { deviceId = "dev-1", name = "dev-1", status = "Online" }));
        var poller = new ServerJobPoller(new HttpClient(handler) { BaseAddress = new Uri("http://server") }, "http://server", "dev-1");

        await poller.RegisterAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/devices", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task FetchPendingAsync_should_parse_claimed_jobs()
    {
        var payload = new
        {
            jobId = "job-1",
            requestId = "req-1",
            totalItems = 2,
            payload = new
            {
                template = new
                {
                    contract = new { name = "location-label", version = "1.0", fields = System.Array.Empty<object>() },
                    layout = new
                    {
                        name = "l", contractName = "location-label", contractVersion = "1.0", widthMm = 100, heightMm = 60,
                        elements = new object[]
                        {
                            new { type = "text", sourceKey = "zone", xMm = 5, yMm = 4, fontHeightMm = 5, fontWidthMm = 5 },
                            new { type = "barcode", sourceKey = "locationCode", xMm = 5, yMm = 26, heightMm = 22, moduleWidth = 2 },
                        },
                    },
                },
                labels = new object[]
                {
                    new { data = new Dictionary<string, string> { ["zone"] = "A-01", ["locationCode"] = "A-01-02-03" } },
                },
            },
        };
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(new[] { payload }));
        var poller = new ServerJobPoller(new HttpClient(handler), "http://server", "dev-1");

        var jobs = await poller.FetchPendingAsync();

        var job = Assert.Single(jobs);
        Assert.Equal("job-1", job.JobId);
        Assert.Equal("req-1", job.RequestId);
        Assert.Equal(2, job.TotalItems);
        Assert.IsType<LabelBarcodeElement>(job.Template.Layout!.Elements[1]);
        Assert.Equal("A-01-02-03", job.Labels[0].Data!["locationCode"]);
    }

    [Fact]
    public async Task WaitForJobAsync_should_query_notify_endpoint()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(new { hasPending = true }));
        var poller = new ServerJobPoller(new HttpClient(handler), "http://server", "dev-1");

        var signaled = await poller.WaitForJobAsync(TimeSpan.FromSeconds(20));

        Assert.True(signaled);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/devices/dev-1/jobs/notify", request.RequestUri!.AbsolutePath);
        Assert.Contains("timeout=20", request.RequestUri!.Query);
    }

    [Fact]
    public async Task WaitForJobAsync_should_return_false_on_timeout_response()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(new { hasPending = false }));
        var poller = new ServerJobPoller(new HttpClient(handler), "http://server", "dev-1");

        var signaled = await poller.WaitForJobAsync(TimeSpan.FromSeconds(20));

        Assert.False(signaled);
    }

    [Fact]
    public async Task ReportResultAsync_should_post_result_to_correct_url()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(new { status = "Completed" }));
        var poller = new ServerJobPoller(new HttpClient(handler), "http://server", "dev-1");

        await poller.ReportResultAsync("job-9", new ServerJobResult("Completed", 2, 0, null));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/devices/dev-1/jobs/job-9/result", request.RequestUri!.AbsolutePath);
    }
}