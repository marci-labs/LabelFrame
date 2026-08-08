using LabelFrame.WinHost.Transport;

namespace LabelFrame.WinHost.Tests.Transport;

public class RawPrinterTransportTests
{
    [Fact]
    public void Constructor_should_require_printer_name()
    {
        Assert.Throws<ArgumentException>(() => new RawPrinterTransport(""));
    }

    [Fact]
    public async Task SendAsync_to_nonexistent_printer_should_throw_clear_error()
    {
        var printerName = $"LabelFrame-Nonexistent-{Guid.NewGuid():N}";
        var transport = new RawPrinterTransport(printerName);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync("^XA^XZ"));

        Assert.Contains(printerName, exception.Message);
        Assert.Contains("无法打开打印机", exception.Message);
    }
}