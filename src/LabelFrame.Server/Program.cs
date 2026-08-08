using System.Text.Json;
using LabelFrame.Server;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

var serverOptions = new ServerOptions();
builder.Configuration.GetSection("Server").Bind(serverOptions);
serverOptions.ApplyEnvironmentOverrides();
builder.WebHost.UseUrls(serverOptions.ListenUrl);

var db = new ServerDb(serverOptions.DatabasePath);
await db.InitializeAsync();
var service = new ServerService(db);

builder.Services.ConfigureHttpJsonOptions(json =>
{
    json.SerializerOptions.PropertyNameCaseInsensitive = true;
    json.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    json.SerializerOptions.Converters.Add(new LabelFrame.Core.Layout.LabelElementJsonConverter());
});
builder.Services.AddSingleton(db);
builder.Services.AddSingleton(service);
builder.Services.AddSingleton(serverOptions);

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { service = "LabelFrame.Server", status = "ok" }));

// ---- 设备注册 / 目录 ----
app.MapPost("/api/devices", async (RegisterDeviceRequest? request, ServerService svc, CancellationToken ct) =>
{
    if (request is null)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请求体不能为空。"));
    }

    try
    {
        return Results.Ok(await svc.RegisterDeviceAsync(request.DeviceId, request.Name, ct));
    }
    catch (ServerException ex)
    {
        return Results.BadRequest(new ErrorView(ex.Code, ex.Message));
    }
});

app.MapGet("/api/devices", async (ServerService svc, CancellationToken ct) =>
    Results.Ok(await svc.ListDevicesAsync(ct)));

// ---- 作业提交 / 查询 ----
app.MapPost("/api/jobs", async (SubmitJobRequest? request, ServerService svc, CancellationToken ct) =>
{
    if (request is null)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请求体不能为空。"));
    }

    try
    {
        var job = await svc.SubmitJobAsync(request, ct);
        return job.Status == "Pending"
            ? Results.Accepted((string?)null, job)
            : Results.Ok(job);
    }
    catch (ServerException ex)
    {
        return ex.Code == ServerErrorCodes.DeviceNotFound
            ? Results.NotFound(new ErrorView(ex.Code, ex.Message))
            : Results.BadRequest(new ErrorView(ex.Code, ex.Message));
    }
});

app.MapGet("/api/jobs", async (ServerService svc, CancellationToken ct) =>
    Results.Ok(await svc.ListJobsAsync(ct)));

app.MapGet("/api/jobs/{jobId}", async (string jobId, ServerService svc, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await svc.GetJobAsync(jobId, ct));
    }
    catch (ServerException ex)
    {
        return Results.NotFound(new ErrorView(ex.Code, ex.Message));
    }
});

// ---- 设备领取 / 回报 ----
app.MapGet("/api/devices/{deviceId}/jobs/pending", async (string deviceId, ServerService svc, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await svc.ClaimPendingJobsAsync(deviceId, ct));
    }
    catch (ServerException ex)
    {
        return Results.NotFound(new ErrorView(ex.Code, ex.Message));
    }
});

app.MapPost("/api/devices/{deviceId}/jobs/{jobId}/result", async (string deviceId, string jobId, ReportResultRequest? report, ServerService svc, CancellationToken ct) =>
{
    if (report is null)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请求体不能为空。"));
    }

    try
    {
        return Results.Ok(await svc.ReportResultAsync(deviceId, jobId, report, ct));
    }
    catch (ServerException ex)
    {
        return ex.Code switch
        {
            ServerErrorCodes.JobNotFound => Results.NotFound(new ErrorView(ex.Code, ex.Message)),
            ServerErrorCodes.NotJobOwner => Results.Forbid(),
            _ => Results.Conflict(new ErrorView(ex.Code, ex.Message)),
        };
    }
});

// ---- 测试入口（无业务系统也能提交打印 / 查看设备与作业）----
app.MapGet("/", () => Results.Content(TestUi.HomePage, "text/html; charset=utf-8"));
app.MapGet("/devices", () => Results.Content(TestUi.DevicesPage, "text/html; charset=utf-8"));
app.MapGet("/jobs", () => Results.Content(TestUi.JobsPage, "text/html; charset=utf-8"));

await app.RunAsync();

internal static partial class TestUi
{
    private const string TemplateExample = """
        {
          "contract": {
            "name": "location-label", "version": "1.0",
            "fields": [
              { "key": "locationCode", "displayName": "库位码", "isRequired": true, "type": "text" },
              { "key": "zone", "displayName": "区域", "isRequired": true, "type": "text" }
            ]
          },
          "layout": {
            "name": "location-label-100x60", "contractName": "location-label", "contractVersion": "1.0",
            "widthMm": 100, "heightMm": 60,
            "elements": [
              { "type": "text", "sourceKey": "zone", "xMm": 5, "yMm": 4, "fontHeightMm": 5, "fontWidthMm": 5 },
              { "type": "barcode", "sourceKey": "locationCode", "xMm": 5, "yMm": 26, "heightMm": 22, "moduleWidth": 2 }
            ]
          }
        }
        """;

    internal static readonly string HomePage = """
        <!DOCTYPE html><html lang="zh-CN"><head><meta charset="utf-8"><title>LabelFrame Server 测试入口</title></head>
        <body><h1>LabelFrame Server 测试入口</h1>
        <p><a href="/devices">设备目录</a> | <a href="/jobs">作业列表</a></p>
        <form id="form">
          <label>幂等键 requestId <input name="requestId" value="demo-1" size="40"></label><br>
          <label>目标设备 targetDeviceId <input name="targetDeviceId" value="device-1" size="40"></label><br>
          <label>模板 template（JSON）<br><textarea name="template" rows="18" cols="100">{TEMPLATE}</textarea></label><br>
          <label>标签 labels（JSON）<br><textarea name="labels" rows="6" cols="100">[ { "data": { "zone": "A-01", "locationCode": "A-01-02-03" } } ]</textarea></label><br>
          <button type="submit">提交作业</button>
        </form>
        <pre id="result"></pre>
        <script>
          document.getElementById('form').addEventListener('submit', async (e) => {
            e.preventDefault();
            const f = new FormData(e.target);
            const body = {
              requestId: f.get('requestId'),
              targetDeviceId: f.get('targetDeviceId'),
              template: JSON.parse(f.get('template')),
              labels: JSON.parse(f.get('labels')),
            };
            const res = await fetch('/api/jobs', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
            document.getElementById('result').textContent = res.status + ' ' + (await res.text());
          });
        </script></body></html>
        """.Replace("{TEMPLATE}", TemplateExample);

    internal static readonly string DevicesPage = """
        <!DOCTYPE html><html lang="zh-CN"><head><meta charset="utf-8"><title>设备目录</title></head>
        <body><h1>设备目录</h1><p><a href="/">返回</a></p><pre id="data">加载中…</pre>
        <script>fetch('/api/devices').then(r => r.text()).then(t => document.getElementById('data').textContent = t);</script></body></html>
        """;

    internal static readonly string JobsPage = """
        <!DOCTYPE html><html lang="zh-CN"><head><meta charset="utf-8"><title>作业列表</title></head>
        <body><h1>作业列表</h1><p><a href="/">返回</a></p><pre id="data">加载中…</pre>
        <script>fetch('/api/jobs').then(r => r.text()).then(t => document.getElementById('data').textContent = t);</script></body></html>
        """;
}