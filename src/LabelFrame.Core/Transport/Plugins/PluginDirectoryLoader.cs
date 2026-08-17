using System.Reflection;
using System.Runtime.Loader;

namespace LabelFrame.Core.Transport.Plugins;

/// <summary>
/// 外部传输插件目录加载器：扫描插件目录下 *.dll（平铺手动放置 + 子目录安装包，迭代 23 决策 3A），
/// 用 collectible AssemblyLoadContext **字节加载**（LoadFromStream）并发现 <see cref="ITransportPlugin"/> 实现；
/// 单个插件失败只记日志、不影响宿主启动（决策 #68）。
/// 字节加载不锁插件文件（Windows LoadFromAssemblyPath 会锁 DLL）——卸载 = 删除插件文件 / 目录即可立即删除、
/// 覆盖安装可直接替换，运行中进程继续使用内存镜像，重启后按新文件装配（决策 2A「卸载 = 删除文件 + 重启生效」）。
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

        foreach (var dll in EnumeratePluginDlls(directory))
        {
            try
            {
                // ALC 命名用相对路径避免重名冲突（平铺 main.dll 与子目录包的 main.dll）
                var alcName = $"transport-plugin:{Path.GetRelativePath(directory, dll)}";
                var alc = new AssemblyLoadContext(alcName, isCollectible: true);
                // 依赖优先回退宿主默认上下文（Core 等已在宿主加载），其次插件目录内的伴生 DLL（字节加载，不锁文件）
                alc.Resolving += (context, name) => ResolvePluginDependency(context, name, Path.GetDirectoryName(dll));
                var assembly = alc.LoadFromStream(new MemoryStream(File.ReadAllBytes(dll), writable: false));
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

    /// <summary>插件 DLL 枚举：平铺（手动放置，向后兼容）+ 子目录（安装包，迭代 23 决策 3A）。</summary>
    private static IEnumerable<string> EnumeratePluginDlls(string directory)
    {
        foreach (var dll in Directory.GetFiles(directory, "*.dll"))
        {
            yield return dll;
        }

        foreach (var subdir in Directory.GetDirectories(directory))
        {
            foreach (var dll in Directory.GetFiles(subdir, "*.dll"))
            {
                yield return dll;
            }
        }
    }

    private static Assembly? ResolvePluginDependency(AssemblyLoadContext context, AssemblyName name, string? pluginDirectory)
    {
        // 宿主默认上下文已加载的同名程序集优先（Core 等）
        var inDefault = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name);
        if (inDefault is not null)
        {
            return inDefault;
        }

        // 插件目录内的伴生 DLL（插件自己的依赖；字节加载，不锁文件）
        if (!string.IsNullOrWhiteSpace(pluginDirectory))
        {
            var candidate = Path.Combine(pluginDirectory, name.Name + ".dll");
            if (File.Exists(candidate))
            {
                return context.LoadFromStream(new MemoryStream(File.ReadAllBytes(candidate), writable: false));
            }
        }

        return null;
    }
}