using System.Net;
using System.Net.Sockets;
using System.Text;
using LabelFrame.AndroidHost.Api;
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
    private readonly ILabelJobStore _store;
    private readonly IPrintTransport _transport;
    private readonly IPrinterStatusProvider? _status;
    private readonly Android.Content.Context _context;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>创建本地 HTTP 服务。</summary>
    public EmbeddedHttpServer(
        int port,
        SubmissionService submission,
        LabelJobQueue queue,
        ILabelJobStore store,
        IPrintTransport transport,
        Android.Content.Context context)
    {
        _port = port;
        _submission = submission;
        _queue = queue;
        _store = store;
        _transport = transport;
        _status = transport as IPrinterStatusProvider;
        _context = context;
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
            var (method, path, headers, body) = await ReadRequestAsync(stream, cancellationToken);
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

    /// <summary>
    /// 统一缓冲读取请求：请求头与请求体从同一份累积字节中解析。
    /// 不能先 StreamReader 读头再读体——StreamReader 预读缓冲会把请求体一并吞掉，
    /// 后续按 Content-Length 直读网络流将永久等待（POST 带体请求挂死）。
    /// </summary>
    private static async Task<(string Method, string Path, Dictionary<string, string> Headers, string Body)> ReadRequestAsync(
        Stream stream, CancellationToken cancellationToken)
    {
        const int MaxHeaderBytes = 64 * 1024;
        const int MaxBodyBytes = 32 * 1024 * 1024;

        var received = new List<byte>(2048);
        var buffer = new byte[4096];
        var headerEnd = -1;
        while (headerEnd < 0)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (n == 0)
            {
                throw new InvalidDataException("连接在请求头结束前关闭。");
            }

            received.AddRange(buffer.AsSpan(0, n));
            if (received.Count > MaxHeaderBytes)
            {
                throw new InvalidDataException("请求头超出大小上限。");
            }

            headerEnd = IndexOfHeaderTerminator(received);
        }

        var headerText = Encoding.UTF8.GetString([.. received.Take(headerEnd)]);
        var lines = headerText.Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length < 3)
        {
            throw new InvalidDataException("请求行不合法。");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var idx = line.IndexOf(':');
            if (idx > 0)
            {
                headers[line[..idx].Trim()] = line[(idx + 1)..].Trim();
            }
        }

        var body = string.Empty;
        if (headers.TryGetValue("Content-Length", out var lengthText)
            && int.TryParse(lengthText, out var length) && length > 0)
        {
            if (length > MaxBodyBytes)
            {
                throw new InvalidDataException("请求体超出大小上限。");
            }

            var bodyBytes = new byte[length];
            var buffered = Math.Min(received.Count - headerEnd - 4, length);
            for (var i = 0; i < buffered; i++)
            {
                bodyBytes[i] = received[headerEnd + 4 + i];
            }

            var read = buffered;
            while (read < length)
            {
                var n = await stream.ReadAsync(bodyBytes.AsMemory(read, length - read), cancellationToken);
                if (n == 0)
                {
                    break;
                }

                read += n;
            }

            body = Encoding.UTF8.GetString(bodyBytes, 0, read);
        }

        return (parts[0].ToUpperInvariant(), parts[1], headers, body);
    }

    /// <summary>在已接收字节中查找请求头结束符 \r\n\r\n 的起始下标。</summary>
    private static int IndexOfHeaderTerminator(List<byte> received)
    {
        for (var i = 0; i <= received.Count - 4; i++)
        {
            if (received[i] == 13 && received[i + 1] == 10 && received[i + 2] == 13 && received[i + 3] == 10)
            {
                return i;
            }
        }

        return -1;
    }

    private (int Status, string ContentType, byte[] Body) Route(
        string method, string path, string body, CancellationToken cancellationToken)
    {
        var basePath = path.Split('?')[0];

        // JS 桥（第三方 WebView / 浏览器页面跨源直连）：宽松 CORS 预检直接放行
        if (method == "OPTIONS")
        {
            return (204, "text/plain; charset=utf-8", []);
        }

        if (method == "GET" && basePath == "/healthz")
        {
            return Json(200, new { service = "LabelFrame.AndroidHost", status = "ok" });
        }

        if (method == "GET" && basePath == "/")
        {
            return Html(200, StatusPageHtml);
        }

        if (method == "POST" && basePath == "/api/jobs")
        {
            return SubmitJob(body);
        }

        if (method == "GET" && basePath == "/api/jobs")
        {
            return ListJobs(path);
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

        if (method == "POST" && basePath.StartsWith("/api/jobs/", StringComparison.Ordinal) && basePath.EndsWith("/retry", StringComparison.Ordinal))
        {
            return RetryItem(basePath);
        }

        if (method == "GET" && basePath == "/api/printer/status")
        {
            return GetPrinterStatus();
        }

        if (method == "POST" && basePath == "/api/printer/test")
        {
            return TestPrinter(cancellationToken);
        }

        // ---- 宿主配置（与 WinHost GET/POST /api/host/config 同构；保存后重启宿主生效）----
        if (method == "GET" && basePath == "/api/host/config")
        {
            return Json(200, HostConfigView.From(LabelHostConfig.Load(_context)));
        }

        if (method == "POST" && basePath == "/api/host/config")
        {
            return SaveHostConfig(body);
        }

        // ---- 测试打印（配置页 / 状态页共用：内置测试标签走完整链路）----
        if (method == "POST" && basePath == "/api/host/test-print")
        {
            return TestPrint();
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

    /// <summary>作业列表（与 WinHost GET /api/jobs?limit= 同构）。</summary>
    private (int, string, byte[]) ListJobs(string path)
    {
        var query = path.Contains('?', StringComparison.Ordinal) ? path.Split('?', 2)[1] : string.Empty;
        var limit = 100;
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0] == "limit" && int.TryParse(kv[1], out var parsed))
            {
                limit = Math.Clamp(parsed, 1, 500);
            }
        }

        var jobs = _store.ListRecentAsync(limit, CancellationToken.None).GetAwaiter().GetResult();
        return Json(200, jobs.Select(JobViews.From).ToList());
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

    /// <summary>失败项单独重打：把指定序号的 Failed Item 重置为 Pending（与 WinHost 端点同构）。</summary>
    private (int, string, byte[]) RetryItem(string basePath)
    {
        // 路径形如 /api/jobs/{jobId}/items/{itemIndex}/retry
        var middle = basePath["/api/jobs/".Length..];
        var parts = middle.Split('/');
        if (parts.Length != 4 || parts[1] != "items" || !int.TryParse(parts[2], out var itemIndex))
        {
            return Json(404, new ErrorView(JobErrorCodes.JobNotFound, "接口不存在。"));
        }

        try
        {
            var job = _queue.RetryItemAsync(parts[0], itemIndex, CancellationToken.None).GetAwaiter().GetResult();
            return Json(200, JobViews.From(job));
        }
        catch (LabelJobException ex)
        {
            return ex.Code == JobErrorCodes.JobNotFound
                ? Json(404, new ErrorView(ex.Code, ex.Message))
                : Json(409, new ErrorView(ex.Code, ex.Message));
        }
    }

    /// <summary>宿主配置视图（GET /api/host/config 响应形状）。</summary>
    private sealed record HostConfigView(
        string TcpHost, int TcpPort, string PrinterBrand, string ConnectionType,
        string ServerUrl, string DeviceId, string DeviceName, string LocalPort)
    {
        public static HostConfigView From(LabelHostConfig config)
            => new(
                config.TcpHost, config.TcpPort, config.PrinterBrand, config.ConnectionType,
                config.ServerUrl, config.DeviceId, config.DeviceName,
                $"127.0.0.1:{LabelHostConfig.LocalPort}");
    }

    /// <summary>保存宿主配置到 SharedPreferences（null / 空白 / 越界字段保持原值）；传输与路由在下次宿主启动时按新配置创建。</summary>
    private (int, string, byte[]) SaveHostConfig(string body)
    {
        try
        {
            var dto = System.Text.Json.JsonSerializer.Deserialize<HostConfigUpdateDto>(body, HostJson.Options);
            var config = LabelHostConfig.Load(_context);
            config.Persist(
                _context,
                dto?.ServerUrl,
                dto?.PrinterBrand,
                dto?.ConnectionType,
                dto?.TcpHost,
                dto?.TcpPort,
                dto?.DeviceName);
            return Json(200, HostConfigView.From(config));
        }
        catch (Exception ex)
        {
            return Json(400, new ErrorView(JobErrorCodes.InvalidRequest, $"配置解析失败：{ex.Message}"));
        }
    }

    private sealed record HostConfigUpdateDto(
        string? ServerUrl, string? PrinterBrand, string? ConnectionType,
        string? TcpHost, int? TcpPort, string? DeviceName);

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

    /// <summary>测试打印：提交内置测试标签作业（校验 → 渲染 → ^GF → TCP 发送），返回作业供调用方轮询终态。</summary>
    private (int, string, byte[]) TestPrint()
    {
        var request = new SubmitJobRequest(
            $"self-test-{Guid.NewGuid():N}",
            new TemplateDto(TestLabelTemplate.Contract, TestLabelTemplate.Layout),
            new List<LabelDto> { new(TestLabelTemplate.SampleData()) });
        var result = _submission.SubmitAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        if (result.Job is null)
        {
            return Json(400, new ErrorView(result.ErrorCode!, result.ErrorMessage!, result.FieldKey));
        }

        return Json(202, JobViews.From(result.Job));
    }

    private static (int, string, byte[]) Html(int status, string html)
        => (status, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));

    /// <summary>宿主状态页：运行状态 + 配置概览 + 测试打印（轻量单页；第三方集成走同端口 HTTP API）。</summary>
    private const string StatusPageHtml = """
        <!DOCTYPE html>
        <html lang="zh-CN">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>LabelFrame 宿主状态</title>
        <style>
          body { font-family: sans-serif; margin: 12px; color: #222; }
          h1 { font-size: 18px; }
          .card { border: 1px solid #ddd; border-radius: 8px; padding: 10px 12px; margin: 10px 0; }
          .row { margin: 4px 0; font-size: 14px; }
          .row b { display: inline-block; min-width: 5.5em; color: #666; font-weight: 600; }
          button { padding: 10px 16px; font-size: 15px; margin: 6px 8px 0 0; }
          #result { margin-top: 8px; color: #555; white-space: pre-wrap; }
        </style>
        </head>
        <body>
        <h1>LabelFrame 宿主状态</h1>
        <div class="card" id="config"><div class="row">加载中…</div></div>
        <div class="card">
          <div class="row"><b>打印机状态</b><span id="printer">探测中…</span></div>
        </div>
        <button id="print">测试打印</button>
        <div id="result"></div>
        <p style="color:#999;font-size:12px;margin-top:16px">
          本页为宿主状态页；第三方程序集成请走同端口 HTTP API（POST /api/jobs 直连打印，契约见仓库文档）。
        </p>
        <script>
        const resultEl = document.getElementById('result');
        async function loadConfig() {
          try {
            const res = await fetch('/api/host/config');
            const c = await res.json();
            document.getElementById('config').innerHTML =
              '<div class="row"><b>设备号</b>' + c.DeviceId + '</div>' +
              '<div class="row"><b>设备名称</b>' + c.DeviceName + '</div>' +
              '<div class="row"><b>服务端</b>' + (c.ServerUrl || '未配置') + '</div>' +
              '<div class="row"><b>打印机</b>' + c.TcpHost + ':' + c.TcpPort + '（' + c.PrinterBrand + ' · ' + c.ConnectionType.toUpperCase() + '）</div>';
          } catch (ex) { document.getElementById('config').textContent = '加载失败：' + ex.message; }
        }
        async function loadPrinter() {
          try {
            const res = await fetch('/api/printer/status');
            const s = await res.json();
            const text = s.IsOnline
              ? '在线' + (s.Message ? '（' + s.Message + '）' : '') + (s.IsPaperOut ? ' · 缺纸' : '') + (s.IsPaused ? ' · 暂停' : '')
              : '离线' + (s.Message ? '（' + s.Message + '）' : '');
            document.getElementById('printer').textContent = text;
          } catch (ex) { document.getElementById('printer').textContent = '探测失败：' + ex.message; }
        }
        document.getElementById('print').onclick = async () => {
          resultEl.textContent = '提交测试作业…';
          try {
            const res = await fetch('/api/host/test-print', { method: 'POST' });
            const job = await res.json();
            if (!res.ok) { resultEl.textContent = '提交失败：' + (job.Message || res.status); return; }
            const timer = setInterval(async () => {
              const r = await fetch('/api/jobs/' + job.JobId);
              const j = await r.json();
              if (j.Status === 'Completed' || j.Status === 'Failed' || j.Status === 'Cancelled') {
                clearInterval(timer);
                const err = (j.Items || []).find(x => x.ErrorMessage)?.ErrorMessage || '';
                resultEl.textContent = (j.Status === 'Completed' ? '✓ 打印完成' : '✗ ' + j.Status) +
                  '（' + j.CompletedItems + '/' + j.TotalItems + '）' + (err ? '：' + err : '');
              } else {
                resultEl.textContent = '打印中…（' + j.CompletedItems + '/' + j.TotalItems + '）';
              }
            }, 1000);
          } catch (ex) { resultEl.textContent = '异常：' + ex.message; }
        };
        loadConfig();
        loadPrinter();
        setInterval(loadPrinter, 10000);
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
            204 => "No Content",
            400 => "Bad Request",
            404 => "Not Found",
            409 => "Conflict",
            500 => "Internal Server Error",
            _ => "OK",
        };
        var head = $"HTTP/1.1 {response.Status} {reason}\r\n" +
                   $"Content-Type: {response.ContentType}\r\n" +
                   $"Content-Length: {response.Body.Length}\r\n" +
                   "Access-Control-Allow-Origin: *\r\n" +
                   "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                   "Access-Control-Allow-Headers: Content-Type\r\n" +
                   "Connection: close\r\n\r\n";
        var headBytes = Encoding.UTF8.GetBytes(head);
        await stream.WriteAsync(headBytes, cancellationToken);
        await stream.WriteAsync(response.Body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
