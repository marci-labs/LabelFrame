using System.Text.Json;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Logs;
using LabelFrame.Core.Templates;
using LabelFrame.Rendering;
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
var templateStore = new TemplateStore(serverOptions.TemplatesDbPath);
await templateStore.InitializeAsync();
var logStore = new SqliteLogStore(serverOptions.LogsDbPath);
await logStore.InitializeAsync();
var service = new ServerService(db, templateStore);

builder.Services.ConfigureHttpJsonOptions(json =>
{
    json.SerializerOptions.PropertyNameCaseInsensitive = true;
    json.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    json.SerializerOptions.Converters.Add(new LabelFrame.Core.Layout.LabelElementJsonConverter());
});
builder.Services.AddSingleton(db);
builder.Services.AddSingleton(service);
builder.Services.AddSingleton(serverOptions);
builder.Services.AddSingleton(templateStore);
builder.Services.AddSingleton(logStore);
builder.Services.AddSingleton<ILabelBitmapRenderer>(new SkiaLabelRenderer());
// 本地工具服务：地址由用户配置（可跨机器 / 跨端口），启用宽松 CORS
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseCors();

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

// ---- 模板库（迭代 16：服务端集中管理）----
app.MapPost("/api/templates", async (TemplatePackageDto? dto, TemplateStore templates, CancellationToken ct) =>
{
    if (dto is null || string.IsNullOrWhiteSpace(dto.Name) || dto.Contract is null || dto.Layout is null)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "缺少模板 name / contract / layout。"));
    }

    await templates.SaveAsync(new TemplatePackage
    {
        Name = dto.Name,
        Group = string.IsNullOrWhiteSpace(dto.Group) ? "默认" : dto.Group,
        Contract = dto.Contract,
        Layout = dto.Layout,
        TestData = dto.TestData ?? new Dictionary<string, string>(),
    }, ct);
    return Results.Ok(new { name = dto.Name, group = string.IsNullOrWhiteSpace(dto.Group) ? "默认" : dto.Group });
});

app.MapGet("/api/templates", async (string? group, TemplateStore templates, CancellationToken ct) =>
    Results.Ok(await templates.ListAsync(group, ct)));

app.MapGet("/api/templates/{name}", async (string name, TemplateStore templates, CancellationToken ct) =>
{
    var package = await templates.GetAsync(name, ct);
    return package is null
        ? Results.NotFound(new ErrorView(ServerErrorCodes.TemplateNotFound, $"模板不存在:{name}。"))
        : Results.Ok(package);
});

app.MapDelete("/api/templates/{name}", async (string name, TemplateStore templates, CancellationToken ct) =>
{
    await templates.DeleteAsync(name, ct);
    return Results.NoContent();
});

app.MapGet("/api/templates/{name}/export", async (string name, TemplateStore templates, CancellationToken ct) =>
{
    var package = await templates.GetAsync(name, ct);
    return package is null
        ? Results.NotFound(new ErrorView(ServerErrorCodes.TemplateNotFound, $"模板不存在:{name}。"))
        : Results.File(TemplatePackageSerializer.Export(package), "application/zip", $"{name}.lfpkg");
});

app.MapPost("/api/templates/import", async (IFormFile file, TemplateStore templates, CancellationToken ct) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "缺少模板包文件。"));
    }

    using var memory = new MemoryStream();
    await file.CopyToAsync(memory, ct);
    try
    {
        var package = TemplatePackageSerializer.Import(memory.ToArray());
        await templates.SaveAsync(package, ct);
        return Results.Ok(package.Name);
    }
    catch (InvalidDataException ex)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, ex.Message));
    }
}).DisableAntiforgery();

app.MapPost("/api/templates/{name}/preview", async (string name, PreviewRequest? request, TemplateStore templates, ILabelBitmapRenderer renderer, CancellationToken ct) =>
{
    var package = await templates.GetAsync(name, ct);
    if (package is null)
    {
        return Results.NotFound(new ErrorView(ServerErrorCodes.TemplateNotFound, $"模板不存在:{name}。"));
    }

    var document = new LabelDocument
    {
        Layout = package.Layout,
        Data = request?.Data ?? package.TestData ?? new Dictionary<string, string>(),
    };
    var png = renderer.RenderLabelBitmapPng(document, serverOptions.Dpi, package.Images);
    return Results.File(png, "image/png");
});

// ---- 调试出图（迭代 16：服务端渲染，浏览器下载；打印以客户端渲染为准）----
app.MapPost("/api/print/render-image", async (SubmitJobRequest? request, ILabelBitmapRenderer renderer, CancellationToken ct) =>
{
    if (request?.Template?.Contract is null || request.Template.Layout is null)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "缺少 template（contract + layout）。"));
    }

    if (request.Labels is null || request.Labels.Count == 0)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "缺少 labels（至少一张）。"));
    }

    var document = new LabelDocument
    {
        Layout = request.Template.Layout,
        Data = request.Labels[0].Data ?? new Dictionary<string, string>(),
    };
    var images = DecodeImages(request.Template.Images);
    var png = renderer.RenderLabelBitmapPng(document, serverOptions.Dpi, images);
    var fileName = $"{(string.IsNullOrWhiteSpace(request.Template.Name) ? "label" : request.Template.Name)}-print.png";
    return Results.File(png, "image/png", fileName);
});

