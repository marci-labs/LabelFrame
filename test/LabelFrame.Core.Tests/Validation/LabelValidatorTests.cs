using LabelFrame.Core.Tests.Samples;
using LabelFrame.Core.Validation;

namespace LabelFrame.Core.Tests.Validation;

public class LabelValidatorTests
{
    [Fact]
    public void Valid_data_should_pass()
    {
        var document = LocationLabelSamples.CreateDocument();

        var result = LabelValidator.Validate(LocationLabelSamples.Contract, document.Data);

        Assert.True(result.IsValid);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Missing_required_field_should_return_problem_code()
    {
        var document = LocationLabelSamples.CreateDocument();
        var data = new Dictionary<string, string>(document.Data);
        data.Remove("locationCode");

        var result = LabelValidator.Validate(LocationLabelSamples.Contract, data);

        Assert.False(result.IsValid);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(LabelProblemCodes.RequiredFieldMissing, problem.Code);
        Assert.Equal("locationCode", problem.FieldKey);
        Assert.Contains("库位码", problem.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Required_field_with_blank_value_should_fail(string value)
    {
        var data = new Dictionary<string, string>
        {
            ["locationCode"] = value,
            ["zone"] = "A-01",
        };

        var result = LabelValidator.Validate(LocationLabelSamples.Contract, data);

        Assert.False(result.IsValid);
        Assert.Equal(LabelProblemCodes.RequiredFieldMissing, Assert.Single(result.Problems).Code);
    }

    [Fact]
    public void Missing_optional_field_should_pass()
    {
        var document = LocationLabelSamples.CreateDocument();
        var data = new Dictionary<string, string>(document.Data);
        data.Remove("remark");

        var result = LabelValidator.Validate(LocationLabelSamples.Contract, data);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Multiple_missing_required_fields_should_return_all_problems()
    {
        var result = LabelValidator.Validate(LocationLabelSamples.Contract, new Dictionary<string, string>());

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Problems.Count);
        Assert.All(result.Problems, p => Assert.Equal(LabelProblemCodes.RequiredFieldMissing, p.Code));
    }
}