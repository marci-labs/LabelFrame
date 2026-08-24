using System.Net.Http;
using System.Net.Http.Json;
using LabelFrame.AndroidHost.Api;
using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;

namespace LabelFrame.AndroidHost.Pc;

/// <summary>PC 单机服务模板摘要。</summary>
public sealed record PcTemplateSummary(string Name, string Group, DateTimeOffset UpdatedAt);

/// <summary>PC 单机服务模板详情（含测试数据）。</summary>
public sealed record PcTemplatePackage(
    string? Name,
    string? Group,
    LabelContract? Contract,
    LabelLayout? Layout,
    IReadOnlyDictionary<string, string>? TestData = null);

/// <summary>PDA 测试模式客户端：从 PC 单机服务拉模板、回传日志。</summary>
public sealed class PcTemplateClient
{
    private readonly HttpClient _http;
    private readonly string _deviceId;

    /// <summary>创建客户端。</summary>
    /// <param name="baseUrl">PC 单机服务地址（如 http://192.168.1.10:53960）。</param>
    /// <param name="deviceId">本机设备标识（日志回传用）。</param>
    public PcTemplateClient(string baseUrl, string deviceId)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(15),
        };
        _deviceId = deviceId;
    }

    /// <summary>拉取模板列表。</summary>
    public async Task<IReadOnlyList<PcTemplateSummary>> ListTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var list = await _http.GetFromJsonAsync<List<PcTemplateSummary>>("api/templates", HostJson.Options, cancellationToken);
        return list ?? [];
    }

    /// <summary>拉取模板详情（含 testData）。</summary>
    public async Task<PcTemplatePackage?> GetTemplateAsync(string name, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/templates/{Uri.EscapeDataString(name)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PcTemplatePackage>(HostJson.Options, cancellationToken);
    }

    /// <summary>回传日志到 PC（PDA 调试用）。</summary>
    public async Task PushLogsAsync(IReadOnlyList<string> lines, CancellationToken cancellationToken = default)
    {
        try
        {
            await _http.PostAsJsonAsync("api/logs", new { deviceId = _deviceId, lines }, HostJson.Options, cancellationToken);
        }
        catch
        {
            // 日志回传失败不影响打印
        }
    }
}
