using LabelFrame.Server;
using Microsoft.Extensions.Logging;

namespace LabelFrame.Server.Tests;

/// <summary>
/// FileLoggerProvider 启动防护与轮转 / 保留测试（决策 #108，AC-01 / AC-02）：
/// 路径无效不抛异常（宿主可正常启动）、按日文件写入、超期清理、通道停用后写入不抛异常。
/// </summary>
public class FileLoggerProviderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"lffilelog-{Guid.NewGuid():N}");

    public FileLoggerProviderTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Constructor_with_invalid_path_should_not_throw_and_report_reason()
    {
        // AC-01：目录不存在且不可创建（父路径是一个文件）——构造不抛异常、通道禁用、给出原因
        var blocker = Path.Combine(_directory, "blocker.txt");
        File.WriteAllText(blocker, "占位文件：以其为目录必然创建失败");
        var invalidPath = Path.Combine(blocker, "logs", "server.log");

        var provider = new FileLoggerProvider(invalidPath);

        Assert.False(provider.FileChannelEnabled);
        Assert.NotNull(provider.InactiveReason);
    }

    [Fact]
    public void Provider_should_write_to_dated_file()
    {
        var basePath = Path.Combine(_directory, "server.log");
        using var provider = new FileLoggerProvider(basePath);

        Assert.True(provider.FileChannelEnabled);
        var logger = provider.CreateLogger("TestCategory");
        logger.LogInformation("业务事件行：jobId=abc");

        var dated = Path.Combine(_directory, $"server-{DateTime.Now:yyyyMMdd}.log");
        Assert.True(File.Exists(dated), $"按日文件未创建：{dated}");
        Assert.Contains("业务事件行：jobId=abc", ReadShared(dated));
    }

    [Fact]
    public void Startup_should_cleanup_files_over_retention_limit()
    {
        // AC-02：构造（启动）即清理超期——保留上限含当日文件（与 Serilog retainedFileCountLimit 口径一致），
        // 6 个按日文件、上限 3 → 删最旧 3 个
        var basePath = Path.Combine(_directory, "server.log");
        foreach (var day in new[] { "20200101", "20200102", "20200103", "20200104", "20200105" })
        {
            File.WriteAllText(Path.Combine(_directory, $"server-{day}.log"), "旧文件");
        }

        using var provider = new FileLoggerProvider(basePath, retainedFileCountLimit: 3);

        Assert.True(provider.FileChannelEnabled);
        Assert.False(File.Exists(Path.Combine(_directory, "server-20200101.log")));
        Assert.False(File.Exists(Path.Combine(_directory, "server-20200102.log")));
        Assert.False(File.Exists(Path.Combine(_directory, "server-20200103.log")));
        Assert.True(File.Exists(Path.Combine(_directory, "server-20200104.log")));
        Assert.True(File.Exists(Path.Combine(_directory, "server-20200105.log")));
        Assert.True(File.Exists(Path.Combine(_directory, $"server-{DateTime.Now:yyyyMMdd}.log")));
    }

    [Fact]
    public void Non_dated_files_should_not_be_touched_by_cleanup()
    {
        // 历史遗留的单名 server.log 与不匹配 <名>-yyyyMMdd 模式的文件不迁移不删除
        var basePath = Path.Combine(_directory, "server.log");
        File.WriteAllText(basePath, "历史单名文件");
        File.WriteAllText(Path.Combine(_directory, "server-备注.log"), "非日期文件");

        using var provider = new FileLoggerProvider(basePath, retainedFileCountLimit: 1);

        Assert.True(File.Exists(basePath));
        Assert.True(File.Exists(Path.Combine(_directory, "server-备注.log")));
    }

    [Fact]
    public void Log_should_not_throw_after_channel_disabled()
    {
        // 运行中写入失败防护：文件句柄已关闭（等价写入失败）后 Log 不抛异常（不打断请求路径），通道自我禁用
        var basePath = Path.Combine(_directory, "server.log");
        var provider = new FileLoggerProvider(basePath);
        var logger = provider.CreateLogger("TestCategory");
        provider.Dispose();

        logger.LogInformation("通道已停用后的写入");

        Assert.False(provider.FileChannelEnabled);
    }

    /// <summary>以兼容写入句柄的共享方式读取（写入器仍持写句柄时 File.ReadAllText 会因共享冲突失败）。</summary>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 句柄延迟释放时忽略清理失败
        }
    }
}
