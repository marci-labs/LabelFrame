using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;

namespace LabelFrame.Studio.Services;

/// <summary>模板摘要。</summary>
public sealed record TemplateSummaryDto(string Name, string Group, DateTimeOffset UpdatedAt);

/// <summary>作业视图。</summary>
public sealed record JobViewDto(
    string JobId,
    string RequestId,
    string Status,
    int TotalItems,
    int CompletedItems,
    IReadOnlyList<JobItemViewDto> Items);

/// <summary>单张标签视图。</summary>
public sealed record JobItemViewDto(int Index, string Status, string? ErrorCode, string? ErrorMessage);

/// <summary>打印机状态。</summary>
public sealed record PrinterStatusDto(bool IsOnline, bool IsPaperOut, bool IsPaused, string? Message);

/// <summary>测试打印结果。</summary>
public sealed record TestPrintDto(bool Sent, int Bytes);

/// <summary>健康检查信息（含传输模式）。</summary>
public sealed record HealthDto(string Service, string Status, string? Transport);

/// <summary>模板提交体（与 WinHost API 同构）。</summary>
public sealed record TemplateSaveDto(string? Name, string? Group, LabelContract? Contract, LabelLayout? Layout);

/// <summary>
/// WinHost API 客户端：模板管理 / 导入导出 / 预览 / 作业 / 打印机。
/// </summary>
public sealed class StudioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new System.Text.Json.Serialization.JsonStringEnumConverter(),
            new LabelElementJsonConverter(),
        },
    };

    private readonly HttpClient _http;

    /// <summary>创建客户端。</summary>
    /// <param name="baseUrl">WinHost 地址（如 http://127.0.0.1:53960）。</param>
    public StudioClient(string baseUrl)
        : this(new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30),
        })
    {
    }

    /// <summary>创建客户端（注入 HttpClient，便于测试）。</summary>
    public StudioClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>健康检查（含传输模式）。</summary>
    public async Task<HealthDto> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var health = await _http.GetFromJsonAsync<HealthDto>("healthz", JsonOptions, cancellationToken);
        return health ?? throw new InvalidOperationException("健康检查无响应。");
    }

    /// <summary>保存（upsert）模板。</summary>
    public async Task SaveTemplateAsync(TemplateSaveDto template, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("api/templates", template, JsonOptions, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"保存失败（{(int)response.StatusCode}）：{body}");
        }
    }

    /// <summary>模板列表（可按分组过滤）。</summary>
    public async Task<IReadOnlyList<TemplateSummaryDto>> ListTemplatesAsync(string? group = null, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(group) ? "api/templates" : $"api/templates?group={Uri.EscapeDataString(group)}";
        return await _http.GetFromJsonAsync<List<TemplateSummaryDto>>(url, JsonOptions, cancellationToken) ?? [];
    }

    /// <summary>模板详情。</summary>
    public async Task<TemplateSaveDto?> GetTemplateAsync(string name, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/templates/{Uri.EscapeDataString(name)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TemplateSaveDto>(JsonOptions, cancellationToken);
    }

    /// <summary>删除模板。</summary>
    public async Task DeleteTemplateAsync(string name, CancellationToken cancellationToken = default)
    {
        var response = await _http.DeleteAsync($"api/templates/{Uri.EscapeDataString(name)}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>导入模板包（.lfpkg zip）。</summary>
    public async Task<string> ImportTemplateAsync(byte[] zipBytes, string fileName, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(zipBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(fileContent, "file", fileName);
        var response = await _http.PostAsync("api/templates/import", content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"导入失败（{(int)response.StatusCode}）：{body}");
        }

        return body.Trim('"');
    }

    /// <summary>导出模板包（.lfpkg zip）。</summary>
    public async Task<byte[]> ExportTemplateAsync(string name, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/templates/{Uri.EscapeDataString(name)}/export", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"导出失败（{(int)response.StatusCode}）。");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>预览 PNG。</summary>
    public async Task<byte[]> PreviewAsync(string name, IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"api/templates/{Uri.EscapeDataString(name)}/preview",
            new { data },
            JsonOptions,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"预览失败（{(int)response.StatusCode}）：{body}");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>提交打印作业。</summary>
    public async Task<JobViewDto> SubmitJobAsync(
        string requestId,
        TemplateSaveDto template,
        IReadOnlyList<Dictionary<string, string>> labels,
        CancellationToken cancellationToken = default)
    {
        var request = new
        {
            requestId,
            template,
            labels = labels.Select(d => new { data = d }).ToList(),
        };
        var response = await _http.PostAsJsonAsync("api/jobs", request, JsonOptions, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"提交失败（{(int)response.StatusCode}）：{body}");
        }

        return JsonSerializer.Deserialize<JobViewDto>(body, JsonOptions)
            ?? throw new InvalidOperationException("提交响应解析失败。");
    }

    /// <summary>查询作业状态。</summary>
    public async Task<JobViewDto> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
        => await _http.GetFromJsonAsync<JobViewDto>($"api/jobs/{jobId}", JsonOptions, cancellationToken)
           ?? throw new InvalidOperationException("作业不存在。");

    /// <summary>打印机状态。</summary>
    public async Task<PrinterStatusDto> GetPrinterStatusAsync(CancellationToken cancellationToken = default)
        => await _http.GetFromJsonAsync<PrinterStatusDto>("api/printer/status", JsonOptions, cancellationToken)
           ?? throw new InvalidOperationException("状态查询无响应。");

    /// <summary>打印机测试页。</summary>
    public async Task<TestPrintDto> TestPrinterAsync(CancellationToken cancellationToken = default)
        => await _http.GetFromJsonAsync<TestPrintDto>("api/printer/test", JsonOptions, cancellationToken)
           ?? throw new InvalidOperationException("测试页无响应。");
}