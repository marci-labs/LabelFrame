namespace LabelFrame.Bootstrapper.Ba;

using WixToolset.BootstrapperApplicationApi;

internal static class Program
{
    private static int Main()
    {
        // WiX v5.4+ out-of-proc 托管 BA：与 Burn 引擎握手并阻塞泵送引擎消息，直到 BA 调用 Quit；
        // UI 运行在引擎派生的 STA「UIThread」（OnStartup → Run），Main 只承担握手
        ManagedBootstrapperApplication.Run(new LabelFrameBootstrapperBa());
        return 0;
    }
}
