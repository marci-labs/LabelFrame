using System.Reflection;
using System.Runtime.Loader;

namespace LabelFrame.Core.Transport.Plugins;

/// <summary>
/// 外部传输插件目录加载器：扫描插件目录下 *.dll，用 collectible AssemblyLoadContext 加载并发现
/// <see cref="ITransportPlugin"/> 实现；单个插件失败只记日志、不影响宿主启动（决策 #68）。
/// 卸载 = 从插件目录删除对应 dll / 文件夹 → 重启宿主后不再加载（本轮不做运行时热卸载，见 DESIGN 未决）。
/// </summary>
public static class PluginDirectoryLoader
{
    /// <summary>
    /// 加载目录下所有可发现的传输插件。
    /// </summary>
    /// <param name="directory">插件目录（不存在 / 为空返回空列表）。</param>
    /// <param name="hostLog">宿主日志写入器（加载失败记录用）。</param>
    /// <returns>(插件实例, 来源 DLL 路径) 列表。</returns>
    public static IReadOnlyList<(ITransportPlugin Plugin, string AssemblyPath)> Load(string? directory, TextWriter hostLog)
    {
        var result = new List<(ITransportPlugin, string)>();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return result;
        }

        foreach (var dll in Directory.GetFiles(directory, "*.dll"))
        {
            try
            {
                var alc = new AssemblyLoadContext($"transport-plugin:{Path.GetFileNameWithoutExtension(dll)}", isCollectible: true);
                // 插件依赖优先回退宿主默认上下文（Core 等已在宿主加载），其次插件目录内的伴生 DLL
                alc.Resolving += (context, name) => ResolvePluginDependency(context, name, Path.GetDirectoryName(dll));
                var assembly = alc.LoadFromAssemblyPath(dll);
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsAbstract || type.IsInterface || !typeof(ITransportPlugin).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    if (Activator.CreateInstance(type) is ITransportPlugin plugin)
                    {
                        result.Add((plugin, dll));
                    }
                }
            }
            catch (Exception ex)
            {
                hostLog.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 传输插件加载失败（{Path.GetFileName(dll)}）：{ex.Message}");
            }
        }

        return result;
    }

    private static Assembly? ResolvePluginDependency(AssemblyLoadContext context, AssemblyName name, string? pluginDirectory)
    {
        // 宿主默认上下文已加载的同名程序集优先（Core 等）
        var inDefault = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name);
        if (inDefault is not null)
        {
            return inDefault;
        }

        // 插件目录内的伴生 DLL（插件自己的依赖）
        if (!string.IsNullOrWhiteSpace(pluginDirectory))
        {
            var candidate = Path.Combine(pluginDirectory, name.Name + ".dll");
            if (File.Exists(candidate))
            {
                return context.LoadFromAssemblyPath(candidate);
            }
        }

        return null;
    }
}
