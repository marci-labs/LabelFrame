namespace LabelFrame.Core.Excel;

/// <summary>Excel 文本表（headers 为表头，rows 为单元格文本）。</summary>
public sealed record ExcelTextTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>xlsx 读取为文本表（解析在 Core 统一实现，Server / WinHost 端点共用）。</summary>
public static class ExcelTableReader
{
    /// <summary>读取 xlsx：表头 + 数据行，所有单元格转为文本（日期 / 数值按固定格式，避免环境差异）。</summary>
    public static ExcelTextTable ReadTextTable(Stream stream)
    {
        var table = TemplateFrame.Excel.Simple.SimpleExcel.Read(stream);
        var headers = (table.Headers ?? []).Select(h => h ?? string.Empty).ToList();
        var rows = table.Rows
            .Select(row => row.Select(FormatCell).ToList())
            .ToList();
        return new ExcelTextTable(headers, rows);
    }

    private static string FormatCell(object? value) => value switch
    {
        null => string.Empty,
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
