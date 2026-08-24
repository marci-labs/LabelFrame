using LabelFrame.Api;
using LabelFrame.Core.Jobs;
using LabelFrame.Core.Encoding;
using LabelFrame.Core.Documents;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Transport;
using LabelFrame.Core.Transport.Plugins;
using LabelFrame.WinHost.Api;
using LabelFrame.WinHost.Jobs;
using LabelFrame.WinHost.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace LabelFrame.WinHost.Api;

/// <summary>插件安装 / 卸载端点：插件包上传服务端 → 客户端下载安装 / 卸载（重启生效）。</summary>
internal static class PluginApi
{
    public static IEndpointRouteBuilder MapPluginApi(this IEndpointRouteBuilder app)
    {
    // ---- 插件安装 / 卸载（插件包上传服务端 → 客户端下载安装 / 卸载；安装 / 卸载 = 写文件 + 重启生效）----
    app.MapGet("/api/plugins/installed", (Transport.PluginInstaller installer) => Results.Ok(installer.ListInstalled()));

    app.MapPost("/api/plugins/install", async (IFormFile file, Transport.PluginInstaller installer, CancellationToken ct) =>
    {
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new ErrorView(ApiErrorCodes.PluginInvalid, "请选择要安装的插件包。"));
        }

        try
        {
            var view = await installer.InstallAsync(file.OpenReadStream(), file.FileName, ct);
            return Results.Ok(new { ok = true, message = $"插件「{view.Name} {view.Version}」已安装，重启客户端后生效。", plugin = view });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Results.BadRequest(new ErrorView(ApiErrorCodes.PluginBusy, ex.Message));
        }
        catch (InvalidDataException ex)
        {
            return Results.BadRequest(new ErrorView(ApiErrorCodes.PluginInvalid, $"插件包无效：{ex.Message}"));
        }
        // 其余意外异常（解压 / 写入故障等）交给全局异常处理器 → 500，不再误报 400 或透出内部信息
    }).DisableAntiforgery();

    app.MapPost("/api/plugins/uninstall", (Api.UninstallPluginRequest? request, Transport.PluginInstaller installer) =>
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PluginId))
        {
            return Results.BadRequest(new ErrorView(ApiErrorCodes.PluginInvalid, "缺少插件 ID。"));
        }

        try
        {
            installer.Uninstall(request.PluginId);
            return Results.Ok(new { ok = true, message = $"插件「{request.PluginId}」已卸载，重启客户端后生效。" });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Results.BadRequest(new ErrorView(ApiErrorCodes.PluginBusy, ex.Message));
        }
        catch (InvalidDataException ex)
        {
            return Results.BadRequest(new ErrorView(ApiErrorCodes.PluginInvalid, ex.Message));
        }
    }).DisableAntiforgery();

        return app;
    }
}
