using LabelFrame.Core.Layout;
using SkiaSharp;

namespace LabelFrame.Rendering;

/// <summary>
/// Skia 字型查找：文本渲染与字宽度量共用同一字型（保证锚定偏移的度量口径与实际绘制一致）。
/// </summary>
internal static class SkiaTypefaceLookup
{
    private const string FontFamily = "Microsoft YaHei";

    /// <summary>
    /// 创建文本字型：优先指定字体族（fontFamily，缺省回退微软雅黑）；文本含非 ASCII（中文等）时用系统字体回退
    /// 匹配常见中文字符，避免指定字体缺字型导致整段文本不绘制。
    /// </summary>
    public static SKTypeface CreateTypeface(string fontFamily, string value)
    {
        var family = string.IsNullOrWhiteSpace(fontFamily) ? LabelTextElement.DefaultFontFamily : fontFamily;
        var preferred = SKTypeface.FromFamilyName(family) ?? SKTypeface.FromFamilyName(FontFamily) ?? SKTypeface.Default;
        if (string.IsNullOrEmpty(value))
        {
            return preferred;
        }

        if (value.Any(c => c > 0x7F))
        {
            // 含中文等非 ASCII：用常见 CJK 字符匹配系统回退字体。
            // 不能用文本首字符匹配——首字符若是生僻字，会匹配到只含该字的小字体，
            // 其余字符无字型导致整段文本只剩一两个字。
            return SKFontManager.Default.MatchCharacter('中') ?? preferred;
        }

        return preferred;
    }
}
