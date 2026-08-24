using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LabelFrame.Api;

/// <summary>全局异常处理：未捕获异常统一 500 + ErrorView（问题码 + 中文提示），不向客户端透出堆栈与内部路径。</summary>
public sealed partial class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "未处理异常：{Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, string path);

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // 客户端断开导致的取消不是服务错误：静默结束
            return true;
        }

        LogUnhandled(_logger, exception, httpContext.Request.Method, httpContext.Request.Path.Value ?? string.Empty);
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(
            new ErrorView("LF_INTERNAL_001", "服务器内部错误，请查看服务端日志。"),
            cancellationToken);
        return true;
    }
}

/// <summary>宿主接入扩展：注册全局异常处理器（需在 Build 后调用 <c>app.UseExceptionHandler()</c> 激活）。</summary>
public static class GlobalExceptionHandlerExtensions
{
    public static IServiceCollection AddLabelFrameExceptionHandler(this IServiceCollection services)
    {
        services.AddExceptionHandler<GlobalExceptionHandler>();
        // 无参 UseExceptionHandler() 的必需配套（.NET 8+：无路径 / 无 ProblemDetails 时中间件构建即抛错——
        // 由迭代 29 集成测试发现的真实启动缺陷）；GlobalExceptionHandler 已处理全部异常，ProblemDetails 仅作兜底形态
        services.AddProblemDetails();
        return services;
    }
}
