using LabelFrame.AndroidHost.Api;
using LabelFrame.AndroidHost.Rendering;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Validation;

namespace LabelFrame.AndroidHost;

/// <summary>提交服务：校验 → 中文栅格化 → ZPL 编码 → 本地作业队列（与 WinHost 同构）。</summary>
public sealed class SubmissionService
{
    private readonly LabelJobQueue _queue;
    private readonly ZplEncoder _encoder;
    private readonly AndroidTextRasterizer _rasterizer;
    private readonly int _dpi;

    /// <summary>创建提交服务。</summary>
    public SubmissionService(LabelJobQueue queue, AndroidTextRasterizer rasterizer, int dpi)
    {
        _queue = queue;
        _encoder = new ZplEncoder();
        _rasterizer = rasterizer;
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

        var zplLabels = new List<string>(request.Labels.Count);
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
                document = _rasterizer.Rasterize(document, _dpi);
                zplLabels.Add(_encoder.Encode(document, _dpi));
            }
            catch (NotSupportedException ex)
            {
                return SubmitResult.Failure(JobErrorCodes.EncodeFailed, ex.Message);
            }
        }

        var (job, created) = await _queue.SubmitAsync(request.RequestId, zplLabels, cancellationToken);
        return SubmitResult.Success(job, created);
    }
}