namespace LabelFrame.Bootstrapper.Wizard;

/// <summary>执行边界契约的 UI 明示文案（Issue #53 AC-03 问卷只读锚点；迭代 62 决策 #124 修订为「确认前只读」）：确认页横幅与欢迎页提示共用，测试断言锚点。</summary>
/// <remarks>
/// #53 原文为「仅预览（dry-run）：绝不下载安装」；#124 起 Apply 在确认页「安装」后启用——问卷阶段（含确认页展示）
/// 仍保持零下载 / 零安装 / 零系统改动，文案相应改为「确认前」口径。
/// </remarks>
public static class ExecuteBoundaryNotice
{
    /// <summary>确认页横幅（醒目位置）：明示点击「安装」前不下载、不安装、不改动系统；点击后按上表执行。</summary>
    public const string Banner = "确认前只读：尚未下载、尚未安装，不会对系统做任何改动；点击「安装」后将按上表下载并安装。";

    /// <summary>欢迎页提示：从第一页就设定预期（完成问卷确认后开始执行）。</summary>
    public const string WelcomeHint = "完成问卷并确认组件集合后，点击「安装」开始下载与安装（确认前不会对系统做任何改动）。";
}