app.MapPost("/api/print/render-images", async (SubmitJobRequest? request, ILabelBitmapRenderer renderer, CancellationToken ct) =>
{
    if (request is null)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请求体不能为空。"));
    }

    if (request.Template?.Contract is null || request.Template.Layout is null || request.Labels is null || request.Labels.Count == 0)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "缺少 template 或 labels。"));
    }

    var images = DecodeImages(request.Template.Images);
    using var stream = new MemoryStream();
    using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
    {
        for (var i = 0; i < request.Labels.Count; i++)
        {
            var document = new LabelDocument
            {
                Layout = request.Template.Layout,
                Data = request.Labels[i].Data ?? new Dictionary<string, string>(),
            };
            var bitmap = renderer.RenderLabelBitmap(document, serverOptions.Dpi, images);
            var entry = archive.CreateEntry($"label-{i + 1}.png");
            using var entryStream = entry.Open();
            var png = LabelBitmapPng.ToPng(bitmap);
            entryStream.Write(png);
        }
    }

    var name = string.IsNullOrWhiteSpace(request.Template.Name) ? "label" : request.Template.Name;
    var zipName = $"{name}-debug-{DateTime.Now:yyyyMMddHHmmss}.zip";
    return Results.File(stream.ToArray(), "application/zip", zipName);
});

// ---- 设备日志（客户端 / PDA 回传，服务端集中查看）----
app.MapPost("/api/logs", async (PushLogRequest? request, SqliteLogStore logs, CancellationToken ct) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.DeviceId) || request.Lines is null || request.Lines.Count == 0)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "缺少 deviceId / lines。"));
    }

    await logs.AppendAsync(request.DeviceId, request.Lines, ct);
    return Results.Ok(new { received = request.Lines.Count });
});

app.MapGet("/api/logs", async (string? deviceId, DateTimeOffset? since, SqliteLogStore logs, CancellationToken ct) =>
{
    var entries = await logs.QueryAsync(deviceId, since, ct);
    return Results.Ok(entries.Select(e => new { e.DeviceId, Time = e.Time, e.Line }));
});

// ---- Excel 数据导入（解析表头 + 数据行，前端做列映射）----
app.MapPost("/api/import/excel", async (IFormFile file, CancellationToken ct) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请上传 .xlsx 文件。"));
    }

    try
    {
        await using var stream = file.OpenReadStream();
        var table = TemplateFrame.Excel.Simple.SimpleExcel.Read(stream);
        var headers = (table.Headers ?? []).Select(h => h ?? string.Empty).ToList();
        var rows = table.Rows
            .Select(row => row.Select(FormatExcelCell).ToList())
            .ToList();
        return Results.Ok(new { headers, rows });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, $"Excel 解析失败：{ex.Message}"));
    }
}).DisableAntiforgery();

// ---- 测试入口（无业务系统也能提交打印 / 查看设备与作业）----
app.MapGet("/", () => Results.Content(TestUi.HomePage, "text/html; charset=utf-8"));
app.MapGet("/devices", () => Results.Content(TestUi.DevicesPage, "text/html; charset=utf-8"));
app.MapGet("/jobs", () => Results.Content(TestUi.JobsPage, "text/html; charset=utf-8"));

// ---- Web UI 静态托管（前端构建产物 web/dist，迭代 16：服务端承载全部界面）----
var webUiPath = ResolveWebUiPath(serverOptions);
if (webUiPath is not null)
{
    var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webUiPath);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
    app.MapFallback(async context =>
    {
        var indexFile = Path.Combine(webUiPath, "index.html");
        if (!File.Exists(indexFile))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(indexFile);
    });
    Console.WriteLine($"[LabelFrame.Server] Web UI: {webUiPath}");
}
else
{
    Console.WriteLine("[LabelFrame.Server] 未找到 Web UI 构建产物（web/dist），仅提供 API。");
}

// 模板图片 base64 → 字节
static IReadOnlyDictionary<string, byte[]> DecodeImages(IReadOnlyDictionary<string, string>? images)
    => images?.ToDictionary(kv => kv.Key, kv => System.Convert.FromBase64String(kv.Value)) ?? new Dictionary<string, byte[]>();

/// <summary>解析 Web UI 静态目录：配置优先，否则探测常见位置（含仓库开发路径）。</summary>
static string? ResolveWebUiPath(ServerOptions options)
{
    if (!string.IsNullOrWhiteSpace(options.WebUiPath) && Directory.Exists(options.WebUiPath))
    {
        return options.WebUiPath;
    }

    var candidates = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "web", "dist"),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "web", "dist")),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "web", "dist")),
    };
    return candidates.FirstOrDefault(Directory.Exists);
}

static string FormatExcelCell(object? value) => value switch
{
    null => string.Empty,
    DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
    IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
    _ => value.ToString() ?? string.Empty,
};

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
