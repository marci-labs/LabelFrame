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
    private string _excelFileName = string.Empty;
    private ExcelTableData? _excelTable;

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

    public ObservableCollection<ExcelColumnMapping> ExcelMappings { get; } = [];

    public IReadOnlyList<string> FieldKeyOptions => _detail?.Contract?.Fields.Select(f => f.Key).ToList() ?? [];

    public string ExcelFileText
    {
        get => _excelFileName;
        private set => SetProperty(ref _excelFileName, value);
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
        ClearExcelState();
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
        var data = Fields.ToDictionary(f => f.Key, f => f.Value ?? string.Empty);
        RenderPreviewWithData(data);
    }

    private void RenderPreviewWithData(Dictionary<string, string> data)
    {
        if (SelectedTemplate is null || _detail?.Layout is null)
        {
            PreviewImage = null;
            return;
        }

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

    /// <summary>读取 Excel（标题行 + 数据行），按当前模板字段自动建议列映射，并用首行刷新预览。</summary>
    public async Task ImportExcelAsync(string filePath)
    {
        EnsureConnected();
        if (_detail?.Contract is null)
        {
            throw new InvalidOperationException("请先选择模板。");
        }

        _excelTable = ExcelImportService.Read(filePath);
        var keys = _detail.Contract.Fields.Select(f => f.Key).ToList();
        var suggested = ExcelImportService.SuggestMapping(_excelTable.Headers, keys);
        ExcelMappings.Clear();
        for (var i = 0; i < _excelTable.Headers.Count; i++)
        {
            ExcelMappings.Add(new ExcelColumnMapping
            {
                ExcelColumn = _excelTable.Headers[i],
                FieldKey = i < suggested.Count ? suggested[i] : string.Empty,
            });
        }

        ExcelFileText = $"{Path.GetFileName(filePath)}（{_excelTable.Rows.Count} 行）";
        OnPropertyChanged(nameof(FieldKeyOptions));
        Log($"已导入 Excel：{ExcelFileText}，共 {_excelTable.Headers.Count} 列。");

        if (_excelTable.Rows.Count > 0)
        {
            var first = ExcelImportService.BuildRowsData(
                _excelTable,
                ExcelMappings.Select(m => m.FieldKey).ToList()).FirstOrDefault();
            if (first is not null)
            {
                RenderPreviewWithData(first);
            }
        }
    }

    /// <summary>按当前映射批量打印 Excel 全部数据行（一次提交多张）。</summary>
    public async Task<int> PrintExcelAsync()
    {
        EnsureConnected();
        if (_detail is null || _excelTable is null)
        {
            throw new InvalidOperationException("请先导入 Excel。");
        }

        var labels = ExcelImportService.BuildRowsData(
            _excelTable,
            ExcelMappings.Select(m => m.FieldKey).ToList());
        if (labels.Count == 0)
        {
            throw new InvalidOperationException("Excel 没有数据行。");
        }

        var requestId = $"studio-excel-{Guid.NewGuid():N}";
        var job = await _client!.SubmitJobAsync(requestId, _detail, labels);
        JobStatusText = $"已提交 Excel 批量打印 {job.JobId}：{job.Status}（{job.CompletedItems}/{job.TotalItems}）";
        Log($"提交 Excel 批量打印 {job.JobId}（{job.TotalItems} 张）。");
        _ = PollJobAsync(job.JobId);
        return job.TotalItems;
    }

    private void ClearExcelState()
    {
        _excelTable = null;
        ExcelMappings.Clear();
        ExcelFileText = string.Empty;
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
        ClearExcelState();
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