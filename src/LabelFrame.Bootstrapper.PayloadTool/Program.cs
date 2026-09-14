using System.Text;
using LabelFrame.Bootstrapper.Placement;

namespace LabelFrame.Bootstrapper.PayloadTool;

/// <summary>
/// 非 MSI 组件落位工具入口（迭代 62，决策 #124）：管理界面 zip → <c>plugins\web-ui</c>；<c>.lfplugin</c> → <c>plugins\&lt;pluginId&gt;\</c>。
/// 中文输出写入 Burn 包日志（引擎捕获进程输出）；退出码契约见工程注释。
/// </summary>
internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitRefusedDowngrade = 20;
    private const int ExitInvalidArchive = 30;
    private const int ExitUnexpected = 90;

    private static int Main(string[] args)
    {
        // Burn 以重定向管道捕获输出：显式 UTF-8，中文消息不乱码
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // 无控制台句柄时忽略（不影响落位）
        }

        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"落位失败（未预期）：{ex}");
            return ExitUnexpected;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            // 无参数 = 空操作成功（Burn 缺省卸载调用形态的安全兜底；落位包 Permanent 不参与卸载编排）
            Console.WriteLine("未指定落位参数，空操作退出。");
            return ExitSuccess;
        }

        string? mode = null;
        string? archive = null;
        string? target = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-place":
                case "-plugin":
                    mode = args[i];
                    break;
                case "-archive" when i + 1 < args.Length:
                    archive = args[++i];
                    break;
                case "-target" when i + 1 < args.Length:
                    target = args[++i];
                    break;
                default:
                    Console.Error.WriteLine($"未知参数：{args[i]}");
                    Console.WriteLine(Usage);
                    return ExitUnexpected;
            }
        }

        if (mode is null || string.IsNullOrWhiteSpace(archive) || string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("参数不完整：需要 -place/-plugin、-archive 与 -target。");
            Console.WriteLine(Usage);
            return ExitUnexpected;
        }

        // 伴生包以相对文件名授权（Burn 将包与 Payload 缓存于同一目录）；同时容忍绝对路径（本地自验）
        var archivePath = Path.IsPathRooted(archive)
            ? archive
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, archive);

        try
        {
            if (mode == "-place")
            {
                PayloadPlacer.PlaceArchive(archivePath!, target!);
                Console.WriteLine($"已落位：{archive} -> {target}");
                return ExitSuccess;
            }

            var outcome = PayloadPlacer.PlacePlugin(archivePath!, target!);
            switch (outcome)
            {
                case PlacementOutcome.Placed:
                    Console.WriteLine($"已安装插件：{archive} -> {target}");
                    return ExitSuccess;
                case PlacementOutcome.SkippedSameVersion:
                    Console.WriteLine($"插件版本相同，幂等跳过（未重复解压）：{target}");
                    return ExitSuccess;
                case PlacementOutcome.RefusedDowngrade:
                    Console.Error.WriteLine($"拒绝降级安装：目标目录已是更新版本，请先卸载再安装（{target}）。");
                    return ExitRefusedDowngrade;
                default:
                    Console.Error.WriteLine($"未知落位结果：{outcome}");
                    return ExitUnexpected;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException)
        {
            // 包损坏 / 契约不符（非 zip、缺 manifest.json、缺 version 等）：明确中文原因
            Console.Error.WriteLine($"落位包无效：{ex.Message}");
            return ExitInvalidArchive;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Console.Error.WriteLine($"落位文件系统错误：{ex.Message}");
            return ExitUnexpected;
        }
    }

    private const string Usage =
        "用法：LabelFrame.Bootstrapper.PayloadTool.exe -place|-plugin -archive <包文件名> -target <目标目录>（-place = 覆盖解压；-plugin = 按 #123 版本比较落位）";
}
