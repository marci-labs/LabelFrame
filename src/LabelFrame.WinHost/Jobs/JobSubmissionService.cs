using LabelFrame.Api;
using LabelFrame.Api.Endpoints;
using LabelFrame.Core.Contracts;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Templates;
using LabelFrame.Core.Validation;
using LabelFrame.Rendering;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Jobs;

/// <summary>
/// 作业提交服务：校验契约数据 → Skia 整版渲染 1bpp 位图 → ^GF 编码入队（幂等）。
/// 任一标签校验 / 渲染失败则整体拒绝（缺数据不打半张）。
/// 打印统一为图片：不再有矢量 ZPL / 文本栅格化路径。
/// Log 连接 = 模拟打印：提交时同时把渲染 PNG 保存到 print\{jobId}\。
/// </summary>
public sealed class JobSubmissionService
{
    private readonly LabelJobQueue _queue;
    private readonly ZplImageEncoder _encoder;
    private readonly ILabelBitmapRenderer _renderer;
    private readonly TemplateStore _templateStore;
    private readonly ITransportManager _transportManager;
    private readonly TextWriter _hostLogWriter;
    private readonly int _dpi;

    /// <summary>创建提交服务。</summary>
    public JobSubmissionService(
        LabelJobQueue queue,
        ZplImageEncoder encoder,
        int dpi,
        ILabelBitmapRenderer renderer,
        TemplateStore templateStore,
        ITransportManager transportManager,
        TextWriter hostLogWriter)
    {
        _queue = queue;
        _encoder = encoder;
        _renderer = renderer;
        _templateStore = templateStore;
        _transportManager = transportManager;
        _hostLogWriter = hostLogWriter;
        _dpi = dpi;
    }

    /// <summary>提交作业；失败返回问题码（不建作业）。</summary>
    public async Task<SubmitJobResult> SubmitAsync(SubmitJobRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            return SubmitJobResult.Failure(JobErrorCodes.InvalidRequest, "缺少 requestId（幂等键）。");
        }

        var rendered = await RenderAllAsync(request, cancellationToken);
        if (rendered.ErrorCode is not null)
        {
            return SubmitJobResult.Failure(rendered.ErrorCode, rendered.ErrorMessage!, rendered.FieldKey);
        }

        var commandLabels = rendered.Items!.Select(i => _encoder.EncodeImage(i.Bitmap, i.WidthMm, i.HeightMm, _dpi)).ToList();
        var (job, created) = await _queue.SubmitAsync(request.RequestId!, commandLabels, cancellationToken);

        // Log 连接 = 模拟打印：保存渲染 PNG 到 print\{jobId}\（不发真机）
        if (created && _transportManager.CurrentConfig.Mode == TransportMode.Log)
        {
            SaveLogPrintImages(job.Id, rendered.Items!);
        }

        return SubmitJobResult.Success(job, created);
    }

    /// <summary>渲染结果：单张标签的整版位图与尺寸。</summary>
    public sealed record RenderedImage(int Index, byte[] Png);

    private sealed record RenderItem(int Index, LabelBitmap Bitmap, double WidthMm, double HeightMm);

    private sealed record RenderResult(IReadOnlyList<RenderItem>? Items, string? ErrorCode, string? ErrorMessage, string? FieldKey);

    private async Task<RenderResult> RenderAllAsync(SubmitJobRequest request, CancellationToken cancellationToken)
    {
        if (request.Template?.Contract is null || request.Template.Layout is null)
        {
            return new RenderResult(null, JobErrorCodes.InvalidRequest, "缺少 template（contract + layout）。", null);
        }

        if (request.Labels is null || request.Labels.Count == 0)
        {
            return new RenderResult(null, JobErrorCodes.InvalidRequest, "缺少 labels（至少一张）。", null);
        }

        var images = await LoadTemplateImagesAsync(request.Template, cancellationToken);
        var items = new List<RenderItem>(request.Labels.Count);
        for (var i = 0; i < request.Labels.Count; i++)
        {
            var label = request.Labels[i];
            var data = label.Data ?? new Dictionary<string, string>();
            var validation = LabelValidator.Validate(request.Template.Contract, data);
            if (!validation.IsValid)
            {
                var problem = validation.Problems[0];
                return new RenderResult(null, problem.Code, problem.Message, problem.FieldKey);
            }

            try
            {
                var document = new LabelDocument
                {
                    Layout = request.Template.Layout,
                    Data = data,
                };
                var bitmap = _renderer.RenderLabelBitmap(document, _dpi, images);
                items.Add(new RenderItem(i, bitmap, document.Layout.WidthMm, document.Layout.HeightMm));
            }
            catch (NotSupportedException ex)
            {
                return new RenderResult(null, JobErrorCodes.EncodeFailed, ex.Message, null);
            }
            catch (ArgumentException ex)
            {
                return new RenderResult(null, JobErrorCodes.EncodeFailed, ex.Message, null);
            }
        }

        return new RenderResult(items, null, null, null);
    }

    /// <summary>Log 模拟打印：保存 PNG 并记录摘要。</summary>
    private void SaveLogPrintImages(string jobId, IReadOnlyList<RenderItem> items)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LabelFrame",
                "print",
                jobId);
            Directory.CreateDirectory(dir);
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                File.WriteAllBytes(Path.Combine(dir, $"label-{item.Index + 1}.png"), LabelBitmapPng.ToPng(item.Bitmap));
            }

            _hostLogWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 模拟打印（Log）：作业 {jobId} 已保存 {items.Count} 张 PNG 到 {dir}");
            _hostLogWriter.Flush();
        }
        catch (Exception ex)
        {
            _hostLogWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 模拟打印（Log）：作业 {jobId} 保存 PNG 失败：{ex.Message}");
            _hostLogWriter.Flush();
        }
    }

    private async Task<IReadOnlyDictionary<string, byte[]>> LoadTemplateImagesAsync(TemplateDto template, CancellationToken cancellationToken)
        // 图片资源解析与共享端点（render-image / render-images）同源：base64 附带优先、按名回退本地模板库
        => await RenderEndpoints.ResolveImagesAsync(template, _templateStore, cancellationToken);
}
