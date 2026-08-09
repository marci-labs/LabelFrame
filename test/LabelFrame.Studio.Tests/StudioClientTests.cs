using System.Net;
using System.Net.Http;
using System.Text;
using LabelFrame.Studio.Services;

namespace LabelFrame.Studio.Tests;

public class StudioClientTests
{
    private static StudioClient CreateClient(FakeHttpMessageHandler handler)
        => new(new HttpClient(handler) { BaseAddress = new Uri("http://winhost/") });

    [Fact]
    public async Task GetHealthAsync_should_parse_transport()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(new { service = "LabelFrame.WinHost", status = "ok", transport = "Zebra" }));
        var client = CreateClient(handler);

        var health = await client.GetHealthAsync();

        Assert.Equal("LabelFrame.WinHost", health.Service);
        Assert.Equal("Zebra", health.Transport);
    }

    [Fact]
    public async Task ListTemplatesAsync_should_parse_summaries()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(new[]
        {
            new { name = "location-label", group = "项目A", updatedAt = "2026-08-09T00:00:00+00:00" },
        }));
        var client = CreateClient(handler);

        var templates = await client.ListTemplatesAsync();

        var item = Assert.Single(templates);
        Assert.Equal("location-label", item.Name);
        Assert.Equal("项目A", item.Group);
    }

    [Fact]
    public async Task ImportTemplateAsync_should_post_multipart_and_return_name()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("location-label"));
        var client = CreateClient(handler);

        var name = await client.ImportTemplateAsync(new byte[] { 1, 2, 3 }, "loc.lfpkg");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/templates/import", request.RequestUri!.AbsolutePath);
        Assert.Equal("location-label", name);
    }

    [Fact]
    public async Task PreviewAsync_should_return_png_bytes()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(png),
        });
        var client = CreateClient(handler);

        var bytes = await client.PreviewAsync("location-label", new Dictionary<string, string> { ["zone"] = "A-01" });

        Assert.Equal(png, bytes);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/templates/location-label/preview", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task SubmitJobAsync_should_post_and_parse_job_view()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(new
        {
            jobId = "job-1",
            requestId = "req-1",
            status = "Pending",
            totalItems = 2,
            completedItems = 0,
            items = new object[] { },
        }));
        var client = CreateClient(handler);

        var job = await client.SubmitJobAsync(
            "req-1",
            new TemplateSaveDto("location-label", "项目A", null, null),
            [new Dictionary<string, string> { ["zone"] = "A-01" }]);

        Assert.Equal("job-1", job.JobId);
        Assert.Equal("Pending", job.Status);
        Assert.Equal(2, job.TotalItems);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/jobs", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task SubmitJobAsync_should_throw_with_server_error_body()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"code":"LF_VAL_001","message":"缺少必填字段。","fieldKey":"locationCode"}""", Encoding.UTF8, "application/json"),
        });
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SubmitJobAsync(
            "req-bad",
            new TemplateSaveDto("location-label", "项目A", null, null),
            [new Dictionary<string, string>()]));

        Assert.Contains("LF_VAL_001", exception.Message);
    }
}