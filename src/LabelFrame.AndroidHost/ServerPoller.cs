using System.Net.Http.Json;
using System.Text.Json;
using LabelFrame.AndroidHost.Api;
using LabelFrame.Core.Layout;

namespace LabelFrame.AndroidHost;

/// <summary>设备领取到的 Server 作业。</summary>
public sealed record PendingJob(string JobId, string RequestId, int TotalItems, TemplateDto Template, IReadOnlyList<LabelDto> Labels);

/// <summary>回报结果。</summary>
public sealed record JobResult(string Status, int CompletedItems, int FailedItems, string? ErrorMessage);

/// <summary>Server 轮询客户端：注册 / 领取定向作业 / 回报结果（与 WinHost 同构，内联实现）。</summary>
public sealed class ServerPoller
{
    private readonly HttpClient _http;
    private readonly string _serverUrl;
    private readonly string _deviceId;
    private readonly string _deviceName;
    private readonly JsonSerializerOptions _json;

    /// <summary>创建轮询客户端。</summary>
    public ServerPoller(string serverUrl, string deviceId, string? deviceName = null)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _serverUrl = serverUrl.TrimEnd('/');
        _deviceId = deviceId;
        _deviceName = string.IsNullOrWhiteSpace(deviceName) ? deviceId : deviceName;
        _json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters =
            {
                new System.Text.Json.Serialization.JsonStringEnumConverter(),
                new LabelElementJsonConverter(),
            },
        };
    }

    /// <summary>注册设备（同时作为心跳）。</summary>
    public async Task RegisterAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"{_serverUrl}/api/devices",
            new { deviceId = _deviceId, name = _deviceName },
            _json,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>领取本设备的定向作业。</summary>
    public async Task<IReadOnlyList<PendingJob>> FetchPendingAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"{_serverUrl}/api/devices/{_deviceId}/jobs/pending", cancellationToken);
        response.EnsureSuccessStatusCode();
        var jobs = await response.Content.ReadFromJsonAsync<List<ClaimedJobDto>>(_json, cancellationToken) ?? [];
        return jobs
            .Where(j => j.JobId is not null && j.RequestId is not null && j.Payload?.Template is not null && j.Payload.Labels is not null)
            .Select(j => new PendingJob(j.JobId!, j.RequestId!, j.TotalItems, j.Payload!.Template!, j.Payload.Labels!))
            .ToList();
    }

    /// <summary>回报作业结果。</summary>
    public async Task ReportResultAsync(string jobId, JobResult result, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"{_serverUrl}/api/devices/{_deviceId}/jobs/{jobId}/result",
            new
            {
                status = result.Status,
                completedItems = result.CompletedItems,
                failedItems = result.FailedItems,
                errorMessage = result.ErrorMessage,
            },
            _json,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed record ClaimedJobDto(string? JobId, string? RequestId, int TotalItems, JobPayloadDto? Payload);

    private sealed record JobPayloadDto(TemplateDto? Template, IReadOnlyList<LabelDto>? Labels);
}