using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using LabelFrame.Core.Contracts;
using LabelFrame.Core.Documents;
using LabelFrame.Rendering;
using LabelFrame.Studio.Services;

namespace LabelFrame.Studio.ViewModels;


/// <summary>主窗口视图模型（V1：连接 / 模板管理 / 导入导出 / 预览 / 测试打印）。</summary>
public sealed class MainViewModel : ObservableObject
{
    private StudioClient? _client;
    private readonly LabelPreviewRenderer _renderer = new();
    private Process? _winHostProcess;

    /// <summary>日志（底部日志栏）。</summary>
    public ObservableCollection<string> Logs { get; } = [];

    /// <summary>追加日志。</summary>
    public void Log(string message)
    {
        Logs.Add($"{DateTime.Now:HH:mm:ss}  {message}");
    }

    /// <summary>清空日志。</summary>
    public void ClearLogs() => Logs.Clear();

    private string _serverUrl = "http://127.0.0.1:53960";
    private string _connectionText = "未连接";
    private string? _transportText;
    private string _winHostExe = string.Empty;
    private string? _selectedGroup;
    private TemplateSummaryDto? _selectedTemplate;
    private TemplateSaveDto? _detail;
    private string _detailText = string.Empty;
    private BitmapImage? _previewImage;
    private string _jobStatusText = string.Empty;

    public ObservableCollection<string> Groups { get; } = [];

    public ObservableCollection<TemplateSummaryDto> Templates { get; } = [];

    public ObservableCollection<FieldEntry> Fields { get; } = [];

    public string ServerUrl
    {
        get => _serverUrl;
        set => SetProperty(ref _serverUrl, value);
    }

    public string ConnectionText
    {
        get => _connectionText;
        set => SetProperty(ref _connectionText, value);
    }

    public string? TransportText
    {
        get => _transportText;
        set => SetProperty(ref _transportText, value);
    }

    public string WinHostExe
    {
        get => _winHostExe;
        set => SetProperty(ref _winHostExe, value);
    }

