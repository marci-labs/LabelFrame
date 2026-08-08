using LabelFrame.Core.Contracts;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Validation;
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
    private readonly int _dpi;

    /// <summary>创建提交服务。</summary>
    public JobSubmissionService(LabelJobQueue queue, IZplEncoder encoder, ITextRasterizer rasterizer, int dpi)
    {
        _queue = queue;
        _encoder = encoder;
        _rasterizer = rasterizer;
        _dpi = dpi;
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
                document = _rasterizer.Rasterize(document, _dpi);
                zplLabels.Add(_encoder.Encode(document, _dpi));
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
}