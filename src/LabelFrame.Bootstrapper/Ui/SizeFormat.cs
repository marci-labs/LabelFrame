using System.Globalization;

namespace LabelFrame.Bootstrapper.Ui;

/// <summary>体积展示格式化（确认页）。</summary>
internal static class SizeFormat
{
    /// <summary>字节 → 可读体积（MB / GB，一位小数，InvariantCulture——与界面语言无关的数字呈现）。</summary>
    public static string Format(long bytes) => bytes >= 1024 * 1024 * 1024
        ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024 * 1024):0.0} GB")
        : string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024):0.0} MB");
}
