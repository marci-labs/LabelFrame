using LabelFrame.Api;
using LabelFrame.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace LabelFrame.Server.Api;

/// <summary>分发端点：客户端安装包（迭代 22 决策 #71）与传输插件包（迭代 23 决策 #72）的列表 / 上传 / 下载 / 删除。</summary>
internal static class PackagesApi
{
    public static IEndpointRouteBuilder MapPackagesApi(this IEndpointRouteBuilder app)
    {
// ---- 客户端下载分发（迭代 22 §2.3 / §5.4，决策 #71：服务端统一分发客户端安装包）----
app.MapGet("/api/client-packages", (ClientPackagesService svc) => Results.Ok(svc.List()));

app.MapPost("/api/client-packages", async (IFormFile file, ClientPackagesService svc, CancellationToken ct) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请选择要上传的安装包文件。"));
    }

    try
    {
        var view = await svc.SaveAsync(file.FileName, file.OpenReadStream(), ct);
        return Results.Ok(view);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, ex.Message));
    }
    // 其余异常（磁盘满 / IO 故障等）交给全局异常处理器 → 500，不再误报 400 或透出内部信息
}).DisableAntiforgery();

app.MapGet("/api/client-packages/{fileName}", (string fileName, ClientPackagesService svc) =>
{
    var path = svc.GetDownloadPath(fileName);
    if (path is null)
    {
        return Results.NotFound(new ErrorView(ServerErrorCodes.ClientPackageNotFound, "安装包不存在。"));
    }

    return Results.File(path, "application/octet-stream", Path.GetFileName(path));
});

app.MapDelete("/api/client-packages/{fileName}", (string fileName, ClientPackagesService svc) =>
{
    var view = svc.Get(fileName);
    if (view is null)
    {
        return Results.NotFound(new ErrorView(ServerErrorCodes.ClientPackageNotFound, "安装包不存在。"));
    }

    svc.Delete(fileName);
    return Results.Ok(new { deleted = view.FileName });
});

// ---- 传输插件包（迭代 23 §2.1 / §5.1，决策 2A：插件包上传服务端，客户端安装用；列表含元数据与 valid 状态，路径穿越防护）----
app.MapGet("/api/plugin-packages", (PluginPackagesService svc) => Results.Ok(svc.List()));

app.MapPost("/api/plugin-packages", async (IFormFile file, PluginPackagesService svc, CancellationToken ct) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, "请选择要上传的插件包文件。"));
    }

    try
    {
        var view = await svc.SaveAsync(file.FileName, file.OpenReadStream(), ct);
        return Results.Ok(view);
    }
    catch (InvalidDataException ex)
    {
        return Results.BadRequest(new ErrorView(ServerErrorCodes.InvalidRequest, $"插件包无效：{ex.Message}"));
    }
    // 其余异常（磁盘满 / IO 故障等）交给全局异常处理器 → 500，不再误报 400 或透出内部信息
}).DisableAntiforgery();

app.MapGet("/api/plugin-packages/{fileName}", (string fileName, PluginPackagesService svc) =>
{
    var path = svc.GetDownloadPath(fileName);
    if (path is null)
    {
        return Results.NotFound(new ErrorView(ServerErrorCodes.PluginPackageNotFound, "插件包不存在。"));
    }

    return Results.File(path, "application/octet-stream", Path.GetFileName(path));
});

app.MapDelete("/api/plugin-packages/{fileName}", (string fileName, PluginPackagesService svc) =>
{
    var view = svc.Get(fileName);
    if (view is null)
    {
        return Results.NotFound(new ErrorView(ServerErrorCodes.PluginPackageNotFound, "插件包不存在。"));
    }

    svc.Delete(fileName);
    return Results.Ok(new { deleted = view.FileName });
});

        return app;
    }
}
