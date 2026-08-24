namespace LabelFrame.Server.Tests;

/// <summary>服务端管理界面插件（静态前端包目录）运行时检测逻辑。</summary>
public class ServerPluginUiTests
{
    [Fact]
    public void Is_enabled_should_require_existing_directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lfui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // 空目录（无 index.html）= 未启用（无头）
            Assert.False(ServerPluginUi.IsEnabled(dir));
            File.WriteAllText(Path.Combine(dir, "index.html"), "<html></html>");
            Assert.True(ServerPluginUi.IsEnabled(dir));
            Assert.False(ServerPluginUi.IsEnabled(null));
            Assert.False(ServerPluginUi.IsEnabled(string.Empty));
            Assert.False(ServerPluginUi.IsEnabled(Path.Combine(dir, "missing")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Resolve_index_should_guard_api_and_healthz()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lfui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var index = Path.Combine(dir, "index.html");
        File.WriteAllText(index, "<html></html>");
        try
        {
            Assert.Equal(index, ServerPluginUi.ResolveIndexFile(dir, "/"));
            Assert.Equal(index, ServerPluginUi.ResolveIndexFile(dir, "/workbench"));
            Assert.Equal(index, ServerPluginUi.ResolveIndexFile(dir, "/assets/app.js"));
            Assert.Null(ServerPluginUi.ResolveIndexFile(dir, "/api/jobs"));
            Assert.Null(ServerPluginUi.ResolveIndexFile(dir, "/api/devices"));
            Assert.Null(ServerPluginUi.ResolveIndexFile(dir, "/healthz"));
            Assert.Null(ServerPluginUi.ResolveIndexFile(dir, ""));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Remove_plugin_directory_should_disable_immediately()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lfui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "index.html"), "<html></html>");
        Assert.True(ServerPluginUi.IsEnabled(dir));

        Directory.Delete(dir, recursive: true);

        Assert.False(ServerPluginUi.IsEnabled(dir));
        Assert.Null(ServerPluginUi.ResolveIndexFile(dir, "/"));
    }
}