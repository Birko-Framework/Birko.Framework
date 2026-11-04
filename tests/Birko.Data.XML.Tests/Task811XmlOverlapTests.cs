using System;
using System.IO;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.XML.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.XML.Tests;

/// <summary>
/// TASK-811, the XML half. Measured before the fix: the XML separate and batch stores found their files by
/// <c>{Name}-*.xml</c>, so a store named <c>items</c> also read and destroyed the files of one named <c>items-archive</c>
/// in the same location — the defect the JSON stores had. A file is a store's only when it is <c>{Name}-</c>, exactly one
/// Guid, then <c>.xml</c>.
/// </summary>
public class Task811XmlOverlapTests : IDisposable
{
    private readonly string _location = "birko-xml-811-" + Guid.NewGuid().ToString("N");
    private string Dir => Path.GetFullPath(_location);

    public Task811XmlOverlapTests() => Directory.CreateDirectory(Dir);

    public void Dispose()
    {
        TestTempDirectory.Delete(Dir);
    }

    public static TheoryData<string> Kinds => new() { "separate", "separate-bulk", "batch", "batch-bulk" };

    private Settings For(string kind, string name)
        => kind.StartsWith("batch") ? new BatchSettings { Location = _location, Name = name, BatchSize = 2 } : new Settings(_location, name);

    private XmlStore<TestModel> Sync(string kind, string name)
    {
        XmlStore<TestModel> store = kind switch
        {
            "separate" => new XmlSeparateStore<TestModel>(),
            "separate-bulk" => new XmlSeparateBulkStore<TestModel>(),
            "batch" => new XmlBatchStore<TestModel>(),
            _ => new XmlBatchBulkStore<TestModel>(),
        };
        store.SetSettings(For(kind, name));
        return store;
    }

    private AsyncXmlStore<TestModel> Async(string kind, string name)
    {
        AsyncXmlStore<TestModel> store = kind switch
        {
            "separate" => new AsyncXmlSeparateStore<TestModel>(),
            "separate-bulk" => new AsyncXmlSeparateBulkStore<TestModel>(),
            "batch" => new AsyncXmlBatchStore<TestModel>(),
            _ => new AsyncXmlBatchBulkStore<TestModel>(),
        };
        store.SetSettings(For(kind, name));
        return store;
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Stores_whose_names_overlap_keep_to_their_own_records(string kind)
    {
        Sync(kind, "items").Create(new TestModel { Name = "item" });
        Sync(kind, "items-archive").Create(new TestModel { Name = "archived" });

        Sync(kind, "items").Count().Should().Be(1);

        Sync(kind, "items").Destroy();

        Sync(kind, "items-archive").Count().Should().Be(1, "destroying items leaves items-archive's records");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Async_stores_whose_names_overlap_keep_to_their_own_records(string kind)
    {
        await Async(kind, "items").CreateAsync(new TestModel { Name = "item" });
        await Async(kind, "items-archive").CreateAsync(new TestModel { Name = "archived" });

        (await Async(kind, "items").CountAsync()).Should().Be(1);

        await Async(kind, "items").DestroyAsync();

        (await Async(kind, "items-archive").CountAsync()).Should().Be(1);
    }
}
