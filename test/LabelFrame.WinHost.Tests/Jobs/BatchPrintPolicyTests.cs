using LabelFrame.WinHost.Jobs;

namespace LabelFrame.WinHost.Tests.Jobs;

public class BatchPrintPolicyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(10)]
    public void Should_not_pause_when_disabled(int sendsCompleted)
    {
        var settings = new PrintSettingsDto(false, 5, 500);
        Assert.False(BatchPrintPolicy.ShouldPauseBeforeSend(settings, sendsCompleted));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(100)]
    public void Should_not_pause_when_zero_sends_completed(int batchSize)
    {
        var settings = new PrintSettingsDto(true, batchSize, 500);
        Assert.False(BatchPrintPolicy.ShouldPauseBeforeSend(settings, 0));
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(5, 4)]
    [InlineData(5, 6)]
    [InlineData(10, 19)]
    public void Should_not_pause_when_not_multiple_of_batch_size(int batchSize, int sendsCompleted)
    {
        var settings = new PrintSettingsDto(true, batchSize, 500);
        Assert.False(BatchPrintPolicy.ShouldPauseBeforeSend(settings, sendsCompleted));
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(5, 10)]
    [InlineData(10, 10)]
    [InlineData(10, 20)]
    public void Should_pause_when_multiple_of_batch_size(int batchSize, int sendsCompleted)
    {
        var settings = new PrintSettingsDto(true, batchSize, 500);
        Assert.True(BatchPrintPolicy.ShouldPauseBeforeSend(settings, sendsCompleted));
    }

    [Fact]
    public void Should_not_pause_when_batch_size_is_zero()
    {
        // 防御：Normalize 后 batchSize 恒 ≥ 1，此处覆盖 0 不抛异常
        var settings = new PrintSettingsDto(true, 0, 500);
        Assert.False(BatchPrintPolicy.ShouldPauseBeforeSend(settings, 5));
    }
}
