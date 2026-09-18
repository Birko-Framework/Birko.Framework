using Birko.Configuration;
using Birko.Data.XML.Stores;
using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.XML.Tests;

/// <summary>
/// CR-H111: XmlSeparateStore/AsyncXmlSeparateStore CreateCore wrote per-entity files with
/// File.OpenWrite, which does NOT truncate — a shorter payload over a stale/longer {Name}-{Guid}.xml
/// left trailing bytes → malformed XML silently skipped on load. CreateCore now uses File.Create
/// (truncating). CR-H113: broader round-trip coverage for the base + separate stores (sync + async).
/// </summary>
public class XmlSeparateAndTruncationTests : IDisposable
{
    private readonly string _location;
    private readonly string _dir;

    public XmlSeparateAndTruncationTests()
    {
        _location = "birko-xml-trunc-" + Guid.NewGuid().ToString("N");
        _dir = Path.GetFullPath(_location);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void SeparateStore_CreateOverStaleFile_DoesNotCorrupt()
    {
        var settings = new Settings(_location, "sep");
        var guid = Guid.NewGuid();

        // Discover the store's directory by writing one throwaway record.
        var seed = new XmlSeparateStore<TestModel>();
        seed.SetSettings(settings);
        seed.Create(new TestModel { Name = "seed", Value = 0 });
        var storeDir = Path.GetDirectoryName(Directory.GetFiles(_dir, "*.xml", SearchOption.AllDirectories).First())!;

        // Plant a stale, LONG, non-XML file at the exact path CreateCore will target for `guid`.
        // Being invalid XML, a fresh store skips it on load (so it's not in the in-memory set).
        var stalePath = Path.Combine(storeDir, $"sep-{guid}.xml");
        File.WriteAllText(stalePath, new string('X', 8000));

        // Fresh session: create that Guid with a SHORT payload. File.OpenWrite would leave trailing
        // 'X' bytes after the short XML → malformed; File.Create truncates.
        var s2 = new XmlSeparateStore<TestModel>();
        s2.SetSettings(settings);
        s2.Create(new TestModel { Guid = guid, Name = "short", Value = 2 });

        var s3 = new XmlSeparateStore<TestModel>();
        s3.SetSettings(settings);
        var loaded = s3.Read(guid);

        loaded.Should().NotBeNull("truncation must leave valid XML that loads");
        loaded!.Name.Should().Be("short");
        loaded.Value.Should().Be(2);
    }

    [Fact]
    public void SeparateStore_Create_Update_Delete_RoundTrips()
    {
        var settings = new Settings(_location, "sepcrud");
        var store = new XmlSeparateStore<TestModel>();
        store.SetSettings(settings);

        var item = new TestModel { Name = "a", Value = 1 };
        store.Create(item);
        var guid = item.Guid!.Value;

        item.Name = "b";
        store.Update(item);
        new XmlSeparateStore<TestModel>().Also(settings).Read(guid)!.Name.Should().Be("b");

        store.Delete(item);
        new XmlSeparateStore<TestModel>().Also(settings).Read(guid).Should().BeNull();
    }

    [Fact]
    public void BaseStore_RoundTrips()
    {
        var settings = new Settings(_location, "base");
        var store = new XmlStore<TestModel>();
        store.SetSettings(settings);
        store.Create(new TestModel { Name = "one", Value = 1 });
        store.Create(new TestModel { Name = "two", Value = 2 });

        var reader = new XmlStore<TestModel>();
        reader.SetSettings(settings);
        reader.Read().Select(x => x.Name).Should().BeEquivalentTo(new[] { "one", "two" });
    }

    [Fact]
    public async Task AsyncSeparateStore_CreateOverStaleFile_DoesNotCorrupt()
    {
        var settings = new Settings(_location, "asep");
        var guid = Guid.NewGuid();

        var seed = new AsyncXmlSeparateStore<TestModel>();
        seed.SetSettings(settings);
        await seed.CreateAsync(new TestModel { Name = "seed", Value = 0 });
        var storeDir = Path.GetDirectoryName(Directory.GetFiles(_dir, "*.xml", SearchOption.AllDirectories).First())!;

        var stalePath = Path.Combine(storeDir, $"asep-{guid}.xml");
        File.WriteAllText(stalePath, new string('Y', 8000));

        var s2 = new AsyncXmlSeparateStore<TestModel>();
        s2.SetSettings(settings);
        await s2.CreateAsync(new TestModel { Guid = guid, Name = "short", Value = 2 });

        var s3 = new AsyncXmlSeparateStore<TestModel>();
        s3.SetSettings(settings);
        var loaded = await s3.ReadAsync(guid);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("short");
    }
}

internal static class XmlSeparateTestExtensions
{
    public static XmlSeparateStore<TestModel> Also(this XmlSeparateStore<TestModel> store, Settings settings)
    {
        store.SetSettings(settings);
        return store;
    }
}