    public string? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                _ = RefreshTemplatesAsync();
            }
        }
    }

    public TemplateSummaryDto? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (SetProperty(ref _selectedTemplate, value))
            {
                _ = LoadDetailAsync();
            }
        }
    }

    public string DetailText
    {
        get => _detailText;
        set => SetProperty(ref _detailText, value);
    }

    public BitmapImage? PreviewImage
    {
        get => _previewImage;
        set => SetProperty(ref _previewImage, value);
    }

    public string JobStatusText
    {
        get => _jobStatusText;
        set => SetProperty(ref _jobStatusText, value);
    }

    public bool IsConnected => _client is not null;

    /// <summary>当前连接的客户端（供编辑器窗口使用）。</summary>
    public StudioClient? Client => _client;

    /// <summary>读取模板详情。</summary>
    public Task<TemplateSaveDto?> GetTemplateAsync(string name) => _client!.GetTemplateAsync(name);

    public async Task ConnectAsync()
    {
        try
        {
            _client = new StudioClient(ServerUrl);
            var health = await _client.GetHealthAsync();
            ConnectionText = $"已连接：{ServerUrl}";
            TransportText = $"传输模式：{health.Transport ?? "未知"}";
            OnPropertyChanged(nameof(IsConnected));
            Log($"已连接 WinHost：{ServerUrl}（{health.Transport ?? "未知"}）");
            await RefreshTemplatesAsync();
        }
        catch (Exception ex)
        {
            _client = null;
            ConnectionText = "连接失败";
            TransportText = null;
            throw new InvalidOperationException($"连接 WinHost 失败：{ex.Message}", ex);
        }
    }

    public void Disconnect()
    {
        _client = null;
        ConnectionText = "未连接";
        TransportText = null;
        Templates.Clear();
        Groups.Clear();
        Fields.Clear();
        DetailText = string.Empty;
        PreviewImage = null;
        JobStatusText = string.Empty;
        OnPropertyChanged(nameof(IsConnected));
    }

    public void StartWinHost()
    {
        var exe = string.IsNullOrWhiteSpace(WinHostExe) ? DetectWinHostExe() : WinHostExe;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            throw new InvalidOperationException("未找到 WinHost 可执行文件，请填写 WinHostExe 路径或先构建 WinHost。");
        }

        if (_winHostProcess is { HasExited: false })
        {
            return;
        }

        _winHostProcess = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
        });
        ConnectionText = $"已启动 WinHost（{Path.GetFileName(exe)}），请稍候连接…";
    }

    public void StopWinHost()
    {
        if (_winHostProcess is { HasExited: false })
        {
            _winHostProcess.Kill();
            _winHostProcess.WaitForExit();
        }

        _winHostProcess = null;
        ConnectionText = "未连接";
    }

    public async Task RefreshTemplatesAsync()
    {
        EnsureConnected();
        var templates = await _client!.ListTemplatesAsync(SelectedGroup);
        Templates.Clear();
        foreach (var item in templates)
        {
            Templates.Add(item);
        }

        var groups = templates.Select(t => t.Group).Distinct().OrderBy(g => g).ToList();
        Groups.Clear();
        foreach (var group in groups)
        {
            Groups.Add(group);
        }
    }

    /// <summary>导入模板包并刷新列表，返回模板名。</summary>
    public async Task<string> ImportTemplateAsync(string filePath)
    {
        EnsureConnected();
        var bytes = await File.ReadAllBytesAsync(filePath);
        var name = await _client!.ImportTemplateAsync(bytes, Path.GetFileName(filePath));
        await RefreshTemplatesAsync();
        SelectedTemplate = Templates.FirstOrDefault(t => t.Name == name);
        return name;
    }

    public async Task<byte[]> ExportAsync(string name)
    {
        EnsureConnected();
        return await _client!.ExportTemplateAsync(name);
    }

    public async Task DeleteAsync(string name)
    {
        EnsureConnected();
        await _client!.DeleteTemplateAsync(name);
        if (SelectedTemplate?.Name == name)
        {
            SelectedTemplate = null;
            DetailText = string.Empty;
            Fields.Clear();
            PreviewImage = null;
        }

        await RefreshTemplatesAsync();
    }

    /// <summary>本地实时预览（共享渲染器，无需网络）。</summary>
    public void PreviewAsync()
    {
        if (SelectedTemplate is null || _detail?.Layout is null)
        {
            PreviewImage = null;
            return;
        }

        var data = Fields.ToDictionary(f => f.Key, f => f.Value ?? string.Empty);
        var document = new LabelDocument { Layout = _detail.Layout, Data = data };
        var png = _renderer.RenderPng(document, dpi: 203, null);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(png);
        image.EndInit();
        image.Freeze();
        PreviewImage = image;
    }

    public async Task PrintTestAsync()
    {
        EnsureConnected();
        var (template, data) = RequireSelectedAndData();
        var missing = Fields.Where(f => f.IsRequired && string.IsNullOrWhiteSpace(f.Value)).Select(f => f.DisplayName).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"必填字段未填写：{string.Join("、", missing)}");
        }

        var requestId = $"studio-{Guid.NewGuid():N}";
        var job = await _client!.SubmitJobAsync(requestId, template, [data]);
        JobStatusText = $"已提交 {job.JobId}：{job.Status}（{job.CompletedItems}/{job.TotalItems}）";
        Log($"提交打印作业 {job.JobId}（{job.TotalItems} 张）。");
        _ = PollJobAsync(job.JobId);
    }

    public async Task RefreshStatusAsync()
    {
        EnsureConnected();
        var status = await _client!.GetPrinterStatusAsync();
        TransportText = status.IsOnline
            ? $"打印机在线（缺纸：{status.IsPaperOut}，暂停：{status.IsPaused}）{status.Message ?? string.Empty}"
            : $"打印机离线：{status.Message}";
    }

    private async Task LoadDetailAsync()
    {
        DetailText = string.Empty;
        Fields.Clear();
        PreviewImage = null;
        JobStatusText = string.Empty;
        if (SelectedTemplate is null)
        {
            return;
        }

        var detail = await _client!.GetTemplateAsync(SelectedTemplate.Name);
        _detail = detail;
        if (detail?.Contract is null || detail.Layout is null)
        {
            DetailText = "模板详情读取失败。";
            return;
        }

        foreach (var field in detail.Contract.Fields)
        {
            Fields.Add(new FieldEntry
            {
                Key = field.Key,
                DisplayName = field.DisplayName,
                IsRequired = field.IsRequired,
                Type = field.Type.ToString(),
            });
        }

        DetailText = FormatDetail(detail);
        PreviewAsync();
    }

    private async Task PollJobAsync(string jobId)
    {
        try
        {
            for (var i = 0; i < 60; i++)
            {
                await Task.Delay(500);
                var job = await _client!.GetJobAsync(jobId);
                JobStatusText = $"{job.JobId}：{job.Status}（{job.CompletedItems}/{job.TotalItems}）";
                var error = job.Items.FirstOrDefault(x => x.ErrorMessage is not null)?.ErrorMessage;
                if (!string.IsNullOrEmpty(error))
                {
                    JobStatusText += $"　失败：{error}";
                }

                if (job.Status is "Completed" or "Failed" or "Cancelled")
                {
                    Log($"作业 {job.JobId} 终态：{job.Status}（{job.CompletedItems}/{job.TotalItems}）。");
                    return;
                }
            }
        }
        catch
        {
            // 轮询失败不打断 UI
        }
    }

    private (TemplateSaveDto Template, Dictionary<string, string> Data) RequireSelectedAndData()
    {
        if (SelectedTemplate is null || _detail?.Contract is null || _detail.Layout is null)
        {
            throw new InvalidOperationException("请先选择模板。");
        }

        var data = Fields.ToDictionary(f => f.Key, f => f.Value ?? string.Empty);
        return (_detail, data);
    }

    private void EnsureConnected()
    {
        if (_client is null)
        {
            throw new InvalidOperationException("请先连接 WinHost。");
        }
    }

    private static string FormatDetail(TemplateSaveDto detail)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"契约：{detail.Contract!.Name} v{detail.Contract.Version}");
        sb.AppendLine("字段：");
        foreach (var field in detail.Contract.Fields)
        {
            sb.AppendLine($"  - {field.Key}（{field.DisplayName}）{(field.IsRequired ? "必填" : "可选")} [{field.Type}]");
        }

        sb.AppendLine();
        sb.AppendLine($"版式：{detail.Layout!.Name}（{detail.Layout.WidthMm} x {detail.Layout.HeightMm} mm）");
        sb.AppendLine("元素：");
        foreach (var element in detail.Layout.Elements)
        {
            sb.AppendLine($"  - {element.Type} @ ({element.XMm},{element.YMm})");
        }

        return sb.ToString();
    }

    private static string? DetectWinHostExe()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("LABELFRAME_WINHOST_EXE"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "LabelFrame.WinHost", "bin", "Debug", "net10.0-windows10.0.26100", "LabelFrame.WinHost.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LabelFrame", "WinHost", "LabelFrame.WinHost.exe"),
        };
        return candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));
    }
}