using LabelFrame.Core.Contracts;
using LabelFrame.Core.Layout;
using LabelFrame.Core.Templates;

namespace LabelFrame.Core.Tests.Templates;

public class TemplateStoreTests
{
    private static TemplatePackage CreatePackage(string name, string group) => new()
    {
        Name = name,
        Group = group,
        Contract = new LabelContract
        {
            Name = "location-label",
            Version = "1.0",
            Fields = [new LabelField { Key = "locationCode", DisplayName = "库位码", IsRequired = true }],
        },
        Layout = new LabelLayout
        {
            Name = "l",
            ContractName = "location-label",
            ContractVersion = "1.0",
            WidthMm = 100,
            HeightMm = 60,
            Elements = [new LabelTextElement { SourceKey = "locationCode", XMm = 5, YMm = 5, FontHeightMm = 8, FontWidthMm = 8 }],
        },
        Images = new Dictionary<string, byte[]> { ["logo.png"] = new byte[] { 9, 8, 7 } },
    };

    [Fact]
    public async Task Save_get_should_preserve_package()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        try
        {
            var store = new TemplateStore(dbPath);
            await store.InitializeAsync();
            await store.SaveAsync(CreatePackage("location-label", "项目A"));

            var loaded = await store.GetAsync("location-label");

            Assert.NotNull(loaded);
            Assert.Equal("项目A", loaded!.Group);
            Assert.Equal("location-label", loaded.Contract.Name);
            Assert.True(loaded.Images.ContainsKey("logo.png"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public async Task Save_get_should_preserve_test_data()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        try
        {
            var store = new TemplateStore(dbPath);
            await store.InitializeAsync();
            var package = CreatePackage("location-label", "项目A");
            package = new TemplatePackage
            {
                Name = package.Name,
                Group = package.Group,
                Contract = package.Contract,
                Layout = package.Layout,
                Images = package.Images,
                TestData = new Dictionary<string, string> { ["locationCode"] = "A-01-02-03", ["zone"] = "成品仓" },
            };
            await store.SaveAsync(package);

            var loaded = await store.GetAsync("location-label");

            Assert.NotNull(loaded);
            Assert.Equal("A-01-02-03", loaded!.TestData["locationCode"]);
            Assert.Equal("成品仓", loaded.TestData["zone"]);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public async Task Save_get_should_default_test_data_empty()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        try
        {
            var store = new TemplateStore(dbPath);
            await store.InitializeAsync();
            await store.SaveAsync(CreatePackage("t1", "项目A"));

            var loaded = await store.GetAsync("t1");

            Assert.NotNull(loaded);
            Assert.Empty(loaded!.TestData);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public async Task Save_should_upsert_and_replace_images()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        try
        {
            var store = new TemplateStore(dbPath);
            await store.InitializeAsync();
            await store.SaveAsync(CreatePackage("t1", "项目A"));
            await store.SaveAsync(CreatePackage("t1", "项目B"));

            var loaded = await store.GetAsync("t1");

            Assert.Equal("项目B", loaded!.Group);
            Assert.Single(await store.ListAsync());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public async Task List_should_filter_by_group()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        try
        {
            var store = new TemplateStore(dbPath);
            await store.InitializeAsync();
            await store.SaveAsync(CreatePackage("a1", "项目A"));
            await store.SaveAsync(CreatePackage("b1", "项目B"));

            var groupA = await store.ListAsync("项目A");
            var all = await store.ListAsync();

            Assert.Single(groupA);
            Assert.Equal(2, all.Count);
            Assert.Equal("项目A", all[0].Group);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }

    [Fact]
    public async Task Delete_should_remove_template_and_images()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lftpl-{Guid.NewGuid():N}.db");
        try
        {
            var store = new TemplateStore(dbPath);
            await store.InitializeAsync();
            await store.SaveAsync(CreatePackage("t1", "项目A"));

            await store.DeleteAsync("t1");

            Assert.Null(await store.GetAsync("t1"));
            Assert.Empty(await store.ListAsync());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
        }
    }
}