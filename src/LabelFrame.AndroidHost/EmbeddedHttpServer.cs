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
    private readonly IPrintTransport _transport;
    private readonly IPrinterStatusProvider? _status;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>创建本地 HTTP 服务。</summary>
    public EmbeddedHttpServer(int port, SubmissionService submission, LabelJobQueue queue, IPrintTransport transport)
    {
        _port = port;
        _submission = submission;
        _queue = queue;
        _transport = transport;
        _status = transport as IPrinterStatusProvider;
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