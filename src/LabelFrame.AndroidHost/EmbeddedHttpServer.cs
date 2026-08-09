using System.Net;
using System.Net.Sockets;
using System.Text;
using LabelFrame.AndroidHost.Api;
using LabelFrame.AndroidHost.Pc;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Transport;

namespace LabelFrame.AndroidHost;

/// <summary>
/// 本地 HTTP 服务（PDA 网页直连，不经 Server）：基于 TcpListener 的极简实现，
/// 避免 Android 上承载完整 ASP.NET Core。仅监听 127.0.0.1。
/// </summary>
public sealed class EmbeddedHttpServer : IDisposable
{
    private readonly int _port;
    private readonly SubmissionService _submission;
    private readonly LabelJobQueue _queue;
    private readonly IPrintTransport _transport;
    private readonly IPrinterStatusProvider? _status;
    private readonly PcTemplateClient? _pc;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>创建本地 HTTP 服务。</summary>
    public EmbeddedHttpServer(
        int port,
        SubmissionService submission,
        LabelJobQueue queue,
        IPrintTransport transport,
        PcTemplateClient? pcClient = null)
    {
        _port = port;
        _submission = submission;
        _queue = queue;
        _transport = transport;
        _status = transport as IPrinterStatusProvider;
        _pc = pcClient;
    }

