using System.Net;

namespace LabelFrame.Bootstrapper.Tests.TestInfrastructure;

/// <summary>记录全部请求的 HttpMessageHandler（dry-run 契约断言锚点：只读 GET / 零请求）。</summary>
public sealed class RecordingHttpMessageHandler(string fixedJson) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(fixedJson),
        });
    }
}
