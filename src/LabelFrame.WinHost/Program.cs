using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Templates;
using LabelFrame.Core.Transport;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Logs;
using LabelFrame.Rendering;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost;

/// <summary>WinHost：本地打印服务（作业队列 + HTTP API + 打印 Worker）。</summary>
public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task Main(string[] args)
    {
        // 内容根固定为程序目录，保证从任意工作目录启动都能读到 appsettings.json
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });
        var options = new HostOptions();
        builder.Configuration.GetSection("WinHost").Bind(options);
        options.ApplyEnvironmentOverrides();
        builder.WebHost.UseUrls(options.ListenUrl);

        var hostLogWriter = OpenHostLogWriter(options);
        var transportManager = new TransportManager(options, hostLogWriter);
        void HostInfo(string message)
        {
            try
            {
                hostLogWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
                hostLogWriter.Flush();
            }
            catch
            {
                // 日志写入失败不影响启动
            }
        }

        HostInfo($"LabelFrame 启动：监听 {options.ListenUrl}，连接 {transportManager.CurrentConfig.Describe()}，DPI {options.Dpi}，OpenBrowser={options.OpenBrowser}");

        var store = new SqliteLabelJobStore(options.DatabasePath);
        await store.InitializeAsync();
        var queue = new LabelJobQueue(store);

        builder.Services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.PropertyNameCaseInsensitive = true;
            json.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            json.SerializerOptions.Converters.Add(new LabelElementJsonConverter());
        });

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<ILabelJobStore>(store);
        builder.Services.AddSingleton(queue);
        builder.Services.AddSingleton<ITransportManager>(transportManager);
        builder.Services.AddSingleton<ZplImageEncoder>();
        builder.Services.AddSingleton<IPrinterStatusProvider>(sp =>
            sp.GetRequiredService<ITransportManager>().CurrentTransport as IPrinterStatusProvider ?? new UnsupportedStatusProvider());
        builder.Services.AddHostedService<JobPrintWorker>();

        var templateStore = new TemplateStore(options.TemplatesDbPath);
        await templateStore.InitializeAsync();
        builder.Services.AddSingleton(templateStore);
        builder.Services.AddSingleton<LabelPreviewRenderer>();
        builder.Services.AddSingleton<ILabelBitmapRenderer>(new SkiaLabelRenderer());
        builder.Services.AddSingleton(sp => new JobSubmissionService(
            queue,
            sp.GetRequiredService<ZplImageEncoder>(),
            options.Dpi,
            sp.GetRequiredService<ILabelBitmapRenderer>(),
            sp.GetRequiredService<TemplateStore>(),
            sp.GetRequiredService<ITransportManager>(),
            hostLogWriter));

        // 本地工具服务：地址由用户配置（可跨机器 / 跨端口），启用宽松 CORS
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        var logStore = new SqliteLogStore(options.LogsDbPath);
        await logStore.InitializeAsync();
        builder.Services.AddSingleton(logStore);

        if (!string.IsNullOrWhiteSpace(options.ServerUrl))
        {
            builder.Services.AddSingleton(sp => new Routing.ServerJobPoller(
                new HttpClient(),
                options.ServerUrl!,
                options.DeviceId,
                options.DeviceName));
            builder.Services.AddHostedService(sp => new Routing.ServerRoutingWorker(
                sp.GetRequiredService<Routing.ServerJobPoller>(),
                sp.GetRequiredService<JobSubmissionService>(),
                queue,
                TimeSpan.FromSeconds(Math.Max(1, options.PollIntervalSeconds)),
                sp.GetRequiredService<ILogger<Routing.ServerRoutingWorker>>()));
        }

        var app = builder.Build();

        app.UseCors();

        app.MapGet("/healthz", (ITransportManager transportManager) =>
            Results.Ok(new { service = "LabelFrame.WinHost", status = "ok", transport = transportManager.CurrentConfig.Mode.ToString() }));

        // ---- 模板管理（单机 CRUD + 导入导出 + 预览）----
        app.MapPost("/api/templates", async (Api.TemplatePackageDto? dto, TemplateStore templateStore, CancellationToken ct) =>
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Name) || dto.Contract is null || dto.Layout is null)
            {
                return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, "缺少模板 name / contract / layout。"));
            }

            await templateStore.SaveAsync(new TemplatePackage
            {
                Name = dto.Name,
                Group = string.IsNullOrWhiteSpace(dto.Group) ? "默认" : dto.Group,
                Contract = dto.Contract,
                Layout = dto.Layout,
                TestData = dto.TestData ?? new Dictionary<string, string>(),
            }, ct);
            return Results.Ok(new { name = dto.Name, group = string.IsNullOrWhiteSpace(dto.Group) ? "默认" : dto.Group });
        });

        app.MapGet("/api/templates", async (string? group, TemplateStore templateStore, CancellationToken ct) =>
            Results.Ok(await templateStore.ListAsync(group, ct)));

        app.MapGet("/api/templates/{name}", async (string name, TemplateStore templateStore, CancellationToken ct) =>
        {
            var package = await templateStore.GetAsync(name, ct);
            return package is null
                ? Results.NotFound(new ErrorView(JobErrorCodes.JobNotFound, $"模板不存在:{name}。"))
                : Results.Ok(package);
        });

        app.MapDelete("/api/templates/{name}", async (string name, TemplateStore templateStore, CancellationToken ct) =>
        {
            await templateStore.DeleteAsync(name, ct);
            return Results.NoContent();
        });

        app.MapGet("/api/templates/{name}/export", async (string name, TemplateStore templateStore, CancellationToken ct) =>
        {
            var package = await templateStore.GetAsync(name, ct);
            return package is null
                ? Results.NotFound(new ErrorView(JobErrorCodes.JobNotFound, $"模板不存在:{name}。"))
                : Results.File(TemplatePackageSerializer.Export(package), "application/zip", $"{name}.lfpkg");
        });

        app.MapPost("/api/templates/import", async (IFormFile file, TemplateStore templateStore, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, "缺少模板包文件。"));
            }

            using var memory = new MemoryStream();
            await file.CopyToAsync(memory, ct);
            try
            {
                var package = TemplatePackageSerializer.Import(memory.ToArray());
                await templateStore.SaveAsync(package, ct);
                return Results.Ok(package.Name);
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, ex.Message));
            }
        }).DisableAntiforgery();

        app.MapPost("/api/templates/{name}/preview", async (string name, Api.PreviewRequest? request, TemplateStore templateStore, LabelPreviewRenderer renderer, CancellationToken ct) =>
        {
            var package = await templateStore.GetAsync(name, ct);
            if (package is null)
            {
                return Results.NotFound(new ErrorView(JobErrorCodes.JobNotFound, $"模板不存在:{name}。"));
            }

            var document = new LabelDocument
            {
                Layout = package.Layout,
                Data = request?.Data ?? new Dictionary<string, string>(),
            };
            var png = renderer.RenderPng(document, dpi: 203, package.Images);
            return Results.File(png, "image/png");
        });

        // 图片打印调试：渲染实际发送给打印机的 1bpp 位图为 PNG（不建作业、不打印），用于排查文字清晰度 / 定位
        app.MapPost("/api/print/render-image", async (Api.SubmitJobRequest? request, TemplateStore templateStore, ILabelBitmapRenderer renderer, CancellationToken ct) =>
        {
            if (request?.Template?.Contract is null || request.Template.Layout is null)
            {
                return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, "缺少 template（contract + layout）。"));
            }

            if (request.Labels is null || request.Labels.Count == 0)
            {
                return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, "缺少 labels（至少一张）。"));
            }

            var document = new LabelDocument
            {
                Layout = request.Template.Layout,
                Data = request.Labels[0].Data ?? new Dictionary<string, string>(),
            };
            IReadOnlyDictionary<string, byte[]> images = new Dictionary<string, byte[]>();
            if (!string.IsNullOrWhiteSpace(request.Template.Name))
            {
                var package = await templateStore.GetAsync(request.Template.Name, ct);
                if (package is not null)
                {
                    images = package.Images;
                }
            }

            var png = renderer.RenderLabelBitmapPng(document, options.Dpi, images);
            var fileName = $"{(string.IsNullOrWhiteSpace(request.Template.Name) ? "label" : request.Template.Name)}-print.png";
            return Results.File(png, "image/png", fileName);
        });

        // 调试出图（批量）：后端渲染全部行为 zip（不建作业、不发驱动；迭代 15）
        app.MapPost("/api/print/render-images", async (Api.SubmitJobRequest? request, JobSubmissionService service, CancellationToken ct) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, "请求体不能为空。"));
            }

            try
            {
                var images = await service.RenderImagesAsync(request, ct);
                using var stream = new MemoryStream();
                using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var image in images)
                    {
                        var entry = archive.CreateEntry($"label-{image.Index + 1}.png");
                        using var entryStream = entry.Open();
                        entryStream.Write(image.Png);
                    }
                }

                var name = string.IsNullOrWhiteSpace(request.Template?.Name) ? "label" : request.Template.Name;
                var fileName = $"{name}-debug-{DateTime.Now:yyyyMMddHHmmss}.zip";
                return Results.File(stream.ToArray(), "application/zip", fileName);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, ex.Message));
            }
        });

        // ---- 连接管理（迭代 15）：查询 / 切换 / 测试；单一连接生效，先测试后生效 ----
        app.MapGet("/api/transport", (ITransportManager transportManager) =>
            Results.Ok(ToTransportConfigDto(transportManager.CurrentConfig)));

        app.MapPost("/api/transport", async (Api.TransportApplyRequest? request, ITransportManager transportManager, CancellationToken ct) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Mode))
            {
                return Results.BadRequest(new ErrorView("LF_TRANSPORT_INVALID", "缺少连接方式（mode）。"));
            }

            if (!Enum.TryParse<TransportMode>(request.Mode, ignoreCase: true, out var mode))
            {
                return Results.BadRequest(new ErrorView("LF_TRANSPORT_INVALID", $"不支持的连接方式：{request.Mode}。"));
            }

            var config = new TransportConfig
            {
                Mode = mode,
                TcpHost = request.TcpHost ?? transportManager.CurrentConfig.TcpHost,
                TcpPort = request.TcpPort ?? transportManager.CurrentConfig.TcpPort,
                PrinterName = request.PrinterName ?? transportManager.CurrentConfig.PrinterName,
                ZebraKind = request.ZebraKind is not null && Enum.TryParse<ZebraTransportKind>(request.ZebraKind, ignoreCase: true, out var zebraKind)
                    ? zebraKind
                    : transportManager.CurrentConfig.ZebraKind,
                ZebraUsbName = request.ZebraUsbName ?? transportManager.CurrentConfig.ZebraUsbName,
            };

            var result = await transportManager.ApplyAsync(config, request.TestOnly ?? false, ct);
            return Results.Ok(new TransportApplyResponse(result.Ok, result.Message, ToTransportConfigDto(result.Config)));
        });

        app.MapPost("/api/jobs", async (SubmitJobRequest? request, JobSubmissionService service, CancellationToken ct) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, "请求体不能为空。"));
            }

            var result = await service.SubmitAsync(request, ct);
            if (result.Job is null)
            {
                return Results.BadRequest(new ErrorView(result.ErrorCode!, result.ErrorMessage!, result.FieldKey));
            }

            var jobView = EnrichPrintInfo(JobViews.From(result.Job), result.Job!.Id, transportManager);
            return result.Created
                ? Results.Accepted((string?)null, jobView)
                : Results.Ok(jobView);
        });

        app.MapGet("/api/jobs/{jobId}", async (string jobId, LabelJobQueue queue, ITransportManager transportManager, CancellationToken ct) =>
        {
            var job = await queue.GetAsync(jobId, ct);
            return job is null
                ? Results.NotFound(new ErrorView(JobErrorCodes.JobNotFound, $"作业不存在：{jobId}。"))
                : Results.Ok(EnrichPrintInfo(JobViews.From(job), job.Id, transportManager));
        });

        app.MapPost("/api/jobs/{jobId}/suspend", async (string jobId, LabelJobQueue queue, CancellationToken ct) =>
            await TransitionAsync(jobId, queue.SuspendAsync, transportManager, ct));

        app.MapPost("/api/jobs/{jobId}/resume", async (string jobId, LabelJobQueue queue, CancellationToken ct) =>
            await TransitionAsync(jobId, queue.ResumeAsync, transportManager, ct));

        app.MapPost("/api/jobs/{jobId}/cancel", async (string jobId, LabelJobQueue queue, CancellationToken ct) =>
            await TransitionAsync(jobId, queue.CancelAsync, transportManager, ct));

        app.MapPost("/api/jobs/{jobId}/items/{itemIndex:int}/retry", async (string jobId, int itemIndex, LabelJobQueue queue, ITransportManager transportManager, CancellationToken ct) =>
        {
            try
            {
                var job = await queue.RetryItemAsync(jobId, itemIndex, ct);
                return Results.Ok(EnrichPrintInfo(JobViews.From(job), job.Id, transportManager));
            }
            catch (LabelJobException ex) when (ex.Code == JobErrorCodes.JobNotFound)
            {
                return Results.NotFound(new ErrorView(ex.Code, ex.Message));
            }
            catch (LabelJobException ex)
            {
                return Results.Conflict(new ErrorView(ex.Code, ex.Message));
            }
        });

        // ---- 设备日志（PDA 回传 / PC 查看）----
        app.MapPost("/api/logs", async (Api.PushLogRequest? request, SqliteLogStore logStore, CancellationToken ct) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.DeviceId) || request.Lines is null || request.Lines.Count == 0)
            {
                return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, "缺少 deviceId / lines。"));
            }

            await logStore.AppendAsync(request.DeviceId, request.Lines, ct);
            return Results.Ok(new { received = request.Lines.Count });
        });

        app.MapGet("/api/logs", async (string? deviceId, DateTimeOffset? since, SqliteLogStore logStore, CancellationToken ct) =>
        {
            var entries = await logStore.QueryAsync(deviceId, since, ct);
            return Results.Ok(entries.Select(e => new { e.DeviceId, Time = e.Time, e.Line }));
        });

        // ---- Excel 数据导入（解析表头 + 数据行，前端做列映射）----
        app.MapPost("/api/import/excel", async (IFormFile file, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, "请上传 .xlsx 文件。"));
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
                return Results.BadRequest(new ErrorView(JobErrorCodes.InvalidRequest, $"Excel 解析失败：{ex.Message}"));
            }
        }).DisableAntiforgery();

        // ---- 打印机测试页 / 在线状态 ----
        app.MapGet("/api/printer/status", async (ITransportManager transportManager, CancellationToken ct) =>
            Results.Ok(await (transportManager.CurrentTransport as IPrinterStatusProvider ?? new UnsupportedStatusProvider()).GetStatusAsync(ct)));

        app.MapPost("/api/printer/test", async (ITransportManager transportManager, ILabelBitmapRenderer renderer, ZplImageEncoder encoder, CancellationToken ct) =>
        {
            // 测试页与正式打印同源：Skia 渲染整版位图经 ^GF 发送（图片打印语义，无矢量 ZPL）
            var document = new LabelDocument
            {
                Layout = new LabelLayout
                {
                    Name = "test",
                    ContractName = "test",
                    ContractVersion = "1.0",
                    WidthMm = 40,
                    HeightMm = 20,
                    Elements =
                    [
                        new LabelTextElement { Literal = "LabelFrame Test", XMm = 2, YMm = 4, FontHeightMm = 3, FontWidthMm = 3, WidthMm = 36, TextAlign = LabelTextAlign.Center },
                    ],
                },
                Data = new Dictionary<string, string>(),
            };
            var bitmap = renderer.RenderLabelBitmap(document, options.Dpi);
            var command = encoder.EncodeImage(bitmap, document.Layout.WidthMm, document.Layout.HeightMm, options.Dpi);
            await transportManager.CurrentTransport.SendAsync(command, ct);
            return Results.Ok(new { sent = true, bytes = System.Text.Encoding.UTF8.GetByteCount(command) });
        });

        // ---- 本机服务关闭（Web UI 设置页「退出程序」用）----
        app.MapPost("/api/host/shutdown", (HttpContext context, IHostApplicationLifetime lifetime) =>
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is null || !System.Net.IPAddress.IsLoopback(remote))
            {
                return Results.Forbid();
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(200);
                HostInfo("收到关闭请求，正在停止宿主…");
                lifetime.StopApplication();
                // 托盘线程（WinForms 消息循环）可能阻止 RunAsync 自然返回，延迟后强制退出
                await Task.Delay(500);
                HostInfo("关闭完成。");
                Environment.Exit(0);
            });
            return Results.Ok(new { shuttingDown = true });
        });

        // ---- Web UI 静态托管（前端构建产物 web/dist）----
        var webUiPath = ResolveWebUiPath(options);
        if (webUiPath is not null)
        {
            var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webUiPath);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
            // SPA fallback：未匹配 /api 的路径返回 index.html
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
            Console.WriteLine($"[LabelFrame] Web UI: {webUiPath}");
        }
        else
        {
            Console.WriteLine("[LabelFrame] 未找到 Web UI 构建产物（web/dist），仅提供 API。");
        }

        // 单机模式：启动后自动打开默认浏览器
        if (options.OpenBrowser)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = ToLocalUiUrl(options.ListenUrl),
                        UseShellExecute = true,
                    });
                    HostInfo($"已尝试打开浏览器：{ToLocalUiUrl(options.ListenUrl)}");
                }
                catch (Exception ex)
                {
                    HostInfo($"打开浏览器失败：{ex.Message}");
                }
            });
        }

        app.Lifetime.ApplicationStopping.Register(() => HostInfo("ApplicationStopping"));
        app.Lifetime.ApplicationStopped.Register(() => HostInfo("ApplicationStopped"));

        var tray = new TrayIconService(HostInfo);
        if (options.EnableTray)
        {
            tray.Start(ToLocalUiUrl(options.ListenUrl), () =>
            {
                app.Lifetime.StopApplication();
                return Task.CompletedTask;
            });
            HostInfo("系统托盘已启用（右键托盘图标可退出）。");
        }

        try
        {
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            // WinExe 无窗口：失败信息写入 host.log（%LOCALAPPDATA%\LabelFrame\host.log）
            HostInfo($"LabelFrame 启动失败：{ex}");
            HostInfo("如端口被占用，可修改 appsettings.json 的 ListenUrl 或结束占用进程后重试。");
            throw;
        }
        finally
        {
            tray.Dispose();
            HostInfo("宿主退出流程完成。");
            Environment.Exit(0);
        }
    }

    private static string FormatExcelCell(object? value) => value switch
    {
        null => string.Empty,
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>解析 Web UI 静态目录：配置优先，否则探测常见位置（含仓库开发路径）。</summary>
    private static string? ResolveWebUiPath(HostOptions options)
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

    /// <summary>Log 模拟打印：把 PNG 目录与张数附到作业视图，便于前端展示「打印图片在哪」。</summary>
    private static JobView EnrichPrintInfo(JobView view, string jobId, ITransportManager transportManager)
    {
        if (transportManager.CurrentConfig.Mode != TransportMode.Log)
        {
            return view;
        }

        var dir = GetLogPrintDir(jobId);
        var count = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.png").Length : 0;
        return view with { PrintImageDir = dir, PrintImageCount = count };
    }

    private static string GetLogPrintDir(string jobId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LabelFrame",
        "print",
        jobId);

    private static async Task<IResult> TransitionAsync(
        string jobId,
        Func<string, CancellationToken, Task<LabelJob>> action,
        ITransportManager transportManager,
        CancellationToken ct)
    {
        try
        {
            var job = await action(jobId, ct);
            return Results.Ok(EnrichPrintInfo(JobViews.From(job), job.Id, transportManager));
        }
        catch (LabelJobException ex) when (ex.Code == JobErrorCodes.JobNotFound)
        {
            return Results.NotFound(new ErrorView(ex.Code, ex.Message));
        }
        catch (LabelJobException ex)
        {
            return Results.Conflict(new ErrorView(ex.Code, ex.Message));
        }
    }

    /// <summary>本地 UI 打开地址：通配监听（0.0.0.0 / * / + / [::]）规范化为 127.0.0.1，避免浏览器/托盘跳到 0.0.0.0。</summary>
    private static string ToLocalUiUrl(string listenUrl)
    {
        if (!Uri.TryCreate(listenUrl, UriKind.Absolute, out var uri))
        {
            return listenUrl;
        }

        if (uri.Host is "0.0.0.0" or "*" or "+" or "::" or "[::]")
        {
            var builder = new UriBuilder(uri) { Host = "127.0.0.1" };
            return builder.Uri.ToString();
        }

        return listenUrl;
    }

    private sealed class UnsupportedStatusProvider : IPrinterStatusProvider
    {
        public Task<PrinterStatusInfo> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PrinterStatusInfo(false, false, false, "当前传输不支持状态查询。"));
    }

    /// <summary>Log 传输写入宿主日志文件（WinExe 无控制台，避免 Console 不可用）。</summary>
    private static TextWriter OpenHostLogWriter(HostOptions options)
    {
        try
        {
            var directory = Path.GetDirectoryName(options.HostLogPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var writer = new StreamWriter(options.HostLogPath, append: true) { AutoFlush = true };
            return TextWriter.Synchronized(writer);
        }
        catch
        {
            return TextWriter.Null;
        }
    }

    /// <summary>TransportConfig → API DTO（params 含全部字段，前端只展示当前模式所需）。</summary>
    private static TransportConfigDto ToTransportConfigDto(TransportConfig config) => new(
        config.Mode.ToString(),
        new TransportParamsDto(
            config.TcpHost,
            config.TcpPort,
            config.PrinterName,
            config.ZebraKind.ToString(),
            config.ZebraUsbName),
        new[] { "Log", "Tcp", "WindowsDriver", "Zebra" });
}
