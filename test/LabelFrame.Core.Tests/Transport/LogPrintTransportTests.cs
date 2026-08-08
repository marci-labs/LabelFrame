using LabelFrame.Core.Transport;

namespace LabelFrame.Core.Tests.Transport;

public class LogPrintTransportTests
{
    [Fact]
    public async Task SendAsync_should_write_command_to_writer()
    {
        var writer = new StringWriter();
        var transport = new LogPrintTransport(writer);

        await transport.SendAsync("^XA^XZ");

        var output = writer.ToString();
        Assert.Contains("^XA^XZ", output);
        Assert.StartsWith("=== LabelFrame 模拟打印机 ===", output);
    }
}