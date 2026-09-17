using System.Text;
using LabelFrame.Bootstrapper.Placement;

namespace LabelFrame.Bootstrapper.PayloadTool;

/// <summary>
/// 非 MSI 组件落位工具入口（迭代 62，决策 #124）：管理界面 zip → <c>plugins\web-ui</c>；<c>.lfplugin</c> → <c>plugins\&lt;pluginId&gt;\</c>。
/// 卸载清理（迭代 69，决策 #133）：<c>-clean</c> 与落位动作同目标对称（Bundle 卸载 / Modify 改选 / 回滚时机由 Burn 以 UninstallArguments 驱动）。
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
            // 无参数 = 空操作成功（缺省调用形态的安全兜底；落位包现以 -clean 显式编排卸载清理，迭代 69）
            Console.WriteLine("未指定落位参数，空操作退出。");
            return ExitSuccess;
        }

        string? mode = null;
        string? archive = null;
        string? target = null;
        string? version = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-place":
                case "-plugin":
                case "-clean":
                    mode = args[i];
                    break;
                case "-archive" when i + 1 < args.Length:
                    archive = args[++i];
                    break;
                case "-target" when i + 1 < args.Length:
                    target = args[++i];
                    break;
                case "-version" when i + 1 < args.Length:
                    version = args[++i];
                    break;
                default:
                    Console.Error.WriteLine($"未知参数：{args[i]}");
                    Console.WriteLine(Usage);
                    return ExitUnexpected;
            }
        }

        // -clean 只需 -target（卸载会话无包文件参与）；落位模式还需 -archive
        if (mode is null || string.IsNullOrWhiteSpace(target) || (mode != "-clean" && string.IsNullOrWhiteSpace(archive)))
        {
            Console.Error.WriteLine("参数不完整：需要 -place/-plugin + -archive + -target，或 -clean + -target。");
            Console.WriteLine(Usage);
            return ExitUnexpected;
        }

        // 伴生包以相对文件名授权（Burn 将包与 Payload 缓存于同一目录）；同时容忍绝对路径（本地自验）
        var archivePath = archive is null
            ? null
            : Path.IsPathRooted(archive)
                ? archive
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, archive);

        try
        {
            if (mode == "-clean")
            {
                // 卸载清理（决策 #133）：与落位同目标对称；目录缺失 = 幂等成功，删除失败 = 尽力而为不阻断卸载主链
                var cleanup = PayloadPlacer.CleanPlacement(target!);
                switch (cleanup)
                {
                    case PlacementCleanupOutcome.Cleaned:
                        Console.WriteLine($"已清理落位目录：{target}");
                        return ExitSuccess;
                    case PlacementCleanupOutcome.AlreadyAbsent:
                        Console.WriteLine($"落位目录不存在，视为已清理：{target}");
                        return ExitSuccess;
                    case PlacementCleanupOutcome.FailedBestEffort:
                        Console.Error.WriteLine($"落位目录清理失败（尽力而为，不阻断卸载）：{target}");
                        return ExitSuccess;
                    default:
                        Console.Error.WriteLine($"未知清理结果：{cleanup}");
                        return ExitUnexpected;
                }
            }

            if (mode == "-place")
            {
                PayloadPlacer.PlaceArchive(archivePath!, target!, version);
                Console.WriteLine($"已落位：{archive} -> {target}");
                return ExitSuccess;
            }

            var outcome = PayloadPlacer.PlacePlugin(archivePath!, target!, version);
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
        "用法：LabelFrame.Bootstrapper.PayloadTool.exe -place|-plugin -archive <包文件名> -target <目标目录> [-version <Bundle 版本>]（-place = 覆盖解压；-plugin = 按 #123 版本比较落位；-version = 写落位凭据，决策 #133）\n"
        + "卸载清理：LabelFrame.Bootstrapper.PayloadTool.exe -clean -target <目标目录>（与落位同目标对称，决策 #133）\n"
        + "无参数 = 空操作成功（清理包 install 形态——登记包已装，卸载语义由 Burn 规划）";
}
