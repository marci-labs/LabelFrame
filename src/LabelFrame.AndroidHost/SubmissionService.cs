using LabelFrame.AndroidHost.Api;
using LabelFrame.AndroidHost.Rendering;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Validation;

namespace LabelFrame.AndroidHost;

/// <summary>提交服务：校验 → Android 整版位图渲染 → ^GF 编码 → 本地作业队列（与 WinHost 同构；迭代 15 起恒为图片打印）。</summary>
public sealed class SubmissionService
{
    private readonly LabelJobQueue _queue;
    private readonly ZplImageEncoder _encoder;
    private readonly AndroidLabelRenderer _renderer;
    private readonly int _dpi;

    /// <summary>创建提交服务。</summary>
    public SubmissionService(LabelJobQueue queue, AndroidLabelRenderer renderer, int dpi)
    {
        _queue = queue;
        _encoder = new ZplImageEncoder();
        _renderer = renderer;
        _dpi = dpi;
    }

    /// <summary>提交作业；失败返回问题码（不建作业）。</summary>
    public async Task<SubmitResult> SubmitAsync(SubmitJobRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            return SubmitResult.Failure(JobErrorCodes.InvalidRequest, "缺少 requestId（幂等键）。");
        }

        if (request.Template?.Contract is null || request.Template.Layout is null)
        {
            return SubmitResult.Failure(JobErrorCodes.InvalidRequest, "缺少 template（contract + layout）。");
        }

        if (request.Labels is null || request.Labels.Count == 0)
        {
            return SubmitResult.Failure(JobErrorCodes.InvalidRequest, "缺少 labels（至少一张）。");
        }

        var commandLabels = new List<string>(request.Labels.Count);
        foreach (var label in request.Labels)
        {
            var data = label.Data ?? new Dictionary<string, string>();
            var validation = LabelValidator.Validate(request.Template.Contract, data);
            if (!validation.IsValid)
            {
                var problem = validation.Problems[0];
                return SubmitResult.Failure(problem.Code, problem.Message, problem.FieldKey);
            }

            try
            {
                var document = new LabelDocument { Layout = request.Template.Layout, Data = data };
                var bitmap = _renderer.RenderLabelBitmap(document, _dpi);
                commandLabels.Add(_encoder.EncodeImage(bitmap, document.Layout.WidthMm, document.Layout.HeightMm, _dpi));
            }
            catch (NotSupportedException ex)
            {
                return SubmitResult.Failure(JobErrorCodes.EncodeFailed, ex.Message);
            }
        }

        var (job, created) = await _queue.SubmitAsync(request.RequestId, commandLabels, cancellationToken);
        return SubmitResult.Success(job, created);
    }
}