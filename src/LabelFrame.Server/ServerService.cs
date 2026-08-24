using LabelFrame.Api;

namespace LabelFrame.Server;

/// <summary>
/// Server 业务服务：设备注册 / 心跳、作业定向投递（宿主轮询领取）、结果回报、集中查询。
/// 设备离线时作业在 Server 暂存（Pending），上线轮询即领取。
/// </summary>
public sealed class ServerService
{
    /// <summary>在线窗口：超过该时长未心跳视为离线。</summary>
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(2);

    private readonly ServerDb _db;
    private readonly LabelFrame.Core.Templates.TemplateStore _templates;
    private readonly PendingJobNotifier? _notifier;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>创建业务服务。</summary>
    /// <param name="templates">服务端模板库（templateName 引用提交用；可为空则不启用引用）。</param>
    /// <param name="notifier">待领取作业通知器（长轮询推送用；为空则不通知）。</param>
    public ServerService(ServerDb db, LabelFrame.Core.Templates.TemplateStore? templates = null, PendingJobNotifier? notifier = null)
    {
        _db = db;
        _templates = templates!;
        _notifier = notifier;
    }

    /// <summary>注册 / 更新设备并刷新心跳。</summary>
    public async Task<DeviceView> RegisterDeviceAsync(string? deviceId, string? name, string? lastIp = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ServerException(ServerErrorCodes.InvalidRequest, "缺少 deviceId。");
        }

