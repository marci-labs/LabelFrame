using System.IO;
using LabelFrame.Studio.Services;

namespace LabelFrame.Studio.Tests;

public class ExcelImportServiceTests
{
    [Fact]
    public void Read_should_parse_headers_and_rows()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lftest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "items.xlsx");
            var table = new TemplateFrame.Excel.Simple.SimpleExcelTable
            {
                Headers = ["物料代码", "物料名称", "数量"],
                Rows =
                [
                    ["AL-6063", "铝型材", 120],
                    ["SS-M8", "螺栓", 500],
                ],
            };
            using (var stream = File.Create(file))
            {
                TemplateFrame.Excel.Simple.SimpleExcel.Write(
                    stream,
                    table,
                    new TemplateFrame.Excel.Simple.SimpleExcelOptions { SheetName = "物料" });
            }

            var data = ExcelImportService.Read(file);

            Assert.Equal(["物料代码", "物料名称", "数量"], data.Headers);
            Assert.Equal(2, data.Rows.Count);
            Assert.Equal("AL-6063", data.Rows[0][0]);
            Assert.Equal("铝型材", data.Rows[0][1]);
            Assert.Equal("120", data.Rows[0][2]);
            Assert.Equal("500", data.Rows[1][2]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SuggestMapping_should_match_by_key_case_insensitive()
    {
        var headers = new[] { "LocationCode", "其他", "zone" };
        var keys = new[] { "locationCode", "zone" };

        var mapping = ExcelImportService.SuggestMapping(headers, keys);

        Assert.Equal("locationCode", mapping[0]);
        Assert.Equal(string.Empty, mapping[1]);
        Assert.Equal("zone", mapping[2]);
    }

    [Fact]
    public void BuildRowsData_should_map_columns_and_skip_empty_mapping()
    {
        var table = new ExcelTableData(
            ["列A", "列B"],
            [["a1", "b1"], ["a2", "b2"]]);

        var rows = ExcelImportService.BuildRowsData(table, ["keyA", ""]);

        Assert.Equal(2, rows.Count);
        Assert.Equal("a1", rows[0]["keyA"]);
        Assert.Equal("a2", rows[1]["keyA"]);
        Assert.False(rows[0].ContainsKey("keyB"));
    }
}