using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace LabelFrame.AndroidHost;

/// <summary>入口 Activity：申请通知权限并启动前台打印服务。</summary>
[Activity(Label = "LabelFrame AndroidHost", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            RequestPermissions([Android.Manifest.Permission.PostNotifications], 1);
        }

        var intent = new Intent(this, typeof(PrintHostService));
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            StartForegroundService(intent);
        }
        else
        {
            StartService(intent);
        }

        Finish();
    }
}