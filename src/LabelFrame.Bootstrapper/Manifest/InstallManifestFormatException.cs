namespace LabelFrame.Bootstrapper.Manifest;

/// <summary>安装清单解析 / 校验失败（fail-closed：消息面向用户为中文可行动文案）。</summary>
public sealed class InstallManifestFormatException(string message, Exception? innerException = null)
    : Exception(message, innerException);
