using LabelFrame.Api;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Transport;
using LabelFrame.Rendering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace LabelFrame.WinHost.Api;

/// <summary>打印机状态 / 测试页端点（测试页与正式打印同源：Skia 整版位图经 ^GF 发送）。</summary>
internal static class PrinterApi
{
    public static IEndpointRouteBuilder MapPrinterApi(this IEndpointRouteBuilder app, int dpi)
    {
    // ---- 打印机测试页 / 在线状态 ----
    app.MapGet("/api/printer/status", async (ITransportManager transportManager, CancellationToken ct) =>
        Results.Ok(await (transportManager.CurrentTransport as IPrinterStatusProvider ?? new Program.UnsupportedStatusProvider()).GetStatusAsync(ct)));

    app.MapPost("/api/printer/test", async (ITransportManager transportManager, ILabelBitmapRenderer renderer, ZplImageEncoder encoder, CancellationToken ct) =>
    {
        // 测试页与正式打印同源：Skia 渲染整版位图经 ^GF 发送（图片打印语义，无矢量 ZPL）
        var document = new LabelDocument
        {
            Layout = new LabelLayout
            {
                Name = "test",
                ContractName = "test",
                ContractVersion = "1.0",
                WidthMm = 40,
                HeightMm = 20,
                Elements =
                [
                    new LabelTextElement { Literal = "LabelFrame Test", XMm = 2, YMm = 4, FontHeightMm = 3, FontWidthMm = 3, WidthMm = 36, TextAlign = LabelTextAlign.Center },
                ],
            },
            Data = new Dictionary<string, string>(),
        };
        var bitmap = renderer.RenderLabelBitmap(document, dpi);
        var command = encoder.EncodeImage(bitmap, document.Layout.WidthMm, document.Layout.HeightMm, dpi);
        await transportManager.CurrentTransport.SendAsync(command, ct);
        return Results.Ok(new { sent = true, bytes = System.Text.Encoding.UTF8.GetByteCount(command) });
    });

        return app;
    }
}