        var now = DateTimeOffset.UtcNow;
        var device = await _db.UpsertDeviceAsync(new Device
        {
            Id = deviceId,
            Name = string.IsNullOrWhiteSpace(name) ? deviceId : name,
            RegisteredAt = now,
            LastSeenAt = now,
            LastIp = NormalizeIpText(lastIp),
        }, cancellationToken);
        return ToView(device, now);
    }

    /// <summary>刷新设备心跳（长轮询通知端点用作在线保活）。设备未注册时抛出 DeviceNotFound。</summary>
    public async Task TouchDeviceAsync(string deviceId, DateTimeOffset now, string? lastIp = null, CancellationToken cancellationToken = default)
    {
        var affected = await _db.TouchDeviceAsync(deviceId, now, NormalizeIpText(lastIp), cancellationToken);
        if (affected == 0)
        {
            throw new ServerException(ServerErrorCodes.DeviceNotFound, $"设备未注册：{deviceId}。");
        }
    }

    /// <summary>设备目录（含在线状态）。</summary>
    public async Task<IReadOnlyList<DeviceView>> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var devices = await _db.ListDevicesAsync(cancellationToken);
        return devices.Select(d => ToView(d, now)).ToList();
    }

    /// <summary>按 last_ip 精确查找设备（忽略大小写；未找到返回 null）。</summary>
    public async Task<DeviceView?> FindDeviceByIpAsync(string ip, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeIpText(ip);
        if (normalized is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var device = await _db.FindDeviceByIpAsync(normalized, cancellationToken);
        return device is null ? null : ToView(device, now);
    }

    /// <summary>提交作业（幂等 requestId）；目标设备未注册时拒绝。</summary>
    public async Task<ServerJobView> SubmitJobAsync(SubmitJobRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            throw new ServerException(ServerErrorCodes.InvalidRequest, "缺少 requestId（幂等键）。");
        }

        // 目标设备解析：targetDeviceId 优先；未提供时按 targetIp 查找（找不到 404）。
        var targetDeviceId = request.TargetDeviceId;
        if (string.IsNullOrWhiteSpace(targetDeviceId))
        {
            if (string.IsNullOrWhiteSpace(request.TargetIp))
            {
                throw new ServerException(ServerErrorCodes.InvalidRequest, "缺少 targetDeviceId / targetIp（定向投递目标）。");
            }

            var normalizedIp = NormalizeIpText(request.TargetIp) ?? request.TargetIp;
            var byIp = await _db.FindDeviceByIpAsync(normalizedIp, cancellationToken);
            if (byIp is null)
            {
                throw new ServerException(ServerErrorCodes.DeviceNotFound, $"按 IP 未找到设备：{request.TargetIp}。");
            }

            targetDeviceId = byIp.Id;
        }

        var template = await ResolveTemplateAsync(request, cancellationToken);
        if (template is null)
        {
            throw new ServerException(ServerErrorCodes.InvalidRequest, "缺少 template（contract + layout）或 templateName。");
        }

        if (request.Labels is null || request.Labels.Count == 0)
        {
            throw new ServerException(ServerErrorCodes.InvalidRequest, "缺少 labels（至少一张）。");
        }

        // 门只覆盖提交路径：幂等 requestId 为「查询-再插入」两步，进程内串行化避免并发重放双双插入
        //（DB 层 request_id UNIQUE 兜底跨进程重复）；注册 / 领取 / 回报路径无 check-then-act，已不再串行化
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (await _db.GetDeviceAsync(targetDeviceId, cancellationToken) is null)
            {
                throw new ServerException(ServerErrorCodes.DeviceNotFound, $"目标设备未注册：{targetDeviceId}。");
            }

            var existing = await _db.GetJobByRequestIdAsync(request.RequestId, cancellationToken);
            if (existing is not null)
            {
                return await ToJobViewAsync(existing, cancellationToken);
            }

            var payloadJson = System.Text.Json.JsonSerializer.Serialize(
                new JobPayload(template, request.Labels),
                RoutingJson.Options);

            var job = await _db.CreateJobAsync(new ServerJob
            {
                Id = Guid.NewGuid().ToString("N"),
                RequestId = request.RequestId,
                TargetDeviceId = targetDeviceId,
                Status = ServerJobStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
                TotalItems = request.Labels.Count,
                PayloadJson = payloadJson,
            }, cancellationToken);
            _notifier?.Notify(targetDeviceId);
            return await ToJobViewAsync(job!, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>设备领取作业：刷新心跳并把该设备的 Pending 作业置为 Claimed。</summary>
    public async Task<IReadOnlyList<ClaimedJob>> ClaimPendingJobsAsync(string deviceId, string? lastIp = null, CancellationToken cancellationToken = default)
    {
        // 并发安全由 DB 层保证（领取为单条 UPDATE ... RETURNING 原子操作），无需进程内串行化
        var now = DateTimeOffset.UtcNow;
        if (await _db.TouchDeviceAsync(deviceId, now, NormalizeIpText(lastIp), cancellationToken) == 0)
        {
            throw new ServerException(ServerErrorCodes.DeviceNotFound, $"设备未注册：{deviceId}。");
        }
        var jobs = await _db.ClaimPendingJobsAsync(deviceId, now, limit: 10, cancellationToken);
        return jobs.Select(job => new ClaimedJob(
            job.Id,
            job.RequestId,
            job.TotalItems,
            System.Text.Json.JsonSerializer.Deserialize<JobPayload>(job.PayloadJson, RoutingJson.Options)!)).ToList();
    }

    /// <summary>设备回报作业结果。</summary>
    public async Task<ServerJobView> ReportResultAsync(string deviceId, string jobId, ReportResultRequest report, CancellationToken cancellationToken = default)
    {
        var job = await _db.GetJobAsync(jobId, cancellationToken)
                ?? throw new ServerException(ServerErrorCodes.JobNotFound, $"作业不存在：{jobId}。");
        if (job.TargetDeviceId != deviceId)
        {
                throw new ServerException(ServerErrorCodes.NotJobOwner, $"设备 {deviceId} 不是作业 {jobId} 的领取者。");
        }

        // 幂等重放：终态作业直接返回
        if (job.Status is ServerJobStatus.Completed or ServerJobStatus.Failed)
        {
                return await ToJobViewAsync(job, cancellationToken);
        }

        if (job.Status != ServerJobStatus.Claimed)
        {
                throw new ServerException(ServerErrorCodes.InvalidTransition, $"作业 {jobId} 当前状态 {job.Status} 不允许回报结果。");
        }

        var isCompleted = string.Equals(report.Status, "Completed", StringComparison.OrdinalIgnoreCase);
        var updated = await _db.UpdateJobResultAsync(
                jobId,
                isCompleted ? ServerJobStatus.Completed : ServerJobStatus.Failed,
                report.CompletedItems ?? 0,
                report.FailedItems ?? 0,
                report.ErrorMessage,
                DateTimeOffset.UtcNow,
                cancellationToken);
        return await ToJobViewAsync(updated!, cancellationToken);
    }

    /// <summary>查询作业（含设备在线状态）。</summary>
    public async Task<ServerJobView> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var job = await _db.GetJobAsync(jobId, cancellationToken)
            ?? throw new ServerException(ServerErrorCodes.JobNotFound, $"作业不存在：{jobId}。");
        return await ToJobViewAsync(job, cancellationToken);
    }

    /// <summary>作业列表（倒序；迭代 22：可选 deviceId 过滤——客户端只看自己的作业，服务端 UI 不传看全部）。</summary>
    public async Task<IReadOnlyList<ServerJobView>> ListJobsAsync(int limit = 100, string? deviceId = null, CancellationToken cancellationToken = default)
    {
        var jobs = await _db.ListJobsAsync(limit, deviceId, cancellationToken);
        var views = new List<ServerJobView>();
        foreach (var job in jobs)
        {
            views.Add(await ToJobViewAsync(job, cancellationToken));
        }

        return views;
    }

    private async Task<ServerJobView> ToJobViewAsync(ServerJob job, CancellationToken cancellationToken)
    {
        var device = await _db.GetDeviceAsync(job.TargetDeviceId, cancellationToken);
        var deviceStatus = device is null
            ? DeviceStatus.Offline
            : IsOnline(device, DateTimeOffset.UtcNow) ? DeviceStatus.Online : DeviceStatus.Offline;
        return new ServerJobView(
            job.Id,
            job.RequestId,
            job.TargetDeviceId,
            job.Status.ToString(),
            job.CreatedAt,
            job.TotalItems,
            job.CompletedItems,
            job.FailedItems,
            job.ErrorMessage,
            deviceStatus.ToString());
    }

    private static DeviceView ToView(Device device, DateTimeOffset now) => new(
        device.Id,
        device.Name,
        device.RegisteredAt,
        device.LastSeenAt,
        IsOnline(device, now) ? DeviceStatus.Online.ToString() : DeviceStatus.Offline.ToString(),
        device.LastIp);

    private static bool IsOnline(Device device, DateTimeOffset now)
        => now - device.LastSeenAt <= OnlineWindow + ClockSkew;

    /// <summary>规范化来源 IP：IPv4-mapped IPv6（::ffff:a.b.c.d）统一为 IPv4 文本；其余原样。</summary>
    public static string? NormalizeRemoteIp(System.Net.IPAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4().ToString();
        }

        return address.ToString();
    }

    /// <summary>规范化 IP 文本：可解析的 IP 按 NormalizeRemoteIp 处理；解析失败原样返回（查找不命中）。</summary>
    public static string? NormalizeIpText(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return null;
        }

        var trimmed = ip.Trim();
        return System.Net.IPAddress.TryParse(trimmed, out var address)
            ? NormalizeRemoteIp(address)
            : trimmed;
    }

    /// <summary>解析提交模板：templateName 引用服务端模板库（含图片 base64）；否则用自包含模板。</summary>
    private async Task<TemplateDto?> ResolveTemplateAsync(SubmitJobRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.TemplateName))
        {
            if (_templates is null)
            {
                throw new ServerException(ServerErrorCodes.InvalidRequest, "服务端未启用模板库，不能按 templateName 提交。");
            }

            var package = await _templates.GetAsync(request.TemplateName, cancellationToken);
            if (package is null)
            {
                throw new ServerException(ServerErrorCodes.TemplateNotFound, $"模板不存在：{request.TemplateName}。");
            }

            return new TemplateDto(
                package.Contract,
                package.Layout,
                package.Name,
                package.Images.ToDictionary(kv => kv.Key, kv => System.Convert.ToBase64String(kv.Value)));
        }

        if (request.Template?.Contract is null || request.Template.Layout is null)
        {
            return null;
        }

        return request.Template;
    }
}