    /// <summary>启动监听。</summary>
    public void Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _cts?.Dispose();
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                continue;
            }

            _ = Task.Run(() => HandleAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = client.GetStream();
            var (method, path, headers) = ReadRequest(stream);
            var body = await ReadBodyAsync(stream, headers, cancellationToken);
            var response = Route(method, path, body, cancellationToken);
            await WriteResponseAsync(stream, response, cancellationToken);
        }
        catch
        {
            // 单请求失败不影响服务
        }
        finally
        {
            client.Dispose();
        }
    }

    private static (string Method, string Path, Dictionary<string, string> Headers) ReadRequest(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var requestLine = reader.ReadLine() ?? throw new InvalidDataException("空请求。");
        var parts = requestLine.Split(' ');
        if (parts.Length < 3)
        {
            throw new InvalidDataException("请求行不合法。");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? line;
        while (!string.IsNullOrEmpty(line = reader.ReadLine()))
        {
            var idx = line.IndexOf(':');
            if (idx > 0)
            {
                headers[line[..idx].Trim()] = line[(idx + 1)..].Trim();
            }
        }

        return (parts[0].ToUpperInvariant(), parts[1], headers);
    }

    private static async Task<string> ReadBodyAsync(Stream stream, Dictionary<string, string> headers, CancellationToken cancellationToken)
    {
        if (!headers.TryGetValue("Content-Length", out var lengthText) || !int.TryParse(lengthText, out var length) || length <= 0)
        {
            return string.Empty;
        }

        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, length - read), cancellationToken);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private (int Status, string ContentType, byte[] Body) Route(
        string method, string path, string body, CancellationToken cancellationToken)
    {
        var basePath = path.Split('?')[0];

        if (method == "GET" && basePath == "/healthz")
        {
            return Json(200, new { service = "LabelFrame.AndroidHost", status = "ok" });
        }

        if (method == "GET" && basePath == "/")
        {
            return Html(200, PcTestPageHtml);
        }

        if (method == "POST" && basePath == "/api/jobs")
        {
            return SubmitJob(body);
        }

        if (method == "GET" && basePath.StartsWith("/api/jobs/", StringComparison.Ordinal))
        {
            var jobId = basePath["/api/jobs/".Length..];
            return GetJob(jobId);
        }

        if (method == "POST" && basePath.StartsWith("/api/jobs/", StringComparison.Ordinal) && basePath.EndsWith("/suspend", StringComparison.Ordinal))
        {
            return Transition(basePath, "suspend");
        }

        if (method == "POST" && basePath.StartsWith("/api/jobs/", StringComparison.Ordinal) && basePath.EndsWith("/resume", StringComparison.Ordinal))
        {
            return Transition(basePath, "resume");
        }

        if (method == "POST" && basePath.StartsWith("/api/jobs/", StringComparison.Ordinal) && basePath.EndsWith("/cancel", StringComparison.Ordinal))
        {
            return Transition(basePath, "cancel");
        }

        if (method == "GET" && basePath == "/api/printer/status")
        {
            return GetPrinterStatus();
        }

        if (method == "POST" && basePath == "/api/printer/test")
        {
            return TestPrinter(cancellationToken);
        }

        // ---- PDA 测试模式（从 PC 单机服务拉模板 / 测试打印 / 日志回传）----
        if (method == "GET" && basePath == "/api/pc/templates")
        {
            return PcTemplates();
        }

        if (method == "POST" && basePath.StartsWith("/api/pc/templates/", StringComparison.Ordinal) && basePath.EndsWith("/print-test", StringComparison.Ordinal))
        {
            var name = Uri.UnescapeDataString(basePath["/api/pc/templates/".Length..^"/print-test".Length]);
            return PcPrintTest(name);
        }

        return Json(404, new ErrorView(JobErrorCodes.JobNotFound, "接口不存在。"));
    }

    private (int, string, byte[]) SubmitJob(string body)
    {
        try
        {
            var request = System.Text.Json.JsonSerializer.Deserialize<SubmitJobRequest>(body, HostJson.Options);
            if (request is null)
            {
                return Json(400, new ErrorView(JobErrorCodes.InvalidRequest, "请求体不能为空。"));
            }

            var result = _submission.SubmitAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            if (result.Job is null)
            {
                return Json(400, new ErrorView(result.ErrorCode!, result.ErrorMessage!, result.FieldKey));
            }

            return Json(result.Created ? 202 : 200, JobViews.From(result.Job));
        }
        catch (Exception ex)
        {
            return Json(400, new ErrorView(JobErrorCodes.InvalidRequest, $"请求解析失败：{ex.Message}"));
        }
    }

    private (int, string, byte[]) GetJob(string jobId)
    {
        var job = _queue.GetAsync(jobId, CancellationToken.None).GetAwaiter().GetResult();
        return job is null
            ? Json(404, new ErrorView(JobErrorCodes.JobNotFound, $"作业不存在：{jobId}。"))
            : Json(200, JobViews.From(job));
    }

    private (int, string, byte[]) Transition(string basePath, string action)
    {
        var middle = basePath["/api/jobs/".Length..];
        var jobId = middle[..middle.LastIndexOf('/')];
        try
        {
            var job = action switch
            {
                "suspend" => _queue.SuspendAsync(jobId, CancellationToken.None).GetAwaiter().GetResult(),
                "resume" => _queue.ResumeAsync(jobId, CancellationToken.None).GetAwaiter().GetResult(),
                _ => _queue.CancelAsync(jobId, CancellationToken.None).GetAwaiter().GetResult(),
            };
            return Json(200, JobViews.From(job));
        }
        catch (LabelJobException ex)
        {
            return ex.Code == JobErrorCodes.JobNotFound
                ? Json(404, new ErrorView(ex.Code, ex.Message))
                : Json(409, new ErrorView(ex.Code, ex.Message));
        }
    }

    private (int, string, byte[]) GetPrinterStatus()
    {
        if (_status is null)
        {
            return Json(200, new PrinterStatusInfo(false, false, false, "当前传输不支持状态查询。"));
        }

        var status = _status.GetStatusAsync(CancellationToken.None).GetAwaiter().GetResult();
        return Json(200, status);
    }

    private (int, string, byte[]) TestPrinter(CancellationToken cancellationToken)
    {
        const string testZpl =
            "^XA^FO40,40^A0N,64,64^FDLabelFrame Test^FS" +
            "^FO40,120^BY2,3^BCN,80,Y,N,N^FDLABELFRAME-TEST^FS^XZ";
        try
        {
            _transport.SendAsync(testZpl, cancellationToken).GetAwaiter().GetResult();
            return Json(200, new { sent = true, bytes = Encoding.UTF8.GetByteCount(testZpl) });
        }
        catch (Exception ex)
        {
            return Json(500, new ErrorView(JobErrorCodes.TransportSendFailed, $"发送失败：{ex.Message}"));
        }
    }

    private (int, string, byte[]) PcTemplates()
    {
        if (_pc is null)
        {
            return Json(400, new ErrorView(JobErrorCodes.InvalidRequest, "未配置 PC 单机服务地址（pc_host）。"));
        }

        try
        {
            var list = _pc.ListTemplatesAsync(CancellationToken.None).GetAwaiter().GetResult();
            return Json(200, new { templates = list.Select(t => new { t.Name, t.Group, t.UpdatedAt }) });
        }
        catch (Exception ex)
        {
            return Json(502, new ErrorView(JobErrorCodes.InvalidRequest, $"PC 服务访问失败：{ex.Message}"));
        }
    }

    /// <summary>PDA 打印测试：拉模板详情 → 用服务端 testData 本地打印 → 日志回传 PC。</summary>
    private (int, string, byte[]) PcPrintTest(string name)
    {
        if (_pc is null)
        {
            return Json(400, new ErrorView(JobErrorCodes.InvalidRequest, "未配置 PC 单机服务地址（pc_host）。"));
        }

        try
        {
            var template = _pc.GetTemplateAsync(name, CancellationToken.None).GetAwaiter().GetResult();
            if (template is null)
            {
                return Json(404, new ErrorView(JobErrorCodes.JobNotFound, $"PC 上不存在模板：{name}。"));
            }

            if (template.Contract is null || template.Layout is null)
            {
                return Json(400, new ErrorView(JobErrorCodes.InvalidRequest, "模板详情不完整（缺 contract / layout）。"));
            }

            var testData = template.TestData ?? new Dictionary<string, string>();
            var request = new SubmitJobRequest(
                $"pc-test-{Guid.NewGuid():N}",
                new TemplateDto(template.Contract, template.Layout),
                new List<LabelDto> { new(testData) });
            var result = _submission.SubmitAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            if (result.Job is null)
            {
                _pc.PushLogsAsync([$"打印测试失败：{result.ErrorMessage}"], CancellationToken.None).GetAwaiter().GetResult();
                return Json(400, new ErrorView(result.ErrorCode!, result.ErrorMessage!, result.FieldKey));
            }

            var job = result.Job;
            _pc.PushLogsAsync(
                [$"打印测试已提交：{name}（{job.Id}，测试数据 {testData.Count} 个字段）"],
                CancellationToken.None).GetAwaiter().GetResult();
            _ = Task.Run(() => ReportPrintResultAsync(job.Id, name));
            return Json(result.Created ? 202 : 200, JobViews.From(job));
        }
        catch (Exception ex)
        {
            _pc.PushLogsAsync([$"打印测试异常：{ex.Message}"], CancellationToken.None).GetAwaiter().GetResult();
            return Json(502, new ErrorView(JobErrorCodes.InvalidRequest, $"打印测试异常：{ex.Message}"));
        }
    }

    /// <summary>异步轮询作业终态并回传结果日志。</summary>
    private async Task ReportPrintResultAsync(string jobId, string name)
    {
        try
        {
            for (var i = 0; i < 120; i++)
            {
                await Task.Delay(500);
                var job = _queue.GetAsync(jobId, CancellationToken.None).GetAwaiter().GetResult();
                if (job is null)
                {
                    return;
                }

                if (job.Status is LabelJobStatus.Completed or LabelJobStatus.Failed or LabelJobStatus.Cancelled)
                {
                    var completed = job.Items.Count(i => i.Status == LabelJobItemStatus.Completed);
                    var error = job.Items.FirstOrDefault(x => x.ErrorMessage is not null)?.ErrorMessage;
                    await _pc!.PushLogsAsync(
                        [$"打印测试终态：{name} {job.Status}（{completed}/{job.Items.Count}）{error ?? string.Empty}"],
                        CancellationToken.None);
                    return;
                }
            }
        }
        catch
        {
            // 轮询失败不打断
        }
    }

    private static (int, string, byte[]) Html(int status, string html)
        => (status, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));

    /// <summary>PDA 测试页：模板列表 → 点击测试打印（拉取 PC 模板 + testData → 本地打印 → 日志回传）。</summary>
    private const string PcTestPageHtml = """
        <!DOCTYPE html>
        <html lang="zh-CN">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>PDA 打印测试</title>
        <style>
          body { font-family: sans-serif; margin: 12px; }
          h1 { font-size: 18px; }
          #list { list-style: none; padding: 0; }
          #list li { margin: 6px 0; }
          #list button { width: 100%; padding: 12px; font-size: 15px; text-align: left; }
          #status { margin: 10px 0; color: #555; white-space: pre-wrap; }
        </style>
        </head>
        <body>
        <h1>PDA 打印测试</h1>
        <div id="status">加载中…</div>
        <ul id="list"></ul>
        <script>
        const statusEl = document.getElementById('list');
        async function load() {
          const box = document.getElementById('status');
          try {
            const res = await fetch('/api/pc/templates');
            const data = await res.json();
            box.textContent = '共 ' + (data.templates || []).length + ' 个模板（来自 PC 单机服务）';
            statusEl.innerHTML = '';
            (data.templates || []).forEach(t => {
              const li = document.createElement('li');
              const btn = document.createElement('button');
              btn.textContent = t.name + (t.group ? '（' + t.group + '）' : '');
              btn.onclick = () => printTest(t.name);
              li.appendChild(btn);
              statusEl.appendChild(li);
            });
          } catch (ex) { box.textContent = '加载失败：' + ex.message; }
        }
        async function printTest(name) {
          const box = document.getElementById('status');
          box.textContent = '正在测试打印：' + name + ' …';
          try {
            const res = await fetch('/api/pc/templates/' + encodeURIComponent(name) + '/print-test', { method: 'POST' });
            const job = await res.json();
            if (!res.ok) { box.textContent = '失败：' + (job.message || res.status); return; }
            box.textContent = '已提交 ' + job.jobId + '，等待打印结果…';
            const timer = setInterval(async () => {
              const r = await fetch('/api/jobs/' + job.jobId);
              const j = await r.json();
              if (j.status === 'Completed' || j.status === 'Failed' || j.status === 'Cancelled') {
                clearInterval(timer);
                const err = (j.items || []).find(x => x.errorMessage)?.errorMessage || '';
                box.textContent = j.status + '（' + j.completedItems + '/' + j.totalItems + '）' + err;
              } else {
                box.textContent = j.status + '…（' + j.completedItems + '/' + j.totalItems + '）';
              }
            }, 1000);
          } catch (ex) { box.textContent = '异常：' + ex.message; }
        }
        load();
        </script>
        </body>
        </html>
        """;

    private static (int, string, byte[]) Json(int status, object value)
    {
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, HostJson.Options);
        return (status, "application/json; charset=utf-8", bytes);
    }

    private static async Task WriteResponseAsync(Stream stream, (int Status, string ContentType, byte[] Body) response, CancellationToken cancellationToken)
    {
        var reason = response.Status switch
        {
            200 => "OK",
            202 => "Accepted",
            400 => "Bad Request",
            404 => "Not Found",
            409 => "Conflict",
            500 => "Internal Server Error",
            _ => "OK",
        };
        var head = $"HTTP/1.1 {response.Status} {reason}\r\n" +
                   $"Content-Type: {response.ContentType}\r\n" +
                   $"Content-Length: {response.Body.Length}\r\n" +
                   "Connection: close\r\n\r\n";
        var headBytes = Encoding.UTF8.GetBytes(head);
        await stream.WriteAsync(headBytes, cancellationToken);
        await stream.WriteAsync(response.Body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}