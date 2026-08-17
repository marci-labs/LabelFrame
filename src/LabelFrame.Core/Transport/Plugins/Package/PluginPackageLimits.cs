namespace LabelFrame.Core.Transport.Plugins.Package;

/// <summary>插件包大小上限（迭代 23 决策 5A；Server / WinHost 上传端点与 Kestrel MaxRequestBodySize 同步）。</summary>
public static class PluginPackageLimits
{
    /// <summary>上限字节数（64MB）。</summary>
    public const long MaxBytes = 64L * 1024 * 1024;

    /// <summary>中文展示（如 "64MB"）。</summary>
    public static string Display => "64MB";
}