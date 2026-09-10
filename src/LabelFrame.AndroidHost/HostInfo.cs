namespace LabelFrame.AndroidHost;

/// <summary>
/// 应用版本（android:versionName）：构建时注入（发版 tag；本地构建为 csproj 默认值 1.0），
/// 供配置页「本机信息」子页与本地 HTTP（/healthz、/api/host/config）展示，用于升级确认与排障。
/// </summary>
public static class HostInfo
{
    /// <summary>取当前安装版本；进程生命周期内不变。</summary>
    public static string GetVersion(Android.Content.Context context)
    {
        try
        {
            return context.PackageManager?.GetPackageInfo(context.PackageName ?? string.Empty, 0)?.VersionName
                ?? "未知";
        }
        catch (Android.Content.PM.PackageManager.NameNotFoundException)
        {
            return "未知";
        }
    }
}
