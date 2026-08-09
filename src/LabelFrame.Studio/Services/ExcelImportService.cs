using System.Globalization;
using System.IO;

namespace LabelFrame.Studio.Services;

/// <summary>Excel 表格数据：表头 + 数据行（单元格统一转为字符串，null 为空串）。</summary>
public sealed record ExcelTableData(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>
/// Excel 导入服务（迭代 9）：读取 .xlsx（标题行 + 数据行，基于 TemplateFrame.Excel.Simple），
/// 提供列 → 契约字段映射建议与按行生成标签数据；UI 栈无关，可被 WPF / Web 复用。
/// </summary>
public static class ExcelImportService
{
    /// <summary>读取 xlsx 文件（优先命名区域 TF_Table，回退第一个非空行作为表头）。</summary>
    public static ExcelTableData Read(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var table = TemplateFrame.Excel.Simple.SimpleExcel.Read(stream);
        var headers = (table.Headers ?? []).Select(h => h ?? string.Empty).ToList();
        var rows = table.Rows
            .Select(row => row.Select(FormatCell).ToList())
            .ToList();
        return new ExcelTableData(headers, rows);
    }

    /// <summary>生成列 → 字段映射建议：Excel 列名与字段 Key / DisplayName 忽略大小写匹配；未匹配留空。</summary>
    public static IReadOnlyList<string> SuggestMapping(
        IReadOnlyList<string> headers,
        IReadOnlyList<string> fieldKeys)
    {
        return headers
            .Select(header => fieldKeys.FirstOrDefault(key =>
                string.Equals(key, header, StringComparison.OrdinalIgnoreCase)) ?? string.Empty)
            .ToList();
    }

    /// <summary>按映射（Excel 列 → 字段 key）把数据行转成标签数据字典（空映射列忽略）。</summary>
    public static List<Dictionary<string, string>> BuildRowsData(
        ExcelTableData table,
        IReadOnlyList<string> mapping)
    {
        var result = new List<Dictionary<string, string>>();
        foreach (var row in table.Rows)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < mapping.Count && i < row.Count; i++)
            {
                var key = mapping[i];
                if (!string.IsNullOrWhiteSpace(key))
                {
                    dict[key] = row[i];
                }
            }

            result.Add(dict);
        }

        return result;
    }

    private static string FormatCell(object? value) => value switch
    {
        null => string.Empty,
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}