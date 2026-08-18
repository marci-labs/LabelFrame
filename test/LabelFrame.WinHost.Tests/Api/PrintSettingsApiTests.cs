using System.Net;
using System.Security.Claims;
using System.Text.Json;
using LabelFrame.WinHost.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LabelFrame.WinHost.Tests.Api;

public class PrintSettingsApiTests
{
    [Fact]
    public async Task Get_should_return_current_snapshot()
    {
        var settings = new PrintSettings();
        settings.Update(new PrintSettingsDto(true, 12, 400));

        var response = await ExecuteAsync(PrintSettingsApi.Get(settings));

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(new PrintSettingsDto(true, 12, 400), body);
    }

    [Fact]
    public async Task Get_should_return_normalized_values_when_store_has_out_of_range()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-api-{Guid.NewGuid():N}.json");
        try
        {
            // 越界：batchSize 0 → 10、interval -5 → 500；GET 永不返回非法值
            File.WriteAllText(path, """{ "batchEnabled": true, "batchSize": 0, "batchIntervalMs": -5 }""");
            var store = new PrintSettingsStore(path);
            var settings = new PrintSettings();
            settings.Update(store.Load());

            var response = await ExecuteAsync(PrintSettingsApi.Get(settings));

            Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
            var body = await ReadJsonAsync(response);
            Assert.Equal(new PrintSettingsDto(true, 10, 500), body);
        }
        finally
        {
            try { if (File.Exists(path)) { File.Delete(path); } } catch { }
        }
    }

    [Fact]
    public async Task Post_non_loopback_should_return_403()
    {
        var settings = new PrintSettings();
        var store = new PrintSettingsStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-api-{Guid.NewGuid():N}.json"));
        var request = new PrintSettingsDto(true, 10, 500);

        var response = await ExecuteAsync(PrintSettingsApi.Post(IPAddress.Parse("192.168.1.10"), request, store, settings));

        Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
        Assert.Equal(PrintSettings.Defaults, settings.Snapshot());
    }

    [Fact]
    public async Task Post_null_remote_ip_should_return_403()
    {
        var settings = new PrintSettings();
        var store = new PrintSettingsStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-api-{Guid.NewGuid():N}.json"));

        var response = await ExecuteAsync(PrintSettingsApi.Post(null, new PrintSettingsDto(true, 10, 500), store, settings));

        Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_null_body_should_return_400()
    {
        var settings = new PrintSettings();
        var store = new PrintSettingsStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-api-{Guid.NewGuid():N}.json"));

        var response = await ExecuteAsync(PrintSettingsApi.Post(IPAddress.Loopback, null, store, settings));

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Post_invalid_batch_size_should_return_400(int batchSize)
    {
        var settings = new PrintSettings();
        var store = new PrintSettingsStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-api-{Guid.NewGuid():N}.json"));

        var response = await ExecuteAsync(PrintSettingsApi.Post(IPAddress.Loopback, new PrintSettingsDto(true, batchSize, 500), store, settings));

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal(PrintSettings.Defaults, settings.Snapshot());
    }

    [Fact]
    public async Task Post_negative_interval_should_return_400()
    {
        var settings = new PrintSettings();
        var store = new PrintSettingsStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-api-{Guid.NewGuid():N}.json"));

        var response = await ExecuteAsync(PrintSettingsApi.Post(IPAddress.Loopback, new PrintSettingsDto(true, 10, -1), store, settings));

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal(PrintSettings.Defaults, settings.Snapshot());
    }

    [Fact]
    public async Task Post_valid_should_save_and_apply_immediately()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-api-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new PrintSettings();
            var store = new PrintSettingsStore(path);

            var response = await ExecuteAsync(PrintSettingsApi.Post(IPAddress.Loopback, new PrintSettingsDto(true, 20, 200), store, settings));

            Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
            // 保存即生效：内存单例已更新
            Assert.Equal(new PrintSettingsDto(true, 20, 200), settings.Snapshot());
            // 已持久化
            Assert.Equal(new PrintSettingsDto(true, 20, 200), store.Load());
            Assert.True(File.Exists(path));
        }
        finally
        {
            try { if (File.Exists(path)) { File.Delete(path); } } catch { }
        }
    }

    private static async Task<(int StatusCode, string Body)> ExecuteAsync(IResult result)
    {
        var context = CreateContext();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        return (context.Response.StatusCode, body);
    }

    private static HttpContext CreateContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<IAuthenticationService, FakeAuthenticationService>();
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<PrintSettingsDto?> ReadJsonAsync((int StatusCode, string Body) response)
        => JsonSerializer.Deserialize<PrintSettingsDto>(response.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    /// <summary>最小假认证服务：ForbidAsync 写 403（ForbidResult 执行所需，测试环境无真实认证）。</summary>
    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
            => Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;
    }
}
