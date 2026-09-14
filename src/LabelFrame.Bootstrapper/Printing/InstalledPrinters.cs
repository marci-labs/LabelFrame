using System.Drawing.Printing;

namespace LabelFrame.Bootstrapper.Printing;

/// <summary>读取 Windows 已装打印机驱动名（只读；dry-run 契约允许的系统读取，无任何写入）。</summary>
public static class InstalledPrinters
{
    /// <summary>已装打印机名列表（如 ZDesigner ZD421-203dpi ZPL）；读不到（打印服务不可用等异常）按空集处理，不阻塞问卷。</summary>
    public static IReadOnlyList<string> GetNames()
    {
        try
        {
            return [.. PrinterSettings.InstalledPrinters.Cast<string>()];
        }
        catch (SystemException)
        {
            // 打印后台服务不可用 / 枚举失败：品牌预选退化为全不勾选，不影响问卷继续
            return [];
        }
    }
}
