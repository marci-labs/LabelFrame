using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using LabelFrame.Api;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Templates;
using LabelFrame.Rendering;

namespace LabelFrame.Api.Endpoints;

/// <summary>模板端点选项（宿主传入各自的问题码前缀，保持两宿主对外错误码不变）。</summary>
public sealed record TemplateApiOptions(
    TemplateStore Store,
    ILabelBitmapRenderer Renderer,
    int Dpi,
    string InvalidRequestCode,
    string TemplateNotFoundCode);

/// <summary>模板库端点（Server 与 WinHost 共用）：CRUD + 导入导出 + 预览。</summary>
public static class TemplateEndpoints
{
    public static IEndpointRouteBuilder MapTemplateApi(this IEndpointRouteBuilder app, TemplateApiOptions options)
    {
        app.MapPost("/api/templates", async (TemplatePackageDto? dto, CancellationToken ct) =>
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Name) || dto.Contract is null || dto.Layout is null)
            {
                return Results.BadRequest(new ErrorView(options.InvalidRequestCode, "缺少模板 name / contract / layout。"));
            }

            await options.Store.SaveAsync(new TemplatePackage
            {
                Name = dto.Name,
                Group = string.IsNullOrWhiteSpace(dto.Group) ? "默认" : dto.Group,
                Contract = dto.Contract,
                Layout = dto.Layout,
                TestData = dto.TestData ?? new Dictionary<string, string>(),
            }, ct);
            return Results.Ok(new { name = dto.Name, group = string.IsNullOrWhiteSpace(dto.Group) ? "默认" : dto.Group });
        });

        app.MapGet("/api/templates", async (string? group, CancellationToken ct) =>
            Results.Ok(await options.Store.ListAsync(group, ct)));

        app.MapGet("/api/templates/{name}", async (string name, CancellationToken ct) =>
        {
            var package = await options.Store.GetAsync(name, ct);
            return package is null
                ? Results.NotFound(new ErrorView(options.TemplateNotFoundCode, $"模板不存在:{name}。"))
                : Results.Ok(package);
        });

        app.MapDelete("/api/templates/{name}", async (string name, CancellationToken ct) =>
        {
            await options.Store.DeleteAsync(name, ct);
            return Results.NoContent();
        });

        app.MapGet("/api/templates/{name}/export", async (string name, CancellationToken ct) =>
        {
            var package = await options.Store.GetAsync(name, ct);
            return package is null
                ? Results.NotFound(new ErrorView(options.TemplateNotFoundCode, $"模板不存在:{name}。"))
                : Results.File(TemplatePackageSerializer.Export(package), "application/zip", $"{name}.lfpkg");
        });

        app.MapPost("/api/templates/import", async (IFormFile file, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new ErrorView(options.InvalidRequestCode, "缺少模板包文件。"));
            }

            using var memory = new MemoryStream();
            await file.CopyToAsync(memory, ct);
            try
            {
                var package = TemplatePackageSerializer.Import(memory.ToArray());
                await options.Store.SaveAsync(package, ct);
                return Results.Ok(package.Name);
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(new ErrorView(options.InvalidRequestCode, ex.Message));
            }
        }).DisableAntiforgery();

        // 预览与打印同源（Skia 整版渲染，DPI 取宿主配置）；请求数据缺省时回退模板 testData
        app.MapPost("/api/templates/{name}/preview", async (string name, PreviewRequest? request, CancellationToken ct) =>
        {
            var package = await options.Store.GetAsync(name, ct);
            if (package is null)
            {
                return Results.NotFound(new ErrorView(options.TemplateNotFoundCode, $"模板不存在:{name}。"));
            }

            var document = new LabelDocument
            {
                Layout = package.Layout,
                Data = request?.Data ?? package.TestData ?? new Dictionary<string, string>(),
            };
            var png = options.Renderer.RenderLabelBitmapPng(document, options.Dpi, package.Images);
            return Results.File(png, "image/png");
        });

        return app;
    }
}
