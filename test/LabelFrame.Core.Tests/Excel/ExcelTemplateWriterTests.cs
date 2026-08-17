using System.IO.Compression;
using LabelFrame.Core.Excel;
using TemplateFrame.Excel.Simple;

namespace LabelFrame.Core.Tests.Excel;

public class ExcelTemplateWriterTests
{
    [Fact]
    public void CreateTemplate_should_write_valid_xlsx_with_headers_and_sample_row()
    {
        var columns = new[]
        {
            new ExcelTemplateColumn("locationCode", "库位码"),
            new ExcelTemplateColumn("zone", "区域"),
        };
        var sampleRow = new Dictionary<string, string>
        {
            ["locationCode"] = "A-01-02-03",
            ["zone"] = "A-01",
        };

        var bytes = ExcelTemplateWriter.CreateTemplate(columns, sampleRow);

        // xlsx 是 zip 容器：以 PK 开头
        Assert.True(bytes.Length > 0);
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);

        // 回读验证表头 + 示例行（SimpleExcel.Read 复用同一库）
        using var stream = new MemoryStream(bytes);
        var table = SimpleExcel.Read(stream);
        Assert.Equal(new[] { "库位码", "区域" }, table.Headers);
        var row = Assert.Single(table.Rows);
        Assert.Equal("A-01-02-03", row[0]);
        Assert.Equal("A-01", row[1]);
    }

    [Fact]
    public void CreateTemplate_without_sample_row_should_write_headers_only()
    {
        var columns = new[]
        {
            new ExcelTemplateColumn("locationCode", "库位码"),
            new ExcelTemplateColumn("zone", "区域"),
        };

        var bytes = ExcelTemplateWriter.CreateTemplate(columns, null);

        using var stream = new MemoryStream(bytes);
        var table = SimpleExcel.Read(stream);
        Assert.Equal(new[] { "库位码", "区域" }, table.Headers);
        Assert.Empty(table.Rows);
    }

    [Fact]
    public void CreateTemplate_should_align_sample_row_to_column_keys()
    {
        // 示例值键与列顺序无关：按列 key 对齐
        var columns = new[]
        {
            new ExcelTemplateColumn("a", "A 列"),
            new ExcelTemplateColumn("b", "B 列"),
        };
        var sampleRow = new Dictionary<string, string> { ["b"] = "B 值", ["a"] = "A 值" };

        var bytes = ExcelTemplateWriter.CreateTemplate(columns, sampleRow);

        using var stream = new MemoryStream(bytes);
        var table = SimpleExcel.Read(stream);
        var row = Assert.Single(table.Rows);
        Assert.Equal("A 值", row[0]);
        Assert.Equal("B 值", row[1]);
    }
}
