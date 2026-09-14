namespace LabelFrame.Bootstrapper.Wizard;

/// <summary>dry-run 契约的 UI 明示文案（Issue #53 AC-03）：确认页横幅与欢迎页提示共用，测试断言锚点。</summary>
public static class DryRunNotice
{
    /// <summary>确认页横幅（醒目位置）：明示本向导只预览、不下载、不安装、不改系统。</summary>
    public const string Banner = "仅预览：尚未下载、尚未安装，不会对系统做任何改动。";

    /// <summary>欢迎页提示：从第一页就设定预期（本轮交付为骨架 + dry-run）。</summary>
    public const string WelcomeHint = "本向导当前为预览版（dry-run）：完成问卷后只展示将要下载与安装的内容，不会实际下载 / 安装。";
}
