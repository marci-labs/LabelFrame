using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;
using LabelFrame.Studio.Services;
using LabelFrame.Studio.ViewModels;

namespace LabelFrame.Studio.Tests;

public class LayoutEditorViewModelTests
{
    private static TemplateSaveDto CreateTemplate() => new(
        "location-label",
        "项目A",
        new LabelContract
        {
            Name = "location-label",
            Version = "1.0",
            Fields =
            [
                new LabelField { Key = "locationCode", DisplayName = "库位码", IsRequired = true },
                new LabelField { Key = "zone", DisplayName = "区域", IsRequired = false },
            ],
        },
        new LabelLayout
        {
            Name = "location-label-100x60",
            ContractName = "location-label",
            ContractVersion = "1.0",
            WidthMm = 100,
            HeightMm = 60,
            Elements =
            [
                new LabelTextElement { SourceKey = "zone", XMm = 5, YMm = 4, FontHeightMm = 5, FontWidthMm = 5 },
                new LabelBarcodeElement { SourceKey = "locationCode", XMm = 5, YMm = 26, HeightMm = 22, ModuleWidth = 2 },
            ],
        });

    [Fact]
    public void LoadFrom_should_populate_fields_and_elements()
    {
        var vm = new LayoutEditorViewModel();

        vm.LoadFrom(CreateTemplate());

        Assert.Equal("location-label", vm.Name);
        Assert.Equal(2, vm.Fields.Count);
        Assert.Equal(2, vm.Elements.Count);
        Assert.Equal(EditorElementType.Text, vm.Elements[0].Type);
        Assert.Equal(EditorElementType.Barcode, vm.Elements[1].Type);
        Assert.Equal("locationCode", vm.Elements[1].SourceKey);
    }

    [Fact]
    public void AddElement_should_use_first_field_as_source_key()
    {
        var vm = new LayoutEditorViewModel();
        vm.LoadFrom(CreateTemplate());

        vm.AddElement(EditorElementType.QrCode);

        var added = vm.Elements.Last();
        Assert.Equal(EditorElementType.QrCode, added.Type);
        Assert.Equal("locationCode", added.SourceKey);
        Assert.Same(added, vm.SelectedElement);
    }

    [Fact]
    public void MoveElement_should_convert_pixels_to_mm()
    {
        var vm = new LayoutEditorViewModel();
        vm.LoadFrom(CreateTemplate());
        var element = vm.Elements[0];
        var originalX = element.XMm;
        var originalY = element.YMm;

        vm.MoveElement(element, dxPx: 40, dyPx: 20); // 100% 缩放 4px/mm

        Assert.Equal(originalX + 10, element.XMm);
        Assert.Equal(originalY + 5, element.YMm);
    }

    [Fact]
    public void BuildContract_and_layout_should_round_trip()
    {
        var vm = new LayoutEditorViewModel();
        vm.LoadFrom(CreateTemplate());
        vm.AddElement(EditorElementType.Line);
        var line = vm.Elements.Last();
        vm.MoveElement(line, 20, 0);

        var contract = vm.BuildContract();
        var layout = vm.BuildLayout();

        Assert.Equal("location-label", contract.Name);
        Assert.Equal(2, contract.Fields.Count);
        Assert.Equal(3, layout.Elements.Count);
        Assert.IsType<LabelLineElement>(layout.Elements[2]);
        Assert.Equal(100, layout.WidthMm);
        Assert.Equal(60, layout.HeightMm);
    }

    [Fact]
    public void LoadFrom_empty_template_should_support_creation_flow()
    {
        var vm = new LayoutEditorViewModel();
        vm.LoadFrom(new TemplateSaveDto(
            "new-label",
            "默认",
            new LabelContract { Name = "new-label", Version = "1.0", Fields = [] },
            new LabelLayout
            {
                Name = "new-label-layout",
                ContractName = "new-label",
                ContractVersion = "1.0",
                WidthMm = 80,
                HeightMm = 40,
                Elements = [],
            }));

        Assert.Empty(vm.Fields);
        Assert.Empty(vm.Elements);
        Assert.Equal(80, vm.WidthMm);
        Assert.Equal(40, vm.HeightMm);

        // 无字段时添加元素：SourceKey 回退默认 text
        vm.AddElement(EditorElementType.Text);
        Assert.Equal("text", vm.Elements[0].SourceKey);

        vm.AddField();
        Assert.Single(vm.Fields);

        var dto = vm.BuildSaveDto();
        Assert.Equal("new-label", dto.Name);
        Assert.Single(dto.Layout!.Elements);
        Assert.Single(dto.Contract!.Fields);
    }

    [Fact]
    public void SaveAsync_without_client_should_throw()
    {
        var vm = new LayoutEditorViewModel();
        vm.LoadFrom(CreateTemplate());

        Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveAsync());
    }
}