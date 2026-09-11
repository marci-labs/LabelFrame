using System.Reflection;
using System.Runtime.Loader;

namespace LabelFrame.Core.Transport.Plugins;

/// <summary>
/// 插件发现探针（安装预检用）——用临时 collectible AssemblyLoadContext **字节加载**
/// 目录内 DLL 并返回发现的插件 ID；只做发现，不 Create 传输实例。
/// 与插件目录加载器共用同一发现逻辑；与 LoadFromAssemblyPath 不同，字节加载不锁文件，
/// 预检后临时目录可正常移动 / 删除。
/// </summary>
public static class PluginProbe
{
    /// <summary>逐 DLL 加载失败记录（安装预检把「DLL 无效」与其他失败区分给用户可行动消息用）。</summary>
    public sealed record PluginAssemblyLoadError(string FileName, Exception Exception);

    /// <summary>探针结果：发现的插件 ID（去重）+ 逐 DLL 加载失败明细。</summary>
    public sealed record PluginProbeResult(IReadOnlyList<string> PluginIds, IReadOnlyList<PluginAssemblyLoadError> LoadErrors);

    /// <summary>发现目录内插件 ID（去重；目录不存在 / 空返回空列表；单 DLL 失败只记日志）。</summary>
    public static IReadOnlyList<string> DiscoverPluginIds(string? directory, TextWriter hostLog)
        => Discover(directory, hostLog).PluginIds;

    /// <summary>
    /// 发现目录内插件并返回逐 DLL 失败明细：DLL 不是有效 .NET 程序集（BadImageFormatException）
    /// 与其他加载失败都进 LoadErrors（宿主日志仍逐条记录英文原始消息，供排障）。
    /// </summary>
    public static PluginProbeResult Discover(string? directory, TextWriter hostLog)
    {
        var ids = new List<string>();
        var errors = new List<PluginAssemblyLoadError>();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return new PluginProbeResult(ids, errors);
        }

        foreach (var dll in Directory.GetFiles(directory, "*.dll"))
        {
            try
            {
                var alc = new AssemblyLoadContext($"plugin-probe:{Path.GetFileName(dll)}", isCollectible: true);
                // 依赖优先回退宿主默认上下文（Core 等），其次包内伴生 DLL（字节加载，不锁文件）
                alc.Resolving += (context, name) => ResolveDependency(context, name, directory);
                var assembly = alc.LoadFromStream(new MemoryStream(File.ReadAllBytes(dll), writable: false));
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsAbstract || type.IsInterface || !typeof(ITransportPlugin).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    if (Activator.CreateInstance(type) is ITransportPlugin plugin)
                    {
                        ids.Add(plugin.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add(new PluginAssemblyLoadError(Path.GetFileName(dll), ex));
                hostLog.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 插件预检加载失败（{Path.GetFileName(dll)}）：{ex.Message}");
            }
        }

        return new PluginProbeResult(ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), errors);
    }

    private static Assembly? ResolveDependency(AssemblyLoadContext context, AssemblyName name, string directory)
    {
        // 宿主默认上下文已加载的同名程序集优先（Core 等）
        var inDefault = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name);
        if (inDefault is not null)
        {
            return inDefault;
        }

        // 包内伴生 DLL（插件自己的依赖）
        var candidate = Path.Combine(directory, name.Name + ".dll");
        return File.Exists(candidate) ? context.LoadFromStream(new MemoryStream(File.ReadAllBytes(candidate), writable: false)) : null;
    }
}