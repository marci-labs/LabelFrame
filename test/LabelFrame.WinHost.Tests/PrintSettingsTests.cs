using LabelFrame.WinHost;

namespace LabelFrame.WinHost.Tests;

public class PrintSettingsTests
{
    [Fact]
    public void Defaults_should_be_disabled_batch_10_interval_500()
    {
        Assert.Equal(new PrintSettingsDto(false, 10, 500), PrintSettings.Defaults);
    }

    [Fact]
    public void Normalize_should_keep_valid_values()
    {
        var normalized = PrintSettings.Normalize(true, 20, 300);
        Assert.Equal(new PrintSettingsDto(true, 20, 300), normalized);
    }

    [Fact]
    public void Normalize_batch_size_below_one_should_return_default_10()
    {
        Assert.Equal(10, PrintSettings.Normalize(true, 0, 500).BatchSize);
        Assert.Equal(10, PrintSettings.Normalize(true, -3, 500).BatchSize);
    }

    [Fact]
    public void Normalize_negative_interval_should_return_default_500()
    {
        Assert.Equal(500, PrintSettings.Normalize(true, 10, -1).BatchIntervalMs);
        Assert.Equal(500, PrintSettings.Normalize(true, 10, -100).BatchIntervalMs);
    }

    [Fact]
    public void Normalize_missing_or_non_bool_enabled_should_return_false()
    {
        Assert.False(PrintSettings.Normalize(null, 10, 500).BatchEnabled);
        Assert.False(PrintSettings.Normalize(false, 10, 500).BatchEnabled);
    }

    [Fact]
    public void Validate_should_accept_valid_values()
    {
        Assert.Null(PrintSettings.Validate(1, 0));
        Assert.Null(PrintSettings.Validate(10, 500));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_should_reject_batch_size_below_one(int batchSize)
    {
        Assert.NotNull(PrintSettings.Validate(batchSize, 500));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-50)]
    public void Validate_should_reject_negative_interval(int intervalMs)
    {
        Assert.NotNull(PrintSettings.Validate(10, intervalMs));
    }

    [Fact]
    public void Update_then_snapshot_should_round_trip()
    {
        var settings = new PrintSettings();
        settings.Update(new PrintSettingsDto(true, 7, 250));

        Assert.Equal(new PrintSettingsDto(true, 7, 250), settings.Snapshot());
    }

    [Fact]
    public void New_instance_should_start_with_defaults()
    {
        Assert.Equal(PrintSettings.Defaults, new PrintSettings().Snapshot());
    }
}
