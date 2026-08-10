using LabelFrame.Core.Contracts;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Templates;
using LabelFrame.Core.Validation;
using LabelFrame.Rendering;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Rendering;

namespace LabelFrame.WinHost.Jobs;

/// <summary>
/// 作业提交服务：校验契约数据 → 中文栅格化 → ZPL 编码 → 入队（幂等）。
/// 任一标签校验 / 编码失败则整体拒绝（缺数据不打半张）。
/// </summary>
public sealed class JobSubmissionService
{
    private readonly LabelJobQueue _queue;
    private readonly IZplEncoder _encoder;
    private readonly ITextRasterizer _rasterizer;
    private readonly ILabelBitmapRenderer _renderer;
    private readonly TemplateStore _templateStore;
    private readonly int _dpi;
    private readonly PrintMode _defaultPrintMode;

    /// <summary>创建提交服务。</summary>
    public JobSubmissionService(
        LabelJobQueue queue,
        IZplEncoder encoder,
        ITextRasterizer rasterizer,
        int dpi,
        ILabelBitmapRenderer renderer,
        TemplateStore templateStore,
        PrintMode defaultPrintMode)
    {
        _queue = queue;
        _encoder = encoder;
        _rasterizer = rasterizer;
        _renderer = renderer;
        _templateStore = templateStore;
        _dpi = dpi;
        _defaultPrintMode = defaultPrintMode;
    }

    /// <summary>提交作业；失败返回问题码（不建作业）。</summary>
    public async Task<SubmitJobResult> SubmitAsync(SubmitJobRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            return SubmitJobResult.Failure(JobErrorCodes.InvalidRequest, "缺少 requestId（幂等键）。");
        }

        if (request.Template?.Contract is null || request.Template.Layout is null)
        {
            return SubmitJobResult.Failure(JobErrorCodes.InvalidRequest, "缺少 template（contract + layout）。");
        }

        if (request.Labels is null || request.Labels.Count == 0)
        {
            return SubmitJobResult.Failure(JobErrorCodes.InvalidRequest, "缺少 labels（至少一张）。");
        }

        var zplLabels = new List<string>(request.Labels.Count);
        var printMode = request.PrintMode ?? _defaultPrintMode;
        foreach (var label in request.Labels)
        {
            var data = label.Data ?? new Dictionary<string, string>();
            var validation = LabelValidator.Validate(request.Template.Contract, data);
            if (!validation.IsValid)
            {
                var problem = validation.Problems[0];
                return SubmitJobResult.Failure(problem.Code, problem.Message, problem.FieldKey);
            }

            try
            {
                var document = new LabelDocument
                {
                    Layout = request.Template.Layout,
                    Data = data,
                };
                if (printMode == PrintMode.Image)
                {
                    // 图片打印：整版渲染 1bpp 位图，经 ^GF 输出，与预览所见一致
                    var images = await LoadTemplateImagesAsync(request.Template.Name, cancellationToken);
                    var bitmap = _renderer.RenderLabelBitmap(document, _dpi, images);
                    zplLabels.Add(_encoder.EncodeImage(bitmap, document.Layout.WidthMm, document.Layout.HeightMm, _dpi));
                }
                else
                {
                    document = _rasterizer.Rasterize(document, _dpi);
                    zplLabels.Add(_encoder.Encode(document, _dpi));
                }
            }
            catch (NotSupportedException ex)
            {
                return SubmitJobResult.Failure(JobErrorCodes.EncodeFailed, ex.Message);
            }
            catch (ArgumentException ex)
            {
                return SubmitJobResult.Failure(JobErrorCodes.EncodeFailed, ex.Message);
            }
        }

        var (job, created) = await _queue.SubmitAsync(request.RequestId, zplLabels, cancellationToken);
        return SubmitJobResult.Success(job, created);
    }
    private async Task<IReadOnlyDictionary<string, byte[]>> LoadTemplateImagesAsync(string? templateName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(templateName))
        {
            return new Dictionary<string, byte[]>();
        }

        var package = await _templateStore.GetAsync(templateName, cancellationToken);
        return package?.Images ?? new Dictionary<string, byte[]>();
    }
}