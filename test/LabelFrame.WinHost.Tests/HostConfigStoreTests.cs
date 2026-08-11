using LabelFrame.WinHost;

namespace LabelFrame.WinHost.Tests;

public class HostConfigStoreTests
{
    [Fact]
    public void Save_then_load_should_round_trip()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-hostcfg-{Guid.NewGuid():N}.json");
        try
        {
            var store = new HostConfigStore(path);
            store.SaveServerUrl("http://127.0.0.1:53961");

            Assert.Equal("http://127.0.0.1:53961", store.LoadServerUrl());
        }
        finally
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
    }

    [Fact]
    public void Load_missing_file_should_return_null()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-hostcfg-{Guid.NewGuid():N}.json");
        var store = new HostConfigStore(path);
        Assert.Null(store.LoadServerUrl());
    }

    [Fact]
    public void Load_corrupt_file_should_return_null()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-hostcfg-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ not json");
            var store = new HostConfigStore(path);
            Assert.Null(store.LoadServerUrl());
        }
        finally
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
    }

    [Fact]
    public void Save_should_overwrite_previous_value()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lf-hostcfg-{Guid.NewGuid():N}.json");
        try
        {
            var store = new HostConfigStore(path);
            store.SaveServerUrl("http://127.0.0.1:53961");
            store.SaveServerUrl("http://192.168.1.10:53961");

            Assert.Equal("http://192.168.1.10:53961", store.LoadServerUrl());
        }
        finally
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
    }
}
